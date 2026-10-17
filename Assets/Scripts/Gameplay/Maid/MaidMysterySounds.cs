using MaidHome.Core.Settings;
using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 神必音效：普通受伤和布娃娃受伤各一条，资源由用户填写。
    /// 路径：Resources/MoreAnimation/mystery_hurt、mystery_ragdoll。
    /// </summary>
    public static class MaidMysterySounds
    {
        static AudioSource _source;
        static MaidMysterySoundConfig _config;
        static AudioClip _hurt;
        static AudioClip _ragdoll;
        static bool _loaded;

        public static void Hurt(MaidAgent agent, bool ragdoll)
        {
            if (agent == null || !MaidInjuryRules.Allows(agent)
                || !GameSettingsStore.Current.MysterySoundEnabled)
            {
                return;
            }

            Load();
            AudioClip clip = ragdoll ? _ragdoll : _hurt;
            if (clip == null)
            {
                return;
            }

            EnsureSource();
            _source.volume = Mathf.Clamp01(MaidAudioService.MasterVolume) * 0.85f;
            _source.PlayOneShot(clip, 1f);
        }

        static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _config = Resources.Load<MaidMysterySoundConfig>("MaidMysterySoundConfig");
            if (_config != null)
            {
                _hurt = _config.Hurt;
                _ragdoll = _config.Ragdoll;
            }

            // 没有在资源里填时，继续兼容按名字放在 Resources/MoreAnimation 下的音频
            if (_hurt == null)
            {
                _hurt = Resources.Load<AudioClip>("MoreAnimation/mystery_hurt");
            }

            if (_ragdoll == null)
            {
                _ragdoll = Resources.Load<AudioClip>("MoreAnimation/mystery_ragdoll");
            }

        }

        static void EnsureSource()
        {
            if (_source != null)
            {
                return;
            }

            GameObject root = new GameObject("MaidMysterySounds");
            Object.DontDestroyOnLoad(root);
            _source = root.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }
    }
}
