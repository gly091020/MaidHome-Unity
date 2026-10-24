using System;

namespace MaidHome.Core.Settings
{
    /// <summary>标记一个可显示的设置字段；标题、说明、顺序和数值范围都从这里读。</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SettingAttribute : Attribute
    {
        public readonly string Label;
        public readonly string Description;
        public readonly int Order;

        public SettingAttribute(string label, string description = "", int order = 0)
        {
            Label = label;
            Description = description;
            Order = order;
            Min = float.NegativeInfinity;
            Max = float.PositiveInfinity;
        }

        public float Min { get; set; }
        public float Max { get; set; }
        /// <summary>数值型字段是否用滑动条显示。</summary>
        public bool Slider { get; set; }
        /// <summary>滑动条/输入框后缀，例如 "%"。</summary>
        public string Suffix { get; set; }
        /// <summary>输入框按密码显示。</summary>
        public bool Secret { get; set; }
        /// <summary>枚举值的显示名；为空就显示 enum 原名。</summary>
        public string[] EnumLabels { get; set; }
        /// <summary>分组标题，例如「声音设置」。</summary>
        public string Section { get; set; }

        public bool HasRange
        {
            get { return !float.IsNegativeInfinity(Min) && !float.IsPositiveInfinity(Max); }
        }
    }

    /// <summary>标记一个设置面板里的按钮；方法必须无参数。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SettingButtonAttribute : Attribute
    {
        public readonly string Label;
        public readonly string Description;
        public readonly int Order;
        public string Section { get; set; }

        public SettingButtonAttribute(string label, string description = "", int order = 0)
        {
            Label = label;
            Description = description;
            Order = order;
        }
    }
}
