using System.IO;
using System.Text;
using System.Threading.Tasks;
using MaidHome.Core.Storage;
using UnityEngine;

namespace MaidHome.Interop.House
{
    /// <summary>
    /// 第一次进游戏（saves/house 里一栋都没有）时给存档补一栋房子，免得空着——没房子就既看不了景
    /// 也没法放女仆。优先装随包的那栋真房子（Assets/StreamingAssets/DefaultHouse，
    /// 见 DefaultHouseInstaller）；万一随包里没有，退回老做法：现场写一个程序化示例房间
    /// （只写 house.json，`model` 留空，由 HouseImporter 按格表搭地板和墙）。
    ///
    /// 只在"一栋都没有"且"从没装过"时做一次（留一个 `.starter-done` 标记），玩家删掉它不会再冒出来。
    /// 装进来的就是一栋普通房子，可以照常删。
    /// </summary>
    public static class HouseStarter
    {
        public const string FolderName = "builtin-starter-room";
        const string MarkerName = ".starter-done";
        const int Size = 8;
        const int Height = 3;

        public static async Task<bool> EnsureAsync(string houseRoot)
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

            bool installed = await DefaultHouseInstaller.InstallAsync(houseRoot);
            if (!installed)
            {
                installed = CreateStarterRoom(houseRoot);
            }

            if (!installed)
            {
                return false;
            }

            try
            {
                File.WriteAllText(marker, "1", new UTF8Encoding(false));
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("写默认房子标记失败（下次启动会再装一次）: " + error.Message);
            }

            return true;
        }

        /// <summary>随包默认房子不可用时的兜底：一个 8×3×8 的程序化示例房间</summary>
        static bool CreateStarterRoom(string houseRoot)
        {
            string folder = Path.Combine(houseRoot, FolderName);
            string file = Path.Combine(folder, "house.json");
            try
            {
                AppPaths.EnsureDirectory(folder);
                File.WriteAllText(file, BuildJson(), new UTF8Encoding(false));
                Debug.Log("[房子] 随包没带默认房子，已生成内置示例房间（导入真房子后可以把它删掉）");
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
