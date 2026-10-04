using System;
using System.Collections;
using System.Collections.Generic;
using MaidHome.Gameplay.Audio;
using MaidHome.Core.Input;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 喂蛋糕模式：底栏上先选强度（相当于单选按钮），再点屏幕触发。
    /// 轻挡（1）不用蛋糕预制体：播吃东西动作 + 吃东西音效，然后喷粒子 + 随机说一句。
    /// 中挡（2）朝玩家点的地方扔蛋糕预制体：出手点固定在屏幕外，初速度朝着点的地方，
    /// 后面怎么飞交给物理引擎。砸到头才算喂到（贴脸 + 砸中音效 + 闪红 + 另一套语音/台词），
    /// 演完清掉那块蛋糕；没砸到（落地/撞墙）就不管它。
    /// 重挡（3）还没做，按钮灰着。
    /// 吃东西动作：GeckoLib 模型演 use_mainhand:eat；SimpleBedrockModel 没有动画表，
    /// 让 MaidSimpleBedrockAnimator 现算一个"抬手吃"的动作。
    /// 音效/蛋糕预制体/粒子贴图这些素材都填在这个组件上——它平时是 MaidInteractionController
    /// 运行时 AddComponent 出来的，要接线得在场景里先手动挂一份。
    /// 挂的位置要常驻激活（推荐就挂控制器那个物体）：粒子/音源是它的子物体，
    /// 而且喂蛋糕靠自己的协程跑，挂在会被 Hide 关掉的 MaidFeedBar 根上就动不了了。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidFeedInteraction : MonoBehaviour
    {
        public const int LevelLight = 1;
        public const int LevelMedium = 2;
        public const int LevelHeavy = 3;

        public event Action Ended;

        [Header("吃东西")]
        [Tooltip("吃东西动画名，TLM 的动画表里叫 use_mainhand:eat；模型没这条就只出声和粒子")]
        [SerializeField] private string _eatClip = "use_mainhand:eat";
        [Tooltip("吃东西动画最长演多久。这条动画本身是循环的，演完自动换回原来那条")]
        [SerializeField] private float _eatMaxSeconds = 2.5f;
        [Tooltip("喂下去多久之后出吃东西音效（秒，从喂的那一刻算）")]
        [SerializeField] private float _eatSoundDelay = 0.2f;
        [Tooltip("女仆语音的放大倍率（只有她自己的语音走这个；吃东西音效、砸中音效这类非语音一律原音量播，不放大）")]
        [SerializeField] private float _soundVolume = 6f;

        [Header("素材（你来填）")]
        [Tooltip("吃东西音效，可以放多个，每次喂随机选一个播")]
        [SerializeField] private AudioClip[] _eatSounds;
        [Tooltip("蛋糕预制体：中挡甩她脸上用（Assets/Prefab/cake.prefab）。不接的话中挡按钮点不动")]
        [SerializeField] private GameObject _cakePrefab;
        [Tooltip("蛋糕砸到脸上的音效")]
        [SerializeField] private AudioClip _cakeHitSound;
        [Tooltip("喷粒子用的贴图")]
        [SerializeField] private Texture2D _particleTexture;
        [Tooltip("喷粒子用的材质（可选）：接了就用它，忽略上面的贴图")]
        [SerializeField] private Material _particleMaterial;
        [Tooltip("进喂蛋糕模式时把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        [SerializeField] private float _feedOrthographicSize = 0.8f;

        [Header("粒子")]
        [SerializeField] private int _particleCount = 12;
        [SerializeField] private float _particleLifetime = 0.6f;
        [SerializeField] private float _particleSpeed = 0.35f;
        [Tooltip("粒子大小（随机取区间），单位是世界单位")]
        [SerializeField] private Vector2 _particleSize = new Vector2(0.05f, 0.09f);
        [SerializeField] private Color _particleColor = Color.white;
        [Tooltip("出粒子的位置，按头部包围盒的半尺寸取比例：y 负数是嘴（头中心往下），z 正数是她的正前方")]
        [SerializeField] private Vector3 _particleOffset = new Vector3(0f, -0.35f, 0.9f);
        [Tooltip("喂下去多久之后喷粒子（秒，从喂的那一刻算）")]
        [SerializeField] private float _particleDelay = 1f;
        [SerializeField] private float _particleGravity = 0.5f;

        [Header("说话")]
        [Tooltip("吃完随机说的那句话（语音）。默认 maid.mode.feed，声音包里没有这条会自动回退 idle")]
        [SerializeField] private string _voiceEvent = MaidSoundId.Feed;
        [Tooltip("喂下去多久之后说（秒，从喂的那一刻算）")]
        [SerializeField] private float _voiceDelay = 1.7f;
        [SerializeField] private float _lineSeconds = 4f;
        [SerializeField] private string[] _lines =
        {
            "唔……好吃，谢谢主人！",
            "这个蛋糕是给我的吗？",
            "奶油沾到嘴上了……",
            "嘿嘿，主人喂的最好吃。",
            "再来一口也可以哦。"
        };

        [Tooltip("场景里手摆一份 MaidFeedPanel 就接这里，留空自动找/自动生成")]
        public MaidFeedPanel _panel;

        [Header("怎么喂")]
        [Tooltip("一进模式选中的挡位：1 轻 / 2 中 / 3 重")]
        [SerializeField] private int _defaultLevel = LevelLight;
        [Tooltip("连点冷却：离上一次喂/扔要隔这么久才会响应下一次点击（轻挡和中挡共用）")]
        [SerializeField] private float _feedCooldownSeconds = 0.3f;

        [Header("中挡：扔蛋糕")]
        [Tooltip("被蛋糕砸中时借这条动画演一下（TLM 里「被打」叫 attacked），演完换回原来那条")]
        [SerializeField] private string _hurtClip = "attacked";
        [Tooltip("受伤动画不管多长，演这么久就换回来")]
        [SerializeField] private float _hurtMaxSeconds = 1f;
        [Tooltip("出手点的屏幕高度（视口坐标，0 = 屏幕下沿，1 = 上沿；越负越靠屏幕外=离她越远）；屏幕 x 跟着你点的地方走，等于从手指下方扔上去")]
        [SerializeField] private float _cakeStartViewportY = -0.6f;
        [Tooltip("出手点沿视线方向离她多远（往摄像机/玩家这边推，单位是世界单位）。正交相机下屏幕位置不变，只影响真实距离：越大=越靠玩家、飞得越远")]
        [SerializeField] private float _cakeStartDepth = 1f;
        [Tooltip("蛋糕从出手点飞到点的地方要多久。用飞行时间反推初速度，保证落到手指点的地方，中途的抛物线还是物理引擎算的")]
        [SerializeField] private float _cakeFlySeconds = 0.35f;
        [Tooltip("蛋糕飞行时的翻滚速度（度/秒）")]
        [SerializeField] private float _cakeSpinDegrees = 720f;
        [Tooltip("场上最多留这么多块扔出去的蛋糕，再多就删最早的（没砸中的那些）")]
        [SerializeField] private int _maxThrownCakes = 10;

        [Header("重挡：用力砸")]
        [Tooltip("重挡的飞行时间（比中挡快，越小越快）")]
        [SerializeField] private float _heavyFlySeconds = 0.18f;
        [Tooltip("重砸时蛋糕砸到脸上的音效（和中挡分开）。留空就还用中挡那个")]
        [SerializeField] private AudioClip _heavyHitSound;
        [Tooltip("重挡砸中后头往一侧扭多少度（参考戳脸的幅度）。扭的方向反了就把这个值改成负数")]
        [SerializeField] private float _heavyTwistDegrees = 34f;
        [Tooltip("扭头时同时低头/抬头的角度")]
        [SerializeField] private float _heavyTwistPitchDegrees = 10f;
        [Tooltip("扭头动作演多久：前 22% 快速扭过去，剩下慢慢回正")]
        [SerializeField] private float _heavyTwistSeconds = 0.5f;
        [Tooltip("重挡砸中的语音事件（和中挡区分开，填声音包里的事件 id）")]
        [SerializeField] private string _heavyVoiceEvent = MaidSoundId.Hurt;
        [SerializeField] private string[] _heavyLines =
        {
            "呜……！主人你干嘛呀！",
            "好疼好疼好疼！",
            "呜哇——脸要被砸扁了……",
            "过、过分！这次真的生气了！",
            "哼，再这样我可要还手了哦。"
        };
        [Tooltip("连击多久没续上就断（只对重挡生效）")]
        [SerializeField] private float _comboTimeoutSeconds = 2.5f;
        [Tooltip("连到多少的倍数放「N连抽」彩蛋（只对重挡生效）")]
        [SerializeField] private int _comboMilestoneStep = 100;
        [Tooltip("「N连抽」的彩蛋音，留空就用现场合成的上行琶音")]
        [SerializeField] private AudioClip _milestoneClip;
        [Tooltip("砸到脸上的闪红。Alpha 当强度用：1 = 纯红（原版受伤），0.5 = 半透明红，0 = 不闪")]
        [SerializeField] private Color _cakeFlashColor = new Color(1f, 0f, 0f, 0.5f);
        [Tooltip("砸到脸上的闪红时长。原版受伤是 0.5 秒")]
        [SerializeField] private float _cakeFlashSeconds = 0.5f;
        [Tooltip("砸中之后的语音事件，和轻挡那句 maid.mode.feed 区分开：填声音包里的事件 id（例如 maid.ai.hurt / maid.ai.tamed）")]
        [SerializeField] private string _cakeVoiceEvent = MaidSoundId.Hurt;
        [SerializeField] private string[] _cakeLines =
        {
            "呜哇！谁扔的蛋糕呀！",
            "唔！我的脸……",
            "蛋、蛋糕砸到脸上了啦！",
            "哼，主人好过分……",
            "不过……还挺好吃的。"
        };
        [Tooltip("SimpleBedrockModel 没有动画表，吃东西时那个抬手动作演多久")]
        [SerializeField] private float _simpleEatSeconds = 1.5f;

        public bool IsActive { get; private set; }

        /// <summary>喂蛋糕模式取景用的正交尺寸（0 = 不推近），控制器拿去调相机</summary>
        public float OrthographicSize
        {
            get { return _feedOrthographicSize; }
        }

        /// <summary>当前选中的挡位（1 轻 / 2 中 / 3 重），控制器拿它决定要不要切透视</summary>
        public int SelectedLevel
        {
            get { return _selectedLevel; }
        }

        MaidAgent _agent;
        MaidFaceRig _rig;
        AudioSource _source;
        ParticleSystem _particles;
        Material _generatedMaterial;
        MaidFeedHitBoxes _hitBoxes;
        readonly List<GameObject> _thrownCakes = new List<GameObject>();
        readonly MaidSlapComboHud _comboHud = new MaidSlapComboHud();
        Coroutine _eatRoutine;
        Coroutine _hurtRoutine;
        bool _twisting;
        float _twistTime;
        float _twistSide = 1f;
        Quaternion _headAnimated;
        Quaternion _headWritten;
        bool _headHasAnimated;
        bool _eating;
        bool _eatPlaying;
        bool _hurtPlaying;
        int _selectedLevel = LevelLight;
        string _eatReturnClip;
        float _eatReturnTime;
        string _hurtReturnClip;
        float _hurtReturnTime;
        float _lastFeedTime = -100f;

        public bool Supports(MaidAgent agent)
        {
            return agent != null;
        }

        public bool Begin(MaidAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("MaidFeedInteraction 所在的物体没激活（别挂在会被 Hide 关掉的 MaidFeedBar 根上），"
                    + "喂蛋糕动不了", this);
                return false;
            }

            Abort();

            _agent = agent;
            _rig = MaidFaceRig.Build(agent.transform);
            _lastFeedTime = -100f;

            if (CountEatSounds() == 0)
            {
                Debug.LogWarning("喂蛋糕没接吃东西音效：MaidFeedInteraction 的「吃东西音效」列表是空的", this);
            }

            if (_cakePrefab == null)
            {
                Debug.LogWarning("中挡要蛋糕预制体：MaidFeedInteraction 的「蛋糕预制体」留空了，中挡会点不动", this);
            }

            // 音源和粒子都先建好：别等喂完那一帧再建，那是要出画面的时候
            EnsureSource();
            if (HasParticles)
            {
                EnsureParticles();
            }
            else
            {
                Debug.LogWarning("喂蛋糕没接粒子贴图/材质：MaidFeedInteraction 的「粒子贴图」留空了", this);
            }

            if (_cakePrefab != null)
            {
                // 命中体先搭好（头 / 躯干 / 左右臂 / 左右腿），扔蛋糕那一帧就别再建物体了
                _hitBoxes = MaidFeedHitBoxes.Build(_agent, _rig);
            }

            EnsurePanel();
            if (_panel != null)
            {
                _panel.SetLevelInteractable(LevelLight, true);
                // 中挡、重挡都要扔蛋糕，没接预制体就灰着
                _panel.SetLevelInteractable(LevelMedium, _cakePrefab != null);
                _panel.SetLevelInteractable(LevelHeavy, _cakePrefab != null);
                SelectDefaultLevel();
                _panel.Show();
            }

            _comboHud.Timeout = _comboTimeoutSeconds;
            _comboHud.Step = _comboMilestoneStep;
            _comboHud.Clear();
            _headHasAnimated = false;

            IsActive = true;
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

        void SelectDefaultLevel()
        {
            int level = _defaultLevel;
            if (level != LevelLight && _cakePrefab == null)
            {
                level = LevelLight;
            }

            _selectedLevel = level;
            if (_panel != null)
            {
                _panel.SetSelectedLevel(level);
            }
        }

        void Update()
        {
            if (!IsActive)
            {
                return;
            }

            CheckCakeHits();

            PointerInput.Pointer pointer = PointerInput.Primary;
            if (!pointer.Pressed || pointer.OverUi)
            {
                return;
            }

            Trigger(pointer.Position);
        }

        /// <summary>
        /// 命中兜底：正常靠 MaidFeedCake 的触发事件，但碰撞矩阵/连续检测出问题时事件可能不来
        /// （表现就是蛋糕从她脸上过去、永远不删），所以每帧自己再看一眼有没有钻进头上的触发体。
        /// </summary>
        void CheckCakeHits()
        {
            if (_hitBoxes == null || _hitBoxes.Head == null || _thrownCakes.Count == 0)
            {
                return;
            }

            Bounds head = _hitBoxes.Head.bounds;
            head.Expand(0.05f);

            for (int i = _thrownCakes.Count - 1; i >= 0; i--)
            {
                GameObject cake = _thrownCakes[i];
                if (cake == null)
                {
                    _thrownCakes.RemoveAt(i);
                    continue;
                }

                Rigidbody body = cake.GetComponent<Rigidbody>();
                Vector3 center = body != null ? body.worldCenterOfMass : cake.transform.position;
                if (!head.Contains(center))
                {
                    continue;
                }

                MaidFeedCake marker = cake.GetComponentInChildren<MaidFeedCake>();
                if (marker != null)
                {
                    // 走同一条命中路径，内部只回调一次，触发事件也来过的话不会重复
                    marker.Report();
                }
                else
                {
                    OnCakeHit(cake, LevelMedium);
                }
            }
        }

        /// <summary>点屏幕：按当前选中的挡位喂 / 扔。返回 false 表示这次没触发</summary>
        public bool Trigger(Vector2 screenPosition)
        {
            if (!IsActive)
            {
                return false;
            }

            if (Time.time - _lastFeedTime < _feedCooldownSeconds)
            {
                return false;
            }

            if (_selectedLevel == LevelMedium || _selectedLevel == LevelHeavy)
            {
                // 中挡 / 重挡：方向由点的地方决定，轨迹交给物理引擎。她正吃着一口也能砸
                bool heavy = _selectedLevel == LevelHeavy;
                _lastFeedTime = Time.time;
                return ThrowCake(screenPosition, heavy ? _heavyFlySeconds : _cakeFlySeconds,
                    _selectedLevel) != null;
            }

            // 轻挡：还在一口一口吃的时候不再喂
            if (_eating)
            {
                return false;
            }

            _lastFeedTime = Time.time;
            _eating = true;
            _eatRoutine = StartCoroutine(LightRoutine());
            return true;
        }

        IEnumerator LightRoutine()
        {
            yield return EatSequence(Time.time, _voiceEvent, _lines, true, true);
            FinishEating();
        }

        /// <summary>
        /// 蛋糕砸到头：砸中音效 + 闪红 + 喷粒子，然后走吃东西那套。
        /// 蛋糕本身**不删、也不冻**，物理照常继续（该落地就落地）；退出模式时才统一清掉。
        /// </summary>
        void OnCakeHit(GameObject cake, int level)
        {
            if (cake == null)
            {
                return;
            }

            if (!IsActive)
            {
                return;
            }

            bool heavy = level == LevelHeavy;

            SpawnParticles();
            // 砸中音效：重砸有单独的，没接就退回中挡那个
            PlayCakeHitSound(heavy && _heavyHitSound != null ? _heavyHitSound : _cakeHitSound);
            if (_agent != null)
            {
                MaidDamageFlash.Flash(_agent.gameObject, _cakeFlashColor, _cakeFlashSeconds);
            }

            PlayHurtAnimation();

            if (heavy)
            {
                // 重挡多两样：头被砸得扭过去，以及连击计数
                PlayHeadTwist(cake);
                _comboHud.Confirm();
                if (_comboHud.MilestoneHit)
                {
                    PlayMilestoneSound();
                }
            }

            if (_eating)
            {
                // 已经在吃了，这颗只是砸个响，不再走一遍吃的流程
                return;
            }

            _eating = true;
            _eatRoutine = StartCoroutine(CakeEatRoutine(heavy));
        }

        IEnumerator CakeEatRoutine(bool heavy)
        {
            // 砸到脸那一刻才算"喂下去"，吃东西那套从这时候开始算；
            // 粒子已经在砸中的时候喷过了，被扔的蛋糕也不再演"自己拿着吃"的动画
            yield return EatSequence(Time.time, heavy ? _heavyVoiceEvent : _cakeVoiceEvent,
                heavy ? _heavyLines : _cakeLines, false, false);
            FinishEating();
        }

        /// <summary>吃东西那一套：动作 + 吃东西音效 + 粒子 + 说话，时间和 delay 都从 start 那一刻算</summary>
        IEnumerator EatSequence(float start, string voiceEvent, string[] lines, bool delayedParticles,
            bool playEatAnimation)
        {
            float eatSeconds = playEatAnimation ? PlayEatAnimation() : 0f;

            yield return WaitUntil(start + _eatSoundDelay);
            PlayEatSound();

            if (delayedParticles)
            {
                yield return WaitUntil(start + _particleDelay);
                SpawnParticles();
            }

            yield return WaitUntil(start + _voiceDelay);
            SaySomething(voiceEvent, lines);

            yield return WaitUntil(start + eatSeconds);
            StopEatAnimation();
        }

        void FinishEating()
        {
            _eatRoutine = null;
            _eating = false;
        }

        // ---------- 重挡：扭头 + 连击 ----------

        /// <summary>
        /// 头被砸得扭一下：方向取蛋糕在屏幕上往哪边飞，幅度参考摸脸的戳脸。
        /// 和摸脸一样是叠在动画之上写的（见 ApplyWorldDelta），所以受伤动画照常播，头会被多拧一下。
        /// </summary>
        void PlayHeadTwist(GameObject cake)
        {
            float side = 1f;
            Rigidbody body = cake != null ? cake.GetComponent<Rigidbody>() : null;
            Camera camera = Camera.main;
            if (body != null && camera != null)
            {
                // 作者实机验证过：蛋糕往屏幕右边飞，她的头是往左甩的（和直觉相反），别"顺手"翻回来
                float right = Vector3.Dot(body.velocity, camera.transform.right);
                if (right > 0.05f)
                {
                    side = -1f;
                }
                else if (right < -0.05f)
                {
                    side = 1f;
                }
            }

            _twistSide = side;
            _twistTime = 0f;
            _twisting = true;
        }

        void LateUpdate()
        {
            if (!_twisting)
            {
                return;
            }

            _twistTime += Time.deltaTime;
            float total = Mathf.Max(0.05f, _heavyTwistSeconds);
            float t = Mathf.Clamp01(_twistTime / total);

            // 前 22% 快速扭过去，剩下慢慢回正
            float curve = t < 0.22f
                ? Mathf.SmoothStep(0f, 1f, t / 0.22f)
                : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.22f) / 0.78f);

            if (_rig != null && _rig.Head != null)
            {
                Quaternion delta = CameraSpaceRotation(Camera.main,
                    _heavyTwistPitchDegrees * curve,
                    _twistSide * _heavyTwistDegrees * curve,
                    0f);
                ApplyWorldDelta(_rig.Head, delta, ref _headAnimated, ref _headWritten, ref _headHasAnimated);
            }

            if (t >= 1f)
            {
                _twisting = false;
                _headHasAnimated = false;
            }
        }

        /// <summary>把摄像机空间的欧拉角变成世界空间的旋转增量（和摸脸那边一模一样）</summary>
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
        /// 判断这一帧动画有没有写过，避免把动画姿势吃掉（和 MaidTailChain / 摸脸一个套路）。
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

        /// <summary>「N连抽」的彩蛋音：没接就用现场合成的上行琶音</summary>
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

        void OnGUI()
        {
            if (!IsActive)
            {
                return;
            }

            // 只有重挡会往里加数，轻/中挡时它就是空的（不画东西）
            _comboHud.Draw();
        }

        /// <summary>等到某个时刻（Time.time）。三个延迟都是"从喂的那一刻算"，写反了也不会互相累加</summary>
        static IEnumerator WaitUntil(float moment)
        {
            while (Time.time < moment)
            {
                yield return null;
            }
        }

        void Release()
        {
            if (_eatRoutine != null)
            {
                StopCoroutine(_eatRoutine);
                _eatRoutine = null;
            }

            StopEatAnimation();
            StopHurtAnimation();
            _twisting = false;
            _headHasAnimated = false;
            _comboHud.Clear();
            if (_hitBoxes != null)
            {
                _hitBoxes.Destroy();
                _hitBoxes = null;
            }

            ClearThrownCakes();
            _eating = false;
            _agent = null;
            _rig = null;
            _eatReturnClip = null;
            _eatReturnTime = 0f;
            if (_particles != null)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_panel != null)
            {
                _panel.Hide();
            }

            IsActive = false;
        }

        // ---------- 吃东西动画 ----------

        /// <summary>
        /// 吃东西动作，返回要演多久（没动作就返回 0）。
        /// GeckoLib 模型借她自己的 use_mainhand:eat；SimpleBedrockModel 没有动画表，
        /// 让 MaidSimpleBedrockAnimator 现算抬手。
        /// </summary>
        float PlayEatAnimation()
        {
            MaidSimpleBedrockAnimator simple = GetSimpleAnimator();
            if (IsSimpleModel())
            {
                float seconds = Mathf.Clamp(_simpleEatSeconds, 0.2f, Mathf.Max(0.2f, _eatMaxSeconds));
                simple.PlayEat(seconds);
                return seconds;
            }

            BedrockAnimationPlayer player = GetPlayer();
            if (player == null || string.IsNullOrEmpty(_eatClip) || !player.HasClip(_eatClip))
            {
                return 0f;
            }

            _eatReturnClip = player.PlayingClipName;
            _eatReturnTime = player.PlayingTime;
            if (!player.Play(_eatClip))
            {
                _eatReturnClip = null;
                return 0f;
            }

            _eatPlaying = true;
            return Mathf.Clamp(player.ClipLength(_eatClip), 0.2f, Mathf.Max(0.2f, _eatMaxSeconds));
        }

        void StopEatAnimation()
        {
            MaidSimpleBedrockAnimator simple = GetSimpleAnimator();
            if (simple != null)
            {
                simple.CancelEat();
            }

            if (!_eatPlaying)
            {
                _eatReturnClip = null;
                return;
            }

            _eatPlaying = false;
            BedrockAnimationPlayer player = GetPlayer();
            if (player != null && !string.IsNullOrEmpty(_eatReturnClip) && player.HasClip(_eatReturnClip))
            {
                player.Play(_eatReturnClip, _eatReturnTime);
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

            _eatReturnClip = null;
            _eatReturnTime = 0f;
        }

        BedrockAnimationPlayer GetPlayer()
        {
            return _agent != null ? _agent.GetComponent<BedrockAnimationPlayer>() : null;
        }

        MaidSimpleBedrockAnimator GetSimpleAnimator()
        {
            return _agent != null ? _agent.GetComponent<MaidSimpleBedrockAnimator>() : null;
        }

        /// <summary>
        /// 程序化动画统一以「挂没挂 MaidSimpleBedrockAnimator（而且是启用状态）」为准：
        /// 模组侧 Java 统一驱动的方块模型没有 animation.json，也是挂这个组件；
        /// GeckoLib 模型上那份（如果有）会被 MaidPlacement 关掉。
        /// </summary>
        bool IsSimpleModel()
        {
            MaidSimpleBedrockAnimator simple = GetSimpleAnimator();
            return simple != null && simple.isActiveAndEnabled;
        }

        // ---------- 受伤动画 ----------

        /// <summary>
        /// 被蛋糕砸中时借她自己的「被打」动画演一下（TLM 里叫 attacked），演完换回原来那条。
        /// 正在演的时候不重开，不然连着砸会一直卡在第一帧。
        /// </summary>
        void PlayHurtAnimation()
        {
            if (_hurtPlaying || _agent == null || string.IsNullOrEmpty(_hurtClip))
            {
                return;
            }

            BedrockAnimationPlayer player = GetPlayer();
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
            float seconds = Mathf.Clamp(player.ClipLength(_hurtClip), 0.2f,
                Mathf.Max(0.2f, _hurtMaxSeconds));
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
                ReturnToHurtClip(GetPlayer());
            }

            _hurtReturnClip = null;
            _hurtReturnTime = 0f;
        }

        void ReturnToHurtClip(BedrockAnimationPlayer player)
        {
            // 退出模式时也得换回来，不然她会一直停在被打的姿势
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

        // ---------- 声音 ----------

        void PlayEatSound()
        {
            int total = CountEatSounds();
            if (total == 0)
            {
                return;
            }

            // 列表里可能留了空槽，只在真正有东西的那几个里随机
            int pick = UnityEngine.Random.Range(0, total);
            AudioClip clip = null;
            for (int i = 0; i < _eatSounds.Length; i++)
            {
                if (_eatSounds[i] == null)
                {
                    continue;
                }

                if (pick-- == 0)
                {
                    clip = _eatSounds[i];
                    break;
                }
            }

            if (clip == null)
            {
                return;
            }

            EnsureSource();
            _source.volume = Mathf.Clamp(MaidAudioService.MasterVolume, 0f, MaidAudioVoice.MaxVolume);
            // 音效原音量播：倍率只给女仆自己的语音
            _source.PlayOneShot(clip, 1f);
        }

        int CountEatSounds()
        {
            if (_eatSounds == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _eatSounds.Length; i++)
            {
                if (_eatSounds[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>蛋糕砸到脸上的音效（控制器上填的那个）</summary>
        /// <summary>砸中音效（中挡/重挡各自的素材传进来）。音效不放大，原音量播</summary>
        void PlayCakeHitSound(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            EnsureSource();
            _source.volume = Mathf.Clamp(MaidAudioService.MasterVolume, 0f, MaidAudioVoice.MaxVolume);
            _source.PlayOneShot(clip, 1f);
        }

        void SaySomething(string voiceEvent, string[] lines)
        {
            if (_agent != null && !string.IsNullOrEmpty(voiceEvent))
            {
                MaidAudioService.Play(_agent, voiceEvent, false, _soundVolume);
            }

            if (_panel != null && lines != null && lines.Length > 0)
            {
                _panel.ShowLine(lines[UnityEngine.Random.Range(0, lines.Length)], _lineSeconds);
            }
        }

        void EnsureSource()
        {
            if (_source != null)
            {
                return;
            }

            GameObject holder = new GameObject("MaidFeedAudio");
            holder.transform.SetParent(transform, false);
            _source = holder.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }

        // ---------- 粒子 ----------

        void SpawnParticles()
        {
            if (!HasParticles)
            {
                return;
            }

            Vector3 mouth;
            if (!TryGetMouthPosition(out mouth))
            {
                return;
            }

            ParticleSystem particles = EnsureParticles();
            if (particles == null)
            {
                return;
            }

            particles.transform.position = mouth;
            Transform maid = _agent != null ? _agent.transform : null;
            Vector3 forward = maid != null ? maid.forward : Vector3.forward;
            Vector3 up = maid != null ? maid.up : Vector3.up;

            for (int i = 0; i < _particleCount; i++)
            {
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
                emit.position = mouth;
                emit.velocity = UnityEngine.Random.insideUnitSphere * _particleSpeed
                    + forward * (_particleSpeed * 0.6f)
                    + up * (_particleSpeed * 0.4f);
                emit.startLifetime = UnityEngine.Random.Range(_particleLifetime * 0.7f, _particleLifetime);
                emit.startSize = UnityEngine.Random.Range(_particleSize.x, _particleSize.y);
                emit.startColor = _particleColor;
                particles.Emit(emit, 1);
            }
        }

        ParticleSystem EnsureParticles()
        {
            if (_particles != null)
            {
                return _particles;
            }

            Material material = GetParticleMaterial();
            if (material == null)
            {
                Debug.LogWarning("喂蛋糕的粒子找不到能用的 shader，喷不出来", this);
                return null;
            }

            GameObject holder = new GameObject("MaidFeedParticles");
            // 先关着建：AddComponent 出来的粒子系统默认就在播，这时改 duration 会被 Unity 拒掉
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            _particles = holder.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = _particleLifetime;
            main.startSpeed = 0f;
            main.startSize = _particleSize.y;
            // 每颗粒子的颜色走 EmitParams，这里给白色免得两处相乘
            main.startColor = Color.white;
            main.gravityModifier = _particleGravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // 粒子靠 Emit 手动放，不要自动持续发射
            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.enabled = false;

            ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;

            holder.SetActive(true);
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return _particles;
        }

        bool HasParticles
        {
            get { return _particleMaterial != null || _particleTexture != null; }
        }

        Material GetParticleMaterial()
        {
            if (_particleMaterial != null)
            {
                return _particleMaterial;
            }

            if (_generatedMaterial != null)
            {
                if (_particleTexture != null && _generatedMaterial.mainTexture != _particleTexture)
                {
                    _generatedMaterial.mainTexture = _particleTexture;
                }

                return _generatedMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            if (shader == null)
            {
                return null;
            }

            _generatedMaterial = new Material(shader);
            _generatedMaterial.name = "MaidFeedParticles (生成)";
            if (_particleTexture != null)
            {
                _generatedMaterial.mainTexture = _particleTexture;
            }

            return _generatedMaterial;
        }

        /// <summary>嘴的位置：头部包围盒中心按半尺寸偏移（默认往下一点、往她正前方一点）</summary>
        bool TryGetMouthPosition(out Vector3 position)
        {
            return TryGetHeadOffsetPoint(_particleOffset, out position);
        }

        /// <summary>
        /// 按头部包围盒的半尺寸算一个点：offset 的 y 负数是往下（嘴在头中心下面），
        /// z 正数是她的正前方。找不到头骨骼就按整个身体的上半段估。
        /// </summary>
        bool TryGetHeadOffsetPoint(Vector3 offset, out Vector3 position)
        {
            position = Vector3.zero;
            if (_agent == null)
            {
                return false;
            }

            Bounds bounds;
            if (_rig != null && _rig.HeadMesh != null)
            {
                bounds = _rig.HeadMesh.bounds;
            }
            else
            {
                // 没找到头就拿整个身体估：取上半身
                Bounds body = _agent.GetBounds();
                bounds = new Bounds(body.center + Vector3.up * (body.extents.y * 0.5f), body.size * 0.5f);
            }

            Transform maid = _agent.transform;
            position = bounds.center
                + maid.right * (bounds.extents.x * offset.x)
                + maid.up * (bounds.extents.y * offset.y)
                + maid.forward * (bounds.extents.z * offset.z);
            return true;
        }

        /// <summary>头的这块包围盒，喂蛋糕模式用来把相机推近</summary>
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

        // ---------- 中挡 / 重挡：扔蛋糕 ----------

        /// <summary>
        /// 扔一块蛋糕：出手点在"点击位置正下方 + 视线方向推出去一点"，用 flySeconds 反推初速度，
        /// 落点就是玩家点的地方，中途交给物理引擎。重挡只是把这个飞行时间给得更短（更快）。
        /// </summary>
        GameObject ThrowCake(Vector2 screenPosition, float flySeconds, int level)
        {
            if (_cakePrefab == null || _agent == null)
            {
                return null;
            }

            Collider head = _hitBoxes != null ? _hitBoxes.Head : null;
            if (head == null)
            {
                return null;
            }

            Vector3 start;
            Vector3 velocity;
            if (!TryResolveThrow(screenPosition, flySeconds, out start, out velocity))
            {
                return null;
            }

            GameObject cake = Instantiate(_cakePrefab);
            cake.name = _cakePrefab.name + " (扔出去的)";
            // 挂到女仆那一层（房子根）底下：她/房子被收走时这块蛋糕跟着走，不会留在空场景里
            if (_agent.transform.parent != null)
            {
                cake.transform.SetParent(_agent.transform.parent, true);
            }

            // cake.prefab 的实体中心离根原点约 0.75，而刚体走的是质心：直接把根原点摆到出手点，
            // 蛋糕实体就落在旁边（容易插进墙里被物理顶飞，落点也全偏）。
            // 偏移量必须在挪之前、用 Renderer 算——物理的 Collider.bounds 要等一次物理同步才更新，
            // 刚摆完就读会拿到旧值，补偿会算成两倍
            Vector3 centerOffset = ResolveCakeCenterOffset(cake);
            cake.transform.position = start - centerOffset;

            Rigidbody body = cake.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = cake.AddComponent<Rigidbody>();
            }

            body.isKinematic = false;
            body.useGravity = true;
            body.drag = 0f;
            body.angularDrag = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // 飞得快，用连续检测免得一帧跨过脸上的触发体
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            EnsureCakeCollider(cake);
            // 原来的控制器（点击用的那个胶囊）对这块蛋糕关掉，不然先撞在身体上，碰不到脸
            IgnoreCakeAgainstController(cake);

            MaidFeedCake marker = body.gameObject.GetComponent<MaidFeedCake>();
            if (marker == null)
            {
                marker = body.gameObject.AddComponent<MaidFeedCake>();
            }

            marker.Target = head;
            GameObject thrown = cake;
            marker.Hit = () => OnCakeHit(thrown, level);
            _thrownCakes.Add(cake);
            TrimThrownCakes();

            body.velocity = velocity;
            body.angularVelocity = UnityEngine.Random.onUnitSphere * (_cakeSpinDegrees * Mathf.Deg2Rad);
            return cake;
        }

        /// <summary>
        /// 算出这次扔的出手点和初速度：出手点 = 点击位置正下方的屏幕外（`_cakeStartViewportY` 是那个高度），
        /// 再沿视线方向往摄像机那边推 `_cakeStartDepth`（＝"垂直于摄像机的距离"，正交相机下屏幕位置不变，
        /// 只改变和她之间的真实距离）；目标 = 玩家点的地方（取女仆所在深度那一层）。
        /// 初速度用"飞行时间反推"算出来，蛋糕正好落到手指点的地方，中途的抛物线交给物理引擎。
        /// </summary>
        bool TryResolveThrow(Vector2 screenPosition, float flySeconds, out Vector3 start,
            out Vector3 velocity)
        {
            start = Vector3.zero;
            velocity = Vector3.zero;

            Vector3 anchor;
            if (!TryGetMouthPosition(out anchor))
            {
                return false;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                start = anchor + Vector3.down * 0.6f;
                velocity = Vector3.up * 2f;
                return true;
            }

            float depth = Vector3.Dot(anchor - camera.transform.position, camera.transform.forward);
            // 出手点往摄像机这边推一点：正交相机下屏幕位置不变，但和她之间的真实距离变长
            float startDepth = Mathf.Max(0.1f, depth - _cakeStartDepth);
            // 出手点的屏幕 x 跟着点击位置走（从手指下方扔上去），高度用 _cakeStartViewportY
            float tapX = Screen.width > 0 ? Mathf.Clamp01(screenPosition.x / Screen.width) : 0.5f;
            start = camera.ViewportToWorldPoint(new Vector3(tapX, _cakeStartViewportY, startDepth));
            Vector3 target = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y,
                Mathf.Max(0.1f, depth)));

            // 出手点先顶出墙/地板（女仆自己的碰撞体不算），再按实际出手点算方向
            start = PushOutOfGeometry(start, anchor);

            float seconds = Mathf.Max(0.05f, flySeconds);
            Vector3 gravity = Physics.gravity;
            velocity = (target - start - (0.5f * seconds * seconds) * gravity) / seconds;
            return true;
        }

        /// <summary>场上蛋糕太多就删最早的（没砸中的那些）</summary>
        void TrimThrownCakes()
        {
            int limit = Mathf.Max(1, _maxThrownCakes);
            while (_thrownCakes.Count > limit)
            {
                GameObject oldest = _thrownCakes[0];
                _thrownCakes.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest);
                }
            }
        }

        /// <summary>
        /// 出手点正好卡在墙/地板里的话，往女仆那侧挪一点再出手，
        /// 不然刚生成就被物理顶飞，看起来像坏掉了。
        /// </summary>
        Vector3 PushOutOfGeometry(Vector3 start, Vector3 towards)
        {
            const float radius = 0.22f;
            if (IsStartClear(start, radius))
            {
                return start;
            }

            // 先只往上抬：出手点压到地板/墙脚时最常见，这样不动和她之间的水平距离（还是"远"的）
            for (int i = 1; i <= 6; i++)
            {
                Vector3 lifted = start + Vector3.up * (0.12f * i);
                if (IsStartClear(lifted, radius))
                {
                    return lifted;
                }
            }

            // 还不行才往她那边挪
            Vector3 direction = towards - start;
            float distance = direction.magnitude;
            if (distance < 0.01f)
            {
                return start;
            }

            direction /= distance;
            for (int i = 1; i <= 6; i++)
            {
                Vector3 candidate = start + direction * (distance * 0.12f * i);
                if (IsStartClear(candidate, radius))
                {
                    return candidate;
                }
            }

            return start;
        }

        /// <summary>这一点周围有没有墙/地板。女仆自己的碰撞体不算，出手点本来就贴着她</summary>
        bool IsStartClear(Vector3 point, float radius)
        {
            Collider[] hits = Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null)
                {
                    continue;
                }

                if (_agent != null && hits[i].GetComponentInParent<MaidAgent>() == _agent)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// 蛋糕实体中心相对根原点的世界偏移。用 Renderer.bounds 算（立刻生效），
        /// 别用 Collider.bounds：那个要等一次物理同步，刚摆完就读是旧值。
        /// </summary>
        static Vector3 ResolveCakeCenterOffset(GameObject cake)
        {
            Renderer[] renderers = cake.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return Vector3.zero;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.center - cake.transform.position;
        }

        static void EnsureCakeCollider(GameObject cake)
        {
            if (cake.GetComponentInChildren<Collider>(true) != null)
            {
                return;
            }

            // 预制体自己没碰撞体就按渲染包围盒补一个，不然砸不到头
            Renderer[] renderers = cake.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                cake.AddComponent<BoxCollider>();
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            BoxCollider box = cake.AddComponent<BoxCollider>();
            box.center = cake.transform.InverseTransformPoint(bounds.center);
            Vector3 scale = cake.transform.lossyScale;
            box.size = new Vector3(
                SafeDivide(bounds.size.x, scale.x),
                SafeDivide(bounds.size.y, scale.y),
                SafeDivide(bounds.size.z, scale.z));
        }

        void IgnoreCakeAgainstController(GameObject cake)
        {
            CharacterController controller = _agent != null ? _agent.GetComponent<CharacterController>() : null;
            if (controller == null)
            {
                return;
            }

            Collider[] colliders = cake.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Physics.IgnoreCollision(colliders[i], controller, true);
            }
        }

        /// <summary>退出喂蛋糕模式时把还留在场景里的蛋糕（没砸中的那些）一起清掉</summary>
        void ClearThrownCakes()
        {
            for (int i = 0; i < _thrownCakes.Count; i++)
            {
                if (_thrownCakes[i] != null)
                {
                    Destroy(_thrownCakes[i]);
                }
            }

            _thrownCakes.Clear();
        }

        static float SafeDivide(float value, float scale)
        {
            return Mathf.Abs(scale) > 0.0001f ? value / Mathf.Abs(scale) : value;
        }

        // ---------- 面板 ----------

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidFeedPanel>();
            }

            // 用户自己摆的那份通常挂在底栏物体上、不在这个物体上，所以再在场景里找一遍
            if (_panel == null)
            {
                _panel = FindScenePanel();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidFeedPanel>();
            }

            _panel.ExitRequested -= End;
            _panel.ExitRequested += End;
            _panel.LevelSelected -= OnLevelSelected;
            _panel.LevelSelected += OnLevelSelected;
        }

        /// <summary>面板上换了强度（单选），记下来，等玩家点屏幕时用</summary>
        void OnLevelSelected(int level)
        {
            if (level != LevelLight && _cakePrefab == null)
            {
                return;
            }

            _selectedLevel = level;
            if (level != LevelHeavy)
            {
                // 连击只在重挡算：切走就清掉
                _comboHud.Clear();
            }
        }

        /// <summary>
        /// 场景里手摆的 MaidFeedPanel：挂在别的物体上、并且自己接了 bar。
        /// 只认唯一一份，有多份就返回 null 让用户自己在 _panel 里指定，免得挑错。
        /// </summary>
        MaidFeedPanel FindScenePanel()
        {
            MaidFeedPanel[] all = FindObjectsOfType<MaidFeedPanel>(true);
            MaidFeedPanel found = null;
            for (int i = 0; i < all.Length; i++)
            {
                MaidFeedPanel panel = all[i];
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
                Debug.Log("[喂蛋糕] 用场景里现成的底栏：" + found.name);
            }

            return found;
        }
    }
}
