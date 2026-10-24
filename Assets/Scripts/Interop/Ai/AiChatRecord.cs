using System;
using System.Collections.Generic;

namespace MaidHome.Interop.Ai
{
    public sealed class AiChatRecord
    {
        public const int CurrentVersion = 1;
        public const int MaxMessages = 512;

        public int SchemaVersion = CurrentVersion;
        public string MaidUuid = "";
        public string HistorySummary = "";
        public int LastTokenUsage;
        public int BaseMessageCount;
        public int SlapHitCount;
        public int CakeHitCount;
        public string LlmSite = "";
        public string LlmModel = "";
        public AiChatProfile Profile = new AiChatProfile();
        public readonly List<AiChatMessage> Messages = new List<AiChatMessage>();

        public AiChatMessage Append(string role, string text, string origin, string status, string requestId)
        {
            AiChatMessage message = new AiChatMessage();
            message.Id = Guid.NewGuid().ToString("N");
            message.RequestId = requestId ?? "";
            message.Role = role;
            message.Text = text ?? "";
            message.RawText = message.Text;
            message.GameTime = 0L;
            message.Origin = origin;
            message.Status = status;
            Messages.Add(message);
            Trim();
            return message;
        }

        void Trim()
        {
            while (Messages.Count > MaxMessages)
            {
                Messages.RemoveAt(0);
                if (BaseMessageCount > 0)
                {
                    BaseMessageCount--;
                }
            }
        }
    }
}
