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
        // 4: 主动画不再写 pre_parallel 独占的骨骼（尾巴/长发），常驻层只烘它自己动的骨骼；
        //    3 及以前是全骨骼铺满的，常驻层会互相盖成静止姿势，必须重转
        public const int FormatVersion = 4;
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

            using (FileStream stream = new FileStream(BinPath(maid), FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream))
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
                    Vector3[] vertices = ReadVector3Array(reader);
                    Vector3[] normals = ReadVector3Array(reader);
                    Vector2[] uvs = ReadVector2Array(reader);
                    int indexCount = reader.ReadInt32();
                    int[] triangles = new int[indexCount];
                    for (int t = 0; t < indexCount; t++)
                    {
                        triangles[t] = reader.ReadInt32();
                    }

                    if (vertices.Length > 65000)
                    {
                        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    }

                    mesh.SetVertices(new List<Vector3>(vertices));
                    mesh.SetNormals(new List<Vector3>(normals));
                    mesh.SetUVs(0, new List<Vector2>(uvs));
                    mesh.SetTriangles(new List<int>(triangles), 0);
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
                        for (int k = 0; k < count; k++)
                        {
                            track.Times[k] = reader.ReadSingle();
                        }

                        for (int k = 0; k < count; k++)
                        {
                            track.Positions[k] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        }

                        for (int k = 0; k < count; k++)
                        {
                            track.Rotations[k] = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        }

                        for (int k = 0; k < count; k++)
                        {
                            track.Scales[k] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        }

                        clip.Tracks.Add(track);
                    }

                    assets.ClipData.Add(clip);
                    BedrockAnimationClipBuilder builder = new BedrockAnimationClipBuilder();
                    builder.SampleRate = MaidAssetLoader.SampleRate;
                    assets.Clips.Add(builder.BuildClip(clip));
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

        static Vector3[] ReadVector3Array(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            Vector3[] values = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            return values;
        }

        static Vector2[] ReadVector2Array(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            Vector2[] values = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }

            return values;
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
