using MaidHome.Gameplay.House;
using MaidHome.Interop.House;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 验证"格数据的方向和模型对不对得上"。
    ///
    /// 做法：对 8 种候选映射（绕 y 转 0/90/180/270 × 是否镜像 x），把每个可走格映射到世界坐标，
    /// 从格子上方 0.5 单位往下打射线，看能不能正好打在楼板上。正确的那一种得分接近 100%。
    /// 依赖房子已经加了 MeshCollider（HouseImporter 会加）。
    /// </summary>
    public sealed class HouseAlignCheckWindow : EditorWindow
    {
        [MenuItem("Tools/MaidHome/房子对齐检查")]
        static void Open()
        {
            HouseAlignCheckWindow window = GetWindow<HouseAlignCheckWindow>("房子对齐检查");
            window.minSize = new Vector2(460f, 300f);
        }

        string _report = "选中挂 HouseGridView 的房子，再点「检查」。";
        Vector2 _scroll;

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "把可走格按 8 种方向映射到世界，用向下射线验证哪几种落在楼板上。\n" +
                "得分最高的那种就是格数据的正确朝向。",
                MessageType.Info);

            if (GUILayout.Button("检查", GUILayout.Height(28f)))
            {
                Check();
            }

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void Check()
        {
            HouseGridView view = null;
            for (int i = 0; i < Selection.gameObjects.Length && view == null; i++)
            {
                view = Selection.gameObjects[i].GetComponentInParent<HouseGridView>();
            }

            if (view == null || view.Grid == null)
            {
                _report = "先选中房子（挂 HouseGridView 的那个物体）。";
                return;
            }

            HouseGrid grid = view.Grid;
            Transform root = view.transform;
            int total = grid.WalkableCount();
            if (total == 0)
            {
                _report = "格表里没有可走格。";
                return;
            }

            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine("可走格 " + total + " 个，逐种映射验证：");
            report.AppendLine();

            int bestScore = -1;
            GridAxis bestAxis = GridAxis.MirrorX;
            System.Array values = System.Enum.GetValues(typeof(GridAxis));
            for (int i = 0; i < values.Length; i++)
            {
                GridAxis axis = (GridAxis)values.GetValue(i);
                int hits = Score(grid, root, axis);
                report.AppendLine(string.Format("{0,-18} 命中 {1,3}/{2}  ({3:0}%)", axis, hits, total,
                    100f * hits / total));
                if (hits > bestScore)
                {
                    bestScore = hits;
                    bestAxis = axis;
                }
            }

            report.AppendLine();
            report.AppendLine("最匹配: " + bestAxis + "（" + bestScore + "/" + total + "）");
            report.AppendLine();
            report.AppendLine("把房子上 HouseGridView 的 Axis 设成 " + bestAxis + " 即可。");
            report.AppendLine("默认 MirrorX 是 glTFast 的坐标系转换（它把顶点 x 取负），正常情况下就该是它。");
            _report = report.ToString();
            Repaint();
        }

        static int Score(HouseGrid grid, Transform root, GridAxis axis)
        {
            int hits = 0;
            int walkable = 0;
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

                        walkable++;
                        Vector3 world = root.TransformPoint(
                            HouseGridMapper.ToLocal(axis, grid.SizeX, grid.SizeZ, new Vector3Int(x, y, z)));
                        RaycastHit hit;
                        Vector3 from = world + Vector3.up * 0.5f;
                        if (Physics.Raycast(from, Vector3.down, out hit, 1.5f) && !hit.collider.isTrigger
                            && Mathf.Abs(hit.point.y - world.y) <= 0.3f)
                        {
                            hits++;
                        }
                    }
                }
            }

            return walkable == 0 ? 0 : hits;
        }

    }
}
