namespace MaidHome.Interop.Ai
{
    public sealed class AiChatProfile
    {
        public string MaidName = "";
        public string OwnerName = "";
        public string ModelId = "";
        public string ChatLanguage = "zh_cn";
        public string CustomSetting = "";
        public string ResolvedSystemPrompt = "";
        public string GeneratedPersona = "";
        public string PersonaSource = "missing";

        public string EffectivePersona
        {
            get { return string.IsNullOrEmpty(GeneratedPersona) ? ResolvedSystemPrompt : GeneratedPersona; }
        }
    }
}
