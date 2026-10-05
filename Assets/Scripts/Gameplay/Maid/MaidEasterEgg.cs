using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 彩蛋女仆（名字叫 5112151111121）的语言表：读 Assets/Resources/gly_lines.json，
    /// 闲着说的用 lines、所有挨疼的台词用 hurt_lines（death_lines 也读进来了，但本工程还没有死亡，先放着）。
    /// 别的女仆这里一律返回 false / 走调用方原来的台词组。
    /// </summary>
    public static class MaidEasterEgg
    {
        public const string MaidName = "5112151111121";

        const string ResourcePath = "gly_lines";

        static List<string> _idleLines;
        static List<string> _hurtLines;
        static bool _loaded;

        public static bool IsEasterEgg(MaidAgent agent)
        {
            if (agent == null || agent.Save == null || agent.Save.Name == null)
            {
                return false;
            }

            return agent.Save.Name.Trim() == MaidName;
        }

        /// <summary>闲着要说的那句；不是彩蛋女仆或者文件没读到就返回空串</summary>
        public static string PickIdleLine(MaidAgent agent)
        {
            return IsEasterEgg(agent) ? Pick(_idleLines) : "";
        }

        /// <summary>彩蛋女仆挨疼就全换成 hurt_lines，否则从传进来的那组里随机挑一条</summary>
        public static string PickHurt(MaidAgent agent, string[] fallback)
        {
            if (IsEasterEgg(agent))
            {
                string line = Pick(_hurtLines);
                if (!string.IsNullOrEmpty(line))
                {
                    return line;
                }
            }

            return fallback != null && fallback.Length > 0
                ? fallback[UnityEngine.Random.Range(0, fallback.Length)]
                : "";
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
                Debug.LogWarning("没有找到 Assets/Resources/" + ResourcePath + ".json，彩蛋女仆的台词用不了");
                return;
            }

            GlyLinesFile file;
            try
            {
                file = JsonUtility.FromJson<GlyLinesFile>(asset.text);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("gly_lines.json 解析失败：" + e.Message);
                return;
            }

            if (file == null)
            {
                return;
            }

            _idleLines = file.lines;
            _hurtLines = file.hurt_lines;
        }

        static string Pick(List<string> lines)
        {
            EnsureLoaded();
            return lines != null && lines.Count > 0
                ? lines[UnityEngine.Random.Range(0, lines.Count)]
                : "";
        }

        // 字段名就是 json 里的键名（JsonUtility 按名字对应），所以这里不按 _camelCase 写
        [System.Serializable]
        sealed class GlyLinesFile
        {
            public List<string> lines = new List<string>();
            public List<string> hurt_lines = new List<string>();
            public List<string> death_lines = new List<string>();
        }
    }
}
