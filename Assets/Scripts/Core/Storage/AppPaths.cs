using System;
using System.IO;
using UnityEngine;

namespace MaidHome.Core.Storage
{
    /// <summary>
    /// 数据根目录。存档和缓存分两条线：存档是玩家数据（不许删），缓存是可再生的中间产物。
    /// 目前还没有从 MC 端下载的流程，先在本地把女仆存档读起来。
    /// </summary>
    public static class AppPaths
    {
        static string _cacheRoot;
        static string _tmpRoot;
        static string _savesRoot;

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
            _savesRoot = Application.platform == RuntimePlatform.Android
                // 还没做 SAF / MediaStore 的 Java 桥，公共 Documents 拿不到，先退回应用私有目录
                ? Path.Combine(persistent, "saves")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MaidHome", "saves");
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
    }
}
