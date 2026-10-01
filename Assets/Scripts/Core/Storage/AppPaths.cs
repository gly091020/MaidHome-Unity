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
        public static string CacheRoot
        {
            get { return Path.Combine(Application.persistentDataPath, "cache"); }
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
                if (Application.platform == RuntimePlatform.Android)
                {
                    // 还没做 SAF / MediaStore 的 Java 桥，公共 Documents 拿不到，先退回应用私有目录
                    return Path.Combine(Application.persistentDataPath, "saves");
                }

                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MaidHome", "saves");
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
