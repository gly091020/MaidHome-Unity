using System.IO;
using System.Text;
using System.Threading.Tasks;
using MaidHome.Core.Storage;
using UnityEngine;

namespace MaidHome.Interop.House
{
    /// <summary>
    /// 随包默认房子：内容在 Assets/StreamingAssets/DefaultHouse/，files.txt 是文件清单
    /// （由 Tools/default_house_pack.py 生成）。第一次启动、存档里一栋房子都没有时，
    /// HouseStarter 调这里把它整棵拷进 saves/house/&lt;uuid&gt;。
    ///
    /// 为什么用清单而不是遍历目录：Android 上 StreamingAssets 在 apk 里，File/Directory 遍历不了；
    /// 照清单一个个读，Windows 走 File、Android 走 UnityWebRequest，两边同一套逻辑。
    /// </summary>
    public static class DefaultHouseInstaller
    {
        public const string SourceFolder = "DefaultHouse";
        public const string ManifestName = "files.txt";

        /// <summary>装进存档时的文件夹名：沿用这栋房子在 MC 侧的 uuid，看起来和正常导入的房子一样</summary>
        public const string FolderName = "0891be0d-4cde-42dd-9c08-7a23bf702677";

        /// <summary>把随包默认房子拷进 houseRoot，成功返回 true</summary>
        public static async Task<bool> InstallAsync(string houseRoot)
        {
            if (string.IsNullOrEmpty(houseRoot))
            {
                return false;
            }

            byte[] manifest = await StreamingAssetsStorage.ReadAllBytesAsync(SourceFolder + "/" + ManifestName);
            if (manifest == null)
            {
                Debug.LogWarning("随包里没有默认房子（" + SourceFolder + "/" + ManifestName + " 不在），跳过");
                return false;
            }

            string[] files = SplitLines(Encoding.UTF8.GetString(manifest));
            if (files.Length == 0)
            {
                Debug.LogWarning("默认房子的文件清单是空的: " + SourceFolder + "/" + ManifestName);
                return false;
            }

            string folder = Path.Combine(houseRoot, FolderName);
            int copied = 0;
            try
            {
                AppPaths.EnsureDirectory(folder);
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = files[i];
                    byte[] data = await StreamingAssetsStorage.ReadAllBytesAsync(SourceFolder + "/" + relative);
                    if (data == null)
                    {
                        continue;
                    }

                    string target = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        AppPaths.EnsureDirectory(parent);
                    }

                    File.WriteAllBytes(target, data);
                    copied++;
                }
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("装默认房子失败: " + error.Message);
                return false;
            }

            if (copied == 0 || !File.Exists(Path.Combine(folder, "house.json")))
            {
                Debug.LogWarning("默认房子没装全（拷到 " + copied + " / " + files.Length + " 个文件），当没装成功");
                return false;
            }

            Debug.Log("[房子] 已把随包默认房子装进存档：" + folder + "（" + copied + " 个文件）");
            return true;
        }

        /// <summary>清单一行一个路径，允许空行和 # 注释，顺便把 Windows 换行去掉</summary>
        static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new string[0];
            }

            string[] raw = text.Split('\n');
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i].Trim().Replace('\\', '/');
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                lines.Add(line);
            }

            return lines.ToArray();
        }
    }
}
