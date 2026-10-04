using System;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 甩出去的那块蛋糕：碰到女仆头上的触发体就算命中，只回调一次。
    /// 必须和 Rigidbody 挂在同一个物体上——触发事件是发给带刚体的那一侧的。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidFeedCake : MonoBehaviour
    {
        public Action Hit;
        public Collider Target;

        bool _reported;

        public bool Reported
        {
            get { return _reported; }
        }

        void OnTriggerEnter(Collider other)
        {
            Check(other);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision != null)
            {
                // 头上的命中体是实体盒子，撞上去走的是碰撞事件
                Check(collision.collider);
            }
        }

        void Check(Collider other)
        {
            if (_reported || Target == null || other == null)
            {
                return;
            }

            if (other != Target && !other.transform.IsChildOf(Target.transform))
            {
                return;
            }

            Report();
        }

        public void Report()
        {
            if (_reported)
            {
                return;
            }

            _reported = true;
            if (Hit != null)
            {
                Hit();
            }
        }
    }
}
