using System.Collections.Generic;
using MaidHome.Interop.Maid;
using MaidHome.Interop.Portal;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Portal
{
    /// <summary>
    /// 传送门面板。场景和 UI 都是你自己搭的，这个脚本只往现成的 Text / Image 里填字、
    /// 按行预制体铺列表，一个 GameObject 都不新建（只 Instantiate 行预制体）。
    /// 按钮不用挂到这里：起服务/停服务/刷新列表直接连 PortalController 上的公开方法。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalPanel : MonoBehaviour
    {
        [SerializeField] private PortalController _controller;

        [Header("状态区（拖场景里现成的物体）")]
        [Tooltip("显示监听/连接状态")]
        [SerializeField] private Text _statusText;
        [Tooltip("显示最近一条结果或错误")]
        [SerializeField] private Text _messageText;
        [Tooltip("Image 的 Type 设成 Filled / Horizontal，这里改 fillAmount")]
        [SerializeField] private Image _progressFill;
        [Tooltip("进度百分比和字节数，留空就不显示")]
        [SerializeField] private Text _progressLabel;

        [Header("列表区")]
        [Tooltip("行预制体往哪个父物体下铺，一般是有 VerticalLayoutGroup 的 Content")]
        [SerializeField] private Transform _listContent;
        [Tooltip("挂在行预制体上的 PortalMaidRow")]
        [SerializeField] private PortalMaidRow _rowPrefab;
        [Tooltip("一只女仆都没有时显示的对象，留空就不用")]
        [SerializeField] private GameObject _emptyHint;

        readonly List<PortalMaidRow> _rows = new List<PortalMaidRow>();
        int _lastRowCount = -1;
        bool _warnedMissingRefs;
        bool _warnedNoLayout;

        void Awake()
        {
            if (_controller == null)
            {
                _controller = GetComponent<PortalController>();
            }
        }

        void OnEnable()
        {
            if (_controller != null)
            {
                _controller.Changed += OnChanged;
            }

            RebuildRows();
        }

        void OnDisable()
        {
            if (_controller != null)
            {
                _controller.Changed -= OnChanged;
            }
        }

        void Update()
        {
            if (_controller == null)
            {
                return;
            }

            if (_statusText != null)
            {
                _statusText.text = StatusText();
            }

            if (_messageText != null)
            {
                _messageText.text = _controller.Message;
            }

            RefreshProgress();
        }

        void OnChanged()
        {
            RebuildRows();
        }

        // ------------------------------------------------------------ 状态

        string StatusText()
        {
            if (!_controller.IsRunning)
            {
                return "未启动";
            }

            if (!_controller.IsClientConnected)
            {
                return "监听 " + _controller.Server.Port + " 端口，等客户端连进来";
            }

            return string.IsNullOrEmpty(_controller.ClientName)
                ? "已连接，等客户端报名字"
                : "已连接: " + _controller.ClientName;
        }

        void RefreshProgress()
        {
            bool active = _controller.IsTransferActive;
            float value = active ? _controller.TransferProgress : 0f;
            if (_progressFill != null)
            {
                _progressFill.fillAmount = value;
            }

            if (_progressLabel != null)
            {
                _progressLabel.text = active
                    ? Mathf.RoundToInt(value * 100f) + "%  "
                        + Bytes(_controller.Server.TransferDone) + " / "
                        + Bytes(_controller.Server.TransferTotal)
                    : "";
            }

            // 传输中把所有发送按钮按下去，免得出两趟车
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    _rows[i].SetInteractable(!active);
                }
            }
        }

        static string Bytes(long value)
        {
            if (value < 1024)
            {
                return value + " B";
            }

            if (value < 1024 * 1024)
            {
                return (value / 1024f).ToString("0.0") + " KB";
            }

            return (value / (1024f * 1024f)).ToString("0.00") + " MB";
        }

        // ------------------------------------------------------------ 列表

        void RebuildRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    Destroy(_rows[i].gameObject);
                }
            }

            _rows.Clear();

            if (_controller == null)
            {
                WarnOnce("PortalPanel 的 Controller 没连上，列表不会出来");
                return;
            }

            if (_listContent == null || _rowPrefab == null)
            {
                WarnOnce("PortalPanel 的 List Content 或 Row Prefab 没连上，列表不会出来");
                return;
            }

            if (_listContent.GetComponent<LayoutGroup>() == null)
            {
                WarnNoLayout("List Content 上没有 VerticalLayoutGroup，所有行会叠在同一处");
            }

            List<MaidSaveData> maids = _controller.Maids;
            if (_emptyHint != null)
            {
                _emptyHint.SetActive(maids.Count == 0);
            }

            for (int i = 0; i < maids.Count; i++)
            {
                MaidSaveData maid = maids[i];
                string blockReason;
                PortalStorage.CanSend(maid, out blockReason);

                PortalMaidRow row = Instantiate(_rowPrefab, _listContent);
                ResetRowTransform(row);
                row.Bind(maid, blockReason, OnSendRequested);
                _rows.Add(row);
            }

            if (_rows.Count != _lastRowCount)
            {
                _lastRowCount = _rows.Count;
                Debug.Log("[Portal] 列表刷新：铺了 " + _rows.Count + " 行，存档里 " + maids.Count + " 只女仆");
            }
        }

        void WarnOnce(string message)
        {
            if (_warnedMissingRefs)
            {
                return;
            }

            _warnedMissingRefs = true;
            Debug.LogWarning("[Portal] " + message);
        }

        void WarnNoLayout(string message)
        {
            if (_warnedNoLayout)
            {
                return;
            }

            _warnedNoLayout = true;
            Debug.LogWarning("[Portal] " + message);
        }

        /// <summary>
        /// 预制体的 localPosition / localScale / localRotation 会跟着复制过来。
        /// 不归零的话每行都落在预制体当初那个位置上，表现出来就是"生成了但在面板外面"。
        /// 有布局组件的时候它反正会把位置接过去，归零不会打架。
        /// </summary>
        static void ResetRowTransform(PortalMaidRow row)
        {
            RectTransform rect = row.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition3D = Vector3.zero;
        }

        void OnSendRequested(string maidId)
        {
            string error;
            if (!_controller.SendMaid(maidId, out error))
            {
                Debug.LogWarning("[Portal] 发送失败: " + error);
            }
        }
    }
}
