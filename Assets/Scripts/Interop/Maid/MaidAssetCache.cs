using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaidHome.Core.Json;
using MaidHome.Core.Storage;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Interop.Maid
{
    /// <summary>
    /// 女仆资源缓存。把「基岩 JSON + PNG」转换出来的网格、动画烘焙结果写到
    /// persistentDataPath/cache/maid/&lt;uuid&gt;/(maid.bin + manifest.json + texture.png)，
    /// 下次直接读缓存，不用再解析、再烘一遍。缓存是可再生的中间产物，删了大不了重转一次。
    /// manifest 里存源文件的时间戳/大小快照，源文件一变就重转；sha256 只留作诊断。
    /// </summary>
    public static class MaidAssetCache
    {
        // 5: Molang 补了 q.anim_time 简写（酒狐的挥雪球动画用它），已烘的采样得重来一遍
        // 4: 主动画不再写 pre_parallel 独占的骨骼（尾巴/长发），常驻层只烘它自己动的骨骼；
        //    3 及以前是全骨骼铺满的，常驻层会互相盖成静止姿势，必须重转
        // 6: manifest 增加快速源文件 stamp；旧缓存没有 stamp，会平稳重建一次
        public const int FormatVersion = 6;
        const string BinName = "maid.bin";
        const string TempSuffix = ".tmp";
        const string ManifestName = "manifest.json";
        const string TextureName = "texture.png";
        const int WriteBufferSize = 1024 * 1024;

        public static string FolderOf(MaidSaveData maid)
        {
            return FolderOf(maid.Id);
        }

        public static string FolderOf(string maidId)
        {
            return Path.Combine(Path.Combine(AppPaths.CacheRoot, "maid"), Sanitize(maidId));
        }

        /// <summary>女仆从存档里消失时（例如被发回 MC）连缓存一起清掉。</summary>
        public static void DeleteFolder(string maidId)
        {
            string folder = FolderOf(maidId);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        public static string BinPath(MaidSaveData maid)
        {
            return Path.Combine(FolderOf(maid), BinName);
        }

        public static string ManifestPath(MaidSaveData maid)
        {
            return Path.Combine(FolderOf(maid), ManifestName);
        }

        /// <summary>参与缓存的源文件：标签 -> 绝对路径（不存在的会被过滤掉）。</summary>
        public static List<KeyValuePair<string, string>> Sources(MaidSaveData maid)
        {
            List<KeyValuePair<string, string>> sources = new List<KeyValuePair<string, string>>();
            AddSource(sources, "model", maid.ModelPath);
            AddSource(sources, "texture", maid.TexturePath);
            AddSource(sources, "anim", maid.AnimationPath);
            AddSource(sources, "shared_anim", MaidAssetLoader.SharedAnimationPath());
            return sources;
        }

        static void AddSource(List<KeyValuePair<string, string>> sources, string label, string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                sources.Add(new KeyValuePair<string, string>(label, path));
            }
        }

        public static bool IsFresh(MaidSaveData maid)
        {
            if (maid == null)
            {
                return false;
            }

            string manifestPath = ManifestPath(maid);
            string binPath = BinPath(maid);
            if (!File.Exists(manifestPath) || !File.Exists(binPath))
            {
                return false;
            }

            JsonValue manifest;
            try
            {
                manifest = MiniJson.ParseFile(manifestPath);
            }
            catch (System.Exception)
            {
                return false;
            }

            if (manifest["version"].AsInt(0) != FormatVersion)
            {
                return false;
            }

            List<KeyValuePair<string, string>> sources = Sources(maid);
            JsonValue saved = manifest["sources"];
            if (saved.Count != sources.Count)
            {
                return false;
            }

            if (manifest["stamp"].AsString("") != SourceStamp(sources))
            {
                return false;
            }

            for (int i = 0; i < sources.Count; i++)
            {
                JsonValue entry = saved[i];
                if (entry["label"].AsString("") != sources[i].Key)
                {
                    return false;
                }

                if (entry["bytes"].AsInt(-1) != new FileInfo(sources[i].Value).Length)
                {
                    return false;
                }
            }

            return true;
        }

        public static void Write(MaidSaveData maid, MaidAssets assets)
        {
            EnsureTracks(assets);
            WriteSnapshot(maid, CaptureSnapshot(maid, assets), null);
        }

        /// <summary>
        /// 后台写缓存。抓取 Transform/Mesh 的副本必须先在主线程做完，之后几十 MB 的
        /// BinaryWriter 序列化和 SHA-256 都交给线程池，进度条可以继续刷新。
        /// </summary>
        public static IEnumerator WriteRoutine(MaidSaveData maid, MaidAssets assets,
            Action<float> progress, Action<string> failed)
        {
            EnsureTracks(assets);
            CacheSnapshot snapshot = CaptureSnapshot(maid, assets);
            Exception error = null;
            int reported = 0;
            Task task = Task.Run(() =>
            {
                try
                {
                    WriteSnapshot(maid, snapshot, value =>
                    {
                        Volatile.Write(ref reported, Math.Max(0, Math.Min(1000, (int)(value * 1000f))));
                    });
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref error, exception);
                }
            });

            while (!task.IsCompleted)
            {
                if (progress != null)
                {
                    progress(Volatile.Read(ref reported) / 1000f);
                }

                yield return null;
            }

            Exception failure = Volatile.Read(ref error);
            if (failure != null)
            {
                if (failed != null)
                {
                    failed("写缓存失败: " + failure.Message);
                }

                yield break;
            }

            if (progress != null)
            {
                progress(1f);
            }
        }

        public static bool TryLoad(MaidSaveData maid, out MaidAssets assets, bool freshnessChecked = false)
        {
            assets = null;
            if (!freshnessChecked && !IsFresh(maid))
            {
                return false;
            }

            try
            {
                assets = LoadBin(maid);
                return assets != null;
            }
            catch (System.Exception error)
            {
                assets = null;
                Debug.LogWarning("读女仆缓存失败，重转一次: " + error.Message);
                return false;
            }
        }

        public static void Delete(MaidSaveData maid)
        {
            string folder = FolderOf(maid);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        // Unity 的 Transform/Mesh 只能在主线程抓，抓成纯数据后再交给后台线程写盘。
        sealed class CacheSnapshot
        {
            public string RootName;
            public List<TransformSnapshot> Transforms;
            public List<BedrockClipData> Clips;
            public int KeyframeCount;
        }

        sealed class TransformSnapshot
        {
            public string Name;
            public string Path;
            public int ParentIndex;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public MeshSnapshot Mesh;
        }

        sealed class MeshSnapshot
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public int[] Triangles;
        }

        // ---------------------------------------------------------------- 二进制

        static void EnsureTracks(MaidAssets assets)
        {
            // 按需加载的动画是从旧文件里读的，必须在覆盖旧文件之前先读出来
            for (int i = 0; i < assets.ClipData.Count; i++)
            {
                assets.ClipData[i].EnsureTracks();
            }
        }

        static CacheSnapshot CaptureSnapshot(MaidSaveData maid, MaidAssets assets)
        {
            CacheSnapshot snapshot = new CacheSnapshot();
            snapshot.RootName = assets.Root != null ? assets.Root.name : maid.Name;
            snapshot.Clips = new List<BedrockClipData>(assets.ClipData);

            List<Transform> transforms = new List<Transform>();
            CollectTransforms(assets.Root != null ? assets.Root.transform : null, transforms);
            Dictionary<Transform, int> indexes = new Dictionary<Transform, int>(transforms.Count);
            for (int i = 0; i < transforms.Count; i++)
            {
                indexes[transforms[i]] = i;
            }

            snapshot.Transforms = new List<TransformSnapshot>(transforms.Count);
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                TransformSnapshot bone = new TransformSnapshot();
                bone.Name = transform.name;
                bone.Path = RelativePath(transform, assets.Root.transform);
                bone.ParentIndex = transform.parent != null && indexes.TryGetValue(transform.parent, out int parent)
                    ? parent
                    : -1;
                bone.LocalPosition = transform.localPosition;
                bone.LocalRotation = transform.localRotation;
                bone.LocalScale = transform.localScale;

                MeshFilter filter = transform.GetComponent<MeshFilter>();
                Mesh mesh = filter == null ? null : filter.sharedMesh;
                if (mesh != null)
                {
                    bone.Mesh = new MeshSnapshot();
                    bone.Mesh.Vertices = mesh.vertices;
                    bone.Mesh.Normals = mesh.normals;
                    bone.Mesh.Uvs = mesh.uv;
                    bone.Mesh.Triangles = mesh.triangles;
                }

                snapshot.Transforms.Add(bone);
            }

            snapshot.KeyframeCount = CountKeyframes(assets);
            return snapshot;
        }

        static void WriteSnapshot(MaidSaveData maid, CacheSnapshot snapshot, Action<float> progress)
        {
            string folder = FolderOf(maid);
            AppPaths.EnsureDirectory(folder);
            if (progress != null)
            {
                progress(0f);
            }

            string binPath = BinPath(maid);
            string tempBin = binPath + TempSuffix;
            if (File.Exists(tempBin))
            {
                File.Delete(tempBin);
            }

            WriteBin(tempBin, snapshot, progress);
            ReplaceFile(tempBin, binPath);
            CopyTexture(maid);
            if (progress != null)
            {
                progress(0.96f);
            }

            WriteManifest(maid, snapshot);
            if (progress != null)
            {
                progress(1f);
            }
        }

        static void WriteBin(string path, CacheSnapshot snapshot, Action<float> progress)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                WriteBufferSize, FileOptions.SequentialScan))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(FormatVersion);
                writer.Write(snapshot.RootName ?? "");

                writer.Write(snapshot.Transforms.Count);
                for (int i = 0; i < snapshot.Transforms.Count; i++)
                {
                    TransformSnapshot transform = snapshot.Transforms[i];
                    writer.Write(transform.Name ?? "");
                    writer.Write(transform.Path ?? "");
                    writer.Write(transform.ParentIndex);
                    writer.Write(transform.LocalPosition.x);
                    writer.Write(transform.LocalPosition.y);
                    writer.Write(transform.LocalPosition.z);
                    writer.Write(transform.LocalRotation.x);
                    writer.Write(transform.LocalRotation.y);
                    writer.Write(transform.LocalRotation.z);
                    writer.Write(transform.LocalRotation.w);
                    writer.Write(transform.LocalScale.x);
                    writer.Write(transform.LocalScale.y);
                    writer.Write(transform.LocalScale.z);

                    writer.Write(transform.Mesh != null);
                    if (transform.Mesh == null)
                    {
                        continue;
                    }

                    WriteVector3Array(writer, transform.Mesh.Vertices);
                    WriteVector3Array(writer, transform.Mesh.Normals);
                    WriteVector2Array(writer, transform.Mesh.Uvs);
                    int[] triangles = transform.Mesh.Triangles;
                    writer.Write(triangles.Length);
                    for (int t = 0; t < triangles.Length; t++)
                    {
                        writer.Write(triangles[t]);
                    }

                    if (progress != null)
                    {
                        progress(0.45f * (i + 1f) / Math.Max(1, snapshot.Transforms.Count));
                    }
                }

                writer.Write(snapshot.Clips.Count);
                for (int i = 0; i < snapshot.Clips.Count; i++)
                {
                    BedrockClipData clip = snapshot.Clips[i];
                    writer.Write(clip.Name);
                    writer.Write(clip.Length);
                    writer.Write((int)clip.WrapMode);
                    writer.Write(clip.Tracks.Count);
                    for (int t = 0; t < clip.Tracks.Count; t++)
                    {
                        BedrockBoneTrack track = clip.Tracks[t];
                        writer.Write(track.Path);
                        writer.Write(track.Count);
                        for (int k = 0; k < track.Count; k++)
                        {
                            writer.Write(track.Times[k]);
                        }

                        for (int k = 0; k < track.Count; k++)
                        {
                            writer.Write(track.Positions[k].x);
                            writer.Write(track.Positions[k].y);
                            writer.Write(track.Positions[k].z);
                        }

                        for (int k = 0; k < track.Count; k++)
                        {
                            writer.Write(track.Rotations[k].x);
                            writer.Write(track.Rotations[k].y);
                            writer.Write(track.Rotations[k].z);
                            writer.Write(track.Rotations[k].w);
                        }

                        for (int k = 0; k < track.Count; k++)
                        {
                            writer.Write(track.Scales[k].x);
                            writer.Write(track.Scales[k].y);
                            writer.Write(track.Scales[k].z);
                        }
                    }

                    if (progress != null)
                    {
                        progress(0.45f + 0.5f * (i + 1f) / Math.Max(1, snapshot.Clips.Count));
                    }
                }
            }
        }

        static MaidAssets LoadBin(MaidSaveData maid)
        {
            MaidAssets assets = new MaidAssets();
            assets.Maid = maid;
            assets.FromCache = true;
            assets.Warnings.AddRange(maid.Warnings);

            // 只读网格那段，动画的几十 MB 采样先跳过：进游戏只用得上 idle/walk 几条，
            // 全部读出来要几百毫秒、还要几十 MB 内存。每条动画记下文件偏移，播到再读（见 LazyClipSource）。
            string binPath = BinPath(maid);
            using (BinStreamReader reader = new BinStreamReader(binPath, 0))
            {
                int version = reader.ReadInt32();
                if (version != FormatVersion)
                {
                    throw new InvalidDataException("缓存版本不对: " + version);
                }

                string rootName = reader.ReadString();
                GameObject root = new GameObject(string.IsNullOrEmpty(rootName) ? "MaidModel" : rootName);
                root.SetActive(false);
                assets.Root = root;
                assets.Texture = LoadTexture(Path.Combine(FolderOf(maid), TextureName));
                Material material = BedrockModelBuilder.CreateMaterial(assets.Texture);

                int transformCount = reader.ReadInt32();
                List<Transform> transforms = new List<Transform>(transformCount);
                for (int i = 0; i < transformCount; i++)
                {
                    string name = reader.ReadString();
                    reader.ReadString();
                    int parentIndex = reader.ReadInt32();
                    Vector3 position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    Quaternion rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    Vector3 scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                    GameObject bone;
                    if (i == 0)
                    {
                        // 0 号就是模型根节点自己：上面已经 new 过一个 root 了，直接复用它。
                        // 以前这里又 new 了一个同名对象，于是模型（含网格）挂在那个新对象上、
                        // 组件挂在空 root 上，两者平级；动画曲线路径是相对 root 的 "MRoot/..."，
                        // 挂在空 root 上自然一条都对不上，表现为"模型和移动的对象是两个东西"。
                        bone = root;
                        bone.name = name;
                    }
                    else
                    {
                        bone = new GameObject(name);
                        bone.transform.SetParent(parentIndex >= 0 && parentIndex < transforms.Count
                            ? transforms[parentIndex]
                            : root.transform, false);
                    }

                    Transform transform = bone.transform;
                    transform.localPosition = position;
                    transform.localRotation = rotation;
                    transform.localScale = scale;
                    transforms.Add(transform);

                    if (!reader.ReadBoolean())
                    {
                        continue;
                    }

                    Mesh mesh = new Mesh();
                    mesh.name = name + "_mesh";
                    Vector3[] vertices = reader.ReadVector3Array();
                    Vector3[] normals = reader.ReadVector3Array();
                    Vector2[] uvs = reader.ReadVector2Array();
                    int[] triangles = reader.ReadIntArray();

                    if (vertices.Length > 65000)
                    {
                        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    }

                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.uv = uvs;
                    mesh.triangles = triangles;
                    mesh.RecalculateBounds();

                    bone.AddComponent<MeshFilter>().sharedMesh = mesh;
                    bone.AddComponent<MeshRenderer>().sharedMaterial = material;
                }

                MaidAssetLoader.ApplyModelRules(assets);

                int clipCount = reader.ReadInt32();
                for (int i = 0; i < clipCount; i++)
                {
                    BedrockClipData clip = new BedrockClipData();
                    clip.Name = reader.ReadString();
                    clip.Length = reader.ReadSingle();
                    clip.WrapMode = (WrapMode)reader.ReadInt32();
                    // 采样区从 trackCount 那个 int 开始（LazyClipSource 会从这儿重读）
                    long dataOffset = reader.Position;
                    int trackCount = reader.ReadInt32();
                    for (int t = 0; t < trackCount; t++)
                    {
                        reader.ReadString();
                        int count = reader.ReadInt32();
                        // 时间 1 + 位置 3 + 旋转 4 + 缩放 3 = 11 个 float
                        reader.Skip(44L * count);
                    }

                    LazyClipSource source = new LazyClipSource(binPath, dataOffset);
                    clip.TrackLoader = source.Load;
                    assets.ClipData.Add(clip);
                }
            }

            return assets;
        }

        static void CollectTransforms(Transform current, List<Transform> result)
        {
            if (current == null)
            {
                return;
            }

            result.Add(current);
            for (int i = 0; i < current.childCount; i++)
            {
                CollectTransforms(current.GetChild(i), result);
            }
        }

        static string RelativePath(Transform transform, Transform root)
        {
            string path = transform.name;
            Transform current = transform.parent;
            for (int i = 0; i < 64 && current != null && current != root; i++)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        static void WriteVector3Array(BinaryWriter writer, Vector3[] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i].x);
                writer.Write(values[i].y);
                writer.Write(values[i].z);
            }
        }

        static void WriteVector2Array(BinaryWriter writer, Vector2[] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i].x);
                writer.Write(values[i].y);
            }
        }

        /// <summary>
        /// 按需把一条动画的采样数据读回来。构造函数里记下它在文件里的位置，第一次播到才真去读。
        /// </summary>
        sealed class LazyClipSource
        {
            readonly string _path;
            readonly long _offset;

            public LazyClipSource(string path, long offset)
            {
                _path = path;
                _offset = offset;
            }

            public void Load(BedrockClipData clip)
            {
                using (BinStreamReader reader = new BinStreamReader(_path, _offset))
                {
                    int trackCount = reader.ReadInt32();
                    for (int t = 0; t < trackCount; t++)
                    {
                        BedrockBoneTrack track = new BedrockBoneTrack();
                        track.Path = reader.ReadString();
                        int count = reader.ReadInt32();
                        track.Times = new float[count];
                        track.Positions = new Vector3[count];
                        track.Rotations = new Quaternion[count];
                        track.Scales = new Vector3[count];
                        reader.ReadFloats(track.Times);
                        reader.ReadVector3Array(track.Positions);
                        reader.ReadQuaternionArray(track.Rotations);
                        reader.ReadVector3Array(track.Scales);
                        clip.Tracks.Add(track);
                    }
                }
            }
        }

        /// <summary>
        /// 读 maid.bin 的小工具。不用 BinaryReader 的原因：
        /// 1) 要能一下子跳过大段采样（`Skip` 直接 seek，不把数据读进来）；
        /// 2) BinaryReader 读字符串走内部缓冲，`stream.Position` 会跑到前面去，没法算偏移；
        /// 3) 数组整块读 + BlockCopy 比逐个 ReadSingle 快一个数量级。
        /// 格式和 WriteBin 一一对应，改了那边必须同步改这里。
        /// </summary>
        sealed class BinStreamReader : System.IDisposable
        {
            // 缓冲别开太大：索引阶段每跳过一段采样就会 seek 一次，读进来的缓冲大部分要丢掉
            // （实测护士酒狐那份 29.7 MB 的缓存：4 KB 缓冲只读 7.6 MB，64 KB 要读 13.6 MB）
            const int BufferSize = 4 * 1024;

            readonly FileStream _stream;
            readonly byte[] _buffer = new byte[BufferSize];
            byte[] _bytes = new byte[4096];
            float[] _floats = new float[1024];
            int _start;
            int _end;

            public BinStreamReader(string path, long offset)
            {
                // bufferSize 1 = 不用 FileStream 自己那层缓冲，读多少就是多少（不然 4 KB 请求会拉来 4 KB 预读）
                _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
                _stream.Seek(offset, SeekOrigin.Begin);
                Position = offset;
            }

            public long Position { get; private set; }

            public void Dispose()
            {
                _stream.Dispose();
            }

            void Fill(int count)
            {
                if (_end - _start >= count)
                {
                    return;
                }

                if (count > _buffer.Length)
                {
                    count = _buffer.Length;
                }

                int left = _end - _start;
                if (left > 0 && _start > 0)
                {
                    Buffer.BlockCopy(_buffer, _start, _buffer, 0, left);
                }

                _start = 0;
                _end = left;
                while (_end < count)
                {
                    int read = _stream.Read(_buffer, _end, _buffer.Length - _end);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException("女仆缓存文件不完整");
                    }

                    _end += read;
                }
            }

            void ReadBytes(byte[] target, int count)
            {
                int copied = 0;
                while (copied < count)
                {
                    Fill(1);
                    int take = Mathf.Min(count - copied, _end - _start);
                    Buffer.BlockCopy(_buffer, _start, target, copied, take);
                    _start += take;
                    copied += take;
                    Position += take;
                }
            }

            byte[] ByteScratch(int byteCount)
            {
                if (_bytes.Length < byteCount)
                {
                    _bytes = new byte[byteCount];
                }

                return _bytes;
            }

            float[] FloatScratch(int floatCount)
            {
                if (_floats.Length < floatCount)
                {
                    _floats = new float[floatCount];
                }

                return _floats;
            }

            public int ReadInt32()
            {
                Fill(4);
                int value = BitConverter.ToInt32(_buffer, _start);
                _start += 4;
                Position += 4;
                return value;
            }

            public float ReadSingle()
            {
                Fill(4);
                float value = BitConverter.ToSingle(_buffer, _start);
                _start += 4;
                Position += 4;
                return value;
            }

            public bool ReadBoolean()
            {
                Fill(1);
                Position += 1;
                return _buffer[_start++] != 0;
            }

            /// <summary>BinaryWriter.Write(string)：7 位长度前缀（字节数）+ UTF8</summary>
            public string ReadString()
            {
                int length = 0;
                int shift = 0;
                while (true)
                {
                    Fill(1);
                    byte value = _buffer[_start++];
                    Position += 1;
                    length |= (value & 0x7F) << shift;
                    if ((value & 0x80) == 0)
                    {
                        break;
                    }

                    shift += 7;
                    if (shift > 28)
                    {
                        throw new InvalidDataException("女仆缓存字符串长度异常");
                    }
                }

                if (length <= 0)
                {
                    return "";
                }

                byte[] bytes = ByteScratch(length);
                ReadBytes(bytes, length);
                return Encoding.UTF8.GetString(bytes, 0, length);
            }

            /// <summary>跳过一段数据。跨度大就直接 seek，别把中间几十 MB 读进内存。</summary>
            public void Skip(long count)
            {
                if (count <= 0)
                {
                    return;
                }

                Position += count;
                if (count <= _end - _start)
                {
                    _start += (int)count;
                    return;
                }

                _start = 0;
                _end = 0;
                _stream.Seek(Position, SeekOrigin.Begin);
            }

            public void ReadFloats(float[] target)
            {
                int byteCount = target.Length * 4;
                if (byteCount == 0)
                {
                    return;
                }

                byte[] bytes = ByteScratch(byteCount);
                ReadBytes(bytes, byteCount);
                Buffer.BlockCopy(bytes, 0, target, 0, byteCount);
            }

            public void ReadVector3Array(Vector3[] target)
            {
                int count = target.Length;
                if (count == 0)
                {
                    return;
                }

                int byteCount = count * 12;
                byte[] bytes = ByteScratch(byteCount);
                ReadBytes(bytes, byteCount);
                float[] values = FloatScratch(count * 3);
                Buffer.BlockCopy(bytes, 0, values, 0, byteCount);
                for (int i = 0; i < count; i++)
                {
                    int offset = i * 3;
                    target[i] = new Vector3(values[offset], values[offset + 1], values[offset + 2]);
                }
            }

            public void ReadQuaternionArray(Quaternion[] target)
            {
                int count = target.Length;
                if (count == 0)
                {
                    return;
                }

                int byteCount = count * 16;
                byte[] bytes = ByteScratch(byteCount);
                ReadBytes(bytes, byteCount);
                float[] values = FloatScratch(count * 4);
                Buffer.BlockCopy(bytes, 0, values, 0, byteCount);
                for (int i = 0; i < count; i++)
                {
                    int offset = i * 4;
                    target[i] = new Quaternion(values[offset], values[offset + 1], values[offset + 2],
                        values[offset + 3]);
                }
            }

            public Vector3[] ReadVector3Array()
            {
                Vector3[] values = new Vector3[ReadInt32()];
                ReadVector3Array(values);
                return values;
            }

            public Vector2[] ReadVector2Array()
            {
                int count = ReadInt32();
                Vector2[] values = new Vector2[count];
                if (count == 0)
                {
                    return values;
                }

                int byteCount = count * 8;
                byte[] bytes = ByteScratch(byteCount);
                ReadBytes(bytes, byteCount);
                float[] floats = FloatScratch(count * 2);
                Buffer.BlockCopy(bytes, 0, floats, 0, byteCount);
                for (int i = 0; i < count; i++)
                {
                    values[i] = new Vector2(floats[i * 2], floats[i * 2 + 1]);
                }

                return values;
            }

            public int[] ReadIntArray()
            {
                int count = ReadInt32();
                int[] values = new int[count];
                int byteCount = count * 4;
                if (byteCount == 0)
                {
                    return values;
                }

                byte[] bytes = ByteScratch(byteCount);
                ReadBytes(bytes, byteCount);
                Buffer.BlockCopy(bytes, 0, values, 0, byteCount);
                return values;
            }
        }

        // ---------------------------------------------------------------- 贴图 / manifest

        static void CopyTexture(MaidSaveData maid)
        {
            if (string.IsNullOrEmpty(maid.TexturePath) || !File.Exists(maid.TexturePath))
            {
                return;
            }

            string destination = Path.Combine(FolderOf(maid), TextureName);
            string temp = destination + TempSuffix;
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            File.Copy(maid.TexturePath, temp, true);
            ReplaceFile(temp, destination);
        }

        public static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        static void WriteManifest(MaidSaveData maid, CacheSnapshot snapshot)
        {
            List<KeyValuePair<string, string>> sources = Sources(maid);
            StringBuilder text = new StringBuilder();
            text.Append("{\n");
            text.Append("  \"version\": ").Append(FormatVersion).Append(",\n");
            text.Append("  \"maid\": ").Append(Quote(maid.Id)).Append(",\n");
            text.Append("  \"name\": ").Append(Quote(maid.Name)).Append(",\n");
            text.Append("  \"built\": ").Append(Quote(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
            text.Append("  \"stamp\": ").Append(Quote(SourceStamp(sources))).Append(",\n");
            text.Append("  \"counts\": {\"bones\": ").Append(snapshot.Transforms.Count).Append(", \"clips\": ")
                .Append(snapshot.Clips.Count).Append(", \"keyframes\": ").Append(snapshot.KeyframeCount).Append("},\n");
            text.Append("  \"sources\": [");
            for (int i = 0; i < sources.Count; i++)
            {
                text.Append(i == 0 ? "\n" : ",\n");
                text.Append("    {\"label\": ").Append(Quote(sources[i].Key))
                    .Append(", \"file\": ").Append(Quote(Path.GetFileName(sources[i].Value)))
                    .Append(", \"sha256\": ").Append(Quote(FileHash(sources[i].Value)))
                    .Append(", \"bytes\": ").Append(new FileInfo(sources[i].Value).Length).Append("}");
            }

            text.Append(sources.Count > 0 ? "\n  ]\n" : "]\n");
            text.Append("}\n");
            string destination = ManifestPath(maid);
            string temp = destination + TempSuffix;
            File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
            ReplaceFile(temp, destination);
        }

        static string SourceStamp(List<KeyValuePair<string, string>> sources)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < sources.Count; i++)
            {
                FileInfo info = new FileInfo(sources[i].Value);
                text.Append(sources[i].Key).Append('|')
                    .Append(info.Name).Append('|')
                    .Append(info.Length).Append('|')
                    .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                StringBuilder stamp = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    stamp.Append(hash[i].ToString("x2"));
                }

                return stamp.ToString();
            }
        }

        static void ReplaceFile(string source, string destination)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }

        static int CountKeyframes(MaidAssets assets)
        {
            int total = 0;
            for (int i = 0; i < assets.ClipData.Count; i++)
            {
                total += assets.ClipData[i].KeyframeCount;
            }

            return total;
        }

        public static string FileHash(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return "";
            }

            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString("x2"));
                }

                return text.ToString();
            }
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unknown";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] buffer = name.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (buffer[i] == invalid[j])
                    {
                        buffer[i] = '_';
                        break;
                    }
                }
            }

            return new string(buffer);
        }

        static string Quote(string text)
        {
            if (text == null)
            {
                return "\"\"";
            }

            StringBuilder result = new StringBuilder("\"");
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\\')
                {
                    result.Append('\\').Append(c);
                }
                else if (c < ' ')
                {
                    result.Append("\\u").Append(((int)c).ToString("x4"));
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.Append('"').ToString();
        }
    }
}
