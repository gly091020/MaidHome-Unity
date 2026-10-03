using System;
using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.Input;
using MaidHome.Gameplay.Audio;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸脸模式，玩法照抄 moreanimation 的 FaceInteractionState / FaceSlapStroke：
    /// 抓住耳朵拖 → 耳朵跟着转，拖得越远拉得越长，超过阈值她会喊疼（闪红 + 台词 + 受伤语音）；
    /// 戳眼睛 / 戳脸颊 → 头弹一下，戳眼睛反应更大；
    /// **在脸上起手快速左右挥 → 扇耳光**（响一巴掌 + 头甩过去 + 连击计数）；
    /// **在脸外起手拖动 → 拖脸**，头跟着手转，掰过头会喊疼。
    /// 也就是：起手落在脸上 = 扇脸（点一下还是戳脸），起手落在空白处 = 拖脸。
    /// 戳脸的反应方向按**屏幕左右**算（模组也是），别拿 Zone 名字取符号——那是骨骼名，和屏幕左右相反。
    /// 相机全程不动，靠她自己面对镜头（进模式时 MaidInteractionController 已经让她转过来了）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidFaceInteraction : MonoBehaviour
    {
        public event Action Ended;

        enum Zone
        {
            None,
            Face,
            LeftCheek,
            RightCheek,
            LeftEye,
            RightEye,
            LeftEar,
            RightEar
        }

        // 照抄 moreanimation SmoothedPose 的弹簧参数（20Hz）
        const float SpringStiffness = 0.18f;
        const float SpringDamping = 0.76f;
        const float SpringStepSeconds = 1f / 20f;
        const float ReversalDistance = 0.012f;
        const int MaxStepsPerFrame = 8;

        [Header("拖耳朵")]
        [Tooltip("横向拖动时耳朵在画面里摆多少度（上限），方向反了改成负数")]
        [SerializeField] private float _earRollDegrees = -55f;
        [Tooltip("纵向拖动时耳朵前后摆多少度（上限）")]
        [SerializeField] private float _earPitchDegrees = -30f;
        [Tooltip("绕竖直轴拧多少度（上限），一般用不上")]
        [SerializeField] private float _earTwistDegrees = 0f;
        [Tooltip("拖多少屏幕高度算「拖到底」，越大越迟钝（tanh 软化）")]
        [SerializeField] private float _earDragResponse = 0.12f;
        [Tooltip("拖出这么多屏幕高度才开始拉长")]
        [SerializeField] private float _earStretchStart = 0.05f;
        [Tooltip("拖到这么多屏幕高度就拉满")]
        [SerializeField] private float _earStretchFull = 0.26f;
        [Tooltip("耳朵最多拉长百分之多少")]
        [SerializeField] private float _earStretchMax = 0.35f;
        [Tooltip("拉长超过这个比例就算拉太狠，她会喊疼")]
        [SerializeField] private float _earDamageStretch = 0.2f;

        [Header("拽脸")]
        [Tooltip("在空白处起手拖动，超过屏幕高度的这个比例才算「拖脸」（起手在脸上的手势归扇脸/戳脸，不拖脸）；阈值照抄模组 DRAG_THRESHOLD = 0.012")]
        [SerializeField] private float _faceDragStartRatio = 0.012f;
        [Tooltip("拖到屏幕边缘时脸最多往左右各转多少度")]
        [SerializeField] private float _faceDragMaxYaw = 105f;
        [Tooltip("拖到屏幕边缘时头最多往上/往下转多少度")]
        [SerializeField] private float _faceDragMaxPitch = 100f;
        [Tooltip("超过这个角度就算拽太狠，她会喊疼（模组 FACE_DAMAGE_ANGLE = 65°）")]
        [SerializeField] private float _faceDragDamageAngle = 65f;
        [Tooltip("松手后回正速度，度/秒")]
        [SerializeField] private float _faceDragReturnSpeed = 300f;

        [Header("扇耳光")]
        [Tooltip("一次横挥至少要划过多少屏幕高度")]
        [SerializeField] private float _slapMinDistance = 0.07f;
        [SerializeField] private float _slapMaxSeconds = 0.4f;
        [Tooltip("横挥速度下限，单位是屏幕高度/秒")]
        [SerializeField] private float _slapMinSpeed = 0.2f;
        [Tooltip("横向位移要大于纵向的多少倍才算「挥」，竖着划脸不算")]
        [SerializeField] private float _slapHorizontalRatio = 2f;
        [Tooltip("两巴掌之间至少隔这么久")]
        [SerializeField] private float _slapRepeatDelay = 0.1f;
        [Tooltip("连着挥的话，多久没再扇到就断连击")]
        [SerializeField] private float _comboTimeoutSeconds = 2.5f;
        [Tooltip("连击到多少的整数倍时放彩蛋（模组是 100）")]
        [SerializeField] private int _comboMilestoneStep = 100;
        [SerializeField] private float _slapYawDegrees = 38f;
        [SerializeField] private float _slapRollDegrees = 10f;
        [Tooltip("slap.ogg 的放大倍率（原文件录得小）")]
        [SerializeField] private float _slapVolume = 6f;
        [Tooltip("100 连抽的彩蛋音，留空就用现场合成的上行琶音")]
        [SerializeField] private AudioClip _milestoneClip;

        [Header("戳脸")]
        [SerializeField] private float _eyeYawDegrees = 24f;
        [SerializeField] private float _eyePitchDegrees = 12f;
        [SerializeField] private float _cheekYawDegrees = 12f;
        [SerializeField] private float _cheekPitchDegrees = 5f;
        [Tooltip("按下到抬起不超过这么久才算「戳」")]
        [SerializeField] private float _tapMaxSeconds = 0.3f;
        [Tooltip("按下到抬起位移不超过这么多像素才算「戳」")]
        [SerializeField] private float _tapSlopPixels = 24f;
        [Tooltip("两次戳之间至少隔这么久，眼睛和脸颊**共用**这一个冷却；调小到 0 就是不限")]
        [SerializeField] private float _pokeCooldownSeconds = 0.2f;
        [Tooltip("戳脸那套反应（闪红 + 台词 + 语音 + 受伤动画）的冷却，0 = 每次戳都演；和拖脸掰疼那句是分开的")]
        [SerializeField] private float _pokeReactionCooldownSeconds = 0f;
        [SerializeField] private float _hitPaddingPixels = 6f;
        [Tooltip("耳朵判定区收多少：1 = 耳朵网格的投影包围盒，越小越难点到耳朵（脸的区域就更大）")]
        [SerializeField] private float _earZoneScale = 0.75f;

        [Header("转身")]
        [Tooltip("进摸脸模式时她转过来面对镜头要多久")]
        [SerializeField] private float _turnSeconds = 0.35f;

        [Header("音量")]
        [Tooltip("女仆受伤语音的放大倍率")]
        [SerializeField] private float _voiceVolume = 6f;

        [Header("闪红")]
        [SerializeField] private Color _flashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _flashSeconds = 0.5f;

        [Header("台词")]
        [SerializeField] private float _lineSeconds = 1f;
        [SerializeField] private float _complainCooldownSeconds = 1f;
        [SerializeField] private float _slapLineCooldownSeconds = 0.6f;

        [Header("受伤动画")]
        [Tooltip("被拉疼 / 被戳 / 被扇的时候借这条动画演一下（TLM 里「被打」叫 attacked）")]
        [SerializeField] private string _hurtClip = "attacked";
        [Tooltip("不管动画多长，演这么久就换回来")]
        [SerializeField] private float _hurtMaxSeconds = 1f;

        [Header("调试")]
        [Tooltip("把触发范围画在屏幕上（红=眼睛 黄=脸颊 青=耳朵 蓝=整张脸），运行时按 F8 也能开关")]
        [SerializeField] private bool _showZones;

        [Header("连击显示")]
        [Tooltip("「连击 ×N」字号占屏幕宽度的比例")]
        [SerializeField] private float _comboSizeRatio = 0.085f;
        [Tooltip("「连击 ×N」距屏幕顶部占屏幕高度的比例（越大越往下）")]
        [SerializeField] private float _comboTopRatio = 0.035f;
        [Tooltip("「N连抽」字高占屏幕宽度的比例")]
        [SerializeField] private float _milestoneSizeRatio = 0.11f;
        [Tooltip("「N连抽」距屏幕顶部占屏幕高度的比例")]
        [SerializeField] private float _milestoneTopRatio = 0.12f;

        // 台词照抄 moreanimation 的 zh_cn
        static readonly string[] EarLines =
        {
            "呀！耳朵好疼，别再拉了！",
            "主人轻一点！耳朵真的要被你扯坏啦！",
            "呜……那里很敏感的，不可以这么用力！",
            "疼疼疼！快松开我的耳朵！"
        };

        static readonly string[] EyeLines =
        {
            "啊！眼睛！那里不能戳啊！",
            "好痛！主人你怎么可以戳眼睛！",
            "呜哇！眼睛要睁不开了……",
            "主人！那里真的会受伤的！"
        };

        static readonly string[] FaceLines =
        {
            "疼疼疼！脸都要被你掰过去了！",
            "主人，轻一点呀！脖子都快扭到了……",
            "呜……别再往那边拉啦，真的很疼！",
            "主人！我的脸不是这样玩的呀！"
        };

        static readonly string[] SlapLines =
        {
            "呀！主人你怎么突然打我的脸！",
            "好痛！不许这样打啦！",
            "呜……脸都被你打歪了！",
            "主人！你还要打几下呀！",
            "疼疼疼！轻一点啦！",
            "呜哇！又来？！"
        };

        public MaidFacePanel _panel;

        public bool IsActive { get; private set; }

        MaidAgent _agent;
        MaidFaceRig _rig;
        AudioSource _source;
        AudioClip _slapClip;

        // 头部弹簧（角度制，按 20Hz 走，渲染时在两步之间插值）
        float _prevYaw;
        float _yaw;
        float _yawVelocity;
        float _prevPitch;
        float _pitch;
        float _pitchVelocity;
        float _prevRoll;
        float _roll;
        float _rollVelocity;
        float _maxYawVelocity = 24f;
        float _maxPitchVelocity = 12f;
        float _maxRollVelocity = 10f;
        float _simAccumulator;

        Quaternion _headAnimated;
        Quaternion _headWritten;
        bool _headHasAnimated;

        /// <summary>一根手指正在拖的那只耳朵（左右各一份，可以同时拽）</summary>
        sealed class EarGrab
        {
            public int FingerId;
            public List<Transform> Chain;
            public int[] Axes;
            public Vector3[] RestScales;
            public Quaternion[] Animated;
            public Quaternion[] Written;
            public bool[] HasAnimated;
            public Vector2 Origin;
            public float Roll;
            public float Pitch;
            public float Twist;
            public float Stretch;
            public bool Applied;
        }

        /// <summary>一根手指按在屏幕上：可能是戳（点一下），也可能是横挥</summary>
        sealed class PressState
        {
            public int FingerId;
            public Zone Zone;
            public Vector2 Origin;
            public Vector2 Last;
            public float StartedAt;
            public bool Dragged;
        }

        /// <summary>一根手指正在拽脸：按抓取点到屏幕各边的距离归一化，拖到那条边就是满偏</summary>
        sealed class FaceDrag
        {
            public int FingerId;
            public Vector2 Origin;
            public float OriginX;
            public float OriginY;
        }

        readonly List<PointerInput.Pointer> _framePointers = new List<PointerInput.Pointer>();
        readonly List<PressState> _presses = new List<PressState>();

        EarGrab _leftEar;
        EarGrab _rightEar;
        FaceDrag _faceDrag;
        float _faceDragYaw;
        float _faceDragPitch;
        int _strokeFinger = -1;

        // 扇耳光的横挥识别（照抄 FaceSlapStroke）
        bool _strokeHeld;
        float _strokeStartX;
        float _strokeStartY;
        float _strokeLastX;
        float _strokeLastY;
        float _strokeStartedAt;
        float _strokeLastTime;
        int _strokeDirection;
        int _lastSlapDirection;
        float _lastSlapAt = -100f;

        readonly MaidSlapComboHud _comboHud = new MaidSlapComboHud();
        int _panelCombo = -1;
        float _lastComplainAt = -100f;
        float _lastPokeReactionAt = -100f;
        float _lastSlapLineAt = -100f;
        float _lastPokeAt = -100f;

        Coroutine _hurtRoutine;
        bool _hurtPlaying;
        string _hurtReturnClip;
        float _hurtReturnTime;

        static Texture2D _ellipse;

        /// <summary>这个女仆能不能摸脸（有头就行），用来决定面板按钮能不能点</summary>
        public bool Supports(MaidAgent agent)
        {
            if (agent == null || (agent.Save != null && agent.Save.SimpleBedrockModel))
            {
                return false;
            }

            return MaidFaceRig.Build(agent.transform).Supports;
        }

        public bool Begin(MaidAgent agent, AudioClip slapClip)
        {
            if (agent == null)
            {
                return false;
            }

            Abort();

            // 方块模型的女仆（这种走 SimpleBedrockModel）没有 GeckoLib 那套骨骼
            if (agent.Save != null && agent.Save.SimpleBedrockModel)
            {
                Debug.LogWarning("SimpleBedrockModel 的女仆不支持摸脸: " + agent.name);
                return false;
            }

            _rig = MaidFaceRig.Build(agent.transform);
            if (!_rig.Supports)
            {
                Debug.LogWarning("这个模型的骨架里没找到头，摸不了脸: " + agent.name);
                return false;
            }

            _agent = agent;
            _slapClip = slapClip;
            // 场景里没接就退回包里自带的那个，省得忘了接线就没声音
            if (_slapClip == null)
            {
                _slapClip = Resources.Load<AudioClip>("MoreAnimation/slap");
            }

            if (_slapClip == null)
            {
                Debug.LogWarning("摸脸模式没找到巴掌音效：场景里的 _slapClip 没接，"
                    + "Assets/Resources/MoreAnimation/slap.ogg 也没有", this);
            }

            _simAccumulator = 0f;
            _headHasAnimated = false;
            ClearEars();
            EnsurePanel();
            if (_panel != null)
            {
                _panel.SetCombo(0);
                _panel.Show();
            }

            _comboHud.Timeout = _comboTimeoutSeconds;
            _comboHud.Step = _comboMilestoneStep;
            _comboHud.ComboSizeRatio = _comboSizeRatio;
            _comboHud.ComboTopRatio = _comboTopRatio;
            _comboHud.MilestoneSizeRatio = _milestoneSizeRatio;
            _comboHud.MilestoneTopRatio = _milestoneTopRatio;
            _comboHud.Clear();
            _panelCombo = -1;
            IsActive = true;
            FaceCamera();
            return true;
        }

        /// <summary>玩家自己退出：退回去重新打开女仆面板</summary>
        public void End()
        {
            if (!IsActive)
            {
                return;
            }

            Release();
            if (Ended != null)
            {
                Ended();
            }
        }

        /// <summary>被外层关掉：不回调</summary>
        public void Abort()
        {
            Release();
        }

        void OnDestroy()
        {
            Release();
            Ended = null;
        }

        void Release()
        {
            StopHurtAnimation();
            ClearEars();
            _presses.Clear();
            _faceDrag = null;
            _faceDragYaw = 0f;
            _faceDragPitch = 0f;
            _strokeFinger = -1;
            _strokeHeld = false;
            if (_panel != null)
            {
                _panel.Hide();
            }

            _agent = null;
            _rig = null;
            _slapClip = null;
            _yaw = _prevYaw = _yawVelocity = 0f;
            _pitch = _prevPitch = _pitchVelocity = 0f;
            _roll = _prevRoll = _rollVelocity = 0f;
            _headHasAnimated = false;
            _comboHud.Clear();
            _panelCombo = -1;
            IsActive = false;
        }

        void Update()
        {
            if (!IsActive)
            {
                return;
            }

            if (_agent == null || !_agent.gameObject.activeInHierarchy)
            {
                Abort();
                return;
            }

            // Android 返回键 / 电脑 Esc
            if (PointerInput.BackPressed)
            {
                End();
                return;
            }

            if (PointerInput.DebugTogglePressed)
            {
                _showZones = !_showZones;
            }

            // 连击超时后把面板那份文字也清掉（彩虹大字是 HUD 自己画的）
            SyncPanelCombo();

            // 每根手指各管一份状态，所以可以两根手指同时拽左右耳 / 同时戳两只眼睛
            PointerInput.CopyPointers(_framePointers);
            for (int i = 0; i < _framePointers.Count; i++)
            {
                PointerInput.Pointer pointer = _framePointers[i];
                if (pointer.Pressed)
                {
                    StartGesture(pointer);
                }

                if (pointer.Released)
                {
                    EndGesture(pointer);
                }
                else if (!pointer.Pressed && pointer.Held)
                {
                    ContinueGesture(pointer);
                }
            }
        }

        void StartGesture(PointerInput.Pointer pointer)
        {
            if (pointer.OverUi)
            {
                return;
            }

            Zone zone = HitZone(pointer.Position);
            if (zone == Zone.LeftEar || zone == Zone.RightEar)
            {
                bool left = zone == Zone.LeftEar;
                if (FindEar(left) == null)
                {
                    StartEar(left, pointer);
                }

                return;
            }

            PressState press = new PressState();
            press.FingerId = pointer.FingerId;
            press.Zone = zone;
            press.Origin = pointer.Position;
            press.Last = pointer.Position;
            press.StartedAt = Time.unscaledTime;
            _presses.Add(press);

            // 起手落在脸上（含眼睛/脸颊）= 扇脸，点一下就是戳脸；起手落在空白处才是拖脸。
            // 横挥同时只认一根手指（第一根按下的），免得两根手指互相打断节奏
            if (IsFaceZone(zone) && _strokeFinger < 0)
            {
                _strokeFinger = pointer.FingerId;
                BeginStroke(pointer.Position);
            }
        }

        void ContinueGesture(PointerInput.Pointer pointer)
        {
            EarGrab ear = FindEar(pointer.FingerId);
            if (ear != null)
            {
                DragEar(ear, pointer.Position);
                return;
            }

            PressState press = FindPress(pointer.FingerId);
            if (press == null)
            {
                return;
            }

            press.Last = pointer.Position;

            // 拖脸：起手在空白处、拖过阈值就开始，之后一直跟到松手；这条手势不会去判耳光
            if (press.Zone == Zone.None && (_faceDrag == null || _faceDrag.FingerId == pointer.FingerId))
            {
                if (_faceDrag == null)
                {
                    float travel = (press.Last - press.Origin).magnitude / Mathf.Max(1f, Screen.height);
                    if (travel >= _faceDragStartRatio)
                    {
                        press.Dragged = true;
                        StartFaceDrag(press);
                    }
                }
                else
                {
                    UpdateFaceDrag(press.Last);
                }
            }

            if (pointer.FingerId == _strokeFinger)
            {
                TrackPress(press);
            }
        }

        void EndGesture(PointerInput.Pointer pointer)
        {
            EarGrab ear = FindEar(pointer.FingerId);
            if (ear != null)
            {
                ReleaseEar(ear);
                return;
            }

            PressState press = FindPress(pointer.FingerId);
            if (press != null)
            {
                FinishPress(press);
            }
        }

        EarGrab FindEar(int fingerId)
        {
            if (_leftEar != null && _leftEar.FingerId == fingerId)
            {
                return _leftEar;
            }

            return _rightEar != null && _rightEar.FingerId == fingerId ? _rightEar : null;
        }

        EarGrab FindEar(bool left)
        {
            return left ? _leftEar : _rightEar;
        }

        PressState FindPress(int fingerId)
        {
            for (int i = 0; i < _presses.Count; i++)
            {
                if (_presses[i].FingerId == fingerId)
                {
                    return _presses[i];
                }
            }

            return null;
        }

        /// <summary>下一个能接手横挥的手指：起手落在脸上的那种</summary>
        PressState FindStrokeCandidate()
        {
            for (int i = 0; i < _presses.Count; i++)
            {
                if (IsFaceZone(_presses[i].Zone))
                {
                    return _presses[i];
                }
            }

            return null;
        }

        /// <summary>起手落在脸上（含眼睛/脸颊）的手势：横挥算扇脸，点一下算戳脸</summary>
        static bool IsFaceZone(Zone zone)
        {
            return zone == Zone.Face || zone == Zone.LeftEye || zone == Zone.RightEye
                || zone == Zone.LeftCheek || zone == Zone.RightCheek;
        }

        void LateUpdate()
        {
            if (!IsActive)
            {
                return;
            }

            // 弹簧是 20Hz 算的，补一个固定步长
            _simAccumulator += Time.unscaledDeltaTime;
            int guard = 0;
            while (_simAccumulator >= SpringStepSeconds && guard++ < MaxStepsPerFrame)
            {
                _simAccumulator -= SpringStepSeconds;
                StepSprings();
            }

            float alpha = Mathf.Clamp01(_simAccumulator / SpringStepSeconds);
            ApplyFace(alpha);
        }

        /// <summary>头的这块包围盒，摸脸模式用来把相机推近</summary>
        public bool TryGetHeadBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_rig == null || _rig.Head == null)
            {
                return false;
            }

            if (_rig.HeadMesh != null)
            {
                bounds = _rig.HeadMesh.bounds;
                return true;
            }

            bounds = new Bounds(_rig.Head.position, Vector3.one * 0.3f);
            return true;
        }

        // ---------- 头部反应 ----------

        void StepSprings()
        {
            _prevYaw = _yaw;
            _prevPitch = _pitch;
            _prevRoll = _roll;
            _yawVelocity = Mathf.Clamp((_yawVelocity - _yaw * SpringStiffness) * SpringDamping,
                -_maxYawVelocity, _maxYawVelocity);
            _pitchVelocity = Mathf.Clamp((_pitchVelocity - _pitch * SpringStiffness) * SpringDamping,
                -_maxPitchVelocity, _maxPitchVelocity);
            _rollVelocity = Mathf.Clamp((_rollVelocity - _roll * SpringStiffness) * SpringDamping,
                -_maxRollVelocity, _maxRollVelocity);
            _yaw += _yawVelocity;
            _pitch += _pitchVelocity;
            _roll += _rollVelocity;

            if (Mathf.Abs(_yaw) < 0.02f && Mathf.Abs(_yawVelocity) < 0.02f)
            {
                _yaw = 0f;
                _yawVelocity = 0f;
            }

            if (Mathf.Abs(_pitch) < 0.02f && Mathf.Abs(_pitchVelocity) < 0.02f)
            {
                _pitch = 0f;
                _pitchVelocity = 0f;
            }

            if (Mathf.Abs(_roll) < 0.02f && Mathf.Abs(_rollVelocity) < 0.02f)
            {
                _roll = 0f;
                _rollVelocity = 0f;
            }
        }

        void Kick(float yaw, float pitch, float roll)
        {
            _yawVelocity = Mathf.Clamp(_yawVelocity + yaw, -_maxYawVelocity, _maxYawVelocity);
            _pitchVelocity = Mathf.Clamp(_pitchVelocity + pitch, -_maxPitchVelocity, _maxPitchVelocity);
            _rollVelocity = Mathf.Clamp(_rollVelocity + roll, -_maxRollVelocity, _maxRollVelocity);
        }

        void ApplyFace(float alpha)
        {
            Camera camera = Camera.main;

            // 松手后拽脸的偏角滑回 0（弹簧那套只负责耳光/戳脸的冲击）
            if (_faceDrag == null)
            {
                float back = Mathf.Max(1f, _faceDragReturnSpeed) * Time.unscaledDeltaTime;
                _faceDragYaw = Mathf.MoveTowards(_faceDragYaw, 0f, back);
                _faceDragPitch = Mathf.MoveTowards(_faceDragPitch, 0f, back);
            }

            float yaw = Mathf.Lerp(_prevYaw, _yaw, alpha) + _faceDragYaw;
            float pitch = Mathf.Lerp(_prevPitch, _pitch, alpha) + _faceDragPitch;
            float roll = Mathf.Lerp(_prevRoll, _roll, alpha);

            if (_rig != null && _rig.Head != null
                && (Mathf.Abs(yaw) > 0.01f || Mathf.Abs(pitch) > 0.01f || Mathf.Abs(roll) > 0.01f))
            {
                Quaternion worldDelta = CameraSpaceRotation(camera, pitch, yaw, roll);
                ApplyWorldDelta(_rig.Head, worldDelta, ref _headAnimated, ref _headWritten, ref _headHasAnimated);
            }

            ApplyEar(camera, _leftEar);
            ApplyEar(camera, _rightEar);
        }

        void ApplyEar(Camera camera, EarGrab ear)
        {
            if (ear == null || ear.Chain == null || ear.Chain.Count == 0)
            {
                return;
            }

            if (ear.FingerId < 0)
            {
                // 松手后滑回静止姿势，别硬切
                float back = 240f * Time.unscaledDeltaTime;
                ear.Roll = Mathf.MoveTowards(ear.Roll, 0f, back);
                ear.Pitch = Mathf.MoveTowards(ear.Pitch, 0f, back);
                ear.Twist = Mathf.MoveTowards(ear.Twist, 0f, back);
                ear.Stretch = Mathf.MoveTowards(ear.Stretch, 0f, 1.2f * Time.unscaledDeltaTime);
            }

            bool idle = Mathf.Abs(ear.Roll) < 0.02f && Mathf.Abs(ear.Pitch) < 0.02f
                && Mathf.Abs(ear.Twist) < 0.02f && ear.Stretch < 0.0005f;
            if (idle)
            {
                RestoreEarScale(ear);
                if (ear.FingerId < 0)
                {
                    // 已经滑回原位了，把这份状态丢掉
                    DropEar(ear);
                }

                return;
            }

            ear.Applied = true;
            Quaternion worldDelta = CameraSpaceRotation(camera, ear.Pitch, ear.Twist, ear.Roll);
            for (int i = 0; i < ear.Chain.Count; i++)
            {
                Transform bone = ear.Chain[i];
                if (bone == null)
                {
                    continue;
                }

                // 越往耳尖转得越少，看着像整条耳朵被拽着走
                Quaternion weight = Quaternion.Slerp(Quaternion.identity, worldDelta, i == 0 ? 1f : 0.45f);
                ApplyWorldDelta(bone, weight, ear.Animated, ear.Written, ear.HasAnimated, i);
            }

            // 拉长按 moreanimation 的做法沿"骨骼指向上方"的那个轴缩放
            int last = ear.Chain.Count - 1;
            Transform tip = ear.Chain[last];
            if (tip != null)
            {
                Vector3 scale = ear.RestScales[last];
                scale[ear.Axes[last]] *= 1f + ear.Stretch;
                tip.localScale = scale;
            }
        }

        static Quaternion CameraSpaceRotation(Camera camera, float pitch, float yaw, float roll)
        {
            Quaternion local = Quaternion.Euler(pitch, yaw, roll);
            if (camera == null)
            {
                return local;
            }

            Quaternion cam = camera.transform.rotation;
            return cam * local * Quaternion.Inverse(cam);
        }

        /// <summary>
        /// 把世界空间的旋转叠到骨骼上。动画每帧都会写 localRotation，所以要用"上次自己写的值"
        /// 判断这一帧动画有没有写过，避免把动画姿势吃掉（和 MaidTailChain 一个套路）。
        /// </summary>
        static void ApplyWorldDelta(Transform bone, Quaternion worldDelta, ref Quaternion animated,
            ref Quaternion written, ref bool hasAnimated)
        {
            Quaternion current = bone.localRotation;
            Quaternion basis = hasAnimated && Quaternion.Dot(current, written) > 0.999999f ? animated : current;
            animated = basis;
            hasAnimated = true;

            Quaternion parent = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
            written = basis * (Quaternion.Inverse(parent) * worldDelta * parent);
            bone.localRotation = written;
        }

        static void ApplyWorldDelta(Transform bone, Quaternion worldDelta, Quaternion[] animated,
            Quaternion[] written, bool[] hasAnimated, int index)
        {
            bool has = hasAnimated[index];
            Quaternion value = animated[index];
            Quaternion last = written[index];
            ApplyWorldDelta(bone, worldDelta, ref value, ref last, ref has);
            animated[index] = value;
            written[index] = last;
            hasAnimated[index] = has;
        }

        // ---------- 耳朵 ----------

        void StartEar(bool left, PointerInput.Pointer pointer)
        {
            List<Transform> chain = left ? _rig.LeftEar : _rig.RightEar;
            if (chain == null || chain.Count == 0)
            {
                return;
            }

            // 同一只耳朵只允许一根手指抓：抢的时候把原来那根的手势交给新手指
            EarGrab previous = FindEar(left);
            if (previous != null)
            {
                // 先还原缩放，不然新抓的这份会把拉长后的比例当成静止比例记下来
                RestoreEarScale(previous);
                ReleaseEar(previous);
            }

            EarGrab ear = new EarGrab();
            ear.FingerId = pointer.FingerId;
            ear.Chain = new List<Transform>(chain);
            ear.Axes = new int[ear.Chain.Count];
            ear.RestScales = new Vector3[ear.Chain.Count];
            ear.Animated = new Quaternion[ear.Chain.Count];
            ear.Written = new Quaternion[ear.Chain.Count];
            ear.HasAnimated = new bool[ear.Chain.Count];
            for (int i = 0; i < ear.Chain.Count; i++)
            {
                Transform bone = ear.Chain[i];
                ear.Axes[i] = MaidFaceRig.StretchAxis(bone, Camera.main);
                ear.RestScales[i] = bone.localScale;
            }

            ear.Origin = pointer.Position;
            if (left)
            {
                _leftEar = ear;
            }
            else
            {
                _rightEar = ear;
            }
        }

        void DragEar(EarGrab ear, Vector2 pointer)
        {
            if (ear == null || ear.Chain == null || Screen.height <= 0)
            {
                return;
            }

            Vector2 delta = pointer - ear.Origin;
            float nx = delta.x / Screen.height;
            float ny = delta.y / Screen.height;
            ear.Roll = _earRollDegrees * (float)Math.Tanh(nx / _earDragResponse);
            ear.Pitch = _earPitchDegrees * (float)Math.Tanh(ny / _earDragResponse);
            ear.Twist = _earTwistDegrees * (float)Math.Tanh(nx / _earDragResponse);

            float distance = delta.magnitude / Screen.height;
            ear.Stretch = SmoothRange(distance, _earStretchStart, _earStretchFull) * _earStretchMax;
            if (ear.Stretch > _earDamageStretch)
            {
                Complain(EarLines);
            }
        }

        /// <summary>松手：不再跟手指，剩下的交给 ApplyEar 滑回原位</summary>
        void ReleaseEar(EarGrab ear)
        {
            if (ear != null)
            {
                ear.FingerId = -1;
            }
        }

        void RestoreEarScale(EarGrab ear)
        {
            if (ear != null && ear.Applied && ear.Chain != null && ear.RestScales != null)
            {
                for (int i = 0; i < ear.Chain.Count; i++)
                {
                    if (ear.Chain[i] != null)
                    {
                        ear.Chain[i].localScale = ear.RestScales[i];
                    }
                }
            }

            if (ear != null)
            {
                ear.Applied = false;
            }
        }

        void ClearEars()
        {
            RestoreEarScale(_leftEar);
            RestoreEarScale(_rightEar);
            _leftEar = null;
            _rightEar = null;
        }

        void DropEar(EarGrab ear)
        {
            if (ear == null)
            {
                return;
            }

            if (_leftEar == ear)
            {
                _leftEar = null;
            }

            if (_rightEar == ear)
            {
                _rightEar = null;
            }
        }

        // ---------- 拽脸 ----------

        void StartFaceDrag(PressState press)
        {
            FaceDrag drag = new FaceDrag();
            drag.FingerId = press.FingerId;
            drag.Origin = press.Origin;
            drag.OriginX = Mathf.Clamp01(press.Origin.x / Mathf.Max(1f, Screen.width));
            drag.OriginY = Mathf.Clamp01(press.Origin.y / Mathf.Max(1f, Screen.height));
            _faceDrag = drag;
            UpdateFaceDrag(press.Last);
        }

        /// <summary>
        /// 照抄模组：位移按「抓取点到那条屏幕边的距离」归一化，拖到边就是满偏；
        /// 角度走 0.15t + 0.85t^2.4 的软曲线，所以小拖动几乎不动，过半屏才明显扭。
        /// </summary>
        void UpdateFaceDrag(Vector2 pointer)
        {
            if (_faceDrag == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            float dx = (pointer.x - _faceDrag.Origin.x) / Screen.width;
            float dy = (pointer.y - _faceDrag.Origin.y) / Screen.height;
            float reachX = Mathf.Max(0.05f, dx >= 0f ? 1f - _faceDrag.OriginX : _faceDrag.OriginX);
            float reachY = Mathf.Max(0.05f, dy >= 0f ? 1f - _faceDrag.OriginY : _faceDrag.OriginY);

            // 正 yaw 是脸往画面左转，所以往右拖要取负，脸才跟着手走（和扇耳光一个约定）
            _faceDragYaw = -FaceAngle(dx / reachX, _faceDragMaxYaw);
            _faceDragPitch = FaceAngle(dy / reachY, _faceDragMaxPitch);

            if (Mathf.Max(Mathf.Abs(_faceDragYaw), Mathf.Abs(_faceDragPitch)) > _faceDragDamageAngle)
            {
                Complain(FaceLines);
            }
        }

        static float FaceAngle(float fraction, float max)
        {
            float t = Mathf.Min(1f, Mathf.Abs(fraction));
            return Mathf.Sign(fraction) * max * (0.15f * t + 0.85f * Mathf.Pow(t, 2.4f));
        }

        // ---------- 戳 / 扇 ----------

        void FinishPress(PressState press)
        {
            bool tap = !press.Dragged
                && Time.unscaledTime - press.StartedAt <= _tapMaxSeconds
                && (press.Last - press.Origin).magnitude <= _tapSlopPixels;
            _presses.Remove(press);

            if (_faceDrag != null && _faceDrag.FingerId == press.FingerId)
            {
                // 角度先留着，交给 ApplyFace 滑回 0
                _faceDrag = null;
            }

            if (press.FingerId == _strokeFinger)
            {
                // 负责横挥的那根手指松开了，交给还在按、而且起手也落在脸上的另一根（如果有）
                _strokeFinger = -1;
                _strokeHeld = false;
                PressState next = FindStrokeCandidate();
                if (next != null)
                {
                    _strokeFinger = next.FingerId;
                    BeginStroke(next.Last);
                }
            }

            if (tap)
            {
                ClickFace(press);
            }
        }

        void TrackPress(PressState press)
        {
            if (!press.Dragged && (press.Last - press.Origin).magnitude > _tapSlopPixels)
            {
                press.Dragged = true;
            }

            if (!_strokeHeld || Screen.height <= 0)
            {
                return;
            }

            Vector2 pointer = press.Last;
            float x = pointer.x / Screen.height;
            float y = pointer.y / Screen.height;
            float now = Time.unscaledTime;
            float step = x - _strokeLastX;
            int next = step > 0f ? 1 : step < 0f ? -1 : 0;
            if (now - _strokeLastTime > _slapMaxSeconds || now - _strokeStartedAt > _slapMaxSeconds
                || (next != 0 && _strokeDirection != 0 && next != _strokeDirection
                    && Mathf.Abs(step) >= ReversalDistance))
            {
                _strokeStartX = _strokeLastX;
                _strokeStartY = _strokeLastY;
                _strokeStartedAt = _strokeLastTime;
                _strokeDirection = next;
            }

            if (_strokeDirection == 0 && next != 0)
            {
                _strokeDirection = next;
            }

            // 抖一下不算换方向，别把抖动当成一次挥
            if (next != 0 && _strokeDirection != 0 && next != _strokeDirection
                && Mathf.Abs(step) < ReversalDistance)
            {
                return;
            }

            _strokeLastX = x;
            _strokeLastY = y;
            _strokeLastTime = now;

            float dx = x - _strokeStartX;
            float dy = y - _strokeStartY;
            float elapsed = now - _strokeStartedAt;
            int sign = dx > 0f ? 1 : -1;
            if (elapsed <= 0f || elapsed > _slapMaxSeconds || Mathf.Abs(dx) < _slapMinDistance
                || Mathf.Abs(dx) < Mathf.Abs(dy) * _slapHorizontalRatio
                || Mathf.Abs(dx) / elapsed < _slapMinSpeed
                || now - _lastSlapAt < _slapRepeatDelay
                || sign == _lastSlapDirection)
            {
                return;
            }

            _lastSlapDirection = sign;
            _strokeStartX = x;
            _strokeStartY = y;
            _strokeStartedAt = now;
            DoSlap(sign);
        }

        void BeginStroke(Vector2 pointer)
        {
            _strokeHeld = true;
            _strokeStartX = _strokeLastX = pointer.x / Mathf.Max(1f, Screen.height);
            _strokeStartY = _strokeLastY = pointer.y / Mathf.Max(1f, Screen.height);
            _strokeStartedAt = _strokeLastTime = Time.unscaledTime;
            _strokeDirection = 0;
            _lastSlapDirection = 0;
        }

        /// <summary>
        /// 戳脸弹一下。方向按**点在屏幕上的左右**定（不能看 Zone 名字，那是模型骨骼名，和屏幕左右是反的）：
        /// 屏幕左 = +1、屏幕右 = -1。反了就翻下面这一个条件。
        /// 眼睛和脸颊共用一份冷却：冷却里再戳不弹也不喊。注意两根手指同时戳会被这个共用冷却挡掉后一下。
        /// </summary>
        void ClickFace(PressState press)
        {
            if (Time.unscaledTime - _lastPokeAt < _pokeCooldownSeconds)
            {
                return;
            }

            _lastPokeAt = Time.unscaledTime;
            int side = 1;
            FaceZones zones;
            if (TryBuildZones(out zones) && press.Origin.x > zones.Head.center.x)
            {
                side = -1;
            }

            switch (press.Zone)
            {
                case Zone.LeftEye:
                case Zone.RightEye:
                    Kick(side * _eyeYawDegrees, _eyePitchDegrees, 0f);
                    // 戳的反应有自己的冷却（默认 0 = 每次都演），别被拖脸那句的 1 秒冷却连带限制
                    if (Time.unscaledTime - _lastPokeReactionAt >= _pokeReactionCooldownSeconds)
                    {
                        _lastPokeReactionAt = Time.unscaledTime;
                        ComplainNow(EyeLines);
                    }

                    break;
                case Zone.LeftCheek:
                case Zone.RightCheek:
                    Kick(side * _cheekYawDegrees, _cheekPitchDegrees, 0f);
                    break;
            }
        }

        void DoSlap(int sign)
        {
            _lastSlapAt = Time.unscaledTime;
            // yaw 正值是「脸朝画面左转」（绕相机 up 轴），所以往右挥要取负，头才跟着手走；
            // roll 反过来：负值是往右倒，正好也是顺着挥的方向
            Kick(-sign * _slapYawDegrees, 0f, -sign * _slapRollDegrees);
            PlaySlapSound();
            PlayHurtAnimation();
            MaidDamageFlash.Flash(_agent != null ? _agent.gameObject : null, _flashColor, _flashSeconds);

            _comboHud.Timeout = _comboTimeoutSeconds;
            _comboHud.Step = _comboMilestoneStep;
            _comboHud.Confirm();
            SyncPanelCombo();

            // 模组：每 100 连放一次「N连抽」+ 彩蛋音，连击期间不刷台词
            if (_comboHud.MilestoneHit)
            {
                PlayMilestoneSound();
                return;
            }

            if (Time.unscaledTime - _lastSlapLineAt > _slapLineCooldownSeconds)
            {
                _lastSlapLineAt = Time.unscaledTime;
                ShowLine(Pick(SlapLines));
            }
        }

        void SyncPanelCombo()
        {
            int count = _comboHud.Count;
            if (count == _panelCombo)
            {
                return;
            }

            _panelCombo = count;
            if (_panel != null)
            {
                _panel.SetCombo(count);
            }
        }

        void PlayMilestoneSound()
        {
            AudioClip clip = _milestoneClip != null ? _milestoneClip : AudioJingle.Milestone();
            if (clip == null)
            {
                return;
            }

            EnsureSource();
            _source.pitch = 1f;
            _source.volume = Mathf.Clamp(MaidAudioService.MasterVolume, 0f, MaidAudioVoice.MaxVolume);
            _source.PlayOneShot(clip, 1f);
        }

        void Complain(string[] lines)
        {
            // 拖脸那句"掰疼了"的冷却；戳脸走自己的 _pokeReactionCooldownSeconds，不共用这个
            if (Time.unscaledTime - _lastComplainAt < _complainCooldownSeconds)
            {
                return;
            }

            _lastComplainAt = Time.unscaledTime;
            ComplainNow(lines);
        }

        /// <summary>不判冷却，直接演一套：闪红 + 台词 + 受伤语音 + 受伤动画</summary>
        void ComplainNow(string[] lines)
        {
            MaidDamageFlash.Flash(_agent != null ? _agent.gameObject : null, _flashColor, _flashSeconds);
            ShowLine(Pick(lines));
            MaidAudioService.Play(_agent, MaidSoundId.Hurt, false, _voiceVolume);
            PlayHurtAnimation();
        }

        void ShowLine(string line)
        {
            if (_panel != null)
            {
                _panel.ShowLine(line, _lineSeconds);
            }
        }

        void PlaySlapSound()
        {
            if (_slapClip == null)
            {
                return;
            }

            EnsureSource();

            // 倍率走采样放大，AudioSource.volume 上不了 1
            _source.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
            _source.volume = Mathf.Clamp(MaidAudioService.MasterVolume, 0f, MaidAudioVoice.MaxVolume);
            _source.PlayOneShot(AudioGain.Amplify(_slapClip, _slapVolume), 1f);
        }

        void EnsureSource()
        {
            if (_source != null)
            {
                return;
            }

            GameObject holder = new GameObject("MaidFaceAudio");
            holder.transform.SetParent(transform, false);
            _source = holder.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }

        // ---------- 命中判定 ----------

        /// <summary>屏幕上各种触发区的位置：命中判定和 F8 调试显示共用这一份，免得两处对不上</summary>
        struct FaceZones
        {
            public bool Valid;
            public Rect Head;
            public Rect LeftEye;
            public Rect RightEye;
            public Rect LeftCheek;
            public Rect RightCheek;
            public Rect LeftEar;
            public Rect RightEar;
            public bool HasLeftEar;
            public bool HasRightEar;
        }

        /// <summary>
        /// 各区的比例照抄 moreanimation（眼睛在头中心上方 0.28、半径 0.25/0.22；脸颊再往下 0.38）。
        /// 注意 Unity 的屏幕坐标 y 是向上的，和模组那套「上为正」的归一化坐标一致。
        /// </summary>
        bool TryBuildZones(out FaceZones zones)
        {
            zones = new FaceZones();
            Camera camera = Camera.main;
            if (camera == null || _rig == null || _rig.Head == null)
            {
                return false;
            }

            Rect head;
            if (!TryHeadRect(camera, out head))
            {
                return false;
            }

            zones.Head = head;
            float halfW = Mathf.Max(1f, head.width * 0.5f);
            float halfH = Mathf.Max(1f, head.height * 0.5f);

            float leftX;
            float rightX;
            float eyeY;
            if (!TryEyeScreen(camera, head, out leftX, out rightX, out eyeY))
            {
                leftX = head.center.x - halfW * 0.42f;
                rightX = head.center.x + halfW * 0.42f;
                eyeY = head.center.y + halfH * 0.28f;
            }

            zones.LeftEye = ZoneRect(leftX, eyeY, halfW * 0.25f, halfH * 0.22f);
            zones.RightEye = ZoneRect(rightX, eyeY, halfW * 0.25f, halfH * 0.22f);
            zones.LeftCheek = ZoneRect(leftX - halfW * 0.04f, eyeY - halfH * 0.38f,
                halfW * 0.36f, halfH * 0.34f);
            zones.RightCheek = ZoneRect(rightX + halfW * 0.04f, eyeY - halfH * 0.38f,
                halfW * 0.36f, halfH * 0.34f);
            Rect ear;
            if (TryBonesRect(camera, _rig.LeftEar, out ear))
            {
                zones.LeftEar = ScaleRect(ear, _earZoneScale);
                zones.HasLeftEar = true;
            }

            if (TryBonesRect(camera, _rig.RightEar, out ear))
            {
                zones.RightEar = ScaleRect(ear, _earZoneScale);
                zones.HasRightEar = true;
            }

            zones.Valid = true;
            return true;
        }

        Zone HitZone(Vector2 screen)
        {
            FaceZones zones;
            if (!TryBuildZones(out zones))
            {
                return Zone.None;
            }

            // 耳朵优先于脸：先判耳朵，再判眼睛/脸颊/整张脸（作者要求"先拽耳朵再扇脸"）。
            // 耳朵的包围盒有三分之一压在头顶上（taisho 实测 33%），所以那两块脸上的位置抓不到扇脸；
            // 嫌耳朵区太大的话调 _earZoneScale（它缩放的就是这两块椭圆）
            if (zones.HasLeftEar && InEllipse(screen, zones.LeftEar))
            {
                return Zone.LeftEar;
            }

            if (zones.HasRightEar && InEllipse(screen, zones.RightEar))
            {
                return Zone.RightEar;
            }

            if (InEllipse(screen, zones.LeftEye))
            {
                return Zone.LeftEye;
            }

            if (InEllipse(screen, zones.RightEye))
            {
                return Zone.RightEye;
            }

            if (InEllipse(screen, zones.LeftCheek))
            {
                return Zone.LeftCheek;
            }

            if (InEllipse(screen, zones.RightCheek))
            {
                return Zone.RightCheek;
            }

            return InEllipse(screen, Dilate(zones.Head, 1.1f)) ? Zone.Face : Zone.None;
        }

        bool TryEyeScreen(Camera camera, Rect head, out float leftX, out float rightX, out float eyeY)
        {
            leftX = head.center.x - head.width * 0.21f;
            rightX = head.center.x + head.width * 0.21f;
            eyeY = head.center.y + head.height * 0.14f;
            if (_rig.LeftEye == null || _rig.RightEye == null)
            {
                return false;
            }

            Vector3 left = camera.WorldToScreenPoint(_rig.LeftEye.position);
            Vector3 right = camera.WorldToScreenPoint(_rig.RightEye.position);
            if (left.z <= 0f || right.z <= 0f)
            {
                return false;
            }

            // 眼睛骨骼位置离谱（有的模型会把特殊点丢在别处）就退回按头的比例算
            if (Mathf.Abs(left.x - head.center.x) > head.width * 0.48f
                || Mathf.Abs(right.x - head.center.x) > head.width * 0.48f
                || Mathf.Abs(left.y - head.center.y) > head.height * 0.48f
                || Mathf.Abs(right.y - head.center.y) > head.height * 0.48f)
            {
                return false;
            }

            leftX = Mathf.Min(left.x, right.x);
            rightX = Mathf.Max(left.x, right.x);
            eyeY = (left.y + right.y) * 0.5f;
            return true;
        }

        static Rect ZoneRect(float centerX, float centerY, float halfX, float halfY)
        {
            return new Rect(centerX - halfX, centerY - halfY, halfX * 2f, halfY * 2f);
        }

        static Rect Dilate(Rect rect, float scale)
        {
            float halfW = rect.width * 0.5f * (scale - 1f);
            float halfH = rect.height * 0.5f * (scale - 1f);
            return new Rect(rect.xMin - halfW, rect.yMin - halfH, rect.width + halfW * 2f,
                rect.height + halfH * 2f);
        }

        static Rect ScaleRect(Rect rect, float scale)
        {
            scale = Mathf.Clamp(scale, 0.1f, 2f);
            return new Rect(rect.center.x - rect.width * 0.5f * scale,
                rect.center.y - rect.height * 0.5f * scale,
                rect.width * scale, rect.height * scale);
        }

        bool TryHeadRect(Camera camera, out Rect rect)
        {
            rect = new Rect();
            if (_rig == null || _rig.Head == null)
            {
                return false;
            }

            if (_rig.HeadMesh != null && TryProject(_rig.HeadMesh.bounds, camera, out rect)
                && rect.width > 4f && rect.height > 4f)
            {
                return true;
            }

            Vector3 screen = camera.WorldToScreenPoint(_rig.Head.position);
            if (screen.z <= 0f)
            {
                return false;
            }

            float size = Screen.height * 0.16f;
            rect = new Rect(screen.x - size * 0.5f, screen.y - size * 0.5f, size, size);
            return true;
        }

        bool TryBonesRect(Camera camera, List<Transform> bones, out Rect rect)
        {
            rect = new Rect();
            if (bones == null)
            {
                return false;
            }

            bool any = false;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < bones.Count; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                {
                    continue;
                }

                Renderer renderer = bone.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                Rect part;
                if (!TryProject(renderer.bounds, camera, out part))
                {
                    continue;
                }

                minX = Mathf.Min(minX, part.xMin);
                minY = Mathf.Min(minY, part.yMin);
                maxX = Mathf.Max(maxX, part.xMax);
                maxY = Mathf.Max(maxY, part.yMax);
                any = true;
            }

            if (!any)
            {
                return false;
            }

            rect = new Rect(minX - _hitPaddingPixels, minY - _hitPaddingPixels,
                maxX - minX + _hitPaddingPixels * 2f, maxY - minY + _hitPaddingPixels * 2f);
            return true;
        }

        static bool TryProject(Bounds bounds, Camera camera, out Rect rect)
        {
            rect = new Rect();
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                Vector3 screen = camera.WorldToScreenPoint(corner);
                if (screen.z <= 0f)
                {
                    return false;
                }

                minX = Mathf.Min(minX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxX = Mathf.Max(maxX, screen.x);
                maxY = Mathf.Max(maxY, screen.y);
            }

            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
            return true;
        }

        static bool InEllipse(Vector2 point, Rect rect)
        {
            float dx = (point.x - rect.center.x) / Mathf.Max(1f, rect.width * 0.5f);
            float dy = (point.y - rect.center.y) / Mathf.Max(1f, rect.height * 0.5f);
            return dx * dx + dy * dy <= 1f;
        }

        static float SmoothRange(float value, float start, float end)
        {
            if (Mathf.Approximately(end, start))
            {
                return value >= end ? 1f : 0f;
            }

            float t = Mathf.Clamp01((value - start) / (end - start));
            return t * t * (3f - 2f * t);
        }

        static string Pick(string[] lines)
        {
            return lines[UnityEngine.Random.Range(0, lines.Length)];
        }

        // ---------- 受伤动画 ----------

        /// <summary>
        /// 借女仆自己的「被打」动画演一下（TLM 里这条叫 attacked），演完换回原来那条。
        /// 正在演的时候不重开，不然连着扇耳光会一直卡在第一帧。
        /// </summary>
        void PlayHurtAnimation()
        {
            if (_hurtPlaying || _agent == null || string.IsNullOrEmpty(_hurtClip))
            {
                return;
            }

            BedrockAnimationPlayer player = _agent.GetComponent<BedrockAnimationPlayer>();
            if (player == null || !player.HasClip(_hurtClip))
            {
                return;
            }

            _hurtReturnClip = player.PlayingClipName;
            _hurtReturnTime = player.PlayingTime;
            if (!player.Play(_hurtClip))
            {
                _hurtReturnClip = null;
                return;
            }

            _hurtPlaying = true;
            _hurtRoutine = StartCoroutine(RestoreHurt(player));
        }

        IEnumerator RestoreHurt(BedrockAnimationPlayer player)
        {
            float seconds = Mathf.Clamp(player.ClipLength(_hurtClip), 0.2f, Mathf.Max(0.2f, _hurtMaxSeconds));
            yield return new WaitForSeconds(seconds);
            _hurtRoutine = null;
            _hurtPlaying = false;
            ReturnToHurtClip(player);
        }

        void StopHurtAnimation()
        {
            if (_hurtRoutine != null)
            {
                StopCoroutine(_hurtRoutine);
                _hurtRoutine = null;
            }

            if (_hurtPlaying)
            {
                _hurtPlaying = false;
                ReturnToHurtClip(_agent != null ? _agent.GetComponent<BedrockAnimationPlayer>() : null);
            }

            _hurtReturnClip = null;
            _hurtReturnTime = 0f;
        }

        void ReturnToHurtClip(BedrockAnimationPlayer player)
        {
            // 退出摸脸模式时也得换回来，不然她会一直停在被打的姿势
            if (player != null && !string.IsNullOrEmpty(_hurtReturnClip) && player.HasClip(_hurtReturnClip))
            {
                player.Play(_hurtReturnClip, _hurtReturnTime);
            }
            else
            {
                MaidWanderer wanderer = _agent != null ? _agent.Wanderer : null;
                if (wanderer != null)
                {
                    // 不知道原来在播什么，就让状态机重播一次 idle / walk
                    wanderer.InvalidateAnimation();
                }
            }

            _hurtReturnClip = null;
            _hurtReturnTime = 0f;
        }

        // ---------- F8 触发范围 ----------

        void OnGUI()
        {
            if (!IsActive)
            {
                return;
            }

            // 连击彩虹字 + 100 连抽彩蛋，和模组一样一直画在屏幕上
            _comboHud.Draw();
            if (!_showZones)
            {
                return;
            }

            FaceZones zones;
            if (!TryBuildZones(out zones))
            {
                return;
            }

            // 触发区算出来是屏幕坐标（左下角原点），GUI 是左上角原点，画之前要翻 y。
            // 蓝的按 HitZone 那个椭圆画（头框放大 1.1），不然显示的范围比实际判定小一圈
            DrawEllipse(Dilate(zones.Head, 1.1f), new Color(0.30f, 0.60f, 1f, 0.18f));
            DrawEllipse(zones.LeftEye, new Color(1f, 0.25f, 0.25f, 0.42f));
            DrawEllipse(zones.RightEye, new Color(1f, 0.25f, 0.25f, 0.42f));
            DrawEllipse(zones.LeftCheek, new Color(1f, 0.85f, 0.2f, 0.34f));
            DrawEllipse(zones.RightCheek, new Color(1f, 0.85f, 0.2f, 0.34f));
            if (zones.HasLeftEar)
            {
                DrawEllipse(zones.LeftEar, new Color(0.2f, 1f, 0.9f, 0.34f));
            }

            if (zones.HasRightEar)
            {
                DrawEllipse(zones.RightEar, new Color(0.2f, 1f, 0.9f, 0.34f));
            }

            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 16f, 900f, 30f),
                "触发范围（F8 隐藏）：红=眼睛  黄=脸颊  青=耳朵（比脸优先）  蓝=脸；起手在蓝区/红/黄=扇脸或戳脸，别处=拖脸");
        }

        static void DrawEllipse(Rect rect, Color color)
        {
            EnsureTextures();
            GUI.color = color;
            GUI.DrawTexture(ToGui(rect), _ellipse);
        }

        static Rect ToGui(Rect screenRect)
        {
            return new Rect(screenRect.x, Screen.height - screenRect.yMax, screenRect.width,
                screenRect.height);
        }

        static void EnsureTextures()
        {
            if (_ellipse != null)
            {
                return;
            }

            const int size = 64;
            _ellipse = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - radius) / radius;
                    float dy = (y + 0.5f - radius) / radius;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 边缘 1px 淡出，看着不刺眼
                    float alpha = Mathf.Clamp01((1f - d) * radius);
                    _ellipse.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            _ellipse.Apply();
        }

        // ---------- 面板 / 转身 ----------

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidFacePanel>();
            }

            // 用户自己摆的那份通常挂在底栏物体上、不在这个物体上，所以再在场景里找一遍
            if (_panel == null)
            {
                _panel = FindScenePanel();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidFacePanel>();
            }

            _panel.ExitRequested -= End;
            _panel.ExitRequested += End;
        }

        /// <summary>
        /// 场景里手摆的 MaidFacePanel：挂在别的物体上、并且自己接了 bar。
        /// 只认唯一一份，有多份就返回 null 让用户自己在 _panel 里指定，免得挑错。
        /// </summary>
        MaidFacePanel FindScenePanel()
        {
            MaidFacePanel[] all = FindObjectsOfType<MaidFacePanel>(true);
            MaidFacePanel found = null;
            for (int i = 0; i < all.Length; i++)
            {
                MaidFacePanel panel = all[i];
                if (panel == null || panel.gameObject == gameObject || !panel.HasOwnBar)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = panel;
            }

            if (found != null)
            {
                Debug.Log("[摸脸] 用场景里现成的底栏：" + found.name);
            }

            return found;
        }

        /// <summary>相机不动，靠她自己转过来面对镜头。</summary>
        void FaceCamera()
        {
            Camera camera = Camera.main;
            MaidWanderer wanderer = _agent != null ? _agent.Wanderer : null;
            if (camera == null || wanderer == null || !_agent.gameObject.activeInHierarchy)
            {
                return;
            }

            wanderer.FaceDirection(camera.transform.position - _agent.transform.position, _turnSeconds);
        }

        public void switchShowZones()
        {
            _showZones = !_showZones;
        }
    }
}
