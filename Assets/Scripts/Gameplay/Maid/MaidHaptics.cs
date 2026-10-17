using MaidHome.Core.Platform;
using MaidHome.Core.Settings;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>受伤震动；默认关闭，连续受伤有短冷却。</summary>
    public static class MaidHaptics
    {
        static float _nextAt;

        public static void Hurt(MaidAgent agent)
        {
            if (agent == null || !MaidInjuryRules.Allows(agent)
                || !GameSettingsStore.Current.VibrateOnHurt || Time.unscaledTime < _nextAt)
            {
                return;
            }

            int strength = Mathf.Clamp(GameSettingsStore.Current.VibrateStrength, 0, 100);
            if (strength <= 0)
            {
                return;
            }

            _nextAt = Time.unscaledTime + 0.25f;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (MaidVibration.Vibrate(strength, 120))
            {
                return;
            }
#endif

#if (UNITY_ANDROID && !UNITY_EDITOR) || (UNITY_IOS && !UNITY_EDITOR)
            Handheld.Vibrate();
#endif
        }
    }
}
