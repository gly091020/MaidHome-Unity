using System.IO;
using System.Text;
using MaidHome.Core.Storage;
using UnityEngine;

namespace MaidHome.Interop.House
{
    /// <summary>
    /// 第一次进游戏（saves/house 里一栋都没有）时写一个内置的示例房间进去，免得空着——没房子就既看不了
    /// 景也没法放女仆。只写一份 house.json（`model` 留空，由 HouseImporter 按格表程序化搭地板和墙），
    /// 不依赖任何随包资源，Android 上也就绕开了 StreamingAssets 读不了的问题。
    ///
    /// 只在"一栋都没有"且"从没生成过"时写一次（留一个 `.starter-done` 标记），玩家删掉它不会再冒出来。
    /// 导入真房子以后它就是一栋普通房子，可以照常删。
    /// </summary>
    public static class HouseStarter
    {
        public const string FolderName = "builtin-starter-room";
        const string MarkerName = ".starter-done";
        const int Size = 8;
        const int Height = 3;

        public static bool Ensure(string houseRoot)
        {
            if (string.IsNullOrEmpty(houseRoot))
            {
                return false;
            }

            string marker = Path.Combine(houseRoot, MarkerName);
            if (File.Exists(marker))
            {
                return false;
            }

            if (HouseSaveData.ScanRoot(houseRoot).Count > 0)
            {
                return false;
            }

            string folder = Path.Combine(houseRoot, FolderName);
            string file = Path.Combine(folder, "house.json");
            try
            {
                AppPaths.EnsureDirectory(folder);
                File.WriteAllText(file, BuildJson(), new UTF8Encoding(false));
                File.WriteAllText(marker, "1", new UTF8Encoding(false));
                Debug.Log("[房子] 存档里一栋房子都没有，已生成内置示例房间（导入真房子后可以把它删掉）");
                return true;
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("生成内置示例房间失败: " + error.Message);
                return false;
            }
        }

        /// <summary>8×3×8 的方房间：外面一圈是墙（不可走），里面 6×6 可走，脚底在 y = 0。</summary>
        static string BuildJson()
        {
            StringBuilder text = new StringBuilder();
            text.Append("{\n");
            text.Append("  \"size\": [").Append(Size).Append(", ").Append(Height).Append(", ").Append(Size)
                .Append("],\n");
            text.Append("  \"name\": \"示例房间\",\n");
            text.Append("  \"origin\": [").Append(Size / 2).Append(", 0, ").Append(Size / 2).Append("],\n");
            text.Append("  \"model\": \"\",\n");
            text.Append("  \"walkable\": [\n");
            for (int y = 0; y < Height; y++)
            {
                text.Append("    \"");
                for (int z = 0; z < Size; z++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        bool inner = x > 0 && x < Size - 1 && z > 0 && z < Size - 1;
                        text.Append(y == 0 && inner ? '1' : '0');
                    }

                    if (z < Size - 1)
                    {
                        // house.json 里行分隔是转义的 \n（解析时按 '\n' 切行）
                        text.Append("\\n");
                    }
                }

                text.Append('"');
                if (y < Height - 1)
                {
                    text.Append(',');
                }

                text.Append('\n');
            }

            text.Append("  ]\n}\n");
            return text.ToString();
        }
    }
}
