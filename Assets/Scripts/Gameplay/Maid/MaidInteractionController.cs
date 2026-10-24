using System;
using MaidHome.Core.Input;
using MaidHome.Core.Settings;
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

        /// <summary>当前点开的那只（挂牌的「游街示众」要避开正在被交互的这一只）</summary>
        public MaidAgent Current
        {
            get { return _current; }
        }

        [Header("配置（留空自动读取 Resources/Settings；指定则优先）")]
        [SerializeField] private MaidInteractionControllerSettings _settings;

        [SerializeField] private MaidInteractionPanel _panel;
        [Tooltip("第二层「挂件」选择面板。留空就找同一物体上的 / 场景里唯一的那份 MaidAttachmentMenuPanel，"
            + "都没有才自动生成一块")]
        [SerializeField] private MaidAttachmentMenuPanel _menu;
        [SerializeField] private HouseCameraFitter _cameraFitter;
        [FormerlySerializedAs("_")]
        [Tooltip("打开女仆互动界面时要藏起来的物体组合")]
        [SerializeField] private GameObject _hideOnOpen;
        [Tooltip("点开女仆后她转过来面对相机要多久")]
        private float _faceCameraSeconds = 0.35f;
        [Tooltip("moreanimation 的 tail_sniff.ogg，留空就只是没声音")]
        private AudioClip _tailSniffClip;
        [Tooltip("moreanimation 的 slap.ogg，留空就只是没声音")]
        private AudioClip _slapClip;
        [Tooltip("进摸脸模式时把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        private float _faceOrthographicSize = 0.8f;
        [Tooltip("摸脸模式专用俯角（度）：相机站在她斜上方往下看的角度，越小越平视她的脸。-1 = 沿用 HouseCameraFitter 的设置")]
        private float _faceFocusPitchDegrees = -1f;
        [Tooltip("摸脸模式取景中心往上抬多少（按头部包围盒半高）。-1 = 沿用 HouseCameraFitter 的设置")]
        private float _faceFocusLookHeightRatio = -1f;
        [Tooltip("摸摸模式把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        private float _petOrthographicSize = 0.65f;
        [Tooltip("摸摸模式专用俯角（度）。-1 = 沿用 HouseCameraFitter 的设置")]
        private float _petFocusPitchDegrees = -1f;
        [Tooltip("摸摸模式取景中心往上抬多少（按头部包围盒半高）。-1 = 沿用 HouseCameraFitter 的设置")]
        private float _petFocusLookHeightRatio = -1f;
        [Tooltip("喂蛋糕选中/重挡时临时把相机切成透视的 fov（切回轻挡或退出时恢复正交）")]
        private float _feedPerspectiveFov = 15f;
        [Tooltip("梳毛模式把相机推近到这个正交尺寸，之后再按 _groomPerspectiveFov 切成透视")]
        private float _groomOrthographicSize = 0.9f;
        [Tooltip("梳毛取景的俯角（度）：她躺着，要从斜上方往下看，比摸脸的 45° 更陡。-1 = 沿用 HouseCameraFitter 的设置")]
        private float _groomFocusPitchDegrees = 65f;
        [Tooltip("梳毛取景的远近倍率：<1 拉近、>1 退远（取景框和最小尺寸一起缩放，所以一定生效）")]
        private float _groomZoom = 1f;
        [Tooltip("梳毛模式的透视 fov。0 = 不切透视，全程用正交（最稳，画面不会跳）；-1 = 自动：按正交取景的画面高度反推 fov，有立体感但画面大小不变；填正数 = 固定 fov（模组那边是坐垫上的第一人称，所以给了这个口子）")]
        private float _groomPerspectiveFov;
        [Tooltip("挂牌模式把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        private float _signOrthographicSize = -1f;
        [Tooltip("面具模式把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        private float _maskOrthographicSize = -1f;
        private float _clickDistance = 300f;
        private LayerMask _clickMask = ~0;

        MaidAgent _current;
        MaidTailInteraction _tail;
        MaidFaceInteraction _face;
        MaidHeadPetInteraction _pet;
        MaidFeedInteraction _feed;
        MaidGroomingInteraction _groom;
        MaidBodyInteraction _body;
        MaidSignInteraction _sign;
        MaidMaskInteraction _mask;
        MaidAiChatController _aiChat;
        float _appliedFeedFov = -1f;
        float _appliedGroomFov = -1f;
        Vector3 _groomFocusCenter;
        bool _hasGroomFocus;

        bool ApplySettings()
        {
            if (_settings == null)
            {
                _settings = Resources.Load<MaidInteractionControllerSettings>("Settings/MaidInteractionControllerSettings");
            }

            if (_settings == null)
            {
                Debug.LogError("MaidInteractionController 没有挂 MaidInteractionControllerSettings，请到 Assets/Resources/Settings 里选择配置", this);
                return false;
            }

            _faceCameraSeconds = _settings.FaceCameraSeconds;
            _tailSniffClip = _settings.TailSniffClip;
            _slapClip = _settings.SlapClip;
            _faceOrthographicSize = _settings.FaceOrthographicSize;
            _faceFocusPitchDegrees = _settings.FaceFocusPitchDegrees;
            _faceFocusLookHeightRatio = _settings.FaceFocusLookHeightRatio;
            _petOrthographicSize = _settings.PetOrthographicSize;
            _petFocusPitchDegrees = _settings.PetFocusPitchDegrees;
            _petFocusLookHeightRatio = _settings.PetFocusLookHeightRatio;
            _feedPerspectiveFov = _settings.FeedPerspectiveFov;
            _groomOrthographicSize = _settings.GroomOrthographicSize;
            _groomFocusPitchDegrees = _settings.GroomFocusPitchDegrees;
            _groomZoom = _settings.GroomZoom;
            _groomPerspectiveFov = _settings.GroomPerspectiveFov;
            _signOrthographicSize = _settings.SignOrthographicSize;
            _maskOrthographicSize = _settings.MaskOrthographicSize;
            _clickDistance = _settings.ClickDistance;
            _clickMask = _settings.ClickMask;

            return true;
        }
        void Awake()
        {
            ApplySettings();
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

            _pet = GetComponent<MaidHeadPetInteraction>();
            if (_pet == null)
            {
                _pet = gameObject.AddComponent<MaidHeadPetInteraction>();
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

            _groom = GetComponent<MaidGroomingInteraction>();
            if (_groom == null)
            {
                _groom = gameObject.AddComponent<MaidGroomingInteraction>();
            }

            _body = GetComponent<MaidBodyInteraction>();
            if (_body == null)
            {
                _body = gameObject.AddComponent<MaidBodyInteraction>();
            }

            _sign = GetComponent<MaidSignInteraction>();
            if (_sign == null)
            {
                _sign = gameObject.AddComponent<MaidSignInteraction>();
            }

            _mask = GetComponent<MaidMaskInteraction>();
            if (_mask == null)
            {
                _mask = gameObject.AddComponent<MaidMaskInteraction>();
            }

            if (_menu == null)
            {
                _menu = GetComponent<MaidAttachmentMenuPanel>();
            }

            if (_menu == null)
            {
                // 场景里恰好摆了一份（挂在别的物体上）就用它；有多份就不猜，自己生成一块
                _menu = FindSingleSceneMenu();
            }

            if (_menu == null)
            {
                _menu = gameObject.AddComponent<MaidAttachmentMenuPanel>();
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

        /// <summary>场景里唯一的第二层挂件面板（挂在别的物体上也认）；有多份就不猜</summary>
        static MaidAttachmentMenuPanel FindSingleSceneMenu()
        {
            MaidAttachmentMenuPanel[] all = FindObjectsOfType<MaidAttachmentMenuPanel>(true);
            MaidAttachmentMenuPanel found = null;
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
            GameSettingsStore.Changed -= OnGameSettingsChanged;
            GameSettingsStore.Changed += OnGameSettingsChanged;
            if (_panel != null)
            {
                _panel.CloseRequested += Close;
                _panel.TailRequested += OpenTail;
                _panel.FaceRequested += OpenFace;
                _panel.PetRequested += OpenPet;
                _panel.FeedRequested += OpenFeed;
                _panel.GroomRequested += OpenGroom;
                _panel.BodyRequested += OpenBody;
                _panel.AttachRequested += OpenAttachments;
                _panel.ChatRequested += OpenAiChat;
            }

            if (_tail != null)
            {
                _tail.Ended += OnTailEnded;
            }

            if (_face != null)
            {
                _face.Ended += OnFaceEnded;
            }

            if (_pet != null)
            {
                _pet.Ended += OnPetEnded;
            }

            if (_feed != null)
            {
                _feed.Ended += OnFeedEnded;
            }

            if (_groom != null)
            {
                _groom.Ended += OnGroomEnded;
            }

            if (_body != null)
            {
                _body.Ended += OnBodyEnded;
            }

            if (_sign != null)
            {
                _sign.Ended += OnSignEnded;
            }

            if (_mask != null)
            {
                _mask.Ended += OnMaskEnded;
            }

            if (_menu != null)
            {
                _menu.SignRequested += OpenSign;
                _menu.MaskRequested += OpenMask;
                _menu.BackRequested += CloseAttachments;
            }
        }

        void OnDisable()
        {
            GameSettingsStore.Changed -= OnGameSettingsChanged;
            if (_panel != null)
            {
                _panel.CloseRequested -= Close;
                _panel.TailRequested -= OpenTail;
                _panel.FaceRequested -= OpenFace;
                _panel.PetRequested -= OpenPet;
                _panel.FeedRequested -= OpenFeed;
                _panel.GroomRequested -= OpenGroom;
                _panel.BodyRequested -= OpenBody;
                _panel.AttachRequested -= OpenAttachments;
                _panel.ChatRequested -= OpenAiChat;
            }

            if (_tail != null)
            {
                _tail.Ended -= OnTailEnded;
            }

            if (_face != null)
            {
                _face.Ended -= OnFaceEnded;
            }

            if (_pet != null)
            {
                _pet.Ended -= OnPetEnded;
            }

            if (_feed != null)
            {
                _feed.Ended -= OnFeedEnded;
            }

            if (_groom != null)
            {
                _groom.Ended -= OnGroomEnded;
            }

            if (_body != null)
            {
                _body.Ended -= OnBodyEnded;
            }

            if (_sign != null)
            {
                _sign.Ended -= OnSignEnded;
            }

            if (_mask != null)
            {
                _mask.Ended -= OnMaskEnded;
            }

            if (_menu != null)
            {
                _menu.SignRequested -= OpenSign;
                _menu.MaskRequested -= OpenMask;
                _menu.BackRequested -= CloseAttachments;
            }

            if (_body != null && _body.IsActive)
            {
                _body.Abort();
            }

            HideAiChatPanel();

            if (_pet != null && _pet.IsActive)
            {
                _pet.Abort();
            }

            // 界面状态跟着控制器走，别让背包按钮留在隐藏状态
            SetInventoryVisible(true);
            RestoreFeedProjection();
            RestoreGroomProjection();
        }

        void Update()
        {
            if (_current != null && !_current.gameObject.activeInHierarchy)
            {
                Close();
                return;
            }

            if (_aiChat != null && _aiChat.PanelOpen)
            {
                if (PointerInput.BackPressed)
                {
                    CloseAiChat();
                }

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

            // 摸摸模式同理
            if (_pet != null && _pet.IsActive)
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

            // 梳毛模式同理（要临时切成透视，取景动画停下来再切）
            if (_groom != null && _groom.IsActive)
            {
                if (_groom.ConsumeRefocusRequest())
                {
                    // 躺下 / 转向之后头的位置整个变了，得重新取一次景；
                    // 取景会把相机按回正交，所以顺手把透视标记清掉，下面 LateUpdate 再按回来
                    _appliedGroomFov = -1f;
                    FocusGroom();
                }

                return;
            }

            // 玩全身模式自己处理拖动和返回键
            if (_body != null && _body.IsActive)
            {
                return;
            }

            // 挂牌模式同理（面板在屏幕上打字，画面别跟着动）
            if (_sign != null && _sign.IsActive)
            {
                // 她刚进面板时可能在转过来，等转稳了补一次取景（不然牌子会偏在画面一边）
                if (_sign.ConsumeRefocusRequest())
                {
                    FocusSign();
                }

                return;
            }

            // 面具模式同理（选照片的时候系统对话框会盖住画面，别在背后乱跑）
            if (_mask != null && _mask.IsActive)
            {
                if (_mask.ConsumeRefocusRequest())
                {
                    FocusMask();
                }

                return;
            }

            // 第二层「挂件」面板开着：点画面别乱切人/关界面，等玩家在面板里选
            if (_menu != null && _menu.IsVisible)
            {
                // Android 返回键 / Esc = 退回女仆面板
                if (PointerInput.BackPressed)
                {
                    CloseAttachments();
                }

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

        /// <summary>
        /// 梳毛的透视必须在这里按：取景协程是 Update 阶段跑的，每次都把相机按回正交
        /// （ApplyView 只按正交算 size），放在 Update 里切会被它当场按回去——表现就是
        /// 换工具 / 转向的时候闪一帧正交。
        /// </summary>
        void LateUpdate()
        {
            if (_groom != null && _groom.IsActive)
            {
                ApplyGroomProjection();
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
                ReopenPanel();
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

            if (_pet != null && _pet.IsActive)
            {
                _pet.Abort();
            }

            if (_feed != null && _feed.IsActive)
            {
                _feed.Abort();
            }

            if (_groom != null && _groom.IsActive)
            {
                _groom.Abort();
            }

            if (_body != null && _body.IsActive)
            {
                _body.Abort();
            }

            if (_sign != null && _sign.IsActive)
            {
                _sign.Abort();
            }

            if (_mask != null && _mask.IsActive)
            {
                _mask.Abort();
            }

            HideAiChatPanel();

            // 关界面可能是从中/重挡直接退出来的，投影也得切回正交
            RestoreFeedProjection();
            RestoreGroomProjection();

            if (_current != null && _current.gameObject.activeInHierarchy)
            {
                SetPaused(_current, false);
            }

            _current = null;
            if (_cameraFitter != null)
            {
                _cameraFitter.SetPerspective(0f);
                _cameraFitter.RestoreHouseView();
            }

            if (_panel != null)
            {
                _panel.Close();
            }

            if (_menu != null)
            {
                _menu.Hide();
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
                TriggerInteractionFeeling("tail", "摸尾巴");
                _panel.Close();
            }
        }

        void OnTailEnded()
        {
            if (_current != null && _panel != null)
            {
                ReopenPanel();
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

            TriggerInteractionFeeling("face", "摸脸");
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
                ReopenPanel();
            }
        }

        void OpenPet()
        {
            if (_pet == null || _current == null || _pet.IsActive)
            {
                return;
            }

            if (!_pet.Begin(_current))
            {
                return;
            }

            TriggerInteractionFeeling("pet", "摸摸头");
            _panel.Close();
            FocusPet();
        }

        void FocusPet()
        {
            if (_cameraFitter == null || _pet == null || _current == null || _petOrthographicSize <= 0f)
            {
                return;
            }

            Bounds head;
            if (!_pet.TryGetHeadBounds(out head))
            {
                return;
            }

            _cameraFitter.FocusKeepingAngle(head, _current.transform, _petOrthographicSize,
                _petFocusPitchDegrees >= 0f ? _petFocusPitchDegrees : _cameraFitter.FocusPitchDegrees,
                _petFocusLookHeightRatio >= 0f
                    ? _petFocusLookHeightRatio
                    : _cameraFitter.FocusLookHeightRatio);
        }

        void OnPetEnded()
        {
            if (_current != null && _cameraFitter != null && _petOrthographicSize > 0f)
            {
                _cameraFitter.FocusKeepingAngle(_current.GetBounds(), _current.transform);
            }

            if (_current != null && _panel != null)
            {
                ReopenPanel();
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

            TriggerInteractionFeeling("feed", "喂蛋糕");
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
                ReopenPanel();
            }
        }

        /// <summary>开女仆面板：各交互模式的可用性一起算（Supports 会去认骨架，所以只在这里调）</summary>
        void ReopenPanel()
        {
            if (_current == null || _panel == null)
            {
                return;
            }

            _panel.Open(_current.Save, _tail != null && _tail.Supports(_current),
                _face != null && _face.Supports(_current), _feed != null && _feed.Supports(_current),
                _groom != null && _groom.Supports(_current), _body != null && _body.Supports(_current),
                _pet != null && _pet.Supports(_current));
        }

        /// <summary>第一层点「挂件」→ 第二层：选挂牌 / 面具</summary>
        void OpenAttachments()
        {
            if (_current == null || _menu == null)
            {
                return;
            }

            _panel.Close();
            _menu.Show(_sign != null && _sign.Supports(_current), _mask != null && _mask.Supports(_current));
        }

        void OpenAiChat()
        {
            if (_current == null || !GameSettingsStore.Current.AiEnabled) return;
            MaidAiChatController chat = _current.GetComponent<MaidAiChatController>();
            if (chat == null) chat = _current.gameObject.AddComponent<MaidAiChatController>();
            if (_aiChat != null && _aiChat != chat) _aiChat.CloseRequested -= CloseAiChat;
            _aiChat = chat;
            _aiChat.CloseRequested -= CloseAiChat;
            _aiChat.CloseRequested += CloseAiChat;
            _panel.Close();
            Canvas canvas = _panel.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            _aiChat.ShowPanel(canvas);
        }

        void CloseAiChat()
        {
            if (_aiChat != null)
            {
                _aiChat.HidePanel();
                _aiChat.CloseRequested -= CloseAiChat;
                _aiChat = null;
            }

            if (_current != null && _panel != null) ReopenPanel();
        }

        void HideAiChatPanel()
        {
            if (_aiChat != null)
            {
                _aiChat.HidePanel();
                _aiChat.CloseRequested -= CloseAiChat;
                _aiChat = null;
            }
        }

        void TriggerInteractionFeeling(string interactionId, string interactionName)
        {
            if (_current == null || !GameSettingsStore.Current.AiEnabled) return;
            MaidAiChatController chat = _current.GetComponent<MaidAiChatController>();
            if (chat == null) chat = _current.gameObject.AddComponent<MaidAiChatController>();
            chat.TriggerInteractionFeeling(interactionId, interactionName);
        }

        void OnGameSettingsChanged()
        {
            bool enabled = GameSettingsStore.Current.AiEnabled;
            if (_panel != null) _panel.SetAiAvailable(enabled);
            if (enabled || _aiChat == null) return;
            _aiChat.DisableForGlobalSwitch();
            CloseAiChat();
        }

        /// <summary>第二层点「返回」→ 回第一层女仆面板</summary>
        void CloseAttachments()
        {
            if (_menu != null)
            {
                _menu.Hide();
            }

            ReopenPanel();
        }

        void OpenGroom()
        {
            if (_groom == null || _current == null || _groom.IsActive)
            {
                return;
            }

            if (!_groom.Begin(_current))
            {
                return;
            }

            TriggerInteractionFeeling("groom", "梳毛");
            // 每次进模式重新对一次投影（上一次可能是透视退出来的）
            _appliedGroomFov = -1f;

            _panel.Close();
            // 会躺下的话先不取景：等她躺好了再一次性推近，免得镜头先近后远地跳两次
            if (!_groom.WillLieDown)
            {
                FocusGroom();
            }
        }

        /// <summary>梳毛取景：框住能梳的那几块（头 / 头发 / 耳朵 / 尾巴），俯角用 _groomFocusPitchDegrees</summary>
        void FocusGroom()
        {
            if (_cameraFitter == null || _groom == null || _current == null || _groomOrthographicSize <= 0f)
            {
                return;
            }

            Bounds focus;
            if (!_groom.TryGetFocusBounds(out focus))
            {
                return;
            }

            _groomFocusCenter = focus.center;
            _hasGroomFocus = true;
            float zoom = Mathf.Clamp(_groomZoom, 0.05f, 5f);
            if (Mathf.Abs(zoom - 1f) > 0.001f)
            {
                focus.extents = focus.extents * zoom;
                _groomFocusCenter = focus.center;
            }

            // 躺着要低头往下看，没躺下（_lieDown 关了）就按摸脸那套平视
            float pitch = _groom.IsLying
                ? (_groomFocusPitchDegrees >= 0f ? _groomFocusPitchDegrees : _cameraFitter.FocusPitchDegrees)
                : (_faceFocusPitchDegrees >= 0f ? _faceFocusPitchDegrees : _cameraFitter.FocusPitchDegrees);
            _cameraFitter.FocusKeepingAngle(focus, _current.transform, _groomOrthographicSize * zoom,
                pitch,
                _faceFocusLookHeightRatio >= 0f
                    ? _faceFocusLookHeightRatio
                    : _cameraFitter.FocusLookHeightRatio);
        }

        void OnGroomEnded()
        {
            // 先恢复正交，再回去取景（取景那套只按正交算 size）
            RestoreGroomProjection();

            if (_current != null && _cameraFitter != null && _groomOrthographicSize > 0f)
            {
                _cameraFitter.FocusKeepingAngle(_current.GetBounds(), _current.transform);
            }

            if (_current != null && _panel != null)
            {
                ReopenPanel();
            }
        }

        void OpenBody()
        {
            if (_body == null || _current == null || _body.IsActive)
            {
                return;
            }

            if (!_body.Begin(_current))
            {
                return;
            }

            TriggerInteractionFeeling("body", "玩全身");
            _panel.Close();
        }

        void OnBodyEnded()
        {
            if (_current != null && _panel != null)
            {
                ReopenPanel();
            }
        }

        void OpenSign()
        {
            if (_sign == null || _current == null || _sign.IsActive)
            {
                return;
            }

            if (!_sign.Begin(_current))
            {
                return;
            }

            TriggerInteractionFeeling("sign", "挂牌");
            _panel.Close();
            if (_menu != null)
            {
                _menu.Hide();
            }

            FocusSign();
        }

        /// <summary>挂牌取景：框住牌子（连带头一起），她身上那块牌子远看是看不清的</summary>
        void FocusSign()
        {
            if (_cameraFitter == null || _sign == null || _current == null)
            {
                return;
            }

            float size = _signOrthographicSize > 0f ? _signOrthographicSize : _sign.FocusOrthographicSize;
            if (size <= 0f)
            {
                return;
            }

            Bounds focus;
            if (!_sign.TryGetFocusBounds(out focus))
            {
                return;
            }

            _cameraFitter.FocusKeepingAngle(focus, _current.transform, size,
                _sign.FocusPitchDegrees >= 0f ? _sign.FocusPitchDegrees : _cameraFitter.FocusPitchDegrees,
                _cameraFitter.FocusLookHeightRatio);
        }

        void OnSignEnded()
        {
            // 挂牌时相机推近了，退出来要回到点开女仆时的取景
            if (_current != null && _cameraFitter != null)
            {
                _cameraFitter.FocusKeepingAngle(_current.GetBounds(), _current.transform);
            }

            if (_current != null && _panel != null)
            {
                ReopenPanel();
            }
        }

        void OpenMask()
        {
            if (_mask == null || _current == null || _mask.IsActive)
            {
                return;
            }

            if (!_mask.Begin(_current))
            {
                return;
            }

            TriggerInteractionFeeling("mask", "戴面具");
            _panel.Close();
            if (_menu != null)
            {
                _menu.Hide();
            }

            FocusMask();
        }

        /// <summary>面具取景：框住面具（连脸一起），她在面板里已经面向镜头了</summary>
        void FocusMask()
        {
            if (_cameraFitter == null || _mask == null || _current == null)
            {
                return;
            }

            float size = _maskOrthographicSize > 0f ? _maskOrthographicSize : _mask.FocusOrthographicSize;
            if (size <= 0f)
            {
                return;
            }

            Bounds focus;
            if (!_mask.TryGetFocusBounds(out focus))
            {
                return;
            }

            _cameraFitter.FocusKeepingAngle(focus, _current.transform, size,
                _mask.FocusPitchDegrees >= 0f ? _mask.FocusPitchDegrees : _cameraFitter.FocusPitchDegrees,
                _cameraFitter.FocusLookHeightRatio);
        }

        void OnMaskEnded()
        {
            // 面具时相机推近了，退出来要回到点开女仆时的取景
            if (_current != null && _cameraFitter != null)
            {
                _cameraFitter.FocusKeepingAngle(_current.GetBounds(), _current.transform);
            }

            if (_current != null && _panel != null)
            {
                ReopenPanel();
            }
        }

        /// <summary>梳毛要看毛的立体感，取景动画停下来以后把相机切成透视（值没变就不动相机）</summary>
        void ApplyGroomProjection()
        {
            if (_cameraFitter == null || _groomPerspectiveFov == 0f)
            {
                return;
            }

            float fov = _groomPerspectiveFov > 0f ? _groomPerspectiveFov : AutoGroomPerspectiveFov();
            if (fov <= 1f)
            {
                return;
            }

            if (_cameraFitter.IsMoving)
            {
                // 取景动画期间要一路按回来，不然那一整段都是正交（换工具时的"闪一下"就是这个）
                _appliedGroomFov = fov;
                _cameraFitter.SetPerspective(fov);
                return;
            }

            if (Mathf.Abs(_appliedGroomFov - fov) < 0.01f)
            {
                return;
            }

            _appliedGroomFov = fov;
            _cameraFitter.SetPerspective(fov);
        }

        /// <summary>
        /// 正交取景已经把画面大小算好了，切透视时按"同一个画面高度"反推 fov：
        /// 固定一个 30° 的话，每次重新取景（躺下、转向）之后画面都会突然放大一下，
        /// 看着就是镜头一会近一会远
        /// </summary>
        float AutoGroomPerspectiveFov()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return 0f;
            }

            Vector3 center = _hasGroomFocus
                ? _groomFocusCenter
                : (_current != null ? _current.GetBounds().center : camera.transform.position + camera.transform.forward);
            float distance = Vector3.Distance(camera.transform.position, center);
            if (distance < 0.1f)
            {
                return 0f;
            }

            return 2f * Mathf.Atan(Mathf.Max(0.01f, camera.orthographicSize) / distance) * Mathf.Rad2Deg;
        }

        /// <summary>退出/中断梳毛：把相机切回正交</summary>
        void RestoreGroomProjection()
        {
            if (_appliedGroomFov <= 0f)
            {
                return;
            }

            _appliedGroomFov = -1f;
            if (_cameraFitter != null)
            {
                _cameraFitter.SetPerspective(0f);
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
