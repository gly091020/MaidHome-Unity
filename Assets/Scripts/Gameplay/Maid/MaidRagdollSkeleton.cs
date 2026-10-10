using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 一次"解析女仆模型"要用到的全部几何特征：可见骨骼、层级、每根骨骼自己网格的包围盒、
    /// 子树包围盒/体积。识别和自动分段都吃这份数据，避免两边各算一遍。
    ///
    /// 被藏起来的节点（MaidAssetLoader 关掉的 FOX / blink 那些）整棵子树都不进来。
    /// </summary>
    public sealed class MaidRagdollSkeleton
    {
        public readonly Transform Root;
        public readonly List<Transform> Bones = new List<Transform>();
        public readonly Dictionary<Transform, List<Transform>> Children =
            new Dictionary<Transform, List<Transform>>();
        public readonly Dictionary<Transform, Transform> Parents = new Dictionary<Transform, Transform>();
        public readonly Dictionary<Transform, int> Depths = new Dictionary<Transform, int>();
        /// <summary>骨骼自己网格的包围盒，换算到模型根局部空间。</summary>
        public readonly Dictionary<Transform, Bounds> Own = new Dictionary<Transform, Bounds>();
        /// <summary>骨骼整棵子树（含自己）的包围盒 / 体积和，同样是模型根局部空间。</summary>
        public readonly Dictionary<Transform, Bounds> SubtreeBounds = new Dictionary<Transform, Bounds>();
        public readonly Dictionary<Transform, float> SubtreeVolume = new Dictionary<Transform, float>();
        public readonly float TotalVolume;

        readonly Dictionary<Transform, List<Transform>> _subtreeCache =
            new Dictionary<Transform, List<Transform>>();

        public static MaidRagdollSkeleton Build(Transform root)
        {
            return root == null ? null : new MaidRagdollSkeleton(root);
        }

        MaidRagdollSkeleton(Transform root)
        {
            Root = root;
            Collect(root, Bones);
            for (int i = 0; i < Bones.Count; i++)
            {
                Children[Bones[i]] = new List<Transform>();
                Depths[Bones[i]] = Depth(Bones[i]);
            }

            for (int i = 0; i < Bones.Count; i++)
            {
                Transform parent = Bones[i].parent;
                List<Transform> list;
                if (parent != null && Children.TryGetValue(parent, out list))
                {
                    list.Add(Bones[i]);
                    Parents[Bones[i]] = parent;
                }
            }

            for (int i = 0; i < Bones.Count; i++)
            {
                Bounds bounds;
                if (OwnBounds(Bones[i], out bounds))
                {
                    Own[Bones[i]] = bounds;
                }
            }

            for (int i = 0; i < Bones.Count; i++)
            {
                ComputeSubtree(Bones[i]);
            }

            foreach (KeyValuePair<Transform, Bounds> pair in Own)
            {
                TotalVolume += Mathf.Abs(pair.Value.size.x * pair.Value.size.y * pair.Value.size.z);
            }
        }

        // ------------------------------------------------------------ 查询

        public bool HasGeometry(Transform bone)
        {
            return SubtreeVolume.ContainsKey(bone) && SubtreeVolume[bone] > 0f;
        }

        public float Value(Transform bone)
        {
            float volume;
            return SubtreeVolume.TryGetValue(bone, out volume) ? volume : 0f;
        }

        public bool BoxOf(Transform bone, out Bounds box)
        {
            return Own.TryGetValue(bone, out box);
        }

        public Bounds Subtree(Transform bone)
        {
            Bounds box;
            return SubtreeBounds.TryGetValue(bone, out box) ? box : new Bounds();
        }

        public List<Transform> SubtreeBones(Transform bone)
        {
            List<Transform> cached;
            if (_subtreeCache.TryGetValue(bone, out cached))
            {
                return cached;
            }

            List<Transform> list = new List<Transform>();
            GatherSubtree(bone, list);
            _subtreeCache[bone] = list;
            return list;
        }

        public bool IsDescendant(Transform node, Transform ancestor)
        {
            Transform cursor = node.parent;
            while (cursor != null)
            {
                if (cursor == ancestor)
                {
                    return true;
                }

                cursor = cursor.parent;
            }

            return false;
        }

        /// <summary>在模型根局部空间里的位置。</summary>
        public Vector3 RootPosition(Transform bone)
        {
            return Root.InverseTransformPoint(bone.position);
        }

        /// <summary>穿过"没有自己几何、只有一个孩子"的骨架节点，落到真正有内容的骨骼上。</summary>
        public Transform Representative(Transform bone)
        {
            Transform cursor = bone;
            for (int i = 0; i < 64; i++)
            {
                if (Own.ContainsKey(cursor) || ChildCount(cursor) != 1)
                {
                    break;
                }

                cursor = Children[cursor][0];
            }

            return cursor;
        }

        /// <summary>
        /// 主干链：往下走"独生子"（或者空骨架里独占绝大部分体积的那个孩子）。
        /// 一条尾巴、一只胳膊在层级上就是一条链，属于同一块。
        /// </summary>
        public List<Transform> Chain(Transform bone, float dominant)
        {
            List<Transform> chain = new List<Transform> { bone };
            Transform cursor = bone;
            for (int i = 0; i < 24; i++)
            {
                List<Transform> kids = Children[cursor];
                if (kids.Count == 0)
                {
                    break;
                }

                Transform next;
                if (kids.Count == 1)
                {
                    next = kids[0];
                }
                else if (!Own.ContainsKey(cursor))
                {
                    Transform best = kids[0];
                    for (int k = 1; k < kids.Count; k++)
                    {
                        if (Value(kids[k]) > Value(best))
                        {
                            best = kids[k];
                        }
                    }

                    if (Value(best) < Mathf.Max(Value(cursor), 1e-9f) * dominant)
                    {
                        break;
                    }

                    next = best;
                }
                else
                {
                    break;
                }

                if (chain.Contains(next))
                {
                    break;
                }

                chain.Add(next);
                cursor = next;
            }

            return chain;
        }

        /// <summary>把根局部空间里的包围盒换算成骨骼局部空间（八角逐点变换）。</summary>
        public void LocalFromRoot(Transform bone, Bounds box, out Vector3 center, out Vector3 size)
        {
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = box.center + Vector3.Scale(box.size * 0.5f,
                            new Vector3(x * 2 - 1, y * 2 - 1, z * 2 - 1));
                        Vector3 local = bone.InverseTransformPoint(Root.TransformPoint(corner));
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                    }
                }
            }

            center = (min + max) * 0.5f;
            size = max - min;
        }

        public int ChildCount(Transform bone)
        {
            List<Transform> list;
            return Children.TryGetValue(bone, out list) ? list.Count : 0;
        }

        public List<Transform> ChildList(Transform bone)
        {
            List<Transform> list;
            return Children.TryGetValue(bone, out list) ? list : new List<Transform>();
        }

        // ------------------------------------------------------------ 名字工具

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            char[] buffer = new char[name.Length];
            int count = 0;
            for (int i = 0; i < name.Length; i++)
            {
                char c = char.ToLowerInvariant(name[i]);
                if (char.IsLetterOrDigit(c))
                {
                    buffer[count++] = c;
                }
            }

            return new string(buffer, 0, count);
        }

        public static bool IsSkipped(string name)
        {
            return Normalize(name).IndexOf("locator", StringComparison.Ordinal) >= 0;
        }

        // ------------------------------------------------------------ 内部

        void Collect(Transform node, List<Transform> into)
        {
            // activeInHierarchy 会把 MaidAssetLoader 藏起来的 FOX / blink 整棵子树跳过
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

        int Depth(Transform bone)
        {
            int depth = 0;
            Transform cursor = bone;
            while (cursor != null && cursor != Root)
            {
                depth++;
                cursor = cursor.parent;
            }

            return depth;
        }

        void GatherSubtree(Transform node, List<Transform> into)
        {
            into.Add(node);
            List<Transform> list;
            if (!Children.TryGetValue(node, out list))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                GatherSubtree(list[i], into);
            }
        }

        void ComputeSubtree(Transform node)
        {
            Bounds bounds = new Bounds();
            float volume = 0f;
            bool any = false;
            Bounds own;
            if (Own.TryGetValue(node, out own))
            {
                bounds = own;
                volume = Mathf.Abs(own.size.x * own.size.y * own.size.z);
                any = true;
            }

            List<Transform> kids;
            if (Children.TryGetValue(node, out kids))
            {
                for (int i = 0; i < kids.Count; i++)
                {
                    ComputeSubtree(kids[i]);
                    if (SubtreeVolume[kids[i]] <= 0f)
                    {
                        continue;
                    }

                    if (any)
                    {
                        bounds.Encapsulate(SubtreeBounds[kids[i]]);
                    }
                    else
                    {
                        bounds = SubtreeBounds[kids[i]];
                        any = true;
                    }

                    volume += SubtreeVolume[kids[i]];
                }
            }

            SubtreeBounds[node] = bounds;
            SubtreeVolume[node] = volume;
        }

        bool OwnBounds(Transform bone, out Bounds bounds)
        {
            bounds = new Bounds();
            Mesh mesh = MeshOf(bone);
            if (mesh == null)
            {
                return false;
            }

            Bounds local = mesh.bounds;
            bool any = false;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = local.center + Vector3.Scale(local.size * 0.5f,
                            new Vector3(x * 2 - 1, y * 2 - 1, z * 2 - 1));
                        Vector3 point = Root.InverseTransformPoint(bone.TransformPoint(corner));
                        if (any)
                        {
                            bounds.Encapsulate(point);
                        }
                        else
                        {
                            bounds = new Bounds(point, Vector3.zero);
                            any = true;
                        }
                    }
                }
            }

            return any;
        }

        static Mesh MeshOf(Transform bone)
        {
            MeshFilter filter = bone.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
