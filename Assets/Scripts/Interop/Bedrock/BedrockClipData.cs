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

        /// <summary>
        /// 采样数据还在缓存文件里没读进来时，用它按需读（进游戏不用把一百多条动画的几十 MB 一次读出来）。
        /// 读之前先清掉自己，读到坏数据也不会每帧重试。内存里本来就有的（刚烘出来的那份）是 null。
        /// </summary>
        public System.Action<BedrockClipData> TrackLoader;

        public bool TracksLoaded
        {
            get { return TrackLoader == null; }
        }

        public void EnsureTracks()
        {
            System.Action<BedrockClipData> loader = TrackLoader;
            if (loader == null)
            {
                return;
            }

            TrackLoader = null;
            try
            {
                loader(this);
            }
            catch (System.Exception error)
            {
                // 缓存文件被删/被截断时只让这一条动画失效，别把游戏搞崩
                UnityEngine.Debug.LogWarning("读取动画采样失败（" + Name + "）: " + error.Message);
            }
        }

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
