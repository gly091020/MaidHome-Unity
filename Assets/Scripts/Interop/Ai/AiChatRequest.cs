using System.Collections.Generic;

namespace MaidHome.Interop.Ai
{
    public sealed class AiChatRequest
    {
        public string SystemPrompt = "";
        public string UserInput = "";
        public string HistorySummary = "";
        public List<AiChatMessage> History = new List<AiChatMessage>();
        public List<AiChatMessage> ExtraMessages = new List<AiChatMessage>();
        public string ToolsJson = "";
        public string ToolChoice = "";
        public int MaxTokens = 512;
    }

}
