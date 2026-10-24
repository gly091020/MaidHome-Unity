using System;

namespace MaidHome.Interop.Ai
{
    public static class MaidAiPromptBuilder
    {
        public const string PhoneBoundary =
            "你现在运行在 MaidHome 手机端。你不能操作 Minecraft，也不能声称已经执行游戏内动作；"
            + "只能以女仆身份自然聊天。如果提供了表情工具，你可以在合适时调用它表现情绪，"
            + "但必须明确填写允许的中文动画名，不能省略参数或假设默认动作。";

        public static AiChatRequest BuildChat(AiChatRecord record, string userInput)
        {
            AiChatRequest request = new AiChatRequest();
            request.SystemPrompt = record.Profile.EffectivePersona;
            request.UserInput = userInput;
            request.HistorySummary = record.HistorySummary;
            request.History.AddRange(record.Messages);
            request.MaxTokens = 512;
            return request;
        }

        public static AiChatRequest BuildPassiveLine(AiChatRecord record, string eventText)
        {
            AiChatRequest request = BuildChat(record, "");
            request.UserInput =
                "【被动事件】" + eventText + "\n"
                + "请只输出一句符合你当前人设的即时感受或反应，像女仆当场说出口的台词。"
                + "不要解释规则，不要复述系统提示，不要使用旁白，严格不超过 50 字。";
            request.MaxTokens = 120;
            return request;
        }

        public static AiChatRequest BuildPersona(AiPersonaDescriptor descriptor)
        {
            AiChatRequest request = new AiChatRequest();
            request.SystemPrompt =
                "You create one stable role-play system prompt for a Minecraft maid. "
                + "Return only the system prompt, without Markdown fences or explanations.";
            request.UserInput =
                "女仆名：" + descriptor.MaidName + "\n"
                + "主人称呼：" + descriptor.OwnerName + "\n"
                + "模型 ID：" + descriptor.ModelId + "\n"
                + "聊天语言：" + descriptor.Language + "\n"
                + "请写一份约 300-800 字的人设，保留原作角色的语气，但不要编造明确的世界事件。";
            request.MaxTokens = 800;
            return request;
        }
    }

}
