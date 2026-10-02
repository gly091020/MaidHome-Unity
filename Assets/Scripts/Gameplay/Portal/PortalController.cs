using System;
using System.Collections.Generic;
using MaidHome.Gameplay.Audio;
using MaidHome.Interop.Maid;
using MaidHome.Interop.Portal;
using UnityEngine;
using UnityEngine.Events;

namespace MaidHome.Gameplay.Portal
{
    /// <summary>
    /// Portal 场景的入口：起服务端、把后台线程的通知翻成三个事件、维护"背包里能发的女仆"列表。
    ///
    /// 三个事件：
    ///   ProgressChanged(操作, 类型, 0~1)
    ///   Succeeded(操作, 类型, 可用名称)   —— 音效包没有名字，给的是 id
    ///   Failed(操作, 类型, 名称, 原因)
    /// 只有主线程发的，随便在这上面挂 UI。另外 Changed 是用来刷新面板的通用通知。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalController : MonoBehaviour
    {
        [SerializeField] private int _port = PortalServer.DefaultPort;
        [SerializeField] private bool _startOnEnable = true;

        public event Action<PortalOperation, PortalKind, float> ProgressChanged;
        public event Action<PortalOperation, PortalKind, string> Succeeded;
        public event Action<PortalOperation, PortalKind, string, string> Failed;
        public event Action Changed;

        // C# 事件不参与序列化，Inspector 里看不到；想用面板连就在下面这几个上连
        [Header("Inspector 连线用（脚本里仍然用上面那四个 C# 事件）")]
        [SerializeField] private UnityEvent<PortalOperation, PortalKind, float> _onProgressChanged
            = new UnityEvent<PortalOperation, PortalKind, float>();
        [SerializeField] private UnityEvent<PortalOperation, PortalKind, string> _onSucceeded
            = new UnityEvent<PortalOperation, PortalKind, string>();
        [SerializeField] private UnityEvent<PortalOperation, PortalKind, string, string> _onFailed
            = new UnityEvent<PortalOperation, PortalKind, string, string>();
        [SerializeField] private UnityEvent _onChanged = new UnityEvent();

        readonly List<MaidSaveData> _maids = new List<MaidSaveData>();

        PortalServer _server;
        string _clientName = "";
        string _message = "";
        bool _transferActive;
        PortalOperation _lastOperation;
        PortalKind _lastKind;
        float _lastProgress = -1f;

        public PortalServer Server
        {
            get { return _server; }
        }

        /// <summary>存档里的全部女仆。能不能发用 PortalStorage.CanSend 单独判断。</summary>
        public List<MaidSaveData> Maids
        {
            get { return _maids; }
        }

        public bool IsRunning
        {
            get { return _server != null && _server.IsRunning; }
        }

        public bool IsClientConnected
        {
            get { return _server != null && _server.IsClientConnected; }
        }

        public string ClientName
        {
            get { return _clientName; }
        }

        /// <summary>最近一条状态/结果文字，面板直接显示。</summary>
        public string Message
        {
            get { return _message; }
        }

        public bool IsTransferActive
        {
            get { return _transferActive; }
        }

        public float TransferProgress
        {
            get { return _lastProgress < 0f ? 0f : _lastProgress; }
        }

        void Awake()
        {
            EnsureServer();
            RefreshMaids();
        }

        void OnEnable()
        {
            if (_startOnEnable)
            {
                StartServer();
            }
        }

        void OnDisable()
        {
            StopServer();
        }

        void OnDestroy()
        {
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }
        }

        void Update()
        {
            if (_server == null)
            {
                return;
            }

            PortalNotice notice;
            while (_server.TryDequeueNotice(out notice))
            {
                HandleNotice(notice);
            }

            PollProgress();
        }

        public void StartServer()
        {
            EnsureServer();
            if (_server.IsRunning)
            {
                return;
            }

            string error;
            if (_server.Start(out error))
            {
                _message = "正在监听 " + _server.Port + " 端口，等客户端连进来";
            }
            else
            {
                _message = error;
                RaiseFailed(PortalOperation.Upload, PortalKind.Maid, "", error);
            }

            RaiseChanged();
        }

        public void StopServer()
        {
            if (_server == null || !_server.IsRunning)
            {
                return;
            }

            _server.Stop();
            _clientName = "";
            _transferActive = false;
            _message = "服务已停止";
            RaiseChanged();
        }

        public void RefreshMaids()
        {
            ReloadMaids();
            RaiseChanged();
        }

        void ReloadMaids()
        {
            _maids.Clear();
            _maids.AddRange(PortalStorage.ListAllMaids());
        }

        /// <summary>把一只（必须在背包里的）女仆发给客户端。返回假时 error 里是原因。</summary>
        public bool SendMaid(string maidId, out string error)
        {
            error = "";
            if (_server == null)
            {
                error = "服务没起来";
                RaiseFailed(PortalOperation.Send, PortalKind.Maid, maidId, error);
                return false;
            }

            if (!_server.TrySendMaid(maidId, out error))
            {
                RaiseFailed(PortalOperation.Send, PortalKind.Maid, NameOf(maidId), error);
                return false;
            }

            _message = "正在发送 " + NameOf(maidId) + " …";
            RaiseChanged();
            return true;
        }

        /// <summary>给按钮的 UnityEvent 用（带 out 的重载绑不上）。失败只写 Console。</summary>
        public void SendMaid(string maidId)
        {
            string error;
            if (!SendMaid(maidId, out error))
            {
                Debug.LogWarning("[Portal] 发送失败: " + error);
            }
        }

        void EnsureServer()
        {
            if (_server == null)
            {
                _server = new PortalServer(_port);
            }
        }

        string NameOf(string maidId)
        {
            for (int i = 0; i < _maids.Count; i++)
            {
                if (_maids[i].Id == maidId)
                {
                    return PortalStorage.DisplayNameOf(_maids[i].Id, _maids[i].Name);
                }
            }

            return maidId;
        }

        void HandleNotice(PortalNotice notice)
        {
            switch (notice.Type)
            {
                case PortalNoticeType.ClientConnected:
                    if (!string.IsNullOrEmpty(notice.Name))
                    {
                        _clientName = notice.Name;
                    }

                    _message = string.IsNullOrEmpty(_clientName)
                        ? notice.Message
                        : "客户端已连接: " + _clientName;
                    break;

                case PortalNoticeType.ClientDisconnected:
                    _clientName = "";
                    _transferActive = false;
                    _lastProgress = -1f;
                    _message = notice.Message;
                    ReloadMaids();
                    break;

                case PortalNoticeType.TransferFinished:
                    _transferActive = false;
                    _lastProgress = -1f;
                    if (notice.Ok)
                    {
                        _message = "已上传 " + notice.Kind.DisplayName() + ": " + notice.Name;
                        if (notice.Operation == PortalOperation.Send)
                        {
                            _message = "已发送 " + notice.Kind.DisplayName() + ": " + notice.Name + "，存档已删除";
                        }

                        if (notice.Kind == PortalKind.SoundPack && notice.Operation == PortalOperation.Upload)
                        {
                            MaidSoundLibrary.Reload();
                        }

                        // 发送成功会从存档删女仆，上传女仆会多出来一只，两种都要刷新列表
                        ReloadMaids();
                        RaiseSucceeded(notice.Operation, notice.Kind, notice.Name);
                    }
                    else
                    {
                        _message = notice.Message;
                        RaiseFailed(notice.Operation, notice.Kind, notice.Name, notice.Message);
                    }

                    if (notice.Ok && !string.IsNullOrEmpty(notice.Message))
                    {
                        Debug.LogWarning("[Portal] " + notice.Message);
                    }

                    break;
            }

            RaiseChanged();
        }

        void PollProgress()
        {
            PortalOperation operation;
            PortalKind kind;
            float progress;
            if (!_server.TryGetTransfer(out operation, out kind, out progress))
            {
                _transferActive = false;
                _lastProgress = -1f;
                return;
            }

            _transferActive = true;
            bool newTransfer = operation != _lastOperation || kind != _lastKind
                || _lastProgress < 0f || progress < _lastProgress;
            if (newTransfer || progress - _lastProgress >= 0.01f || progress >= 1f)
            {
                _lastOperation = operation;
                _lastKind = kind;
                _lastProgress = progress;
                Action<PortalOperation, PortalKind, float> handler = ProgressChanged;
                if (handler != null)
                {
                    handler(operation, kind, progress);
                }

                if (_onProgressChanged != null)
                {
                    _onProgressChanged.Invoke(operation, kind, progress);
                }
            }
        }

        void RaiseSucceeded(PortalOperation operation, PortalKind kind, string name)
        {
            Action<PortalOperation, PortalKind, string> handler = Succeeded;
            if (handler != null)
            {
                handler(operation, kind, name);
            }

            if (_onSucceeded != null)
            {
                _onSucceeded.Invoke(operation, kind, name);
            }
        }

        void RaiseFailed(PortalOperation operation, PortalKind kind, string name, string message)
        {
            Action<PortalOperation, PortalKind, string, string> handler = Failed;
            if (handler != null)
            {
                handler(operation, kind, name, message);
            }

            if (_onFailed != null)
            {
                _onFailed.Invoke(operation, kind, name, message);
            }
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }

            if (_onChanged != null)
            {
                _onChanged.Invoke();
            }
        }
    }
}
