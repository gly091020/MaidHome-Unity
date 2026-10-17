using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 示众牌的 16 个染料色，色名和 MC 的 DyeColor 一一对应（存档里存的就是这个名字），
    /// 文字颜色照 MC 的 DyeColor.getTextColor()，这样牌面观感和原版告示牌一致。
    /// </summary>
    public static class MaidSignPalette
    {
        public const string Default = "white";

        public struct Entry
        {
            public string Name;
            public string Label;
            public Color Text;
        }

        public static readonly Entry[] All =
        {
            Make("white", "白", 0xF9FFFE),
            Make("orange", "橙", 0xF9801D),
            Make("magenta", "品红", 0xC74EBD),
            Make("light_blue", "淡蓝", 0x3AB3DA),
            Make("yellow", "黄", 0xFED83D),
            Make("lime", "黄绿", 0x80C71F),
            Make("pink", "粉", 0xF38BAA),
            Make("gray", "灰", 0x474F52),
            Make("light_gray", "淡灰", 0x9D9D97),
            Make("cyan", "青", 0x169C9C),
            Make("purple", "紫", 0x8932B8),
            Make("blue", "蓝", 0x3C44AA),
            Make("brown", "棕", 0x835432),
            Make("green", "绿", 0x5E7C16),
            Make("red", "红", 0xB02E26),
            Make("black", "黑", 0x1D1D21),
        };

        /// <summary>色名补齐大小写/空格；认不出的名字落到白色（存档不受信任，别把牌面刷成透明）</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Default;
            }

            string trimmed = name.Trim().ToLowerInvariant();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Name == trimmed)
                {
                    return trimmed;
                }
            }

            return Default;
        }

        public static Color ColorOf(string name)
        {
            string normalized = Normalize(name);
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Name == normalized)
                {
                    return All[i].Text;
                }
            }

            return Color.white;
        }

        public static string LabelOf(string name)
        {
            string normalized = Normalize(name);
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Name == normalized)
                {
                    return All[i].Label;
                }
            }

            return normalized;
        }

        public static int IndexOf(string name)
        {
            string normalized = Normalize(name);
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Name == normalized)
                {
                    return i;
                }
            }

            return 0;
        }

        static Entry Make(string name, string label, int hex)
        {
            Entry entry;
            entry.Name = name;
            entry.Label = label;
            entry.Text = new Color(
                ((hex >> 16) & 0xFF) / 255f,
                ((hex >> 8) & 0xFF) / 255f,
                (hex & 0xFF) / 255f,
                1f);
            return entry;
        }
    }
}
