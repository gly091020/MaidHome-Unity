using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 判断两块物理块"是不是真插在一起了"，以及插在哪（相交区域中心当关节锚点）。
    ///
    /// Unity 没有现成的"网格相交体积"接口（PhysX 只有 <see cref="Physics.ComputePenetration"/>，
    /// 给穿深和分离方向，没有体积；mesh 布尔也没有内置的），所以这里这么算：
    ///
    /// 1. 粗筛：两个碰撞体的包围盒相交（便宜）；
    /// 2. <see cref="Physics.ComputePenetration"/> 精确问"真穿了吗、穿多深" —— 凸包/盒是几何精确的；
    /// 3. 真穿了就在交集里均匀撒点（默认 8×8×8 = 512），数多少点同时在两块**碰撞体内部**
    ///    （凸碰撞体用 <see cref="Collider.ClosestPoint"/> 判：在内部会把点原样返回），
    ///    交集体积 × 命中比例 = 相交体积。
    ///
    /// 判定的是真实形状，不是外接长方体：斜着的头发片、斜着的胳膊都不会被误判。
    /// </summary>
    public static class MaidRagdollOverlap
    {
        /// <summary>相交体积要达到较小那块的这么多比例才算"重叠"。</summary>
        public const float Ratio = 0.05f;
        /// <summary>采样点数（实际取最接近的立方数，512 = 8×8×8）。</summary>
        public const int Samples = 512;
        /// <summary>小于这个穿深（米）当只是蹭到。</summary>
        public const float MinDepth = 0.001f;

        /// <summary>
        /// 编辑器预览用：按 plan 临时搭一遍碰撞体来判定（不改场景里的任何东西），判完就销毁。
        /// 返回的 Key/Value 是 plan.Parts 的下标。别在每帧的界面回调里调。
        /// </summary>
        public static List<KeyValuePair<int, int>> Find(MaidRagdollPlan plan)
        {
            List<KeyValuePair<int, int>> pairs = new List<KeyValuePair<int, int>>();
            if (plan == null || plan.Parts.Count == 0)
            {
                return pairs;
            }

            List<GameObject> holders = new List<GameObject>();
            List<List<Collider>> colliders = new List<List<Collider>>();
            try
            {
                for (int i = 0; i < plan.Parts.Count; i++)
                {
                    List<Collider> list = new List<Collider>();
                    colliders.Add(list);
                    for (int c = 0; c < plan.Parts[i].Colliders.Count; c++)
                    {
                        MaidRagdollCollider entry = plan.Parts[i].Colliders[c];
                        if (entry.Bone == null)
                        {
                            continue;
                        }

                        GameObject holder = new GameObject("ragdoll_overlap_probe");
                        holder.hideFlags = HideFlags.HideAndDontSave;
                        holder.transform.SetPositionAndRotation(entry.Bone.position, entry.Bone.rotation);
                        holder.transform.localScale = entry.Bone.lossyScale;
                        holders.Add(holder);
                        AddCollider(holder, entry, list);
                    }
                }

                return FindPairs(colliders);
            }
            finally
            {
                for (int i = 0; i < holders.Count; i++)
                {
                    if (holders[i] != null)
                    {
                        Object.DestroyImmediate(holders[i]);
                    }
                }
            }
        }

        /// <summary>运行时用：直接拿已经建好的碰撞体判定（每块物理体一组）。</summary>
        public static List<KeyValuePair<int, int>> Find(IList<List<Collider>> colliders)
        {
            return colliders == null ? new List<KeyValuePair<int, int>>() : FindPairs(colliders);
        }

        /// <summary>单对查询：两块真相交就返回相交区域中心（世界坐标，当关节锚点用）。</summary>
        public static bool Overlaps(IList<Collider> first, IList<Collider> second, out Vector3 center)
        {
            center = Vector3.zero;
            if (first == null || second == null)
            {
                return false;
            }

            for (int a = 0; a < first.Count; a++)
            {
                if (first[a] == null)
                {
                    continue;
                }

                for (int b = 0; b < second.Count; b++)
                {
                    if (second[b] != null && Overlaps(first[a], second[b], out center))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------------------ 内部

        static void AddCollider(GameObject holder, MaidRagdollCollider entry, List<Collider> into)
        {
            if (entry.IsMesh)
            {
                MeshCollider meshCollider = holder.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = entry.Mesh;
                meshCollider.convex = true;
                into.Add(meshCollider);
                return;
            }

            BoxCollider box = holder.AddComponent<BoxCollider>();
            box.center = entry.Center;
            box.size = entry.Size;
            into.Add(box);
        }

        static List<KeyValuePair<int, int>> FindPairs(IList<List<Collider>> colliders)
        {
            List<KeyValuePair<int, int>> pairs = new List<KeyValuePair<int, int>>();
            int count = colliders.Count;
            for (int i = 0; i < count; i++)
            {
                List<Collider> first = colliders[i];
                if (first == null || first.Count == 0)
                {
                    continue;
                }

                for (int j = i + 1; j < count; j++)
                {
                    Vector3 center;
                    if (colliders[j] != null && colliders[j].Count > 0
                        && Overlaps(first, colliders[j], out center))
                    {
                        pairs.Add(new KeyValuePair<int, int>(i, j));
                    }
                }
            }

            return pairs;
        }

        static bool Overlaps(Collider a, Collider b, out Vector3 center)
        {
            center = Vector3.zero;
            Bounds boundA;
            Bounds boundB;
            if (!LocalBounds(a, out boundA) || !LocalBounds(b, out boundB))
            {
                return false;
            }

            Bounds worldA = ToWorld(a.transform, boundA);
            Bounds worldB = ToWorld(b.transform, boundB);
            Bounds overlap = Intersect(worldA, worldB);
            if (overlap.size.x <= 0f || overlap.size.y <= 0f || overlap.size.z <= 0f)
            {
                return false;
            }

            // 真的穿了吗（凸包 / 盒 / 凸包-网格 都支持；两个非凸网格不支持）
            Vector3 direction;
            float depth;
            if (!Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                    b, b.transform.position, b.transform.rotation, out direction, out depth))
            {
                return false;
            }

            if (depth < MinDepth)
            {
                return false;
            }

            float smaller = Mathf.Min(Volume(worldA), Volume(worldB));
            if (smaller <= 0f)
            {
                return false;
            }

            int side = Mathf.Max(2, Mathf.RoundToInt(Mathf.Pow(Samples, 1f / 3f)));
            int total = side * side * side;
            int inside = 0;
            Vector3 sum = Vector3.zero;
            for (int x = 0; x < side; x++)
            {
                for (int y = 0; y < side; y++)
                {
                    for (int z = 0; z < side; z++)
                    {
                        Vector3 point = new Vector3(
                            overlap.min.x + overlap.size.x * (x + 0.5f) / side,
                            overlap.min.y + overlap.size.y * (y + 0.5f) / side,
                            overlap.min.z + overlap.size.z * (z + 0.5f) / side);
                        if (Contains(a, point) && Contains(b, point))
                        {
                            inside++;
                            sum += point;
                        }
                    }
                }
            }

            if (inside == 0)
            {
                return false;
            }

            float volume = Volume(overlap) * inside / total;
            if (volume < smaller * Ratio)
            {
                return false;
            }

            center = sum / inside;
            return true;
        }

        /// <summary>凸碰撞体的"点在内部"判定：在内部时 ClosestPoint 会把点原样返回。</summary>
        static bool Contains(Collider collider, Vector3 point)
        {
            Vector3 closest = collider.ClosestPoint(point);
            return (closest - point).sqrMagnitude < 1e-8f;
        }

        /// <summary>碰撞体自己在局部空间的包围盒（盒类用 center/size，网格用 mesh.bounds）。</summary>
        static bool LocalBounds(Collider collider, out Bounds bounds)
        {
            BoxCollider box = collider as BoxCollider;
            if (box != null)
            {
                bounds = new Bounds(box.center, box.size);
                return true;
            }

            MeshCollider mesh = collider as MeshCollider;
            if (mesh != null && mesh.sharedMesh != null)
            {
                bounds = mesh.sharedMesh.bounds;
                return true;
            }

            bounds = new Bounds();
            return false;
        }

        static Bounds ToWorld(Transform transform, Bounds local)
        {
            Bounds world = new Bounds();
            bool any = false;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = local.center + Vector3.Scale(local.size * 0.5f,
                            new Vector3(x * 2 - 1, y * 2 - 1, z * 2 - 1));
                        Vector3 point = transform.TransformPoint(corner);
                        if (any)
                        {
                            world.Encapsulate(point);
                        }
                        else
                        {
                            world = new Bounds(point, Vector3.zero);
                            any = true;
                        }
                    }
                }
            }

            return world;
        }

        static Bounds Intersect(Bounds a, Bounds b)
        {
            Vector3 min = Vector3.Max(a.min, b.min);
            Vector3 max = Vector3.Min(a.max, b.max);
            return new Bounds((min + max) * 0.5f, max - min);
        }

        static float Volume(Bounds bounds)
        {
            return Mathf.Max(0f, bounds.size.x) * Mathf.Max(0f, bounds.size.y) * Mathf.Max(0f, bounds.size.z);
        }
    }
}
