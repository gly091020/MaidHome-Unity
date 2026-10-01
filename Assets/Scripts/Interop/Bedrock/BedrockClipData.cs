using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>一条烘焙好的动画，还没变成 AnimationClip。缓存文件存的也是这个。</summary>
    public sealed class BedrockClipData
    {
        public string Name = "";
        public float Length;
        public WrapMode WrapMode = WrapMode.ClampForever;
        public readonly List<BedrockBoneTrack> Tracks = new List<BedrockBoneTrack>();

        public int KeyframeCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Tracks.Count; i++)
                {
                    total += Tracks[i].Count;
                }

                return total;
            }
        }
    }
}
