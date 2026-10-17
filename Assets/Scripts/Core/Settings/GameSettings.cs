using System;
using MaidHome.Interop.Portal;
using UnityEngine;

namespace MaidHome.Core.Settings
{
    public enum InjuryMode
    {
        AllOff,
        WineFoxOnly,
        StrictWineFoxOnly,
        All,
    }

    /// <summary>设置数据只放字段和按钮方法；界面按 Attribute 反射生成。</summary>
    [Serializable]
    public sealed class GameSettings
    {
        /// <summary>配置格式版本；不显示在设置界面。</summary>
        public int Version = 1;

        [Setting("背景声音", "0~100%", 10, Min = 0f, Max = 100f, Slider = true, Suffix = "%",
            Section = "声音设置")]
        public int BackgroundVolume = 60;

        [Setting("女仆音效声音", "0~100%", 20, Min = 0f, Max = 100f, Slider = true, Suffix = "%",
            Section = "声音设置")]
        public int MaidVolume = 100;

        [Setting("流眼泪", "默认开启", 30, Section = "交互设置")]
        public bool TearsEnabled = true;

        [Setting("受伤时手机震动", "默认关闭", 40, Section = "交互设置")]
        public bool VibrateOnHurt;

        [Setting("震动强度", "0~100%；仅支持振幅控制的 Android 设备", 45,
            Min = 0f, Max = 100f, Slider = true, Suffix = "%", Section = "交互设置")]
        public int VibrateStrength = 60;

        [Setting("神必音效", "所有受伤播放额外音效；布娃娃使用另一条", 50, Section = "交互设置")]
        public bool MysterySoundEnabled;

        [Setting("受伤模式", "全关 / 仅酒狐 / 仅严格酒狐 / 全开", 60,
            EnumLabels = new string[] { "全关", "仅酒狐", "仅严格酒狐", "全开" },
            Section = "交互设置")]
        public InjuryMode Injury = InjuryMode.All;

        [Setting("传送门端口", "默认 7411", 70, Min = 1f, Max = 65535f,
            Section = "传送门设置")]
        public int PortalPort = PortalServer.DefaultPort;

        [SettingButton("关于", "查看关于信息", 100, Section = "关于")]
        public void ShowAbout()
        {
            if (SettingsPanel.Instance != null)
            {
                SettingsPanel.Instance.OpenAbout();
            }
        }
    }
}
