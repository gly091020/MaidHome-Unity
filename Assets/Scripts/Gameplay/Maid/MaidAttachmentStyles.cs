namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 各挂件的配置放在哪：以场景里手摆的那份 Interaction 上的配置为准（它在 Awake 里注册进来），
    /// 没人注册（例如纯 AR 场景没挂控制器）就用这里的默认值。
    /// 这样"女仆面板里挂上"和"从存档恢复时挂上"用的是同一套参数，不会出现两处调不一样的怪事。
    /// </summary>
    public static class MaidAttachmentStyles
    {
        static readonly MaidAttachmentStyle SignDefault = new MaidAttachmentStyle
        {
            SizeRatio = 0.3f,
        };

        static readonly MaidAttachmentStyle MaskDefault = new MaidAttachmentStyle
        {
            SizeRatio = 1.15f,
        };

        static MaidAttachmentStyle _sign;
        static MaidAttachmentStyle _mask;

        /// <summary>牌子的配置（两种模型共用；方块模型的额外修正写在 MaidAttachmentStyle 里）</summary>
        public static MaidAttachmentStyle Sign
        {
            get { return _sign != null ? _sign : SignDefault; }
        }

        public static MaidAttachmentStyle Mask
        {
            get { return _mask != null ? _mask : MaskDefault; }
        }

        public static void UseSign(MaidAttachmentStyle style)
        {
            if (style != null)
            {
                _sign = style;
            }
        }

        public static void UseMask(MaidAttachmentStyle style)
        {
            if (style != null)
            {
                _mask = style;
            }
        }

        /// <summary>
        /// 是不是方块模型（Java 程序化驱动的那类）。判定和工程其它地方一致：
        /// 以 `MaidSimpleBedrockAnimator` 在不在、启没启用为准，不看存档标记。
        /// </summary>
        public static bool IsSimpleBedrock(MaidAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            MaidSimpleBedrockAnimator animator = agent.GetComponent<MaidSimpleBedrockAnimator>();
            return animator != null && animator.enabled;
        }

    }
}
