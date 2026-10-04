using System;
using System.IO;
using UnityEngine;

namespace MaidHome.Core.Storage
{
    /// <summary>
    /// 数据根目录。存档和缓存分两条线：存档是玩家数据（不许删），缓存是可再生的中间产物。
    /// 存档优先放「公共文档目录」（Android 的 /storage/emulated/0/Documents，卸载后还在），
    /// 拿不到权限就退回应用私有目录 persistentDataPath/saves。
    /// </summary>
    public static class AppPaths
    {
        static string _cacheRoot;
        static string _tmpRoot;
        static string _savesRoot;

        /// <summary>当前存档是不是放在公共目录里（设置界面可以拿它显示状态）</summary>
        public static bool SavesRootIsPublic { get; private set; }

        /// <summary>
        /// Application.persistentDataPath 只能在主线程调，而 Portal 的收文件是后台线程干的，
        /// 所以进游戏时先把根目录算出来存住，之后任何线程都只是读字符串。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void WarmUp()
        {
            string persistent = Application.persistentDataPath;
            _cacheRoot = Path.Combine(persistent, "cache");
            _tmpRoot = Path.Combine(persistent, "tmp");
            Refresh();
        }

        /// <summary>
        /// 重新算一次存档根目录。Android 上玩家在系统设置里给了权限后会从私有目录切到公共
        /// Documents，切换时把旧目录里缺的文件补拷过去（只补不覆盖、也不删原件）。
        /// 回到前台 / 授权回来都要调一次；只在主线程调（会走 JNI）。
        /// </summary>
        public static void Refresh()
        {
            string previous = _savesRoot;
            string next = ResolveSavesRoot();
            _savesRoot = next;
            EnsureDirectory(next);
            EnsureNoMedia(next);

            if (string.IsNullOrEmpty(previous))
            {
                Debug.Log("[存档] 目录：" + next + "｜" + PublicStorage.Describe()
                    + (SavesRootIsPublic ? "（可用）" : "（拿不到，先用应用私有目录）"));
                return;
            }

            if (previous == next)
            {
                return;
            }

            int copied = CopyMissing(previous, next);
            Debug.Log("[存档] 目录从 " + previous + " 切到 " + next + "，补拷 " + copied + " 个文件");
        }

        /// <summary>
        /// 存档根目录：公共目录能写就用它（卸载后还在），否则退回应用私有目录。
        /// 平台差异全部交给 PublicStorage，这里不看 Application.platform —— Device Simulator
        /// 会把 platform / isMobilePlatform / isEditor 一起伪装成 Android。
        /// </summary>
        static string ResolveSavesRoot()
        {
            string documents = PublicStorage.DocumentsRoot;
            if (!string.IsNullOrEmpty(documents))
            {
                SavesRootIsPublic = true;
                return Path.Combine(documents, "MaidHome", "saves");
            }

            SavesRootIsPublic = false;
            return Path.Combine(Application.persistentDataPath, "saves");
        }

        /// <summary>把 from 里有、to 里没有的文件补过去（存档迁移用，绝不覆盖、也不删）</summary>
        static int CopyMissing(string from, string to)
        {
            if (!Directory.Exists(from))
            {
                return 0;
            }

            int copied = 0;
            try
            {
                string[] files = Directory.GetFiles(from, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = files[i].Substring(from.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string target = Path.Combine(to, relative);
                    if (File.Exists(target))
                    {
                        continue;
                    }

                    string folder = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    File.Copy(files[i], target, false);
                    copied++;
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[存档] 补拷旧存档失败（不影响继续玩）：" + error.Message);
            }

            return copied;
        }

        public static string CacheRoot
        {
            get
            {
                if (string.IsNullOrEmpty(_cacheRoot))
                {
                    WarmUp();
                }

                return _cacheRoot;
            }
        }

        /// <summary>传输/下载中的半成品，写完再 Move 到正式目录。</summary>
        public static string TmpRoot
        {
            get
            {
                if (string.IsNullOrEmpty(_tmpRoot))
                {
                    WarmUp();
                }

                return _tmpRoot;
            }
        }

        /// <summary>女仆存档根目录，里面一个 uuid 一个文件夹。</summary>
        public static string MaidSaveRoot
        {
            get { return Path.Combine(SavesRoot, "maid"); }
        }

        /// <summary>房子存档根目录，结构和女仆那边一样。</summary>
        public static string HouseSaveRoot
        {
            get { return Path.Combine(SavesRoot, "house"); }
        }

        /// <summary>声音包根目录：saves/sounds/&lt;soundId&gt;/maid/...</summary>
        public static string SoundsRoot
        {
            get { return Path.Combine(SavesRoot, "sounds"); }
        }

        public static string SavesRoot
        {
            get
            {
                if (string.IsNullOrEmpty(_savesRoot))
                {
                    WarmUp();
                }

                return _savesRoot;
            }
        }

        public static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// Android 的相册/音乐播放器会扫公共目录，女仆贴图一张张冒到用户相册里是要被骂的。
        /// 存档根目录放一个 .nomedia，MediaScanner 会连着跳过整棵子树；Windows 上放着也无害。
        /// </summary>
        static void EnsureNoMedia(string root)
        {
            try
            {
                string marker = Path.Combine(root, ".nomedia");
                if (!File.Exists(marker))
                {
                    File.WriteAllText(marker, "");
                }
            }
            catch (Exception)
            {
                // 只是给扫描器看的标记，写不进去不影响存档
            }
        }
    }
}
