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
        public int Version = 5;

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

        [Setting("昼夜模式", "MC 时间：20 分钟一天，退出游戏后继续流逝；现实时间：跟随本机时钟", 75,
            EnumLabels = new string[] { "MC 时间", "现实时间" },
            Section = "世界设置")]
        public DayNightMode DayNight = DayNightMode.Minecraft;

        public long DayNightAnchorUtcTicks;
        public long DayNightAnchorDay;
        public float DayNightAnchorTick;

        [Setting("传送门端口", "默认 7411", 70, Min = 1f, Max = 65535f,
            Section = "传送门设置")]
        public int PortalPort = PortalServer.DefaultPort;

        [Setting("启用 AI 功能", "关闭后隐藏聊天入口，并停止所有主动对话、被动对话、表情工具和网络请求", 79,
            Section = "AI 聊天")]
        public bool AiEnabled = true;

        [Setting("AI 站点", "手机端聊天使用的 OpenAI 兼容服务；模型由站点默认值决定", 80,
            EnumLabels = new string[] { "DeepSeek", "OpenAI", "硅基流动", "Moonshot" },
            Section = "AI 聊天")]
        public AiChatSite AiSite = AiChatSite.DeepSeek;

        [Setting("AI Key", "只保存在本机设置，不随女仆档案传输", 81, Secret = true,
            Section = "AI 聊天")]
        public string AiApiKey = "";

        [Setting("人设缺失时自动生成", "默认关闭；开启后首次聊天会调用一次 AI 生成手机端人设", 82,
            Section = "AI 聊天")]
        public bool AiAutoGeneratePersona;

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
