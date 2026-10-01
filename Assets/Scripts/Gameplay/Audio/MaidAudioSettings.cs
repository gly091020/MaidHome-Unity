using UnityEngine;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>
    /// 音频音量设置。挂到 MaidManager / GUI 任意常驻物体上，
    /// 调这里的滑条就会覆盖 MaidAudioService 的静态默认值。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidAudioSettings : MonoBehaviour
    {
        [Range(0f, 1f)] public float MasterVolume = 1f;
        [Range(0f, 1f)] public float VoiceVolume = 1f;
        [Range(0f, 1f)] public float PositionalVolume = 1f;
        [Range(0f, 1f)] public float PreviewVolume = 1f;
        [Tooltip("语音整体增益。源文件录得小就调大，1 = 原始音量，4 = 明显变响")]
        [Range(1f, 8f)] public float VoiceGain = 1f;

        [Header("女仆语音")]
        [Range(0f, 1f)] public float VoiceSpatialBlend;
        public float VoiceMinDistance = 5f;
        public float VoiceMaxDistance = 80f;
        public AudioRolloffMode VoiceRolloff = AudioRolloffMode.Linear;

        void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply")]
        public void Apply()
        {
            MaidAudioService.MasterVolume = MasterVolume;
            MaidAudioService.VoiceVolume = VoiceVolume;
            MaidAudioService.PositionalVolume = PositionalVolume;
            MaidAudioService.PreviewVolume = PreviewVolume;
            MaidAudioService.VoiceGain = VoiceGain;
            MaidAudioService.VoiceSpatialBlend = VoiceSpatialBlend;
            MaidAudioService.VoiceMinDistance = VoiceMinDistance;
            MaidAudioService.VoiceMaxDistance = VoiceMaxDistance;
            MaidAudioService.VoiceRolloff = VoiceRolloff;
        }
    }
}
