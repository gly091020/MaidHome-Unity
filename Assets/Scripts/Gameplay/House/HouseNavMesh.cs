using System.Collections.Generic;
using MaidHome.Interop.House;
using UnityEngine;
using UnityEngine.AI;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 房子加载后烘一次 NavMesh，再按格数据把"内部不能走"的地方挖掉。
    ///
    /// 为什么要挖：烘出来的"能走"是几何意义上的能走，台阶拼的桌子它照走不误。
    /// 外墙不用挖——几何本来就把它们挡住了，挖了反而会把贴墙的走道吃掉一圈；
    /// 只挖被走道围住的内部障碍（判定见 HouseGrid.MarkEnclosedNonWalkable）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HouseNavMesh : MonoBehaviour
    {
        [SerializeField] private float _agentRadius = 0.3f;
        [SerializeField] private float _agentHeight = 1.75f;
        [SerializeField] private float _agentClimb = 0.5f;
        [SerializeField] private float _agentSlope = 45f;
        [SerializeField] private bool _carveBlockedCells = true;

        NavMeshData _data;
        NavMeshDataInstance _instance;
        GameObject _obstacleRoot;

        public bool IsBuilt { get; private set; }
        public Bounds Bounds { get; private set; }
        public int SourceCount { get; private set; }
        public int CarvedCells { get; private set; }

        /// <summary>烘一次。返回是否成功；调用方应该在加完 MeshCollider 之后再调。</summary>
        public bool Build()
        {
            if (IsBuilt)
            {
                return true;
            }

            HouseGridView view = GetComponent<HouseGridView>();
            Bounds localBounds = ComputeLocalBounds();
            Bounds worldBounds = TransformBounds(transform, localBounds, false);
            Bounds = worldBounds;

            // markups 不能传 null：Unity 的绑定层不接受，会抛 ArgumentNullException
            List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
            try
            {
                NavMeshBuilder.CollectSources(worldBounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("收集 NavMesh 源失败: " + error.Message, this);
                return false;
            }

            SourceCount = sources.Count;
            if (sources.Count == 0)
            {
                Debug.LogWarning("房子没有可用的碰撞体，NavMesh 烘不出来（HouseImporter 会给每个 MeshFilter 加 MeshCollider）", this);
                return false;
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = _agentRadius;
            settings.agentHeight = _agentHeight;
            settings.agentClimb = _agentClimb;
            settings.agentSlope = _agentSlope;

            // CollectSources 收的是**世界坐标**的源，而 BuildNavMeshData 的 position/rotation
            // 又是给结果定位的——这两种理解（源已是世界坐标 / 源是局部坐标）之间差一次变换，
            // 所以两种放置各试一遍，用"能不能在已知可走点上采样到"来判定哪种对。
            Vector3 probe = ProbePoint(view);
            if (!TryPlace(settings, sources, localBounds, transform.position, transform.rotation, probe)
                && !TryPlace(settings, sources, worldBounds, Vector3.zero, Quaternion.identity, probe))
            {
                Debug.LogWarning("两种放置方式都烘不出能命中的 NavMesh，探针点 " + probe, this);
                return false;
            }

            IsBuilt = true;

            if (_carveBlockedCells && view != null && view.Grid != null)
            {
                CarvedCells = Carve(view);
            }

            return true;
        }

        void OnDestroy()
        {
            if (_instance.valid)
            {
                NavMesh.RemoveNavMeshData(_instance);
            }
        }

        /// <summary>按给定放置方式烘一次，并在探针点验证；验证不过就撤掉并返回 false。</summary>
        bool TryPlace(NavMeshBuildSettings settings, List<NavMeshBuildSource> sources, Bounds bounds,
            Vector3 position, Quaternion rotation, Vector3 probe)
        {
            NavMeshData data;
            try
            {
                data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, position, rotation);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("烘 NavMesh 失败: " + error.Message, this);
                return false;
            }

            if (data == null)
            {
                return false;
            }

            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(probe, out hit, 1.5f, NavMesh.AllAreas))
            {
                _data = data;
                _instance = instance;
                return true;
            }

            // 这一种没命中，撤掉试下一种
            NavMesh.RemoveNavMeshData(instance);
            Object.Destroy(data);
            return false;
        }

        /// <summary>从格表里挑一个可走格当探针：它的世界坐标应该落在 NavMesh 上。</summary>
        Vector3 ProbePoint(HouseGridView view)
        {
            if (view == null || view.Grid == null)
            {
                return transform.position;
            }

            HouseGrid grid = view.Grid;
            for (int y = 0; y < grid.SizeY; y++)
            {
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    for (int x = 0; x < grid.SizeX; x++)
                    {
                        if (!grid.IsWalkable(x, y, z))
                        {
                            continue;
                        }

                        Vector3 local = HouseGridMapper.ToLocal(view.Axis, grid.SizeX, grid.SizeZ,
                            new Vector3Int(x, y, z));
                        return transform.TransformPoint(local);
                    }
                }
            }

            return transform.position;
        }

        /// <summary>按格数据在内部障碍处摆 NavMeshObstacle，用 carving 把烘好的网挖掉。</summary>
        int Carve(HouseGridView view)
        {
            HouseGrid grid = view.Grid;
            bool[] marks = new bool[grid.Count];
            int count = grid.MarkEnclosedNonWalkable(marks);
            if (count == 0)
            {
                return 0;
            }

            _obstacleRoot = new GameObject("NavMeshObstacles");
            _obstacleRoot.transform.SetParent(transform, false);

            for (int y = 0; y < grid.SizeY; y++)
            {
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    for (int x = 0; x < grid.SizeX; x++)
                    {
                        if (!marks[grid.Index(x, y, z)])
                        {
                            continue;
                        }

                        GameObject cell = new GameObject("blocked " + x + "," + y + "," + z);
                        cell.transform.SetParent(_obstacleRoot.transform, false);
                        // ToLocal 给的是格子中心、脚底高度；+0.5 让方块正好包住这一格
                        cell.transform.localPosition =
                            HouseGridMapper.ToLocal(view.Axis, grid.SizeX, grid.SizeZ, new Vector3Int(x, y, z))
                            + new Vector3(0f, 0.5f, 0f);
                        cell.transform.localRotation = Quaternion.identity;

                        NavMeshObstacle obstacle = cell.AddComponent<NavMeshObstacle>();
                        obstacle.shape = NavMeshObstacleShape.Box;
                        obstacle.size = Vector3.one;
                        obstacle.center = Vector3.zero;
                        obstacle.carving = true;
                        obstacle.carveOnlyStationary = true;
                    }
                }
            }

            return count;
        }

        Bounds ComputeLocalBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool first = true;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds world = renderers[i].bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? world.min.x : world.max.x,
                        (corner & 2) == 0 ? world.min.y : world.max.y,
                        (corner & 4) == 0 ? world.min.z : world.max.z);
                    Vector3 local = transform.InverseTransformPoint(point);
                    if (first)
                    {
                        min = local;
                        max = local;
                        first = false;
                    }
                    else
                    {
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                    }
                }
            }

            if (first)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Bounds bounds = new Bounds((min + max) * 0.5f, max - min);
            return bounds;
        }

        static Bounds TransformBounds(Transform transform, Bounds bounds, bool inverse)
        {
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3(
                    (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 moved = inverse ? transform.InverseTransformPoint(point) : transform.TransformPoint(point);
                if (corner == 0)
                {
                    min = moved;
                    max = moved;
                }
                else
                {
                    min = Vector3.Min(min, moved);
                    max = Vector3.Max(max, moved);
                }
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }
    }
}
