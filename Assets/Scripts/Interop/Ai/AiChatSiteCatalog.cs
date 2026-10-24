using MaidHome.Core.Settings;

namespace MaidHome.Interop.Ai
{
    public static class AiChatSiteCatalog
    {
        public static AiChatSiteInfo Get(AiChatSite site)
        {
            switch (site)
            {
                case AiChatSite.OpenAi:
                    return Create(site, "OpenAI", "https://api.openai.com/v1/chat/completions", "gpt-4o-mini");
                case AiChatSite.SiliconFlow:
                    return Create(site, "硅基流动", "https://api.siliconflow.cn/v1/chat/completions",
                        "Qwen/Qwen3.5-397B-A17B");
                case AiChatSite.Moonshot:
                    return Create(site, "Moonshot", "https://api.moonshot.cn/v1/chat/completions",
                        "kimi-k2.5");
                default:
                    return Create(AiChatSite.DeepSeek, "DeepSeek", "https://api.deepseek.com/chat/completions",
                        "deepseek-v4-flash");
            }
        }

        static AiChatSiteInfo Create(AiChatSite site, string displayName, string url, string model)
        {
            AiChatSiteInfo info = new AiChatSiteInfo();
            info.Site = site;
            info.DisplayName = displayName;
            info.Url = url;
            info.Model = model;
            return info;
        }
    }
}
