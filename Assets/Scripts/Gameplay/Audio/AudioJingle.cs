using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>
    /// 现场合成的短音效。moreanimation 的连击彩蛋用的是 Minecraft 自带的
    /// UI_TOAST_CHALLENGE_COMPLETE，那份音频不能随包发，这里合成一个类似的：
    /// 上行琶音 + 钟声式衰减。合成结果按名字缓存，只算一次。
    /// </summary>
    public static class AudioJingle
    {
        const int SampleRate = 44100;

        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        /// <summary>100 连抽的彩蛋音：C5-E5-G5-C6 上行，最后一下拖长</summary>
        public static AudioClip Milestone()
        {
            const string key = "milestone";
            AudioClip cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            AudioClip clip = Build("maidhome_milestone",
                new[] { 523.25f, 659.25f, 783.99f, 1046.5f },
                new[] { 0.1f, 0.1f, 0.1f, 0.34f });
            Cache[key] = clip;
            return clip;
        }

        static AudioClip Build(string name, float[] notes, float[] lengths)
        {
            float total = 0f;
            for (int i = 0; i < lengths.Length && i < notes.Length; i++)
            {
                total += lengths[i];
            }

            int samples = Mathf.Max(1, Mathf.RoundToInt(total * SampleRate));
            float[] data = new float[samples];
            int cursor = 0;
            for (int n = 0; n < notes.Length && n < lengths.Length; n++)
            {
                int count = Mathf.RoundToInt(lengths[n] * SampleRate);
                for (int i = 0; i < count && cursor + i < samples; i++)
                {
                    float t = (float)i / SampleRate;
                    // 4ms 起音 + 指数衰减，避免爆音
                    float attack = Mathf.Min(1f, t / 0.004f);
                    float envelope = attack * Mathf.Exp(-6f * t / Mathf.Max(0.01f, lengths[n]));
                    float phase = 2f * Mathf.PI * notes[n] * t;
                    float value = Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2f) * 0.2f;
                    data[cursor + i] += value * envelope * 0.5f;
                }

                cursor += count;
            }

            // 尾巴再淡出一次，收得干净
            int fade = Mathf.Min(samples, SampleRate / 50);
            for (int i = 0; i < fade; i++)
            {
                data[samples - 1 - i] *= (float)i / fade;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
