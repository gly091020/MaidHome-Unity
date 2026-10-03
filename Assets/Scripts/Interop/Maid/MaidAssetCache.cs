using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    /// manifest 里存源文件的 sha256，源文件一变就重转。
    /// </summary>
    public static class MaidAssetCache
    {
        // 5: Molang 补了 q.anim_time 简写（酒狐的挥雪球动画用它），已烘的采样得重来一遍
        // 4: 主动画不再写 pre_parallel 独占的骨骼（尾巴/长发），常驻层只烘它自己动的骨骼；
        //    3 及以前是全骨骼铺满的，常驻层会互相盖成静止姿势，必须重转
        public const int FormatVersion = 5;
        const string BinName = "maid.bin";
        const string ManifestName = "manifest.json";
        const string TextureName = "texture.png";

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

            for (int i = 0; i < sources.Count; i++)
            {
                JsonValue entry = saved[i];
                if (entry["label"].AsString("") != sources[i].Key)
                {
                    return false;
                }

                if (entry["sha256"].AsString("") != FileHash(sources[i].Value))
                {
                    return false;
                }
            }

            return true;
        }

        public static void Write(MaidSaveData maid, MaidAssets assets)
        {
            string folder = FolderOf(maid);
            AppPaths.EnsureDirectory(folder);

            // 按需加载的动画是从旧文件里读的，必须在删旧文件之前先读出来
            for (int i = 0; i < assets.ClipData.Count; i++)
            {
                assets.ClipData[i].EnsureTracks();
            }

            if (File.Exists(BinPath(maid)))
            {
                File.Delete(BinPath(maid));
            }

            WriteBin(BinPath(maid), maid, assets);
            CopyTexture(maid);
            WriteManifest(maid, assets);
        }

        public static bool TryLoad(MaidSaveData maid, out MaidAssets assets)
        {
            assets = null;
            if (!IsFresh(maid))
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

        // ---------------------------------------------------------------- 二进制

        static void WriteBin(string path, MaidSaveData maid, MaidAssets assets)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(FormatVersion);
                writer.Write(assets.Root != null ? assets.Root.name : maid.Name);

                List<Transform> transforms = new List<Transform>();
                CollectTransforms(assets.Root != null ? assets.Root.transform : null, transforms);
                writer.Write(transforms.Count);
                for (int i = 0; i < transforms.Count; i++)
                {
                    Transform transform = transforms[i];
                    writer.Write(transform.name);
                    writer.Write(RelativePath(transform, assets.Root.transform));
                    writer.Write(transforms.IndexOf(transform.parent));
                    writer.Write(transform.localPosition.x);
                    writer.Write(transform.localPosition.y);
                    writer.Write(transform.localPosition.z);
                    writer.Write(transform.localRotation.x);
                    writer.Write(transform.localRotation.y);
                    writer.Write(transform.localRotation.z);
                    writer.Write(transform.localRotation.w);
                    writer.Write(transform.localScale.x);
                    writer.Write(transform.localScale.y);
                    writer.Write(transform.localScale.z);

                    MeshFilter filter = transform.GetComponent<MeshFilter>();
                    Mesh mesh = filter == null ? null : filter.sharedMesh;
                    writer.Write(mesh != null);
                    if (mesh == null)
                    {
                        continue;
                    }

                    WriteVector3Array(writer, mesh.vertices);
                    WriteVector3Array(writer, mesh.normals);
                    WriteVector2Array(writer, mesh.uv);
                    int[] triangles = mesh.triangles;
                    writer.Write(triangles.Length);
                    for (int t = 0; t < triangles.Length; t++)
                    {
                        writer.Write(triangles[t]);
                    }
                }

                writer.Write(assets.ClipData.Count);
                for (int i = 0; i < assets.ClipData.Count; i++)
                {
                    BedrockClipData clip = assets.ClipData[i];
                    // 按需加载的缓存对象也能安全地再写一遍
                    clip.EnsureTracks();
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

            File.Copy(maid.TexturePath, Path.Combine(FolderOf(maid), TextureName), true);
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

        static void WriteManifest(MaidSaveData maid, MaidAssets assets)
        {
            List<KeyValuePair<string, string>> sources = Sources(maid);
            StringBuilder text = new StringBuilder();
            text.Append("{\n");
            text.Append("  \"version\": ").Append(FormatVersion).Append(",\n");
            text.Append("  \"maid\": ").Append(Quote(maid.Id)).Append(",\n");
            text.Append("  \"name\": ").Append(Quote(maid.Name)).Append(",\n");
            text.Append("  \"built\": ").Append(Quote(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
            text.Append("  \"counts\": {\"bones\": ").Append(CountTransforms(assets)).Append(", \"clips\": ")
                .Append(assets.ClipData.Count).Append(", \"keyframes\": ").Append(CountKeyframes(assets)).Append("},\n");
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
            File.WriteAllText(ManifestPath(maid), text.ToString(), new UTF8Encoding(false));
        }

        static int CountTransforms(MaidAssets assets)
        {
            return assets.Root == null ? 0 : assets.Root.GetComponentsInChildren<Transform>(true).Length;
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
