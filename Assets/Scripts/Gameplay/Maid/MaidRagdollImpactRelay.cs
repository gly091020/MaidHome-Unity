using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 挂在每块布娃娃刚体上，把碰撞转回 MaidRagdoll。
    /// 布娃娃的物理组件是运行时生成的，这个转发器也跟它们一起清理。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidRagdollImpactRelay : MonoBehaviour
    {
        public MaidRagdoll Owner { get; set; }

        void OnCollisionEnter(Collision collision)
        {
            if (Owner == null)
            {
                return;
            }

            Rigidbody other = collision.collider != null ? collision.collider.attachedRigidbody : null;
            if (other != null && other.transform.IsChildOf(Owner.transform))
            {
                return;
            }

            Owner.ReportImpact(collision);
        }
    }
}
