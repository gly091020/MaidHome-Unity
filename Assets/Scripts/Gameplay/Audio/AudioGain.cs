using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>
    /// 把「音源文件本身就录得小」的素材放大。不能靠 AudioSource.volume：
    /// 那个值实际被夹在 1.0，倍率给上去也没用。所以直接把解码后的采样乘一遍，
    /// 再过 tanh 软削波（比硬切好听）。结果按 clip + 倍率缓存，只算一次。
    /// </summary>
    public static class AudioGain
    {
        const float MinGain = 1.01f;

        static readonly Dictionary<long, AudioClip> Cache = new Dictionary<long, AudioClip>();
        static readonly HashSet<int> Warned = new HashSet<int>();

        public static AudioClip Amplify(AudioClip clip, float gain)
        {
            if (clip == null || gain < MinGain)
            {
                return clip;
            }

            long key = ((long)clip.GetInstanceID() << 16) ^ Mathf.RoundToInt(gain * 100f);
            AudioClip cached;
            if (Cache.TryGetValue(key, out cached))
            {
                return cached != null ? cached : clip;
            }

            int count = clip.samples * clip.channels;
            if (count <= 0)
            {
                return clip;
            }

            float[] data = new float[count];
            if (!clip.GetData(data, 0))
            {
                // CompressedInMemory 的 clip 读不出采样，只能退回原音量
                if (Warned.Add(clip.GetInstanceID()))
                {
                    Debug.LogWarning("语音放大失败（clip 不是解压状态，导入设置改成 Decompress On Load 即可）: "
                        + clip.name);
                }

                return clip;
            }

            for (int i = 0; i < count; i++)
            {
                data[i] = (float)System.Math.Tanh(data[i] * gain);
            }

            AudioClip louder = AudioClip.Create(clip.name + "_x" + gain.ToString("F1"), clip.samples,
                clip.channels, clip.frequency, false);
            if (!louder.SetData(data, 0))
            {
                return clip;
            }

            Cache[key] = louder;
            return louder;
        }
    }
}
