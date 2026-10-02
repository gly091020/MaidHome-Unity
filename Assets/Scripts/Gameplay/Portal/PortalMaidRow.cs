using System;
using MaidHome.Interop.Maid;
using MaidHome.Interop.Portal;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Portal
{
    /// <summary>
    /// 女仆列表里的一行。挂在你自己做的行预制体上，两个字段在 Inspector 里连现成的子物体。
    /// 面板负责 Instantiate 它，它自己只管显示和把点击转出去。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalMaidRow : MonoBehaviour
    {
        [Tooltip("显示名字等级的 Text")]
        [SerializeField] private Text _label;

        [Tooltip("这一行的发送按钮")]
        [SerializeField] private Button _sendButton;

        [Tooltip("按钮上的字，留空就不用")]
        [SerializeField] private Text _buttonLabel;

        Action<string> _onSend;
        string _maidId = "";
        bool _sendable;

        void Awake()
        {
            if (_sendButton != null)
            {
                _sendButton.onClick.AddListener(OnSendClicked);
            }
        }

        void OnDestroy()
        {
            if (_sendButton != null)
            {
                _sendButton.onClick.RemoveListener(OnSendClicked);
            }
        }

        /// <summary>blockReason 为空表示能发；不为空就原样显示出来并把按钮置灰。</summary>
        public void Bind(MaidSaveData maid, string blockReason, Action<string> onSend)
        {
            _maidId = maid != null ? maid.Id : "";
            _onSend = onSend;
            _sendable = string.IsNullOrEmpty(blockReason);

            if (_label != null)
            {
                string name = maid != null ? PortalStorage.DisplayNameOf(maid.Id, maid.Name) : "";
                _label.text = name + "  Lv." + (maid != null ? maid.Level : 0)
                    + (_sendable ? "" : "   (" + blockReason + ")");
            }

            if (_buttonLabel != null)
            {
                _buttonLabel.text = "发送";
            }

            if (_sendButton != null)
            {
                _sendButton.interactable = _sendable;
            }
        }

        /// <summary>有传输在跑的时候面板会把所有行的按钮按下去，避免连点。</summary>
        public void SetInteractable(bool value)
        {
            if (_sendButton != null)
            {
                _sendButton.interactable = value && _sendable;
            }
        }

        void OnSendClicked()
        {
            if (_onSend != null && !string.IsNullOrEmpty(_maidId))
            {
                _onSend(_maidId);
            }
        }
    }
}
