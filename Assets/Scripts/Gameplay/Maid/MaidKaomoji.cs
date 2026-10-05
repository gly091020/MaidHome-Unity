using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 读 Assets/Resources/kaomoji.json：照 TLM 的 KaomojiData 把 core 并进 work / idle 两组
    /// （core 是工作和空闲共用的那批），sleep / hurt 单独用。
    /// 文件就是 { "core": [...], "idle": [...], ... }，解析失败只警告一次、返回空串。
    /// </summary>
    public static class MaidKaomoji
    {
        const string ResourcePath = "kaomoji";

        static readonly Dictionary<string, List<string>> Groups = new Dictionary<string, List<string>>();
        static bool _loaded;

        /// <summary>从某一组里随机取一句；组不存在、文件没读到就返回空字符串</summary>
        public static string Random(string group)
        {
            EnsureLoaded();

            List<string> list;
            if (string.IsNullOrEmpty(group) || !Groups.TryGetValue(group, out list) || list.Count == 0)
            {
                return "";
            }

            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("没有找到 Assets/Resources/" + ResourcePath + ".json，颜文字气泡用不了");
                return;
            }

            KaomojiFile file;
            try
            {
                file = JsonUtility.FromJson<KaomojiFile>(asset.text);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("kaomoji.json 解析失败：" + e.Message);
                return;
            }

            if (file == null)
            {
                return;
            }

            Add("core", file.core);
            Add("work", file.work);
            Add("idle", file.idle);
            Add("sleep", file.sleep);
            Add("hurt", file.hurt);

            Merge("work", "core");
            Merge("idle", "core");
            Groups.Remove("core");
        }

        static void Add(string group, List<string> values)
        {
            if (values != null && values.Count > 0)
            {
                Groups[group] = values;
            }
        }

        static void Merge(string group, string extra)
        {
            List<string> target;
            List<string> source;
            if (Groups.TryGetValue(group, out target) && Groups.TryGetValue(extra, out source))
            {
                target.AddRange(source);
            }
        }

        [System.Serializable]
        sealed class KaomojiFile
        {
            public List<string> core = new List<string>();
            public List<string> work = new List<string>();
            public List<string> idle = new List<string>();
            public List<string> sleep = new List<string>();
            public List<string> hurt = new List<string>();
        }
    }
}
