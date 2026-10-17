using MaidHome.Core.Settings;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>受伤模式：全关 / 仅酒狐 / 仅严格酒狐 / 全开。</summary>
    public static class MaidInjuryRules
    {
        public static bool Allows(MaidAgent agent)
        {
            GameSettings settings = GameSettingsStore.Current;
            switch (settings.Injury)
            {
                case InjuryMode.AllOff:
                    return false;
                case InjuryMode.WineFoxOnly:
                    return IsWineFox(agent);
                case InjuryMode.StrictWineFoxOnly:
                    return IsWineFox(agent) && !IsSimpleBedrock(agent);
                default:
                    return true;
            }
        }

        /// <summary>名字带「酒狐」，或模型标识同时包含 wine 和 fox。</summary>
        public static bool IsWineFox(MaidAgent agent)
        {
            if (agent == null || agent.Save == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(agent.Save.Name) && agent.Save.Name.Contains("酒狐"))
            {
                return true;
            }

            string model = (agent.Save.Id + " " + agent.Save.ModelFile + " "
                + agent.Save.TextureFile + " " + agent.Save.AnimationFile + " "
                + agent.Save.Folder).ToLowerInvariant();
            return model.Contains("wine") && model.Contains("fox");
        }

        static bool IsSimpleBedrock(MaidAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            if (agent.Save != null && agent.Save.SimpleBedrockModel)
            {
                return true;
            }

            return agent.GetComponent<MaidSimpleBedrockAnimator>() != null
                && MaidSimpleBedrockAnimator.HasDefaultRig(agent.transform);
        }
    }
}
