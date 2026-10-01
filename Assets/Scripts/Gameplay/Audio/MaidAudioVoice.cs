using UnityEngine;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>挂在女仆根节点上的 3D 语音源。同一只女仆同时只播一条语音。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class MaidAudioVoice : MonoBehaviour
    {
        /// 音量上限，防止 Inspector 里填出爆音的值
        public const float MaxVolume = 4f;

        AudioSource _source;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            ApplySettings();
        }

        void ApplySettings()
        {
            _source.playOnAwake = false;
            _source.spatialBlend = MaidAudioService.VoiceSpatialBlend;
            _source.minDistance = MaidAudioService.VoiceMinDistance;
            _source.maxDistance = MaidAudioService.VoiceMaxDistance;
            _source.rolloffMode = MaidAudioService.VoiceRolloff;
            _source.dopplerLevel = 0f;
        }

        public void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || _source == null)
            {
                return;
            }

            ApplySettings();
            _source.Stop();
            _source.clip = clip;
            _source.volume = Mathf.Clamp(volume, 0f, MaxVolume);
            _source.pitch = pitch;
            _source.Play();
        }

        public void Stop()
        {
            if (_source != null)
            {
                _source.Stop();
            }
        }
    }
}
