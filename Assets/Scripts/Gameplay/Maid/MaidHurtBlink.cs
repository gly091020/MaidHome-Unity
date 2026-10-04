using System;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 受伤闭眼：方块（SimpleBedrockModel 那类）模型里有个 blink 节点，就是盖在脸上的整张闭眼贴片
    /// （平面、比脸往前偏 0.001），`MaidAssetLoader.HiddenNodes` 平时把它藏起来（所以平时是睁眼）。
    /// 受伤时把它亮一下再收回去，当作这类模型的"被打"表现——它们没有 GeckoLib 的 attacked 动画可借。
    /// 节点不存在（GeckoLib 模型）就什么都不做。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidHurtBlink : MonoBehaviour
    {
        [Tooltip("闭眼节点名，模型里盖在脸上的那张闭眼贴片")]
        [SerializeField] private string _nodeName = "blink";
        [Tooltip("受伤时闭眼保持多久")]
        [SerializeField] private float _seconds = 0.25f;
        [Tooltip("闭眼贴片沿脸的正前方再推多少（世界单位）。原模型只比脸往前 0.001 像素，Unity 里深度精度不够会 z-fighting")]
        [SerializeField] private float _forwardOffset = 0.02f;

        Transform _node;
        Vector3 _restPosition;
        float _remaining;
        bool _closed;

        /// <summary>这个模型有没有闭眼节点，没有的话 Play 是空操作</summary>
        public bool HasNode
        {
            get { return _node != null; }
        }

        void Awake()
        {
            _node = FindNode();
            if (_node != null)
            {
                _restPosition = _node.localPosition;
            }

            Hide();
        }

        void OnDisable()
        {
            Hide();
        }

        /// <summary>受伤时闭一下眼，已经在闭就重新计时</summary>
        public void Play()
        {
            if (_node == null || _closed)
            {
                return;
            }

            _remaining = Mathf.Max(0.05f, _seconds);
            if (!_node.gameObject.activeSelf)
            {
                _node.gameObject.SetActive(true);
            }
        }

        /// <summary>一直闭着眼（睡觉用）。true 就亮着不放，false 还原。</summary>
        public void SetClosed(bool closed)
        {
            _closed = closed;
            _remaining = 0f;
            if (_node == null)
            {
                return;
            }

            if (_node.gameObject.activeSelf != closed)
            {
                _node.gameObject.SetActive(closed);
            }
        }

        // LateUpdate：legacy Animation 在这一帧的普通 Update 阶段写姿势，晚一步补偏移才不会被盖回去
        void LateUpdate()
        {
            if (_closed)
            {
                ApplyForwardOffset();
                return;
            }

            if (_remaining <= 0f)
            {
                return;
            }

            // 动画每帧会把 blink 的 localPosition 钉回静止姿势，所以偏移得在动画之后、闭眼的这段时间里逐帧补
            ApplyForwardOffset();

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Hide();
            }
        }

        /// 往"脸的正面"推，而不是模型根的前方：这样头转过去以后推的方向还是贴着这张脸
        void ApplyForwardOffset()
        {
            if (_node == null || _forwardOffset == 0f)
            {
                return;
            }

            Transform parent = _node.parent;
            if (parent == null)
            {
                return;
            }

            _node.localPosition = _restPosition
                + parent.InverseTransformDirection(parent.forward) * _forwardOffset;
        }

        void Hide()
        {
            _remaining = 0f;
            if (_node != null && _node.gameObject.activeSelf)
            {
                _node.gameObject.SetActive(false);
            }
        }

        /// 隐藏节点也是节点，必须带 includeInactive 找
        Transform FindNode()
        {
            if (string.IsNullOrEmpty(_nodeName))
            {
                return null;
            }

            Transform[] nodes = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (string.Equals(nodes[i].name, _nodeName, StringComparison.OrdinalIgnoreCase))
                {
                    return nodes[i];
                }
            }

            return null;
        }
    }
}
