using System;
using System.Collections.Generic;
using MaidHome.Gameplay.Maid;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>
    /// 女仆音频公共入口。游戏逻辑只调这里，不直接碰 AudioSource 和文件路径。
    /// 例：MaidAudioService.Play(agent, MaidSoundId.Idle);
    /// </summary>
    public static class MaidAudioService
    {
        public static float MasterVolume = 1f;
        public static float VoiceVolume = 1f;
        public static float PositionalVolume = 1f;
        public static float PreviewVolume = 1f;
        /// <summary>女仆语音的整体增益，源文件录得小就调大（&gt;1 会做采样放大）</summary>
        public static float VoiceGain = 1f;

        // 3渲2 + 正交远景没有“玩家耳朵位置”，女仆语音按 2D 播，避免距离衰减
        public static float VoiceSpatialBlend = 0f;
        public static float VoiceMinDistance = 5f;
        public static float VoiceMaxDistance = 80f;
        public static AudioRolloffMode VoiceRolloff = AudioRolloffMode.Linear;

        public static void Play(MaidAgent agent, string eventId, bool test = false, float volumeScale = 1f)
        {
            if (agent == null || agent.Save == null || string.IsNullOrEmpty(agent.Save.SoundPackId))
            {
                return;
            }

            if (!test && !RollFrequency(agent.Save.SoundFrequency))
            {
                return;
            }

            MaidAudioRuntime.Instance.PlayVoice(agent, agent.Save.SoundPackId, eventId, test, volumeScale);
        }

        public static void Play(MaidSaveData maid, Vector3 position, string eventId, bool test = false)
        {
            if (maid == null || string.IsNullOrEmpty(maid.SoundPackId))
            {
                return;
            }

            if (!test && !RollFrequency(maid.SoundFrequency))
            {
                return;
            }

            MaidAudioRuntime.Instance.PlayAt(maid.SoundPackId, eventId, position, 1f, 1f, test);
        }

        public static void PlayAt(string soundPackId, string eventId, Vector3 position, float volume = 1f,
            float pitch = 1f, bool test = false)
        {
            if (string.IsNullOrEmpty(soundPackId))
            {
                return;
            }

            MaidAudioRuntime.Instance.PlayAt(soundPackId, eventId, position, volume, pitch, test);
        }

        public static void Preview(string soundPackId, string eventId)
        {
            if (string.IsNullOrEmpty(soundPackId))
            {
                return;
            }

            MaidAudioRuntime.Instance.Preview(soundPackId, eventId);
        }

        public static void Stop(MaidAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            MaidAudioVoice voice = agent.GetComponent<MaidAudioVoice>();
            if (voice != null)
            {
                voice.Stop();
            }
        }

        public static string[] ListSoundPacks()
        {
            return MaidSoundLibrary.ListPackIds();
        }

        public static string[] ListEvents(string soundPackId)
        {
            MaidSoundPack pack = MaidSoundLibrary.Get(soundPackId);
            if (pack == null)
            {
                return new string[0];
            }

            List<string> events = new List<string>(pack.EventIds);
            events.Sort(StringComparer.OrdinalIgnoreCase);
            return events.ToArray();
        }

        static bool RollFrequency(float frequency)
        {
            if (frequency <= 0f)
            {
                return false;
            }

            if (frequency >= 1f)
            {
                return true;
            }

            return UnityEngine.Random.value <= frequency;
        }
    }
}
