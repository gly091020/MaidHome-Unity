using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>一根骨骼烘焙后的采样结果：一串时间点，和每个时间点的局部姿势。</summary>
    public sealed class BedrockBoneTrack
    {
        public string Path = "";
        public float[] Times;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
        public Vector3[] Scales;

        public int Count
        {
            get { return Times == null ? 0 : Times.Length; }
        }
    }
}
