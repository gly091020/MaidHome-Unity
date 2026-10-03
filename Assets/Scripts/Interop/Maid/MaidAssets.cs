using System.Collections.Generic;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Interop.Maid
{
    /// <summary>一份女仆资源：模型根节点 + 全部动画 + 贴图。可能来自缓存，也可能是刚转出来的。</summary>
    public sealed class MaidAssets
    {
        public MaidSaveData Maid;
        public GameObject Root;
        public Texture2D Texture;
        /// <summary>
        /// 运行时一般是空的：AnimationClip 改由 BedrockAnimationPlayer 在第一次播到某条动画时按需烘
        /// （一次全烘出来要三十多万次 SetCurve，进游戏会卡几秒）。动画条数看 ClipData。
        /// </summary>
        public readonly List<AnimationClip> Clips = new List<AnimationClip>();
        public readonly List<BedrockClipData> ClipData = new List<BedrockClipData>();
        /// <summary>按硬编码规则关掉的节点名，给报告用的</summary>
        public readonly List<string> HiddenNodes = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool FromCache;
        public float BuildSeconds;

        public AnimationClip FindClip(string name)
        {
            for (int i = 0; i < Clips.Count; i++)
            {
                if (Clips[i] != null && Clips[i].name == name)
                {
                    return Clips[i];
                }
            }

            return null;
        }

        /// <summary>把建出来的对象销毁。编辑器里不播放时要用 DestroyImmediate，那边自己处理。</summary>
        public void Dispose()
        {
            if (Root != null)
            {
                Object.Destroy(Root);
                Root = null;
            }

            Clips.Clear();
            ClipData.Clear();
        }
    }
}
