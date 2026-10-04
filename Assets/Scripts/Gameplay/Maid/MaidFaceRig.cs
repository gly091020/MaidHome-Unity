using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆脸部骨骼索引：头 + 左右耳 + 眼睛。名字规则照抄 moreanimation
    /// （FaceHitProjection.role / FaceInteractionState.earSide），
    /// 被隐藏的骨骼（FOX 底下那只狐狸的头和耳朵）自动跳过。
    /// </summary>
    public sealed class MaidFaceRig
    {
        // 优先顺序照抄 moreanimation：Head / MHead / AllHead，最后才轮到狐狸那套 Head2
        static readonly string[] HeadNames = { "head", "mhead", "allhead", "head2" };

        static readonly string[] LeftEyeNames =
        {
            "lefteyepublic", "lefteyelidbase", "lefteyelid", "lefteye", "leye", "eyeleft"
        };

        static readonly string[] RightEyeNames =
        {
            "righteyepublic", "righteyelidbase", "righteyelid", "righteye", "reye", "eyeright"
        };

        public Transform Head;
        /// <summary>头这一块的可见几何，屏幕命中判定拿它的包围盒用</summary>
        public Renderer HeadMesh;
        public readonly List<Transform> LeftEar = new List<Transform>();
        public readonly List<Transform> RightEar = new List<Transform>();
        public Transform LeftEye;
        public Transform RightEye;

        /// <summary>
        /// 没有眼睛骨骼的模型（方块模型 / SimpleBedrockModel）按正脸 UV 推出来的眼睛锚点，
        /// 两个都存成 Head 的局部坐标（头会动、会转，得每帧换算到世界）。
        /// </summary>
        public bool HasEyePoints;
        public Vector3 LeftEyePoint;
        public Vector3 RightEyePoint;

        /// <summary>
        /// 方块模型的正脸四个角（顺序：左上/右上/右下/左下），同样是 Head 的局部坐标。
        /// 脸部判定区要用**脸**的框，不能用整块头部网格的包围盒——那个里面还挂着头发和侧板，框会大一圈。
        /// </summary>
        public Vector3[] FaceCorners;

        public bool Supports
        {
            get { return Head != null; }
        }

        /// <summary>不算眼睛锚点（只要头的位置时用，例如喂蛋糕的头部判定盒）</summary>
        public static MaidFaceRig Build(Transform root)
        {
            return Build(root, Vector2.zero, Vector2.zero, 0f);
        }

        /// <summary>
        /// eyeUvMin / eyeUvSize 是方块模型眼睛在**正脸局部 UV**里的位置（0 基像素，facePixels = 正脸像素边长）。
        /// 这类模型没有眼睛骨骼，眼睛只画在正脸贴图上，只能按这个约定把锚点算出来；facePixels 传 0 就不算。
        /// </summary>
        public static MaidFaceRig Build(Transform root, Vector2 eyeUvMin, Vector2 eyeUvSize, float facePixels)
        {
            MaidFaceRig rig = new MaidFaceRig();
            if (root == null)
            {
                return rig;
            }

            List<Transform> all = new List<Transform>();
            Collect(root, all);

            rig.Head = PickHead(all, out rig.HeadMesh);
            if (rig.Head == null)
            {
                return rig;
            }

            CollectEar(all, rig.Head, true, rig.LeftEar);
            CollectEar(all, rig.Head, false, rig.RightEar);
            // 有的模型耳朵就叫 ear / ear1 / ear2，只能按头的左右位置分边
            if (rig.LeftEar.Count == 0 && rig.RightEar.Count == 0)
            {
                CollectPlainEar(all, rig);
            }

            rig.LeftEye = PickFirst(all, rig.Head, LeftEyeNames);
            rig.RightEye = PickFirst(all, rig.Head, RightEyeNames);
            if ((rig.LeftEye == null || rig.RightEye == null) && facePixels > 0.001f)
            {
                rig.BuildEyePoints(eyeUvMin, eyeUvSize, facePixels);
            }

            return rig;
        }

        /// <summary>
        /// 方块模型的眼睛没有骨骼，只在正脸贴图上画着。做法：在头这块几何里找**法线朝 +Z、面积最大**的
        /// 那个三角形 —— 脸、内外两层贴片、头发侧板都在同一块网格里，但脸是最大那块 —— 再用它三个顶点
        /// 把「脸部 UV ↔ 局部 x/y」解成线性的（矩形上本来就是线性的，三个点就够，也就不用去猜贴图朝向），
        /// 最后把眼睛矩形的中心换算成头局部坐标。右眼按脸的中轴镜像（u' = facePixels - u）。
        /// </summary>
        void BuildEyePoints(Vector2 eyeUvMin, Vector2 eyeUvSize, float facePixels)
        {
            MeshFilter filter = HeadMesh != null ? HeadMesh.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || Head == null)
            {
                return;
            }

            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            if (verts.Length == 0 || uvs.Length != verts.Length || normals.Length != verts.Length
                || triangles.Length < 3)
            {
                return;
            }

            int a = -1;
            int b = -1;
            int c = -1;
            float best = 0f;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length)
                {
                    continue;
                }

                if (normals[i0].z < 0.9f || normals[i1].z < 0.9f || normals[i2].z < 0.9f)
                {
                    continue;
                }

                float area = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).magnitude;
                if (area <= best)
                {
                    continue;
                }

                best = area;
                a = i0;
                b = i1;
                c = i2;
            }

            float duDx;
            float dvDy;
            if (a < 0 || !TryFaceSlopes(verts, uvs, a, b, c, out duDx, out dvDy))
            {
                return;
            }

            float minUvX = Mathf.Min(uvs[a].x, Mathf.Min(uvs[b].x, uvs[c].x));
            float maxUvX = Mathf.Max(uvs[a].x, Mathf.Max(uvs[b].x, uvs[c].x));
            float minUvY = Mathf.Min(uvs[a].y, Mathf.Min(uvs[b].y, uvs[c].y));
            float maxUvY = Mathf.Max(uvs[a].y, Mathf.Max(uvs[b].y, uvs[c].y));

            float face = Mathf.Max(1f, facePixels);
            float sizeU = Mathf.Clamp(eyeUvSize.x, 0.01f, face);
            float sizeV = Mathf.Clamp(eyeUvSize.y, 0.01f, face);
            float centerU = Mathf.Clamp(eyeUvMin.x, 0f, face - sizeU) + sizeU * 0.5f;
            float centerV = Mathf.Clamp(eyeUvMin.y, 0f, face - sizeV) + sizeV * 0.5f;
            float planeZ = Mathf.Max(verts[a].z, Mathf.Max(verts[b].z, verts[c].z));

            // 正脸那四个角：三个顶点就能确定矩形的 x/y 范围
            float minX = Mathf.Min(verts[a].x, Mathf.Min(verts[b].x, verts[c].x));
            float maxX = Mathf.Max(verts[a].x, Mathf.Max(verts[b].x, verts[c].x));
            float minY = Mathf.Min(verts[a].y, Mathf.Min(verts[b].y, verts[c].y));
            float maxY = Mathf.Max(verts[a].y, Mathf.Max(verts[b].y, verts[c].y));
            FaceCorners = new[]
            {
                HeadLocalFromMeshLocal(new Vector3(minX, maxY, planeZ)),
                HeadLocalFromMeshLocal(new Vector3(maxX, maxY, planeZ)),
                HeadLocalFromMeshLocal(new Vector3(maxX, minY, planeZ)),
                HeadLocalFromMeshLocal(new Vector3(minX, minY, planeZ)),
            };

            HasEyePoints = true;
            LeftEyePoint = ToHeadLocal(verts[a], uvs[a], duDx, dvDy, planeZ,
                FaceUv(centerU, centerV, face, minUvX, maxUvX, minUvY, maxUvY));
            RightEyePoint = ToHeadLocal(verts[a], uvs[a], duDx, dvDy, planeZ,
                FaceUv(face - centerU, centerV, face, minUvX, maxUvX, minUvY, maxUvY));
        }

        /// 脸部像素坐标 → 网格 UV：贴图左上角 = u 最小、v 最大（Unity 的 v 朝上）
        static Vector2 FaceUv(float pixelU, float pixelV, float facePixels,
            float minUvX, float maxUvX, float minUvY, float maxUvY)
        {
            return new Vector2(
                Mathf.Lerp(minUvX, maxUvX, pixelU / facePixels),
                Mathf.Lerp(maxUvY, minUvY, pixelV / facePixels));
        }

        /// 网格 UV → 头局部坐标：矩形上 UV 和局部 x/y 是线性的，用三个顶点解出来的斜率直接换算
        Vector3 ToHeadLocal(Vector3 reference, Vector2 referenceUv, float duDx, float dvDy,
            float planeZ, Vector2 uv)
        {
            Vector3 local = new Vector3(
                reference.x + (uv.x - referenceUv.x) / duDx,
                reference.y + (uv.y - referenceUv.y) / dvDy,
                planeZ);
            return HeadLocalFromMeshLocal(local);
        }

        /// 头这一块的几何可能挂在子骨骼上（HeadMesh 是往下找出来的），所以要经它的 transform 绕一圈
        Vector3 HeadLocalFromMeshLocal(Vector3 meshLocal)
        {
            return Head.InverseTransformPoint(HeadMesh.transform.TransformPoint(meshLocal));
        }

        /// 三个顶点在矩形上是仿射的：挑一对 x 不同的解 du/dx，挑一对 y 不同的解 dv/dy
        static bool TryFaceSlopes(Vector3[] verts, Vector2[] uvs, int a, int b, int c,
            out float duDx, out float dvDy)
        {
            duDx = Slope(verts, uvs, a, b, true);
            if (Mathf.Abs(duDx) < 0.000001f)
            {
                duDx = Slope(verts, uvs, a, c, true);
            }

            if (Mathf.Abs(duDx) < 0.000001f)
            {
                duDx = Slope(verts, uvs, b, c, true);
            }

            dvDy = Slope(verts, uvs, a, b, false);
            if (Mathf.Abs(dvDy) < 0.000001f)
            {
                dvDy = Slope(verts, uvs, a, c, false);
            }

            if (Mathf.Abs(dvDy) < 0.000001f)
            {
                dvDy = Slope(verts, uvs, b, c, false);
            }

            return Mathf.Abs(duDx) > 0.000001f && Mathf.Abs(dvDy) > 0.000001f;
        }

        static float Slope(Vector3[] verts, Vector2[] uvs, int i, int j, bool horizontal)
        {
            float delta = horizontal ? verts[j].x - verts[i].x : verts[j].y - verts[i].y;
            if (Mathf.Abs(delta) < 0.00001f)
            {
                return 0f;
            }

            float uv = horizontal ? uvs[j].x - uvs[i].x : uvs[j].y - uvs[i].y;
            return uv / delta;
        }

        /// <summary>
        /// 耳朵被拉长时往哪个局部轴缩放：挑"几何最长、而且在画面上看得见"的那个轴。
        /// 模组是按骨骼旋转推的（FaceInteractionMath.longitudinalAxis），但正交相机下
        /// 沿视线拉长根本看不出来，所以这里改成按网格尺寸 × 画面投影来挑。
        /// </summary>
        public static int StretchAxis(Transform bone, Camera camera)
        {
            MeshFilter filter = bone.GetComponent<MeshFilter>();
            Vector3 size = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds.size
                : Vector3.one;
            Vector3 view = camera != null ? camera.transform.forward : Vector3.forward;
            float best = -1f;
            int axis = 0;
            for (int i = 0; i < 3; i++)
            {
                Vector3 direction = bone.TransformDirection(AxisVector(i));
                Vector3 onScreen = direction - Vector3.Dot(direction, view) * view;
                float score = Mathf.Abs(size[i]) * onScreen.magnitude;
                if (score > best)
                {
                    best = score;
                    axis = i;
                }
            }

            return axis;
        }

        static Vector3 AxisVector(int axis)
        {
            if (axis == 0)
            {
                return Vector3.right;
            }

            return axis == 1 ? Vector3.up : Vector3.forward;
        }

        static void Collect(Transform node, List<Transform> into)
        {
            if (node == null || !node.gameObject.activeInHierarchy)
            {
                return;
            }

            into.Add(node);
            for (int i = 0; i < node.childCount; i++)
            {
                Collect(node.GetChild(i), into);
            }
        }

        static Transform PickHead(List<Transform> all, out Renderer mesh)
        {
            mesh = null;
            Transform fallback = null;
            for (int n = 0; n < HeadNames.Length; n++)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (Normalize(all[i].name) != HeadNames[n])
                    {
                        continue;
                    }

                    if (fallback == null)
                    {
                        fallback = all[i];
                    }

                    Renderer renderer = all[i].GetComponent<Renderer>();
                    if (renderer != null && renderer.enabled)
                    {
                        mesh = renderer;
                        return all[i];
                    }
                }
            }

            // 头骨骼自己没有方块（几何在子骨骼上）时，往下找一个
            if (fallback != null)
            {
                mesh = FindMesh(fallback);
            }

            return fallback;
        }

        static Renderer FindMesh(Transform node)
        {
            if (node == null || !node.gameObject.activeInHierarchy)
            {
                return null;
            }

            Renderer renderer = node.GetComponent<Renderer>();
            if (renderer != null && renderer.enabled)
            {
                return renderer;
            }

            for (int i = 0; i < node.childCount; i++)
            {
                Renderer found = FindMesh(node.GetChild(i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static Transform PickFirst(List<Transform> all, Transform head, string[] names)
        {
            for (int n = 0; n < names.Length; n++)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (Normalize(all[i].name) == names[n] && IsUnder(all[i], head))
                    {
                        return all[i];
                    }
                }
            }

            return null;
        }

        static void CollectEar(List<Transform> all, Transform head, bool left, List<Transform> into)
        {
            for (int i = 0; i < all.Count; i++)
            {
                Transform node = all[i];
                if (!IsEar(node.name, left) || !IsUnder(node, head))
                {
                    continue;
                }

                // 只从这条耳朵链的最上面那根开始（左_ear 挂在 Ear 下面）
                if (node.parent != null && IsEar(node.parent.name, left) && IsUnder(node.parent, head))
                {
                    continue;
                }

                into.Add(node);
                CollectEarChildren(node, left, into);
            }
        }

        static void CollectEarChildren(Transform node, bool left, List<Transform> into)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (!child.gameObject.activeInHierarchy || !IsEar(child.name, left))
                {
                    continue;
                }

                into.Add(child);
                CollectEarChildren(child, left, into);
            }
        }

        static void CollectPlainEar(List<Transform> all, MaidFaceRig rig)
        {
            List<Transform> plain = new List<Transform>();
            for (int i = 0; i < all.Count; i++)
            {
                Transform node = all[i];
                if (!IsUnder(node, rig.Head) || !IsPlainEar(node.name))
                {
                    continue;
                }

                if (node.parent != null && IsPlainEar(node.parent.name) && IsUnder(node.parent, rig.Head))
                {
                    continue;
                }

                plain.Add(node);
            }

            if (plain.Count == 0)
            {
                return;
            }

            if (plain.Count == 1)
            {
                rig.LeftEar.Add(plain[0]);
                return;
            }

            plain.Sort((a, b) => rig.Head.InverseTransformPoint(a.position).x
                .CompareTo(rig.Head.InverseTransformPoint(b.position).x));
            rig.LeftEar.Add(plain[0]);
            rig.RightEar.Add(plain[plain.Count - 1]);
            Debug.LogWarning("耳朵骨骼没有左右命名，按头部左右位置分成两半：" + plain[0].name
                + " / " + plain[plain.Count - 1].name);
        }

        static bool IsEar(string name, bool left)
        {
            string n = Normalize(name);
            if (n.Length == 0 || n.Contains("earring") || n.Contains("earphone"))
            {
                return false;
            }

            // 排除拿着的东西和饰品
            if (n.Contains("hand") || n.Contains("arm") || n.Contains("held") || n.Contains("item"))
            {
                return false;
            }

            if (!n.Contains("ear"))
            {
                return false;
            }

            bool isLeft = n.Contains("left") || n.StartsWith("lear") || n.StartsWith("earleft");
            bool isRight = n.Contains("right") || n.StartsWith("rear") || n.StartsWith("earright");
            return left ? isLeft : isRight;
        }

        static bool IsPlainEar(string name)
        {
            string n = Normalize(name);
            if (n.Length == 0 || !n.Contains("ear") || n.Contains("earring"))
            {
                return false;
            }

            if (n.Contains("left") || n.Contains("right") || n.StartsWith("lear") || n.StartsWith("rear"))
            {
                return false;
            }

            return n.StartsWith("ear");
        }

        static bool IsUnder(Transform node, Transform ancestor)
        {
            Transform current = node;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            return name.ToLowerInvariant()
                .Replace("_", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace(" ", "");
        }
    }
}
