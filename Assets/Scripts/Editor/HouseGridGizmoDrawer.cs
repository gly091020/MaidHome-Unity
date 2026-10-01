using MaidHome.Gameplay.House;
using MaidHome.Interop.House;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 在 Scene 视图里把房子的可通行格画出来。**只用于调试**：生产流程不依赖它，
    /// 但"行到底是 z 还是 x"这种朝向问题只能靠眼睛确认，没有这个没法核对数据。
    /// 选中挂 HouseGridView 的物体即可看到。
    /// </summary>
    public static class HouseGridGizmoDrawer
    {
        const int MaxLabels = 400;

        [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy)]
        static void Draw(HouseGridView view, GizmoType type)
        {
            if (view == null || view.Grid == null)
            {
                return;
            }

            HouseGrid grid = view.Grid;
            Transform root = view.transform;

            int labels = 0;
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

                        Vector3 feet = view.CellFeet(new Vector3Int(x, y, z));
                        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
                        Gizmos.DrawWireCube(feet + root.up * 0.05f, new Vector3(0.9f, 0.1f, 0.9f));

                        if (labels < MaxLabels)
                        {
                            Handles.color = Color.white;
                            Handles.Label(feet + root.up * 0.15f, x + "," + y + "," + z);
                            labels++;
                        }
                    }
                }
            }

            // 包围盒：格数据在文件坐标系里是 [0,Size]，映射到 Unity 后 x 落在 [-SizeX, 0]
            Vector3 center = root.TransformPoint(
                new Vector3(-grid.SizeX * 0.5f, grid.SizeY * 0.5f, grid.SizeZ * 0.5f));
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(center, new Vector3(grid.SizeX, grid.SizeY, grid.SizeZ));
        }
    }
}
