using System;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆能走动的范围，一个 xz 平面上的方盒。只做几何，不碰 Unity 的随机数，
    /// 方便单测。以后换成"从方块数据来的可通行格"时，把这里换掉就行。
    /// </summary>
    [Serializable]
    public sealed class WanderArea
    {
        [Tooltip("方盒中心（运行时由 MaidWanderer 设成出生点）")]
        public Vector3 Center = Vector3.zero;

        [Tooltip("方盒大小，只有 x/z 有用")]
        public Vector3 Size = new Vector3(8f, 2f, 8f);

        [Tooltip("目标点至少要比当前位置远这么多，不然不值得走一趟")]
        public float MinDistance = 1.5f;

        public bool Contains(Vector3 point)
        {
            Vector3 min = Center - Size * 0.5f;
            Vector3 max = Center + Size * 0.5f;
            return point.x >= min.x && point.x <= max.x
                && point.z >= min.z && point.z <= max.z;
        }

        /// <summary>从 from 出发随机挑一个够远的目标点；挑不到返回 false，调用方应该原地再等一轮。</summary>
        public bool TryPick(Vector3 from, System.Random rng, int maxTries, out Vector3 point)
        {
            // 范围比最小距离还小的时候把最小距离压下来，否则永远挑不到
            float reach = 0.5f * new Vector2(Size.x, Size.z).magnitude;
            float minDistance = Mathf.Min(MinDistance, reach);

            for (int i = 0; i < maxTries; i++)
            {
                Vector3 candidate = new Vector3(
                    Center.x - Size.x * 0.5f + (float)rng.NextDouble() * Size.x,
                    Center.y,
                    Center.z - Size.z * 0.5f + (float)rng.NextDouble() * Size.z);

                Vector3 delta = candidate - from;
                delta.y = 0f;
                if (delta.magnitude >= minDistance)
                {
                    point = candidate;
                    return true;
                }
            }

            point = from;
            return false;
        }
    }
}
