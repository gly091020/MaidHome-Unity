using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>旋转合成顺序，命名方式跟 TLM 的 rotateZYX 一致（写在前面的先乘）。</summary>
    public enum BedrockRotationOrder
    {
        XZY,
        ZXY,
        ZYX,
        XYZ,
        YXZ,
        YZX,
    }

    public sealed class BedrockModelBuildResult
    {
        public GameObject Root;
        public int VertexCount;
        public int TriangleCount;
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 把基岩模型数据转成 Unity 的骨骼层级 + 网格。
    /// 基岩模型面朝 -Z，转出来面朝 Unity 的 +Z；1 格 = PixelsPerUnit 像素。
    /// 旋转约定照抄 Blockbench：它内部把模型沿 X 镜像存放（bb_x = -x），Euler 顺序固定 ZYX。
    /// 换算到本工程的 (-x, y, -z) 坐标系后就是 Rz*Ry*Rx、角度全部不取反。
    /// 用 Tools/obj_oracle.py 和 Blockbench 导出的 OBJ 对拍过，顶点/UV 偏差都是 0。
    /// </summary>
    public sealed class BedrockModelBuilder
    {
        static readonly string[] Faces = { "north", "south", "east", "west", "up", "down" };

        static readonly Dictionary<string, Vector3> FaceNormals = new Dictionary<string, Vector3>
        {
            { "north", new Vector3(0f, 0f, -1f) },
            { "south", new Vector3(0f, 0f, 1f) },
            { "east", new Vector3(1f, 0f, 0f) },
            { "west", new Vector3(-1f, 0f, 0f) },
            { "up", new Vector3(0f, 1f, 0f) },
            { "down", new Vector3(0f, -1f, 0f) },
        };

        struct FaceAxes
        {
            public Vector3 U;
            public Vector3 VTop;

            public FaceAxes(Vector3 u, Vector3 vTop)
            {
                U = u;
                VTop = vTop;
            }
        }

        /// <summary>
        /// 每个面的贴图方向（基岩坐标系）：U 是 u 增大的方向，VTop 是指向矩形上沿（v 变小）的方向。
        /// 从 Blockbench 导出的 OBJ（faces 里的顶点/UV 配对）反推出来的，改这个表等于改某个面的贴图朝向。
        /// </summary>
        static readonly Dictionary<string, FaceAxes> FaceAxesTable = new Dictionary<string, FaceAxes>
        {
            { "north", new FaceAxes(new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)) },
            { "south", new FaceAxes(new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f)) },
            { "east", new FaceAxes(new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f)) },
            { "west", new FaceAxes(new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f)) },
            { "up", new FaceAxes(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)) },
            { "down", new FaceAxes(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)) },
        };

        public float PixelsPerUnit = 16f;
        public bool CutoutMaterial = true;
        public string RootName = "BedrockModel";
        /// <summary>每个轴的旋转符号（1 或 -1）。默认直接用不取反。</summary>
        public Vector3 RotationSigns = Vector3.one;
        public BedrockRotationOrder RotationOrder = BedrockRotationOrder.ZYX;

        readonly Dictionary<string, Transform> _boneTransforms = new Dictionary<string, Transform>();
        readonly List<string> _warnings = new List<string>();
        float _texWidth = 64f;
        float _texHeight = 64f;

        public BedrockModelBuildResult Build(BedrockGeometry geometry, Texture2D texture)
        {
            BedrockModelBuildResult result = new BedrockModelBuildResult();
            if (geometry == null)
            {
                result.Warnings.Add("模型数据为空");
                return result;
            }

            _warnings.Clear();
            _boneTransforms.Clear();
            _texWidth = Mathf.Max(1f, geometry.TextureWidth);
            _texHeight = Mathf.Max(1f, geometry.TextureHeight);

            GameObject root = new GameObject(string.IsNullOrEmpty(RootName) ? "BedrockModel" : RootName);
            Material material = CreateMaterial(texture);

            Dictionary<string, BedrockBone> byName = new Dictionary<string, BedrockBone>();
            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                BedrockBone bone = geometry.Bones[i];
                if (!byName.ContainsKey(bone.Name))
                {
                    byName.Add(bone.Name, bone);
                }
                else
                {
                    _warnings.Add("骨骼重名: " + bone.Name);
                }
            }

            string[] resolvedParents = new string[geometry.Bones.Count];
            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                BedrockBone bone = geometry.Bones[i];
                Vector3 parentPivot = Vector3.zero;
                if (!string.IsNullOrEmpty(bone.Parent))
                {
                    BedrockBone parentBone;
                    if (byName.TryGetValue(bone.Parent, out parentBone))
                    {
                        parentPivot = parentBone.Pivot;
                        resolvedParents[i] = bone.Parent;
                    }
                    else
                    {
                        _warnings.Add("找不到父骨骼 " + bone.Parent + "，按根骨骼处理: " + bone.Name);
                    }
                }

                GameObject boneObject = new GameObject(string.IsNullOrEmpty(bone.Name) ? "bone" : bone.Name);
                boneObject.transform.SetParent(root.transform, false);
                boneObject.transform.localPosition = ToUnity(bone.Pivot - parentPivot);
                boneObject.transform.localRotation = ToUnityRotation(bone.Rotation);
                _boneTransforms[bone.Name] = boneObject.transform;
            }

            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                string parentName = resolvedParents[i];
                if (!string.IsNullOrEmpty(parentName) && _boneTransforms.ContainsKey(parentName))
                {
                    _boneTransforms[geometry.Bones[i].Name].SetParent(_boneTransforms[parentName], false);
                }
            }

            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                BedrockBone bone = geometry.Bones[i];
                Mesh mesh = BuildBoneMesh(bone, geometry);
                if (mesh == null)
                {
                    continue;
                }

                GameObject boneObject = _boneTransforms[bone.Name].gameObject;
                MeshFilter filter = boneObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = boneObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                result.VertexCount += mesh.vertexCount;
                result.TriangleCount += mesh.triangles.Length / 3;
            }

            result.Root = root;
            result.Warnings.AddRange(_warnings);
            return result;
        }

        Mesh BuildBoneMesh(BedrockBone bone, BedrockGeometry geometry)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            for (int i = 0; i < bone.Cubes.Count; i++)
            {
                AppendCube(vertices, normals, uvs, triangles, bone, bone.Cubes[i]);
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh();
            mesh.name = bone.Name + "_mesh";
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void AppendCube(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, BedrockBone bone, BedrockCube cube)
        {
            float inflate = cube.Inflate + bone.Inflate;
            Vector3 min = cube.Origin - new Vector3(inflate, inflate, inflate);
            Vector3 max = min + cube.Size + new Vector3(inflate * 2f, inflate * 2f, inflate * 2f);
            bool hasRotation = cube.HasRotation;
            Quaternion cubeRotation = ToUnityRotation(cube.Rotation);
            Vector3 bonePivot = ToUnity(bone.Pivot);
            Vector3 cubePivot = ToUnity(cube.Pivot);

            // 厚度为 0 的方块是单个面片，正面和背面都要能看见（否则背面会被剔除掉）
            bool flatX = max.x - min.x < 1e-5f;
            bool flatY = max.y - min.y < 1e-5f;
            bool flatZ = max.z - min.z < 1e-5f;

            for (int f = 0; f < Faces.Length; f++)
            {
                string face = Faces[f];
                Vector4 rect;
                if (!FaceRect(cube, face, out rect))
                {
                    continue;
                }

                Vector3 n = FaceNormals[face];
                FaceAxes axes = FaceAxesTable[face];
                Vector3 right = axes.U;
                Vector3 up = axes.VTop;
                Vector3[] corners = FaceCorners(min, max, face);

                float minRight = float.MaxValue;
                float maxUp = float.MinValue;
                float maxRight = float.MinValue;
                float minUp = float.MaxValue;
                for (int c = 0; c < 4; c++)
                {
                    float r = Vector3.Dot(corners[c], right);
                    float u = Vector3.Dot(corners[c], up);
                    minRight = Mathf.Min(minRight, r);
                    maxRight = Mathf.Max(maxRight, r);
                    minUp = Mathf.Min(minUp, u);
                    maxUp = Mathf.Max(maxUp, u);
                }

                float spanRight = maxRight - minRight;
                float spanUp = maxUp - minUp;
                if (spanRight < 1e-6f || spanUp < 1e-6f)
                {
                    continue;
                }

                Vector3[] local = new Vector3[4];
                Vector2[] faceUvs = new Vector2[4];
                for (int c = 0; c < 4; c++)
                {
                    Vector3 point = corners[c];
                    Vector3 converted = ToUnity(point);
                    if (hasRotation)
                    {
                        converted = cubePivot + cubeRotation * (converted - cubePivot);
                    }

                    local[c] = converted - bonePivot;

                    float s = (Vector3.Dot(point, right) - minRight) / spanRight;
                    float t = (maxUp - Vector3.Dot(point, up)) / spanUp;
                    if (cube.Mirror && cube.BoxUv)
                    {
                        s = 1f - s;
                    }

                    float px = rect.x + s * rect.z;
                    float py = rect.y + t * rect.w;
                    if (px < -0.01f || py < -0.01f || px > _texWidth + 0.01f || py > _texHeight + 0.01f)
                    {
                        _warnings.Add("UV 超出贴图范围: 骨骼=" + bone.Name + " 面=" + face + " 像素=(" + px.ToString("0.##") + ", " + py.ToString("0.##") + ")");
                    }

                    faceUvs[c] = new Vector2(px / _texWidth, 1f - py / _texHeight);
                }

                Vector3 expected = hasRotation ? cubeRotation * ToUnityDirection(n) : ToUnityDirection(n);
                Vector3 geometric = Vector3.Cross(local[1] - local[0], local[2] - local[0]);
                if (Vector3.Dot(geometric, expected) < 0f)
                {
                    local = new[] { local[0], local[3], local[2], local[1] };
                    faceUvs = new[] { faceUvs[0], faceUvs[3], faceUvs[2], faceUvs[1] };
                    geometric = -geometric;
                }

                int baseIndex = vertices.Count;
                Vector3 normal = geometric.normalized;
                for (int c = 0; c < 4; c++)
                {
                    vertices.Add(local[c]);
                    normals.Add(normal);
                    uvs.Add(faceUvs[c]);
                }

                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);

                bool twoSided = face == "north" || face == "south" ? flatZ
                    : face == "east" || face == "west" ? flatX
                    : flatY;
                if (!twoSided)
                {
                    continue;
                }

                int backIndex = vertices.Count;
                Vector3 backNormal = -normal;
                for (int c = 0; c < 4; c++)
                {
                    vertices.Add(local[c]);
                    normals.Add(backNormal);
                    uvs.Add(faceUvs[c]);
                }

                triangles.Add(backIndex);
                triangles.Add(backIndex + 2);
                triangles.Add(backIndex + 1);
                triangles.Add(backIndex);
                triangles.Add(backIndex + 3);
                triangles.Add(backIndex + 2);
            }
        }

        bool FaceRect(BedrockCube cube, string face, out Vector4 rect)
        {
            if (cube.BoxUv)
            {
                float u = cube.Uv.x;
                float v = cube.Uv.y;
                // 盒式 UV 的尺寸按 Minecraft 的规则向下取整（和 Blockbench / TLM 一致），
                // 用原始小数会让 UV 岛错位
                float w = Mathf.Floor(cube.Size.x);
                float h = Mathf.Floor(cube.Size.y);
                float d = Mathf.Floor(cube.Size.z);
                // mirror 的方块在 Blockbench 里会把 east / west 两块贴图区域对调
                bool swapSides = cube.Mirror;
                switch (face)
                {
                    case "north":
                        rect = new Vector4(u + d, v + d, w, h);
                        return true;
                    case "south":
                        rect = new Vector4(u + 2f * d + w, v + d, w, h);
                        return true;
                    case "east":
                        rect = swapSides ? new Vector4(u, v + d, d, h) : new Vector4(u + d + w, v + d, d, h);
                        return true;
                    case "west":
                        rect = swapSides ? new Vector4(u + d + w, v + d, d, h) : new Vector4(u, v + d, d, h);
                        return true;
                    case "up":
                        rect = new Vector4(u + d, v, w, d);
                        return true;
                    default:
                        rect = new Vector4(u + d + w, v, w, d);
                        return true;
                }
            }

            BedrockFaceUv entry;
            if (!cube.FaceUvs.TryGetValue(face, out entry))
            {
                rect = Vector4.zero;
                return false;
            }

            // 单独指定 UV 时，基岩的 down 面是从反向角起算的，Blockbench 会把它交换一次
            if (face == "down")
            {
                rect = new Vector4(entry.Uv.x, entry.Uv.y + entry.UvSize.y, entry.UvSize.x, -entry.UvSize.y);
            }
            else
            {
                rect = new Vector4(entry.Uv.x, entry.Uv.y, entry.UvSize.x, entry.UvSize.y);
            }
            return true;
        }

        static Vector3[] FaceCorners(Vector3 min, Vector3 max, string face)
        {
            float x0 = min.x;
            float y0 = min.y;
            float z0 = min.z;
            float x1 = max.x;
            float y1 = max.y;
            float z1 = max.z;
            switch (face)
            {
                case "north":
                    return new[] { new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0) };
                case "south":
                    return new[] { new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1) };
                case "east":
                    return new[] { new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0) };
                case "west":
                    return new[] { new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0) };
                case "up":
                    return new[] { new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1) };
                default:
                    return new[] { new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1) };
            }
        }

        Vector3 ToUnity(Vector3 v)
        {
            return ConvertPosition(v, PixelsPerUnit);
        }

        static Vector3 ToUnityDirection(Vector3 v)
        {
            return ConvertDirection(v);
        }

        Quaternion ToUnityRotation(Vector3 degrees)
        {
            return ComposeRotation(degrees, RotationSigns, RotationOrder);
        }

        /// <summary>基岩坐标（像素）→ Unity 局部坐标。x、z 反向，1 格 = PixelsPerUnit 像素。</summary>
        public static Vector3 ConvertPosition(Vector3 bedrockPoint, float pixelsPerUnit)
        {
            return new Vector3(-bedrockPoint.x, bedrockPoint.y, -bedrockPoint.z) / pixelsPerUnit;
        }

        /// <summary>基岩方向向量 → Unity 方向向量（不缩放）。</summary>
        public static Vector3 ConvertDirection(Vector3 bedrockDirection)
        {
            return new Vector3(-bedrockDirection.x, bedrockDirection.y, -bedrockDirection.z);
        }

        /// <summary>
        /// 基岩欧拉角（度）→ Unity 四元数。默认就是 Blockbench 的约定：不取反 + ZYX。
        /// 模型和动画必须走同一个函数，否则姿势会对不上。
        /// </summary>
        public static Quaternion ComposeRotation(Vector3 degrees, Vector3 signs, BedrockRotationOrder order)
        {
            if (degrees.sqrMagnitude < 1e-10f)
            {
                return Quaternion.identity;
            }

            float x = degrees.x * signs.x;
            float y = degrees.y * signs.y;
            float z = degrees.z * signs.z;
            Quaternion qx = Quaternion.AngleAxis(x, Vector3.right);
            Quaternion qy = Quaternion.AngleAxis(y, Vector3.up);
            Quaternion qz = Quaternion.AngleAxis(z, Vector3.forward);

            switch (order)
            {
                case BedrockRotationOrder.XYZ:
                    return qx * qy * qz;
                case BedrockRotationOrder.XZY:
                    return qx * qz * qy;
                case BedrockRotationOrder.YXZ:
                    return qy * qx * qz;
                case BedrockRotationOrder.YZX:
                    return qy * qz * qx;
                case BedrockRotationOrder.ZXY:
                    return qz * qx * qy;
                default:
                    return qz * qy * qx;
            }
        }

        /// <summary>模型和缓存里重建出来的模型共用同一个材质做法：MC 方块着色器（AlphaTest + 逐面亮度）+ 点采样贴图。</summary>
        public static Material CreateMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find("MaidHome/MinecraftBlock");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent Cutout");
            }
            if (shader == null)
            {
                Debug.LogError("找不到可用的方块着色器，模型材质没建出来");
                return null;
            }

            Material material = new Material(shader);
            material.name = texture != null ? texture.name + "_mat" : "bedrock_mat";
            material.mainTexture = texture;
            if (shader.name == "MaidHome/MinecraftBlock")
            {
                material.SetFloat("_Cutoff", 0.5f);
            }
            else if (shader.name == "Standard")
            {
                material.SetFloat("_Mode", 1f);
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.SetFloat("_Cutoff", 0.5f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 2450;
            }

            return material;
        }
    }
}
