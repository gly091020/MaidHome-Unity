using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MaidHome.Core.Storage;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>
    /// 扫描 saves/sounds/&lt;soundId&gt;/maid/&lt;category&gt;/&lt;event&gt;&lt;index&gt;.ogg，
    /// 建立和 TLM 一致的事件 id 表，并提供带回退链的查询。
    /// </summary>
    public static class MaidSoundLibrary
    {
        public const string DefaultPackId = "touhou_little_maid";

        static readonly Dictionary<string, MaidSoundPack> Packs =
            new Dictionary<string, MaidSoundPack>(StringComparer.OrdinalIgnoreCase);

        static readonly Regex TrailingDigits = new Regex("\\d+$", RegexOptions.Compiled);

        public static MaidSoundPack Get(string packId)
        {
            if (string.IsNullOrEmpty(packId))
            {
                return null;
            }

            MaidSoundPack pack;
            if (Packs.TryGetValue(packId, out pack))
            {
                return pack;
            }

            pack = Build(packId);
            Packs[packId] = pack;
            return pack;
        }

        public static bool TryGetPaths(string packId, string eventId, out List<string> paths)
        {
            paths = null;
            MaidSoundPack pack = Get(packId);
            if (pack == null)
            {
                return false;
            }

            string key = MaidSoundId.Normalize(eventId);
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(key); i++)
            {
                if (pack.TryGet(key, out paths) && paths.Count > 0)
                {
                    return true;
                }

                string fallback = MaidSoundId.Fallback(key);
                if (string.IsNullOrEmpty(fallback) || fallback == key)
                {
                    break;
                }

                key = fallback;
            }

            paths = null;
            return false;
        }

        public static string[] ListPackIds()
        {
            if (!Directory.Exists(AppPaths.SoundsRoot))
            {
                return new string[0];
            }

            string[] folders = Directory.GetDirectories(AppPaths.SoundsRoot);
            string[] ids = new string[folders.Length];
            for (int i = 0; i < folders.Length; i++)
            {
                ids[i] = Path.GetFileName(folders[i]);
            }

            Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public static void Reload()
        {
            Packs.Clear();
        }

        static MaidSoundPack Build(string packId)
        {
            string root = Path.Combine(AppPaths.SoundsRoot, packId);
            MaidSoundPack pack = new MaidSoundPack(packId, root);
            if (!Directory.Exists(root))
            {
                return pack;
            }

            string[] files = Directory.GetFiles(root, "*.ogg", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                string[] parts = relative.Split('/');
                if (parts.Length < 3 || !string.Equals(parts[0], "maid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string category = parts[1].ToLowerInvariant();
                string fileName = Path.GetFileNameWithoutExtension(parts[parts.Length - 1]);
                string eventName = TrailingDigits.Replace(fileName, "").ToLowerInvariant();
                if (string.IsNullOrEmpty(eventName))
                {
                    continue;
                }

                string eventId = string.Equals(category, "other", StringComparison.OrdinalIgnoreCase)
                    ? "maid." + eventName
                    : "maid." + category + "." + eventName;
                pack.Add(eventId, files[i]);
            }

            return pack;
        }
    }
}
