using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 一个碰撞体：挂在哪根骨骼的物体上、用什么形状。
    ///
    /// 一个"部位"（合成块）里可以有好几个：例如头部块 = 头 + 脸 + 头发 + 耳朵，
    /// 每块网格一个凸 MeshCollider，但都挂在**同一根刚体**（部位根那根）下面 ——
    /// 骨骼自己没有刚体，碰撞体就自动属于祖先那根刚体，这就是 Unity 的复合碰撞体。
    /// MeshCollider 用的是各骨骼局部空间的网格，不用重新烘。
    /// </summary>
    public struct MaidRagdollCollider
    {
        /// <summary>碰撞体挂在这根骨骼的物体上。</summary>
        public Transform Bone;
        /// <summary>非空 = 凸 MeshCollider（网格就是这根骨骼的局部空间）。</summary>
        public Mesh Mesh;
        /// <summary>Mesh 为空时用它：骨骼局部空间里的盒。</summary>
        public Vector3 Center;
        public Vector3 Size;

        public bool IsMesh { get { return Mesh != null; } }

        public float Volume
        {
            get { return Mathf.Abs(Size.x * Size.y * Size.z); }
        }
    }
}
