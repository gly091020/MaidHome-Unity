using System;

namespace MaidHome.Interop.Portal
{
    public static class PortalKindExtensions
    {
        /// <summary>线上用的名字，改了会破坏和 MC 模组的兼容。</summary>
        public static string ToWire(this PortalKind kind)
        {
            switch (kind)
            {
                case PortalKind.House: return "house";
                case PortalKind.SoundPack: return "sound";
                default: return "maid";
            }
        }

        public static bool TryParse(string text, out PortalKind kind)
        {
            kind = PortalKind.Maid;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            switch (text.Trim().ToLowerInvariant())
            {
                case "maid": kind = PortalKind.Maid; return true;
                case "house": kind = PortalKind.House; return true;
                case "sound":
                case "soundpack":
                case "sound_pack": kind = PortalKind.SoundPack; return true;
                default: return false;
            }
        }

        /// <summary>UI 上显示的类型名。</summary>
        public static string DisplayName(this PortalKind kind)
        {
            switch (kind)
            {
                case PortalKind.House: return "房子";
                case PortalKind.SoundPack: return "音效包";
                default: return "女仆";
            }
        }
    }
}
