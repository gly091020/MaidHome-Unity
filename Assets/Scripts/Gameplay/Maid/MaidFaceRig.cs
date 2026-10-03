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

        public bool Supports
        {
            get { return Head != null; }
        }

        public static MaidFaceRig Build(Transform root)
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
            return rig;
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
