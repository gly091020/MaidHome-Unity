using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 唯一的生成方案（网格 + 合并）：
    ///
    /// 1. **先认部位**：按骨骼名认出 头 / 身体 / 左右手 / 左右脚 / 裙子 / 尾巴
    ///    （名字规则照抄原来的六个部位那套：精确匹配优先于包含匹配，层级浅优先）；
    /// 2. **识别到的部位把子树里的网格合成一块** —— "头 + 脸 + 头发 + 耳朵"是一块，
    ///    "裙子 + 各片裙摆"是一块；刚体挂在部位根骨骼上，各块网格的凸网格碰撞体挂在
    ///    各自骨骼上（属于同一根刚体，不用重新烘网格）；
    /// 3. 没识别到的网格各自一块（等于原来的逐 MeshRenderer 方案）；
    /// 4. 关节按骨架层级连（头发的上一节就是上一段头发），父子真的相交时锚点放在相交区域中心。
    /// </summary>
    public static class MaidRagdollPlanner
    {
        /// <summary>能"合成一块"的部位 + 名字规则。越靠前的别名优先级越高。</summary>
        static readonly string[][] PartNames =
        {
            new[] { "head", "mhead", "allhead" },
            new[] { "body", "upbody", "upperbody", "allbody", "torso", "chest", "spine", "waist" },
            new[] { "skirt", "clothe", "clothes", "dress", "robe" },
            new[] { "tail" },
            new[] { "armleft", "leftarm", "larm", "arml" },
            new[] { "armright", "rightarm", "rarm", "armr" },
            new[] { "legleft", "leftleg", "lleg", "legl" },
            new[] { "legright", "rightleg", "rleg", "legr" },
        };

        /// <summary>报告里用的部位名，和 PartNames 一一对应。</summary>
        static readonly string[] PartLabels =
        {
            "head", "body", "skirt", "tail", "armLeft", "armRight", "legLeft", "legRight",
        };

        /// <summary>
        /// 哪些部位要把子树里的网格合成一块。只有头 / 身体 / 裙子合并：
        /// 手、脚、尾巴这几条长链要一段一段各自成块（手肘、膝盖、尾巴各节都能弯），
        /// 但它们仍然是"识别到的部位"，用来划边界 —— 胳膊里的网格不会被并进身体。
        /// </summary>
        static readonly bool[] PartMerges =
        {
            true,   // head
            true,   // body
            true,   // skirt
            false,  // tail
            false,  // armLeft
            false,  // armRight
            false,  // legLeft
            false,  // legRight
        };

        public static MaidRagdollPlan Plan(Transform root)
        {
            MaidRagdollPlan plan = new MaidRagdollPlan();
            plan.Root = root;
            MaidRagdollSkeleton skeleton = MaidRagdollSkeleton.Build(root);
            if (skeleton == null)
            {
                plan.Warnings.Add("模型根节点是空的");
                return plan;
            }

            List<Transform> roots = PickPartRoots(skeleton, out List<bool> merges);
            BuildParts(skeleton, roots, merges, plan);
            LinkParts(skeleton, plan);
            return plan;
        }

        // ------------------------------------------------------------ 认部位

        /// <summary>挑出识别到的部位根骨骼；merges 与之一一对应，标记这个部位要不要合并子树。</summary>
        static List<Transform> PickPartRoots(MaidRagdollSkeleton skeleton, out List<bool> merges)
        {
            List<Transform> picked = new List<Transform>();
            merges = new List<bool>();
            Transform body = null;

            for (int group = 0; group < PartNames.Length; group++)
            {
                Transform best = null;
                int bestFuzzy = 0;
                int bestAlias = 0;
                int bestDepth = 0;
                int bestCubes = 0;
                for (int i = 0; i < skeleton.Bones.Count; i++)
                {
                    Transform bone = skeleton.Bones[i];
                    if (!skeleton.HasGeometry(bone) || MaidRagdollSkeleton.IsSkipped(bone.name)
                        || picked.Contains(bone))
                    {
                        continue;
                    }

                    // 已经是别的部位的后代就不再参选（身体除外：手脚都是身体的后代）
                    if (UnderOtherPart(skeleton, picked, body, bone))
                    {
                        continue;
                    }

                    int fuzzy;
                    int alias;
                    if (!MatchPreference(MaidRagdollSkeleton.Normalize(bone.name), PartNames[group],
                            out fuzzy, out alias))
                    {
                        continue;
                    }

                    int cubes = MeshCount(bone);
                    if (best == null || Better(fuzzy, alias, skeleton.Depths[bone], -cubes,
                            bestFuzzy, bestAlias, bestDepth, bestCubes))
                    {
                        best = bone;
                        bestFuzzy = fuzzy;
                        bestAlias = alias;
                        bestDepth = skeleton.Depths[bone];
                        bestCubes = -cubes;
                    }
                }

                if (best == null)
                {
                    continue;
                }

                picked.Add(best);
                merges.Add(PartMerges[group]);
                if (PartLabels[group] == "body")
                {
                    body = best;
                }
            }

            return picked;
        }

        static bool UnderOtherPart(MaidRagdollSkeleton skeleton, List<Transform> picked, Transform body,
            Transform bone)
        {
            for (int i = 0; i < picked.Count; i++)
            {
                if (picked[i] != body && skeleton.IsDescendant(bone, picked[i]))
                {
                    return true;
                }
            }

            return false;
        }

        static bool MatchPreference(string normalized, string[] aliases, out int fuzzy, out int index)
        {
            for (int i = 0; i < aliases.Length; i++)
            {
                if (string.Equals(normalized, aliases[i], StringComparison.Ordinal))
                {
                    fuzzy = 0;
                    index = i;
                    return true;
                }
            }

            for (int i = 0; i < aliases.Length; i++)
            {
                if (normalized.IndexOf(aliases[i], StringComparison.Ordinal) >= 0)
                {
                    fuzzy = 1;
                    index = i;
                    return true;
                }
            }

            fuzzy = 0;
            index = 0;
            return false;
        }

        static bool Better(int fuzzy, int alias, int depth, int cubes,
            int bestFuzzy, int bestAlias, int bestDepth, int bestCubes)
        {
            if (fuzzy != bestFuzzy)
            {
                return fuzzy < bestFuzzy;
            }

            if (alias != bestAlias)
            {
                return alias < bestAlias;
            }

            if (depth != bestDepth)
            {
                return depth < bestDepth;
            }

            return cubes < bestCubes;
        }

        // ------------------------------------------------------------ 分块

        static void BuildParts(MaidRagdollSkeleton skeleton, List<Transform> roots, List<bool> merges,
            MaidRagdollPlan plan)
        {
            // 每块网格归到"最近的部位根祖先"（含自己）：
            // * 这个祖先是"合并型"部位（头/身体/裙子）→ 并进这块；
            // * 是"分段型"部位（手/脚/尾巴）→ 不并，各自一块（手肘膝盖尾巴各节都能弯）；
            // * 没有部位祖先 → 自己一块。
            List<Transform> buckets = new List<Transform>();
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                Transform bone = skeleton.Bones[i];
                if (!skeleton.BoxOf(bone, out _))
                {
                    continue;
                }

                Transform owner = FindOwner(skeleton, roots, merges, bone);
                if (!buckets.Contains(owner))
                {
                    buckets.Add(owner);
                }
            }

            // 合并型部位在前（按 PartNames 顺序），单块网格在后
            buckets.Sort(delegate (Transform a, Transform b)
            {
                int indexA = IsMergeBucket(roots, merges, a) ? roots.IndexOf(a) : -1;
                int indexB = IsMergeBucket(roots, merges, b) ? roots.IndexOf(b) : -1;
                if (indexA >= 0 && indexB >= 0)
                {
                    return indexA.CompareTo(indexB);
                }

                if (indexA >= 0)
                {
                    return -1;
                }

                return indexB >= 0 ? 1 : string.CompareOrdinal(a.name, b.name);
            });

            for (int i = 0; i < buckets.Count; i++)
            {
                Transform owner = buckets[i];
                MaidRagdollPart part = new MaidRagdollPart();
                part.Index = plan.Parts.Count;
                part.Bone = owner;
                part.BoneName = owner.name;
                int rootIndex = roots.IndexOf(owner);
                bool recognized = rootIndex >= 0;
                part.Label = recognized ? PartLabels[rootIndex] : owner.name;
                part.Matched = recognized ? PartLabels[rootIndex] : "";
                part.MeshCount = 0;

                Bounds bounds = new Bounds();
                bool any = false;
                for (int b = 0; b < skeleton.Bones.Count; b++)
                {
                    Transform bone = skeleton.Bones[b];
                    if (FindOwner(skeleton, roots, merges, bone) != owner || !skeleton.BoxOf(bone, out Bounds box))
                    {
                        continue;
                    }

                    AddCollider(skeleton, part, bone, box);
                    part.MeshCount++;
                    if (any)
                    {
                        bounds.Encapsulate(box);
                    }
                    else
                    {
                        bounds = box;
                        any = true;
                    }
                }

                part.RootBounds = any ? bounds : new Bounds();
                plan.Parts.Add(part);
            }

            int merged = 0;
            for (int i = 0; i < plan.Parts.Count; i++)
            {
                if (plan.Parts[i].MeshCount > 1)
                {
                    merged++;
                }
            }

            plan.Warnings.Add("网格方案：" + plan.Parts.Count + " 块物理块（" + merged
                + " 块是合并的部位：头/身体/裙子；手、脚、尾巴按段各自成块）");
        }

        /// <summary>这块是不是"合并型"部位（头/身体/裙子）。</summary>
        static bool IsMergeBucket(List<Transform> roots, List<bool> merges, Transform bucket)
        {
            int index = roots.IndexOf(bucket);
            return index >= 0 && merges[index];
        }

        /// <summary>
        /// 一块网格归谁：往上找最近的部位根。
        /// 命中合并型部位 → 并进它；命中分段型部位（手/脚/尾巴）或没有部位祖先 → 自己一块。
        /// </summary>
        static Transform FindOwner(MaidRagdollSkeleton skeleton, List<Transform> roots, List<bool> merges,
            Transform bone)
        {
            Transform cursor = bone;
            while (cursor != null)
            {
                int index = roots.IndexOf(cursor);
                if (index >= 0)
                {
                    return merges[index] ? cursor : bone;
                }

                cursor = cursor.parent;
            }

            return bone;
        }

        static void AddCollider(MaidRagdollSkeleton skeleton, MaidRagdollPart part, Transform bone, Bounds box)
        {
            MaidRagdollCollider collider = new MaidRagdollCollider();
            collider.Bone = bone;
            MeshFilter filter = bone.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null && mesh.triangles.Length / 3 <= MaxConvexTriangles)
            {
                collider.Mesh = mesh;
            }
            else
            {
                // 三角面太多（凸包上限 255）：退回这块网格自己的包围盒
                Vector3 center;
                Vector3 size;
                skeleton.LocalFromRoot(bone, box, out center, out size);
                collider.Center = center;
                collider.Size = new Vector3(
                    Mathf.Max(0.03f, size.x), Mathf.Max(0.03f, size.y), Mathf.Max(0.03f, size.z));
            }

            part.Colliders.Add(collider);
        }

        /// <summary>凸网格碰撞体的三角面上限（Unity 的硬限制是 255，留点余量）。</summary>
        const int MaxConvexTriangles = 240;

        static int MeshCount(Transform bone)
        {
            MeshFilter filter = bone.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            return mesh != null ? mesh.vertexCount : 0;
        }

        // ------------------------------------------------------------ 连关节

        /// <summary>
        /// 连关节：先挂到最近的祖先块（头发的上一节就是上一段头发）；
        /// 没有祖先块的（例如两条腿挂在没有刚体的 AllBody 上）挂到**和它相交最多的那块**；
        /// 还是不行的挂到主干（层级最浅的那块，一般是躯干）。
        /// </summary>
        static void LinkParts(MaidRagdollSkeleton skeleton, MaidRagdollPlan plan)
        {
            int main = 0;
            for (int i = 1; i < plan.Parts.Count; i++)
            {
                int depth = skeleton.Depths[plan.Parts[i].Bone];
                int bestDepth = skeleton.Depths[plan.Parts[main].Bone];
                if (depth < bestDepth
                    || (depth == bestDepth && plan.Parts[i].ColliderVolume > plan.Parts[main].ColliderVolume))
                {
                    main = i;
                }
            }

            for (int i = 0; i < plan.Parts.Count; i++)
            {
                MaidRagdollPart part = plan.Parts[i];
                int parent = -1;
                Transform cursor = part.Bone.parent;
                while (cursor != null && parent < 0)
                {
                    for (int j = 0; j < plan.Parts.Count; j++)
                    {
                        if (plan.Parts[j].Bone == cursor && j != i)
                        {
                            parent = plan.Parts[j].Index;
                            break;
                        }
                    }

                    cursor = cursor.parent;
                }

                if (parent < 0)
                {
                    parent = OverlapParent(plan, i);
                }

                if (parent < 0 && i != main)
                {
                    parent = plan.Parts[main].Index;
                }

                part.Parent = parent;
            }
        }

        /// <summary>跟哪一块的包围盒交得最多（层级上找不到祖先时的兜底）。</summary>
        static int OverlapParent(MaidRagdollPlan plan, int index)
        {
            Bounds bounds = plan.Parts[index].RootBounds;
            float best = 0f;
            int parent = -1;
            for (int j = 0; j < plan.Parts.Count; j++)
            {
                if (j == index)
                {
                    continue;
                }

                Bounds other = plan.Parts[j].RootBounds;
                Vector3 min = Vector3.Max(bounds.min, other.min);
                Vector3 max = Vector3.Min(bounds.max, other.max);
                float volume = Mathf.Max(0f, max.x - min.x) * Mathf.Max(0f, max.y - min.y)
                    * Mathf.Max(0f, max.z - min.z);
                if (volume > best)
                {
                    best = volume;
                    parent = plan.Parts[j].Index;
                }
            }

            return parent;
        }
    }
}
