namespace MaidHome.Interop.Ai
{
    public sealed class AiChatMessage
    {
        public string Id = "";
        public string RequestId = "";
        public string Role = "";
        public string Text = "";
        public string RawText = "";
        public string TtsText = "";
        public long GameTime;
        public string Origin = "";
        public string Status = "complete";
        public string ToolCallsJson = "";
        public string ToolCallId = "";

        public AiChatMessage Copy()
        {
            return new AiChatMessage
            {
                Id = Id,
                RequestId = RequestId,
                Role = Role,
                Text = Text,
                RawText = RawText,
                TtsText = TtsText,
                GameTime = GameTime,
                Origin = Origin,
                Status = Status,
                ToolCallsJson = ToolCallsJson,
                ToolCallId = ToolCallId
            };
        }
    }
}
