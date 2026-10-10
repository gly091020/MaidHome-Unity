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
    /// 梳毛模式，机制照抄 moreanimation 的 GroomingClient / GroomingSession / BrushGrain：
    /// 沿**头发 / 耳朵 / 尾巴**的表面刷动 → 那一小块毛跟着让；
    /// 顺着毛流方向刷 = 舒服（每 3 秒一句气泡），逆着刷 = 疼（闪红 + 受伤语音 + 生气气泡）；
    /// 累计刷够 400 tick（20 秒有效接触）她会满足地夸一句；
    /// 切到「手」就是原版鼠标模式：按住尾巴拖、按钮固定尾巴。
    ///
    /// 和模组不同的地方（Unity 这边没有坐垫和第一人称）：
    /// ① 输入是触摸，射线从手指经相机打出去，只在进入模式时让她转过来面对镜头；
    /// ② 不冻结姿态，反应直接叠在当帧动画上（方块模型也吃得下）；
    /// ③ 相机由 MaidInteractionController 临时切成透视（模组是第一人称）。
    /// </summary>
    [DisallowMultipleComponent]
    // 动画在 LateUpdate 写骨骼，反应必须排在后面，不然刚写上的弯曲会被动画吃掉
    [DefaultExecutionOrder(200)]
    public sealed class MaidGroomingInteraction : MonoBehaviour
    {
        public enum Tool
        {
            Brush,
            Hand
        }

        public event Action Ended;

        /// <summary>照抄模组 GroomingBrushState.FAVOR_TICKS：有效接触 400 tick = 20 秒</summary>
        const float FavorTicks = 400f;
        /// <summary>模组 CONTACT_GRACE = 2 tick，接触断开一会儿就暂停累计，但不丢余额</summary>
        const float ContactGraceSeconds = 0.1f;
        /// <summary>模组的服务端 tick 是 20Hz</summary>
        const float TickRate = 20f;

        [Header("拾取")]
        [Tooltip("耳朵的补拾取放大倍率（模组是 1.3；手指没有鼠标准，默认再宽一点）")]
        [SerializeField] private float _earHitScale = 1.4f;
        [Tooltip("一次刷动超过这个长度就不算（配合投影跳变丢样本）")]
        [SerializeField] private float _maxDragWorld = 0.35f;

        [Header("顺毛 / 逆毛")]
        [Tooltip("头发也判顺逆毛（模组只在尾巴上判）。关掉就是只有尾巴有反应")]
        [SerializeField] private bool _grainOnHair = true;
        [Tooltip("顺毛气泡的间隔，秒（模组 COMFORT_COOLDOWN = 60 tick）")]
        [SerializeField] private float _comfortCooldown = 3f;
        [Tooltip("逆毛反应（闪红 + 语音 + 气泡）的间隔，秒（模组 REVERSE_COOLDOWN = 12 tick）")]
        [SerializeField] private float _againstCooldown = 0.6f;
        [SerializeField] private float _lineSeconds = 1.6f;

        [Header("摸耳朵")]
        [Tooltip("手指刚按到耳朵上就先压一点（等拖动才压会觉得没反应）")]
        [SerializeField] private float _earPressOnTouch = 0.35f;

        [Header("躺姿（照模组：梳毛时她是躺着的）")]
        [Tooltip("进模式就让她躺下。原模组那边是坐垫上的睡姿（moresleep5）再整体放倒 90°")]
        [SerializeField] private bool _lieDown = true;
        [Tooltip("躺下时根节点绕自身 X 轴转多少度。+90 = 趴着（背、头发和尾巴都朝上，尾巴才梳得到）；-90 = 仰面躺着（工程里睡觉那套，但尾巴会压在身下够不着）")]
        [SerializeField] private float _lieTiltDegrees = 90f;
        [Tooltip("贴地高度是按几何自动算的（放倒后最低点落到地面），这里是额外微调，单位是模型空间的米（会乘缩放）")]
        [SerializeField] private float _lieLift = 0f;
        [Tooltip("放倒后离地留多高，免得和地板完全贴住闪面")]
        [SerializeField] private float _lieGroundClearance = 0.02f;
        [Tooltip("有这条动画的模型直接播它拿躺姿；没有的（方块模型）走 MaidSimpleBedrockAnimator 的程序化睡姿 + 闭眼")]
        [SerializeField] private string _sleepClip = "sleep";
        [Tooltip("放倒前最多等这么久（秒）：她要是在转向镜头就等转完，已经站定了就立刻躺下")]
        [SerializeField] private float _lieDelaySeconds = 0.45f;
        [Tooltip("退出梳毛时她转回来看镜头要多久")]
        [SerializeField] private float _turnBackSeconds = 0.35f;
        [Tooltip("取景要不要把尾巴也框进来：躺下后尾巴摊在一边，不框它的话尾巴在画面外就梳不到；嫌画面太远就关掉")]
        [SerializeField] private bool _focusTailToo;

        [Header("拖尾巴")]
        [SerializeField] private float _tailSensitivity = 1.85f;
        [Tooltip("拖动多少像素相当于 45°，越大越迟钝")]
        [SerializeField] private float _tailDragPixels = 400f;

        [Header("闪红 / 音量")]
        [SerializeField] private Color _flashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _flashSeconds = 0.5f;
        [Tooltip("女仆受伤语音的放大倍率")]
        [SerializeField] private float _voiceVolume = 6f;

        [Header("满足")]
        [Tooltip("累计梳够这么久（秒）她会满足地夸一句。模组是 400 tick")]
        [SerializeField] private float _cheerSeconds = 20f;
        [Tooltip("满足时放的这个音（女仆声音包里的 id）")]
        [SerializeField] private string _cheerSoundId = MaidSoundId.Tamed;

        [Header("调试")]
        [Tooltip("把当前刷到的部位 / 顺逆毛状态画在屏幕上，运行时按 F8 也能开关")]
        [SerializeField] private bool _showDebug;

        // 台词照抄 moreanimation 的 zh_cn
        static readonly string[] WithGrainLines =
        {
            "嗯……这样梳好舒服。",
            "最喜欢主人帮我梳了。",
            "主人梳得好温柔呀……",
            "毛都变得顺顺的了，嘿嘿。",
            "再多梳一会儿嘛，主人。",
            "这样顺着梳，感觉好舒服……"
        };

        static readonly string[] AgainstGrainLines =
        {
            "呀！不能反着梳，会很疼的！",
            "主人，这样梳不舒服……",
            "呜……毛都被梳乱了。",
            "轻一点，毛都要打结了……",
            "方向反啦，主人……",
            "主人不要逆着梳呀，好难受……"
        };

        static readonly string[] CheerLines =
        {
            "嘿嘿，毛都梳顺了，主人真厉害。",
            "被主人梳得这么舒服，今天心情特别好！"
        };

        [Tooltip("底栏，留空就用同一物体上的 / 自动生成一条")]
        [SerializeField] private MaidGroomingPanel _panel;
        [Tooltip("手上的刷子模型，留空就用同一物体上的 MaidGroomingBrush")]
        [SerializeField] private MaidGroomingBrush _brush;

        public bool IsActive { get; private set; }
        public Tool CurrentTool { get; private set; }

        /// <summary>现在是不是躺着（相机取景要按这个换俯角）</summary>
        public bool IsLying { get { return _lying; } }

        /// <summary>进模式会不会躺下：会的话外层先别取景，等她躺好了再一次性推近（免得镜头先近后远地跳两次）</summary>
        public bool WillLieDown { get { return _lieDown; } }

        /// <summary>一根手指正在刷的一次笔画（同时只认一根，模组也是一次一笔）</summary>
        sealed class Stroke
        {
            public int FingerId;
            /// <summary>这一笔允许刷的范围：头发/头皮/耳朵算"头上"，尾巴单独一类</summary>
            public string Class;
            public Vector2 PreviousScreen;
            public bool HasPrevious;
            public bool Contact;
            public float ContactAt;
            public bool HasProgress;
            public float Progress;
            public bool GrainEligible;
        }

        /// <summary>一根手指拖一条尾巴（多指各拖各的）</summary>
        sealed class TailGrab
        {
            public int FingerId;
            public MaidTailChain Chain;
            public Vector2 Origin;
            public float StartYaw;
            public float StartPitch;
            public float TargetYaw;
            public float TargetPitch;
            /// <summary>正的目标角度会让尾尖往屏幕哪边走：抓的时候量一次，姿态变了也不会反</summary>
            public float YawSign = 1f;
            public float PitchSign = 1f;
        }

        readonly List<PointerInput.Pointer> _pointers = new List<PointerInput.Pointer>();
        readonly List<TailGrab> _grabs = new List<TailGrab>();
        readonly List<MaidTailChain> _chains = new List<MaidTailChain>();
        readonly HashSet<string> _frozen = new HashSet<string>();
        readonly MaidBrushGrain _grain = new MaidBrushGrain();

        MaidAgent _agent;
        MaidGroomingRig _rig;
        BedrockAnimationPlayer _player;
        bool _parallelWas = true;
        Stroke _stroke;
        float _simAccumulator;
        float _brushTicks;
        float _lastComfortAt = -100f;
        float _lastAgainstAt = -100f;
        float _lastCheerAt = -100f;
        string _hint = "";
        string _lastPart = "";
        bool _lying;
        float _lieYaw;
        Coroutine _lieRoutine;
        Vector3 _standingPosition;
        bool _hasStandingPosition;
        bool _refocus;
        MaidSimpleBedrockAnimator _simple;
        MaidHurtBlink _blink;
        MaidWanderer _wanderer;
        CharacterController _controller;

        /// <summary>这个女仆能不能梳毛（认得出头发 / 耳朵 / 头顶 / 尾巴之一就行）</summary>
        public bool Supports(MaidAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            MaidGroomingRig rig = MaidGroomingRig.Build(agent, MaidFaceRig.Build(agent.transform),
                VisibleTails(agent));
            return rig.Supports;
        }

        public bool Begin(MaidAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            Abort();

            _rig = MaidGroomingRig.Build(agent, MaidFaceRig.Build(agent.transform), VisibleTails(agent));
            if (!_rig.Supports)
            {
                Debug.LogWarning("这个模型的骨架里认不出头发 / 耳朵 / 尾巴，梳不了毛: " + agent.name, this);
                _rig = null;
                return false;
            }

            _agent = agent;
            _chains.Clear();
            _chains.AddRange(_rig.Tails);
            CurrentTool = Tool.Brush;
            _brushTicks = 0f;
            _stroke = null;
            _grabs.Clear();
            _frozen.Clear();
            _simAccumulator = 0f;
            _lastPart = "";
            _rig.RefreshSurfaces();

            // 常驻摆动（pre_parallel）也写头发和尾巴，不关掉的话刷出来的弯会被它每帧盖回去
            _player = agent.GetComponent<BedrockAnimationPlayer>();
            if (_player != null)
            {
                _parallelWas = _player.ParallelEnabled;
                _player.ParallelEnabled = false;
            }

            EnsurePanel();
            EnsureBrush();
            if (_panel != null)
            {
                _panel.SetHasTail(_chains.Count > 0);
                _panel.SetTool(CurrentTool);
                _panel.SetHint(ToolHint());
                _panel.SetProgress(0f);
                _panel.Show();
            }

            if (_brush != null)
            {
                _brush.Hide();
            }

            _lying = false;
            _refocus = false;
            _hasStandingPosition = false;
            if (_panel != null)
            {
                _panel.SetFlipEnabled(false);
            }

            IsActive = true;
            if (_lieDown)
            {
                _lieRoutine = StartCoroutine(LieDownRoutine());
            }

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
            if (_lieRoutine != null)
            {
                StopCoroutine(_lieRoutine);
                _lieRoutine = null;
            }

            StandUp();
            ReleaseGrabs();
            _chains.Clear();
            _frozen.Clear();
            if (_rig != null)
            {
                _rig.Clear();
            }

            if (_player != null)
            {
                _player.ParallelEnabled = _parallelWas;
            }

            _player = null;
            _rig = null;
            _stroke = null;
            _grain.Reset();
            _brushTicks = 0f;
            _simAccumulator = 0f;
            _hint = "";
            _lastPart = "";
            _refocus = false;

            if (_brush != null)
            {
                _brush.Hide();
            }

            if (_panel != null)
            {
                _panel.Hide();
            }

            _agent = null;
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
                _showDebug = !_showDebug;
            }

            if (_rig != null)
            {
                _rig.RefreshSurfaces();
            }

            PointerInput.CopyPointers(_pointers);
            if (CurrentTool == Tool.Hand)
            {
                UpdateGrabs();
                TryStartGrabs();
                return;
            }

            for (int i = 0; i < _pointers.Count; i++)
            {
                PointerInput.Pointer pointer = _pointers[i];
                if (pointer.Pressed && !pointer.OverUi && _stroke == null)
                {
                    StartStroke(pointer);
                }
                else if (pointer.Held && _stroke != null && _stroke.FingerId == pointer.FingerId)
                {
                    ContinueStroke(pointer);
                }

                if (pointer.Released && _stroke != null && _stroke.FingerId == pointer.FingerId)
                {
                    EndStroke();
                }
            }

            // 有效接触累计（模组是每个服务端 tick 加一次，断开一会儿就暂停、不丢余额）
            if (_stroke != null && _stroke.Contact
                && Time.unscaledTime - _stroke.ContactAt <= ContactGraceSeconds)
            {
                _brushTicks += Time.unscaledDeltaTime * TickRate;
                if (_brushTicks >= FavorTicks)
                {
                    _brushTicks -= FavorTicks;
                    Cheer();
                }

                if (_panel != null)
                {
                    _panel.SetProgress(_brushTicks / FavorTicks);
                }
            }
        }

        void LateUpdate()
        {
            if (!IsActive || _rig == null)
            {
                return;
            }

            // 弹簧都是 20Hz（模组的客户端 tick），补一个固定步长再插值
            _simAccumulator += Time.unscaledDeltaTime;
            int guard = 0;
            while (_simAccumulator >= MaidTailChain.StepSeconds && guard++ < 8)
            {
                _simAccumulator -= MaidTailChain.StepSeconds;
                for (int i = 0; i < _chains.Count; i++)
                {
                    _chains[i].Step();
                }
            }

            _rig.Step(Time.unscaledDeltaTime);
            float alpha = Mathf.Clamp01(_simAccumulator / MaidTailChain.StepSeconds);
            for (int i = 0; i < _chains.Count; i++)
            {
                _chains[i].Apply(1f, 1f, 0.08f, alpha);
            }

            _rig.Apply(alpha);
            _rig.RefreshSurfaces();

            // 躺着的时候每帧把朝向+放倒压上去：转场协程、状态机都别想把她掀起来
            if (_lying && _agent != null)
            {
                ApplyLieRotation();
            }
        }

        // ---------- 躺下 / 转向 ----------

        IEnumerator LieDownRoutine()
        {
            // 放倒那一刻要把朝向记下来，转到一半就记歪了。所以等她"不再转"再放：
            // 通常进模式时她已经面向镜头了 → 一两帧就躺下；没转完最多等 _lieDelaySeconds
            float deadline = Time.unscaledTime + Mathf.Max(0f, _lieDelaySeconds);
            Quaternion last = _agent != null ? _agent.transform.rotation : Quaternion.identity;
            int stable = 0;
            while (Time.unscaledTime < deadline)
            {
                yield return null;
                if (_agent == null)
                {
                    _lieRoutine = null;
                    yield break;
                }

                Quaternion now = _agent.transform.rotation;
                if (Quaternion.Angle(now, last) < 0.2f)
                {
                    if (++stable >= 2)
                    {
                        break;
                    }
                }
                else
                {
                    stable = 0;
                }

                last = now;
            }

            _lieRoutine = null;
            if (!IsActive || _agent == null)
            {
                yield break;
            }

            LieDown();
        }

        /// <summary>
        /// 躺下：先摆骨架睡姿（有 sleep 就播，方块模型走程序化 + 闭眼），再把整根放倒。
        /// 放倒这一步必须在动画之外自己做——原版让睡觉实体躺下的是渲染器对整个实体的旋转，
        /// 动画自己只负责四肢（工程里 AR 睡觉那条也是这么处理的）。
        /// </summary>
        void LieDown()
        {
            _lying = true;
            _lieYaw = _agent.transform.eulerAngles.y;
            _standingPosition = _agent.transform.position;
            _hasStandingPosition = true;

            // 躺着的时候她是"摆设"：状态机关掉，CharacterController 也关掉。
            // 不关的话 MaidWanderer 每帧那句 SimpleMove(Vector3.zero) 会拿躺平（其实是横过来）的胶囊
            // 去解算和地板的穿插，把她顶得上下飘、位置也就回不来了（AR 那边放倒女仆也是这么关的）
            _wanderer = _agent.Wanderer;
            if (_wanderer != null)
            {
                _wanderer.enabled = false;
            }

            _controller = _agent.GetComponent<CharacterController>();
            if (_controller != null)
            {
                _controller.enabled = false;
            }

            _simple = _agent.GetComponent<MaidSimpleBedrockAnimator>();
            _blink = _agent.GetComponent<MaidHurtBlink>();
            if (_player != null && _player.HasClip(_sleepClip))
            {
                _player.Play(_sleepClip);
            }
            else
            {
                if (_simple != null)
                {
                    _simple.SetSleeping(true);
                }

                // 没有 sleep 的那批：别让 idle 的姿势和放倒打架，把主动画停掉
                if (_player != null)
                {
                    _player.Stop();
                }
            }

            if (_blink != null)
            {
                _blink.SetClosed(true);
            }

            ApplyLiePose();
            _refocus = true;
            if (_panel != null)
            {
                _panel.SetFlipEnabled(true);
            }

            SetHint("她已经躺下了，顺着毛慢慢梳吧");
        }

        /// <summary>站起来：先回姿势和朝向，再让状态机把 idle 播回来</summary>
        void StandUp()
        {
            if (!_lying)
            {
                return;
            }

            _lying = false;
            if (_simple != null)
            {
                _simple.SetSleeping(false);
            }

            if (_blink != null)
            {
                _blink.SetClosed(false);
            }

            _simple = null;
            _blink = null;
            // 先回位置和朝向，再把控制器和状态机放回来：顺序反了她会被解算顶走
            if (_agent != null && _hasStandingPosition)
            {
                _agent.transform.position = _standingPosition;
                _agent.transform.rotation = Quaternion.Euler(0f, _lieYaw, 0f);
            }

            if (_controller != null)
            {
                _controller.enabled = true;
            }

            if (_wanderer != null)
            {
                _wanderer.enabled = true;
            }

            _controller = null;
            _wanderer = null;
            if (_agent != null && _hasStandingPosition)
            {
                // 状态机被暂停时不会自己把 idle 播回来，借完睡姿得让它重播一次
                MaidWanderer wanderer = _agent.Wanderer;
                if (wanderer != null)
                {
                    wanderer.ReplayAnimation();
                    // 站起来以后转回来看镜头（她转向过的话，这里就是那次 180° 的回程）
                    Camera camera = Camera.main;
                    if (camera != null)
                    {
                        Vector3 toCamera = camera.transform.position - _agent.transform.position;
                        wanderer.FaceDirection(toCamera, _turnBackSeconds);
                    }
                }
            }

            _hasStandingPosition = false;
            if (_panel != null)
            {
                _panel.SetFlipEnabled(false);
            }
        }


        /// <summary>
        /// 放倒 + 按几何把最低点落到地面。高度不能写死：模型厚度差得远，
        /// 写死一个值总有几个会陷进地板或者浮在空中
        /// </summary>
        void ApplyLiePose()
        {
            if (_agent == null)
            {
                return;
            }

            ApplyLieRotation();
            if (!_hasStandingPosition)
            {
                return;
            }

            float lowest = LowestVisibleY();
            float scale = Mathf.Max(0.01f, _agent.transform.lossyScale.y);
            float target = _standingPosition.y + _lieGroundClearance + _lieLift * scale;
            float lift = Mathf.Max(0f, target - lowest);
            _agent.transform.position = new Vector3(_standingPosition.x, _standingPosition.y + lift,
                _standingPosition.z);
        }

        /// <summary>
        /// 贴地高度看的是最低的那块**显示着的**几何。
        /// 不能用 MaidAgent.GetBounds：它连被藏起来的节点（FOX 那只小狐狸）也算进去，最低点会跑到别处
        /// </summary>
        float LowestVisibleY()
        {
            Renderer[] renderers = _agent.GetComponentsInChildren<Renderer>(false);
            bool has = false;
            float lowest = float.MaxValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                lowest = Mathf.Min(lowest, renderer.bounds.min.y);
                has = true;
            }

            return has ? lowest : _standingPosition.y;
        }

        void ApplyLieRotation()
        {
            if (_agent == null)
            {
                return;
            }

            _agent.transform.rotation = Quaternion.Euler(0f, _lieYaw, 0f)
                * Quaternion.Euler(_lieTiltDegrees, 0f, 0f);
        }

        /// <summary>模组的「转向」：躺着的时候掉个头（那边是挪到主人另一侧，我们原地转 180°）</summary>
        void Flip()
        {
            if (!IsActive || !_lying)
            {
                return;
            }

            // 不做空间检查了（地板/薄地台老是误判）；贴墙转可能插一点，作者要求就这样
            _lieYaw += 180f;
            ApplyLiePose();
            _refocus = true;
            SetHint(ToolHint());
        }

        /// <summary>躺下 / 转向之后头的位置变了，让外层重新取一次景（取景那套算完就定死，不会自己跟）</summary>
        public bool ConsumeRefocusRequest()
        {
            if (!_refocus)
            {
                return false;
            }

            _refocus = false;
            return true;
        }

        // ---------- 刷毛 ----------

        void StartStroke(PointerInput.Pointer pointer)
        {
            Camera camera = Camera.main;
            MaidGroomHit hit = _rig.Pick(pointer.Position, camera, _earHitScale);
            if (hit == null)
            {
                return;
            }

            Stroke stroke = new Stroke();
            stroke.FingerId = pointer.FingerId;
            stroke.Class = ClassOf(hit.Part);
            stroke.PreviousScreen = pointer.Position;
            stroke.HasPrevious = true;
            stroke.Contact = true;
            stroke.ContactAt = Time.unscaledTime;
            // 头发和尾巴才判顺逆毛；头发那一条在方块模型上是"挂在发根底下、名字不叫 hair"的骨骼，
            // 所以这里问的是这块表面自己（模型里每束毛的中轴），不是部位
            stroke.GrainEligible = hit.Surface.GrainEligible
                && (hit.Part.Kind != MaidGroomKind.Hair || _grainOnHair);
            stroke.HasProgress = stroke.GrainEligible;
            stroke.Progress = _rig.Progress(hit);
            if (stroke.Progress < 0f)
            {
                stroke.HasProgress = false;
            }

            // 一次一笔：新的一笔要把上一笔的顺逆毛累计清掉（模组 resetStroke 的 GRAIN.reset）
            _grain.Reset();
            _stroke = stroke;
            _lastPart = hit.Part.Id;

            // 耳朵按下去就要有反应，不然要拖动才压会显得没反应
            if (hit.Part.Kind == MaidGroomKind.Ear && _earPressOnTouch > 0.001f)
            {
                _rig.PressEar(hit.Part, _earPressOnTouch);
            }

            ShowBrush(hit);
        }

        void ContinueStroke(PointerInput.Pointer pointer)
        {
            Stroke stroke = _stroke;
            Camera camera = Camera.main;
            MaidGroomHit hit = _rig.Pick(pointer.Position, camera, _earHitScale);
            if (stroke == null)
            {
                return;
            }

            // 一笔只在"同类"里连着刷：头发每一节、尾巴每一节都是独立部位，
            // 按部位锁的话手指从上一节滑到下一节就断笔（踩过：头发上段刷不到下段、尾巴前端刷不到后端）。
            // 换到别的类（例如从尾巴滑到头发）才断笔，抬起来重按算下一笔。
            if (hit == null || ClassOf(hit.Part) != stroke.Class)
            {
                stroke.Contact = false;
                stroke.HasPrevious = false;
                stroke.HasProgress = false;
                if (_brush != null)
                {
                    _brush.Hide();
                }

                return;
            }

            Vector3 previous = ProjectOnPlane(camera, stroke.PreviousScreen, hit.Point, hit.Normal);
            Vector3 delta = hit.Point - previous;
            float length = delta.magnitude;
            bool hadPrevious = stroke.HasPrevious;
            stroke.PreviousScreen = pointer.Position;
            stroke.HasPrevious = true;
            stroke.Contact = true;
            stroke.ContactAt = Time.unscaledTime;
            _lastPart = hit.Part.Id;

            // 中间断过接触（手指滑出毛面又滑回来）就先不比位移，免得一次算出很大的跳变
            if (hadPrevious && length > 0.0008f && length < _maxDragWorld)
            {
                _rig.BrushTailNeighbours(hit.Part, hit, delta);
            }

            if (stroke.GrainEligible)
            {
                float progress = hadPrevious ? _rig.Progress(hit) : -1f;
                if (progress >= 0f && stroke.HasProgress)
                {
                    MaidBrushGrain.Direction direction = _grain.Sample(progress - stroke.Progress);
                    if (direction == MaidBrushGrain.Direction.WithGrain)
                    {
                        Comfort();
                    }
                    else if (direction == MaidBrushGrain.Direction.AgainstGrain)
                    {
                        Against();
                    }
                }
                else
                {
                    _grain.Reset();
                }

                stroke.HasProgress = progress >= 0f;
                stroke.Progress = progress;
            }

            ShowBrush(hit);
        }

        void EndStroke()
        {
            _stroke = null;
            _grain.Reset();
            if (_brush != null)
            {
                _brush.Hide();
            }
        }

        /// <summary>一笔能连着刷的范围：头上（头发 / 头皮 / 耳朵）是一类，尾巴单独一类</summary>
        static string ClassOf(MaidGroomPart part)
        {
            return part != null && part.Kind == MaidGroomKind.Tail ? "tail" : "head";
        }

        /// <summary>把上一帧的屏幕位置投到当前命中点的切平面上，得到"手指在毛表面划了多远"</summary>
        static Vector3 ProjectOnPlane(Camera camera, Vector2 screenPoint, Vector3 planePoint, Vector3 planeNormal)
        {
            if (camera == null)
            {
                return planePoint;
            }

            Ray ray = camera.ScreenPointToRay(screenPoint);
            float denominator = Vector3.Dot(ray.direction, planeNormal);
            if (Mathf.Abs(denominator) < 1e-5f)
            {
                return planePoint;
            }

            float distance = Vector3.Dot(planePoint - ray.origin, planeNormal) / denominator;
            if (distance <= 0f)
            {
                return planePoint;
            }

            return ray.origin + ray.direction * distance;
        }

        // ---------- 手（拖尾巴 / 固定） ----------

        void TryStartGrabs()
        {
            for (int i = 0; i < _pointers.Count; i++)
            {
                PointerInput.Pointer pointer = _pointers[i];
                if (!pointer.Pressed || pointer.OverUi || FindGrab(pointer.FingerId) != null)
                {
                    continue;
                }

                MaidGroomHit hit = _rig.Pick(pointer.Position, Camera.main, _earHitScale);
                if (hit == null || hit.Part.Kind != MaidGroomKind.Tail || hit.Part.Tail == null)
                {
                    continue;
                }

                Grab(hit.Part.Tail, pointer);
            }
        }

        void UpdateGrabs()
        {
            for (int i = _grabs.Count - 1; i >= 0; i--)
            {
                TailGrab grab = _grabs[i];
                PointerInput.Pointer pointer;
                if (!TryFindPointer(grab.FingerId, out pointer) || !pointer.Held)
                {
                    Release(grab);
                    _grabs.RemoveAt(i);
                    continue;
                }

                Drag(grab, pointer.Position);
            }
        }

        void Grab(MaidTailChain chain, PointerInput.Pointer pointer)
        {
            for (int i = _grabs.Count - 1; i >= 0; i--)
            {
                if (_grabs[i].Chain == chain)
                {
                    _grabs.RemoveAt(i);
                }
            }

            TailGrab grab = new TailGrab();
            grab.FingerId = pointer.FingerId;
            grab.Chain = chain;
            grab.Origin = pointer.Position;
            chain.SnapshotStart(out grab.StartYaw, out grab.StartPitch);
            grab.TargetYaw = grab.StartYaw;
            grab.TargetPitch = grab.StartPitch;
            ComputeDragSigns(grab, chain);
            chain.SetTarget(grab.TargetYaw, grab.TargetPitch, true);
            _grabs.Add(grab);
            _frozen.Remove(chain.Id);
            chain.Freeze(false);
            _rig.BusyGroups.Add(GroupOf(chain));
        }

        void Drag(TailGrab grab, Vector2 pointer)
        {
            if (grab == null || grab.Chain == null)
            {
                return;
            }

            Vector2 delta = pointer - grab.Origin;
            float yaw = grab.StartYaw
                + Mathf.Atan2(delta.x, _tailDragPixels) * Mathf.Rad2Deg * _tailSensitivity * grab.YawSign;
            float pitch = grab.StartPitch
                + Mathf.Atan2(delta.y, _tailDragPixels) * Mathf.Rad2Deg * _tailSensitivity * grab.PitchSign;
            yaw = Mathf.Clamp(yaw, -MaidTailChain.MaxYaw, MaidTailChain.MaxYaw);
            pitch = Mathf.Clamp(pitch, MaidTailChain.MinPitch, MaidTailChain.MaxPitch);
            grab.TargetYaw = yaw;
            grab.TargetPitch = pitch;
            grab.Chain.SetTarget(grab.TargetYaw, grab.TargetPitch, true);
        }

        /// <summary>
        /// 抓尾巴时量一次：正的目标 yaw / pitch 会让尾尖往屏幕哪边走。
        /// 站着、背对镜头、趴着这三种姿态下尾巴的局部轴在世界上朝的方向完全不一样，
        /// 写死符号必然有一种姿态是反的（模组那边主体姿态固定，才敢写死）。
        /// </summary>
        void ComputeDragSigns(TailGrab grab, MaidTailChain chain)
        {
            grab.YawSign = 1f;
            grab.PitchSign = 1f;
            Camera camera = Camera.main;
            Transform root = chain.Bones != null && chain.Bones.Length > 0 ? chain.Bones[0] : null;
            Transform tip = chain.Bones != null && chain.Bones.Length > 0
                ? chain.Bones[chain.Bones.Length - 1]
                : null;
            if (camera == null || root == null || tip == null || root == tip)
            {
                return;
            }

            grab.YawSign = ScreenSign(camera, tip.position, root.position, root.up, false);
            grab.PitchSign = ScreenSign(camera, tip.position, root.position, root.right, true);
        }

        /// <summary>
        /// 把尾尖按 +1 度的目标角度绕轴转一下，看它在屏幕上往哪边跑（竖着看 y、横着看 x）。
        /// Apply 里正的 yaw 是绕局部 Y 转 -yaw，所以这里试的是 -1 度。
        /// </summary>
        static float ScreenSign(Camera camera, Vector3 tip, Vector3 pivot, Vector3 axis, bool vertical)
        {
            Vector3 direction = tip - pivot;
            if (axis.sqrMagnitude < 1e-8f || direction.sqrMagnitude < 1e-8f)
            {
                return 1f;
            }

            Vector3 before = camera.WorldToScreenPoint(tip);
            Vector3 after = camera.WorldToScreenPoint(
                pivot + Quaternion.AngleAxis(-1f, axis.normalized) * direction);
            if (before.z <= 0f || after.z <= 0f)
            {
                return 1f;
            }

            float delta = vertical ? after.y - before.y : after.x - before.x;
            return Mathf.Abs(delta) < 1e-4f ? 1f : Mathf.Sign(delta);
        }

        void Release(TailGrab grab)
        {
            if (grab == null || grab.Chain == null)
            {
                return;
            }

            if (!_frozen.Contains(grab.Chain.Id))
            {
                grab.Chain.SetTarget(0f, 0f, false);
                _rig.BusyGroups.Remove(GroupOf(grab.Chain));
            }
        }

        void ReleaseGrabs()
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                Release(_grabs[i]);
            }

            _grabs.Clear();
            for (int i = 0; i < _chains.Count; i++)
            {
                MaidTailChain chain = _chains[i];
                if (chain == null)
                {
                    continue;
                }

                chain.Freeze(false);
                chain.ResetAll();
                chain.Apply(1f, 1f, 0.08f, 1f);
            }

            if (_rig != null)
            {
                _rig.BusyGroups.Clear();
            }
        }

        /// <summary>面板上的「固定 / 松开尾巴」：固定住不给弹回去，再按一下放开</summary>
        void ToggleFix()
        {
            MaidTailChain chain = _grabs.Count > 0 ? _grabs[0].Chain : null;
            if (chain == null && _chains.Count > 0)
            {
                chain = _chains[0];
            }

            if (chain == null)
            {
                SetHint(_chains.Count == 0
                    ? "这个模型的骨架里没有尾巴，没得固定"
                    : "先按住尾巴再点「固定尾巴」");
                return;
            }

            bool fixedNow = !_frozen.Contains(chain.Id);
            if (fixedNow)
            {
                _frozen.Add(chain.Id);
                chain.Freeze(true);
                _rig.BusyGroups.Add(GroupOf(chain));
            }
            else
            {
                _frozen.Remove(chain.Id);
                chain.Freeze(false);
                chain.SetTarget(0f, 0f, false);
                if (FindGrab(chain) == null)
                {
                    _rig.BusyGroups.Remove(GroupOf(chain));
                }
            }
        }

        TailGrab FindGrab(int fingerId)
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                if (_grabs[i].FingerId == fingerId)
                {
                    return _grabs[i];
                }
            }

            return null;
        }

        TailGrab FindGrab(MaidTailChain chain)
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                if (_grabs[i].Chain == chain)
                {
                    return _grabs[i];
                }
            }

            return null;
        }

        bool TryFindPointer(int fingerId, out PointerInput.Pointer pointer)
        {
            for (int i = 0; i < _pointers.Count; i++)
            {
                if (_pointers[i].FingerId == fingerId)
                {
                    pointer = _pointers[i];
                    return true;
                }
            }

            pointer = new PointerInput.Pointer();
            return false;
        }

        static string GroupOf(MaidTailChain chain)
        {
            return "tail/" + chain.Id;
        }

        // ---------- 反馈 ----------

        void Comfort()
        {
            if (Time.unscaledTime - _lastComfortAt < _comfortCooldown)
            {
                return;
            }

            _lastComfortAt = Time.unscaledTime;
            string line = Pick(WithGrainLines);
            MaidChatBubble.Show(_agent, line, _lineSeconds);
            if (_panel != null)
            {
                _panel.ShowLine(line, _lineSeconds);
            }

            SetHint("顺着毛刷：她舒服得眯起眼睛");
        }

        void Against()
        {
            if (Time.unscaledTime - _lastAgainstAt < _againstCooldown)
            {
                return;
            }

            _lastAgainstAt = Time.unscaledTime;
            // 闪红、台词、受伤语音三者共用这个冷却（和摸尾巴的喊疼一个套路）
            string line = MaidEasterEgg.PickHurt(_agent, AgainstGrainLines);
            MaidDamageFlash.Flash(_agent != null ? _agent.gameObject : null, _flashColor, _flashSeconds);
            MaidAudioService.Play(_agent, MaidSoundId.Hurt, false, _voiceVolume);
            MaidChatBubble.Show(_agent, line, _lineSeconds);
            if (_panel != null)
            {
                _panel.ShowLine(line, _lineSeconds);
            }

            SetHint("逆着毛刷：她疼了，快换个方向");
        }

        void Cheer()
        {
            if (Time.unscaledTime - _lastCheerAt < _cheerSeconds * 0.5f)
            {
                return;
            }

            _lastCheerAt = Time.unscaledTime;
            string line = Pick(CheerLines);
            MaidChatBubble.Show(_agent, line, _lineSeconds * 1.5f);
            if (_panel != null)
            {
                _panel.ShowLine(line, _lineSeconds * 1.5f);
            }

            MaidAudioService.Play(_agent, _cheerSoundId, false, _voiceVolume);
            if (_panel != null)
            {
                _panel.SetProgress(0f);
            }

            SetHint("梳够一阵子啦");
        }

        static string Pick(string[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return "";
            }

            return lines[UnityEngine.Random.Range(0, lines.Length)];
        }

        // ---------- 面板 / 毛刷 ----------

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidGroomingPanel>();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidGroomingPanel>();
            }

            _panel.ExitRequested -= End;
            _panel.ExitRequested += End;
            _panel.ModeToggled -= ToggleTool;
            _panel.ModeToggled += ToggleTool;
            _panel.FixToggled -= ToggleFix;
            _panel.FixToggled += ToggleFix;
            _panel.FlipRequested -= Flip;
            _panel.FlipRequested += Flip;
        }

        void EnsureBrush()
        {
            if (_brush == null)
            {
                _brush = GetComponent<MaidGroomingBrush>();
            }

            if (_brush == null)
            {
                _brush = gameObject.AddComponent<MaidGroomingBrush>();
            }
        }

        void ToggleTool()
        {
            CurrentTool = CurrentTool == Tool.Brush ? Tool.Hand : Tool.Brush;
            EndStroke();
            ReleaseGrabs();
            if (_panel != null)
            {
                _panel.SetTool(CurrentTool);
            }

            SetHint(ToolHint());
            // 换工具 = 取景框跟着换（「手」得看得见尾巴），让外层重新取一次景
            _refocus = true;
        }

        string ToolHint()
        {
            if (CurrentTool == Tool.Brush)
            {
                return "沿头发、耳朵或尾巴刷动（顺毛舒服，逆毛会疼）";
            }

            return _chains.Count > 0
                ? "按住尾巴拖 · 点「固定尾巴」让它保持住"
                : "这个模型的骨架里没有尾巴，只能梳头发和耳朵";
        }

        void SetHint(string hint)
        {
            _hint = hint;
            if (_panel != null)
            {
                _panel.SetHint(string.IsNullOrEmpty(_hint) ? ToolHint() : _hint);
            }
        }

        void ShowBrush(MaidGroomHit hit)
        {
            if (_brush == null)
            {
                return;
            }

            _brush.Show(hit.Point, hit.Normal, hit.Surface != null ? hit.Surface.Flow : Vector3.forward);
        }

        /// <summary>
        /// 梳毛取景用哪块包围盒：头 / 头发 / 耳朵 / 尾巴这几块能梳的合起来
        /// （躺着的时候尾巴摊在旁边，只框头的话够不到它）。找不到就退回头骨骼本身
        /// </summary>
        public bool TryGetFocusBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_rig == null)
            {
                return false;
            }

            // 毛刷模式只框头上那几块（近、好刷）；切到「手」要拖尾巴，尾巴必须一起框进来
            bool withTail = _focusTailToo || CurrentTool == Tool.Hand;
            if (_rig.TryGetBounds(out bounds, withTail))
            {
                bounds.Expand(0.04f);
                return true;
            }

            Transform head = _rig.Head;
            if (head == null)
            {
                return false;
            }

            Renderer renderer = head.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                bounds = renderer.bounds;
                return true;
            }

            bounds = new Bounds(head.position, Vector3.one * 0.3f);
            return true;
        }

        static List<MaidTailChain> VisibleTails(MaidAgent agent)
        {
            List<MaidTailChain> result = new List<MaidTailChain>();
            List<MaidTailChain> found = MaidTailChain.Build(agent.transform);
            for (int i = 0; i < found.Count; i++)
            {
                // 被 HiddenNodes 藏起来的骨骼（例如酒狐的小狐狸尾巴）不算
                if (found[i].HasVisibleGeometry())
                {
                    result.Add(found[i]);
                }
            }

            return result;
        }

        void OnGUI()
        {
            if (!IsActive || !_showDebug)
            {
                return;
            }

            MaidBrushGrain.Direction direction = _grain.Current;
            string text = "梳毛 · " + (CurrentTool == Tool.Brush ? "毛刷" : "手")
                + " · 部位 " + (string.IsNullOrEmpty(_lastPart) ? "-" : _lastPart)
                + " · 方向 " + direction
                + " · 有效 " + _brushTicks.ToString("0") + "/" + FavorTicks.ToString("0");
            GUI.Label(new Rect(8f, 52f, Screen.width - 16f, 24f), text);
        }
    }
}
