using System;
using MaidHome.Core.Input;
using MaidHome.Gameplay.House;
using UnityEngine;
using UnityEngine.Serialization;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 点场景里的女仆：暂停游走、相机平滑拉近、打开交互面板。
    /// 点空白或面板关闭按钮取消，女仆恢复游走，相机回房子视角。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidInteractionController : MonoBehaviour
    {
        /// <summary>现在是不是点开了一只女仆（看景的拖动/缩放要让位，别和她抢镜头）</summary>
        public bool IsFocused
        {
            get { return _current != null; }
        }

        [SerializeField] private MaidInteractionPanel _panel;
        [SerializeField] private HouseCameraFitter _cameraFitter;
        [FormerlySerializedAs("_")]
        [Tooltip("打开女仆互动界面时要藏起来的物体组合")]
        [SerializeField] private GameObject _hideOnOpen;
        [Tooltip("点开女仆后她转过来面对相机要多久")]
        [SerializeField] private float _faceCameraSeconds = 0.35f;
        [Tooltip("moreanimation 的 tail_sniff.ogg，留空就只是没声音")]
        [SerializeField] private AudioClip _tailSniffClip;
        [Tooltip("moreanimation 的 slap.ogg，留空就只是没声音")]
        [SerializeField] private AudioClip _slapClip;
        [Tooltip("进摸脸模式时把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        [SerializeField] private float _faceOrthographicSize = 0.8f;
        [Tooltip("摸脸模式专用俯角（度）：相机站在她斜上方往下看的角度，越小越平视她的脸。-1 = 沿用 HouseCameraFitter 的设置")]
        [SerializeField] private float _faceFocusPitchDegrees = -1f;
        [Tooltip("摸脸模式取景中心往上抬多少（按头部包围盒半高）。-1 = 沿用 HouseCameraFitter 的设置")]
        [SerializeField] private float _faceFocusLookHeightRatio = -1f;
        [Tooltip("喂蛋糕选中/重挡时临时把相机切成透视的 fov（切回轻挡或退出时恢复正交）")]
        [SerializeField] private float _feedPerspectiveFov = 15f;
        [SerializeField] private float _clickDistance = 300f;
        [SerializeField] private LayerMask _clickMask = ~0;

        MaidAgent _current;
        MaidTailInteraction _tail;
        MaidFaceInteraction _face;
        MaidFeedInteraction _feed;
        float _appliedFeedFov = -1f;

        void Awake()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidInteractionPanel>();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidInteractionPanel>();
            }

            if (_cameraFitter == null && Camera.main != null)
            {
                _cameraFitter = Camera.main.GetComponent<HouseCameraFitter>();
            }

            _tail = GetComponent<MaidTailInteraction>();
            if (_tail == null)
            {
                _tail = gameObject.AddComponent<MaidTailInteraction>();
            }

            _face = GetComponent<MaidFaceInteraction>();
            if (_face == null)
            {
                _face = gameObject.AddComponent<MaidFaceInteraction>();
            }

            _feed = GetComponent<MaidFeedInteraction>();
            if (_feed == null)
            {
                // 场景里手摆的那份（吃喝的素材都填在它上面）优先，找不到再自己加一个
                _feed = FindSceneFeedInteraction();
            }

            if (_feed == null)
            {
                _feed = gameObject.AddComponent<MaidFeedInteraction>();
            }

            if (_hideOnOpen == null)
            {
                // 兜底：改脚本时 Unity 可能正开着，新字段在场景里还是 None，按名字再找一次
                _hideOnOpen = GameObject.Find("InvButton");
            }
        }

        /// <summary>
        /// 场景里手摆的 MaidFeedInteraction（吃东西音效、蛋糕预制体、粒子贴图都填在它上面）。
        /// 只认唯一一份，有多份就返回 null 让控制器自己加，免得挑错。
        /// </summary>
        static MaidFeedInteraction FindSceneFeedInteraction()
        {
            MaidFeedInteraction[] all = FindObjectsOfType<MaidFeedInteraction>(true);
            MaidFeedInteraction found = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = all[i];
            }

            return found;
        }

        void OnEnable()
        {
            if (_panel != null)
            {
                _panel.CloseRequested += Close;
                _panel.TailRequested += OpenTail;
                _panel.FaceRequested += OpenFace;
                _panel.FeedRequested += OpenFeed;
            }

            if (_tail != null)
            {
                _tail.Ended += OnTailEnded;
            }

            if (_face != null)
            {
                _face.Ended += OnFaceEnded;
            }

            if (_feed != null)
            {
                _feed.Ended += OnFeedEnded;
            }
        }

        void OnDisable()
        {
            if (_panel != null)
            {
                _panel.CloseRequested -= Close;
                _panel.TailRequested -= OpenTail;
                _panel.FaceRequested -= OpenFace;
                _panel.FeedRequested -= OpenFeed;
            }

            if (_tail != null)
            {
                _tail.Ended -= OnTailEnded;
            }

            if (_face != null)
            {
                _face.Ended -= OnFaceEnded;
            }

            if (_feed != null)
            {
                _feed.Ended -= OnFeedEnded;
            }

            // 界面状态跟着控制器走，别让背包按钮留在隐藏状态
            SetInventoryVisible(true);
            RestoreFeedProjection();
        }

        void Update()
        {
            if (_current != null && !_current.gameObject.activeInHierarchy)
            {
                Close();
                return;
            }

            // 摸尾巴模式自己处理输入，这里别抢
            if (_tail != null && _tail.IsActive)
            {
                return;
            }

            // 摸脸模式同理
            if (_face != null && _face.IsActive)
            {
                return;
            }

            // 喂蛋糕模式同理
            if (_feed != null && _feed.IsActive)
            {
                // 中/重挡要透视，轻挡要正交：每帧对一下（取景动画跑的时候先不切，见 ApplyFeedProjection）
                ApplyFeedProjection();
                return;
            }

            if (_current == null && _panel != null && _panel.IsOpen)
            {
                Close();
            }

            PointerInput.Pointer pointer = PointerInput.Primary;
            if (!pointer.Pressed || pointer.OverUi)
            {
                return;
            }

            MaidAgent agent = RaycastMaid(pointer.Position);
            if (agent != null)
            {
                Open(agent);
                return;
            }

            if (_current != null)
            {
                Close();
            }
        }

        void Open(MaidAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            if (_current == agent && _panel != null && _panel.IsOpen)
            {
                return;
            }

            if (_current != null && _current != agent)
            {
                SetPaused(_current, false);
            }

            _current = agent;
            SetPaused(_current, true);

            if (_cameraFitter != null)
            {
                // 相机保持现在的朝向推近，不绕到她正面
                _cameraFitter.FocusKeepingAngle(_current.GetBounds(), _current.transform);
            }

            FaceCamera(_current);

            if (_panel != null)
            {
                _panel.Open(_current.Save, _tail != null && _tail.Supports(_current),
                    _face != null && _face.Supports(_current), _feed != null && _feed.Supports(_current));
            }

            SetInventoryVisible(false);
        }

        void Close()
        {
            if (_tail != null && _tail.IsActive)
            {
                _tail.Abort();
            }

            if (_face != null && _face.IsActive)
            {
                _face.Abort();
            }

            if (_feed != null && _feed.IsActive)
            {
                _feed.Abort();
            }

            // 关界面可能是从中/重挡直接退出来的，投影也得切回正交
            RestoreFeedProjection();

            if (_current != null && _current.gameObject.activeInHierarchy)
            {
                SetPaused(_current, false);
            }

            _current = null;
            if (_cameraFitter != null)
            {
                _cameraFitter.RestoreHouseView();
            }

            if (_panel != null)
            {
                _panel.Close();
            }

            SetInventoryVisible(true);
        }

        /// <summary>让她转过来面对相机。相机不动，靠她转身给正脸。</summary>
        void FaceCamera(MaidAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            Camera camera = Camera.main;
            MaidWanderer wanderer = agent.Wanderer;
            if (camera == null || wanderer == null)
            {
                return;
            }

            Vector3 toCamera = camera.transform.position - agent.transform.position;
            wanderer.FaceDirection(toCamera, _faceCameraSeconds);
        }

        /// <summary>摸尾巴模式不算关界面，所以只在 Open/Close 两头切换，中途不动它。</summary>
        void SetInventoryVisible(bool visible)
        {
            if (_hideOnOpen != null && _hideOnOpen.activeSelf != visible)
            {
                _hideOnOpen.SetActive(visible);
            }
        }

        void OpenTail()
        {
            if (_tail == null || _current == null || _tail.IsActive)
            {
                return;
            }

            if (_tail.Begin(_current, _tailSniffClip))
            {
                _panel.Close();
            }
        }

        void OnTailEnded()
        {
            if (_current != null && _panel != null)
            {
                _panel.Open(_current.Save, _tail != null && _tail.Supports(_current),
                    _face != null && _face.Supports(_current), _feed != null && _feed.Supports(_current));
            }
        }

        void OpenFace()
        {
            if (_face == null || _current == null || _face.IsActive)
            {
                return;
            }

            if (!_face.Begin(_current, _slapClip))
            {
                return;
            }

            _panel.Close();
            if (_cameraFitter != null && _faceOrthographicSize > 0f)
            {
                Bounds head;
                if (_face.TryGetHeadBounds(out head))
                {
                    _cameraFitter.FocusKeepingAngle(head, _current.transform, _faceOrthographicSize,
                        _faceFocusPitchDegrees >= 0f ? _faceFocusPitchDegrees : _cameraFitter.FocusPitchDegrees,
                        _faceFocusLookHeightRatio >= 0f
                            ? _faceFocusLookHeightRatio
                            : _cameraFitter.FocusLookHeightRatio);
                }
            }
        }

        void OnFaceEnded()
        {
            // 摸脸时相机推近了，退出来要回到点开女仆时的取景
            if (_current != null && _cameraFitter != null && _faceOrthographicSize > 0f)
            {
                _cameraFitter.FocusKeepingAngle(_current.GetBounds(), _current.transform);
            }

            if (_current != null && _panel != null)
            {
                _panel.Open(_current.Save, _tail != null && _tail.Supports(_current),
                    _face != null && _face.Supports(_current), _feed != null && _feed.Supports(_current));
            }
        }

        void OpenFeed()
        {
            if (_feed == null || _current == null || _feed.IsActive)
            {
                return;
            }

            if (!_feed.Begin(_current))
            {
                return;
            }

            // 每次进模式重新对一次投影（上一次可能是透视退出来的）
            _appliedFeedFov = -1f;

            _panel.Close();
            if (_cameraFitter != null && _feed.OrthographicSize > 0f)
            {
                Bounds head;
                if (_feed.TryGetHeadBounds(out head))
                {
                    _cameraFitter.FocusKeepingAngle(head, _current.transform, _feed.OrthographicSize,
                        _faceFocusPitchDegrees >= 0f ? _faceFocusPitchDegrees : _cameraFitter.FocusPitchDegrees,
                        _faceFocusLookHeightRatio >= 0f
                            ? _faceFocusLookHeightRatio
                            : _cameraFitter.FocusLookHeightRatio);
                }
            }
        }

        void OnFeedEnded()
        {
            // 先把投影恢复正交，再回去取景（取景那套只会按正交算）
            RestoreFeedProjection();

            // 喂蛋糕时相机推近了，退出来要回到点开女仆时的取景
            if (_current != null && _cameraFitter != null && _feed != null && _feed.OrthographicSize > 0f)
            {
                _cameraFitter.FocusKeepingAngle(_current.GetBounds(), _current.transform);
            }

            if (_current != null && _panel != null)
            {
                _panel.Open(_current.Save, _tail != null && _tail.Supports(_current),
                    _face != null && _face.Supports(_current), _feed != null && _feed.Supports(_current));
            }
        }

        /// <summary>中/重挡用透视（`_feedPerspectiveFov`），轻挡用正交；值没变就不动相机</summary>
        void ApplyFeedProjection()
        {
            if (_cameraFitter == null || _feed == null)
            {
                return;
            }

            // 取景动画期间它每帧都会把相机按回正交，等它停下来再切
            if (_cameraFitter.IsMoving)
            {
                return;
            }

            float fov = _feed.SelectedLevel == MaidFeedInteraction.LevelLight ? 0f : _feedPerspectiveFov;
            if (Mathf.Abs(_appliedFeedFov - fov) < 0.001f)
            {
                return;
            }

            _appliedFeedFov = fov;
            _cameraFitter.SetPerspective(fov);
        }

        /// <summary>退出/中断喂蛋糕：把相机切回正交</summary>
        void RestoreFeedProjection()
        {
            if (_appliedFeedFov <= 0f)
            {
                return;
            }

            _appliedFeedFov = -1f;
            if (_cameraFitter != null)
            {
                _cameraFitter.SetPerspective(0f);
            }
        }

        static void SetPaused(MaidAgent agent, bool paused)
        {
            if (agent == null)
            {
                return;
            }

            MaidWanderer wanderer = agent.Wanderer;
            if (wanderer != null)
            {
                wanderer.SetPaused(paused);
            }
        }

        MaidAgent RaycastMaid(Vector2 screenPosition)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, _clickDistance, _clickMask);
            Array.Sort(hits, CompareHits);
            for (int i = 0; i < hits.Length; i++)
            {
                MaidAgent agent = hits[i].collider.GetComponentInParent<MaidAgent>();
                if (agent != null)
                {
                    return agent;
                }

                // 命中墙/地板就先当被挡住，避免隔着墙点到女仆
                if (hits[i].collider.GetComponentInParent<HouseGridView>() != null)
                {
                    return null;
                }
            }

            return null;
        }

        static int CompareHits(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
