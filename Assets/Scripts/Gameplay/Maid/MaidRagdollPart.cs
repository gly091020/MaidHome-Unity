using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 一个物理块。两种来源：
    /// * **识别到的部位**（头 / 身体 / 左右手 / 左右脚 / 裙子 / 尾巴）：整棵子树里的网格
    ///   合成一块 —— "头 + 脸 + 头发 + 耳朵"是一块，"裙子 + 各片裙摆"是一块；
    /// * 没识别到的网格：自己一块（就是原来的逐网格方案）。
    ///
    /// 刚体挂在 <see cref="Bone"/>（部位根骨骼）上；碰撞体挂在该网格自己的骨骼上
    /// （见 <see cref="MaidRagdollCollider"/>），骨骼没有刚体，碰撞体自动属于上面那根
    /// 刚体，所以一个部位可以带好几块网格碰撞体，网格也不用重新烘。
    /// </summary>
    public sealed class MaidRagdollPart
    {
        public int Index;
        /// <summary>部位名（head / body / skirt / …）；没识别到的就是网格骨骼名。</summary>
        public string Label;

        public Transform Bone;
        public string BoneName;

        /// <summary>连到哪个块上（根块是 -1）。</summary>
        public int Parent = -1;

        /// <summary>这块里的全部碰撞体（每块网格一个）。</summary>
        public readonly List<MaidRagdollCollider> Colliders = new List<MaidRagdollCollider>();

        /// <summary>这块的包围盒（模型根局部空间）：报告、找重叠父级都用它。</summary>
        public Bounds RootBounds;

        public Vector3 Size { get { return RootBounds.size; } }

        /// <summary>关节位置（世界坐标）：和父块真的相交时放在相交区域中心，否则放骨骼轴。</summary>
        public Vector3 JointAnchor;
        public bool UseJointAnchor;

        /// <summary>命中的名字规则（没命中就是空）。</summary>
        public string Matched;
        /// <summary>这块合了多少块网格。</summary>
        public int MeshCount = 1;

        public string ColliderLabel
        {
            get { return MeshCount + "网格/" + Colliders.Count + "碰撞体"; }
        }

        public float ColliderVolume
        {
            get
            {
                float volume = 0f;
                for (int i = 0; i < Colliders.Count; i++)
                {
                    volume += Colliders[i].Volume;
                }

                return volume;
            }
        }
    }
}
