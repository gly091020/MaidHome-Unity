using MaidHome.Interop.House;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 挂在房子根节点上，带着可通行格表，负责"世界坐标 ↔ 格"的换算。
    /// 女仆寻路从这里拿格数据；编辑器里的 gizmo 也读这个组件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HouseGridView : MonoBehaviour
    {
        public HouseGrid Grid { get; private set; }
        public HouseSaveData Data { get; private set; }

        [Tooltip("格数据相对模型的方向。默认 MirrorX 是 glTFast 的坐标系转换；对不上就用 Tools/MaidHome/房子对齐检查 量一下再改这里")]
        [SerializeField] private GridAxis _axis = GridAxis.MirrorX;

        public GridAxis Axis
        {
            get { return _axis; }
            set { _axis = value; }
        }

        public void Set(HouseGrid grid, HouseSaveData data)
        {
            Grid = grid;
            Data = data;
        }

        /// <summary>这一格中心的世界坐标，y 是脚底高度。</summary>
        public Vector3 CellFeet(Vector3Int cell)
        {
            return transform.TransformPoint(HouseGridMapper.ToLocal(_axis, Grid.SizeX, Grid.SizeZ, cell));
        }

        /// <summary>世界坐标落在哪一格，以及这一格能不能站。</summary>
        public bool TryWorldToCell(Vector3 world, out Vector3Int cell, out bool walkable)
        {
            cell = default(Vector3Int);
            walkable = false;
            if (Grid == null)
            {
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(world);
            cell = HouseGridMapper.ToCell(_axis, Grid.SizeX, Grid.SizeZ, local);
            if (!Grid.InBounds(cell.x, cell.y, cell.z))
            {
                return false;
            }

            walkable = Grid.IsWalkable(cell.x, cell.y, cell.z);
            return true;
        }

        /// <summary>把世界坐标吸附到最近的可行走格中心；不可走或越界返回 false。</summary>
        public bool TryGetWalkableFeet(Vector3 world, out Vector3 feet)
        {
            Vector3Int cell;
            bool walkable;
            if (TryWorldToCell(world, out cell, out walkable) && walkable)
            {
                feet = CellFeet(cell);
                return true;
            }

            feet = world;
            return false;
        }

        /// <summary>存档里的位置失效时，找最近的可行走格兜底。</summary>
        public bool TryFindNearestWalkable(Vector3 world, out Vector3Int cell, out Vector3 feet)
        {
            cell = default(Vector3Int);
            feet = world;
            if (Grid == null)
            {
                return false;
            }

            float best = float.MaxValue;
            bool found = false;
            for (int y = 0; y < Grid.SizeY; y++)
            {
                for (int z = 0; z < Grid.SizeZ; z++)
                {
                    for (int x = 0; x < Grid.SizeX; x++)
                    {
                        if (!Grid.IsWalkable(x, y, z))
                        {
                            continue;
                        }

                        Vector3 candidate = CellFeet(new Vector3Int(x, y, z));
                        Vector3 delta = candidate - world;
                        float distance = delta.x * delta.x + delta.z * delta.z + delta.y * delta.y * 4f;
                        if (distance < best)
                        {
                            best = distance;
                            cell = new Vector3Int(x, y, z);
                            feet = candidate;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

    }
}
