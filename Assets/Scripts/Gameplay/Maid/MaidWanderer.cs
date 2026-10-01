using MaidHome.Gameplay.Audio;
using MaidHome.Gameplay.House;
using MaidHome.Interop.Bedrock;
using MaidHome.Interop.House;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 随机走动：原地待机几秒 → 在活动范围里挑一个目标点走过去 → 到了再待机。
    /// 模型转换后朝 +Z，所以转向目标方向就是转向移动方向；移动用 CharacterController，不用刚体。
    /// 走路动画的播放速率按实际速度算，避免脚底打滑。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(BedrockAnimationPlayer))]
    public sealed class MaidWanderer : MonoBehaviour
    {
        enum State
        {
            Idle,
            Walk,
        }

        [Header("动画名")]
        [SerializeField] private string _idleClip = "idle";
        [SerializeField] private string _walkClip = "walk";
        [SerializeField] private string _walkFallbackClip = "run";

        [Header("移动")]
        [SerializeField] private float _moveSpeed = 1.8f;
        [Tooltip("一个走路动画循环前进的距离（格），用来算播放速率")]
        [SerializeField] private float _stridePerCycle = 2.4f;
        [SerializeField] private float _turnSpeed = 360f;
        [SerializeField] private float _arriveDistance = 0.25f;
        [SerializeField] private float _walkTimeoutSeconds = 20f;

        [Header("待机")]
        [SerializeField] private float _idleMinSeconds = 2f;
        [SerializeField] private float _idleMaxSeconds = 6f;

        [Header("语音")]
        [SerializeField] private float _idleVoiceMinDelay = 5f;
        [SerializeField] private float _idleVoiceMaxDelay = 12f;

        [Header("卡住判定")]
        [SerializeField] private float _stuckCheckSeconds = 1.5f;
        [SerializeField] private float _stuckDistance = 0.15f;

        [Header("活动范围")]
        [SerializeField] private WanderArea _area = new WanderArea();

        readonly System.Random _rng = new System.Random();

        CharacterController _controller;
        BedrockAnimationPlayer _player;
        Animation _animation;
        MaidSimpleBedrockAnimator _simpleBedrock;
        string _resolvedWalkClip;
        string _currentClip;

        HouseNavMesh _houseNav;
        MaidNavigator _navigator;
        MaidAgent _agent;
        float _idleTime;
        float _nextIdleVoiceAt;

        State _state = State.Idle;
        float _timer;
        Vector3 _target;
        bool _hasTarget;
        float _stuckTimer;
        float _travelled;
        float _nextNavigateWarning;
        float _nextStuckLog;
        bool _paused;

        public WanderArea Area
        {
            get { return _area; }
        }

        public bool Paused
        {
            get { return _paused; }
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _player = GetComponent<BedrockAnimationPlayer>();
            _animation = GetComponent<Animation>();
            _simpleBedrock = GetComponent<MaidSimpleBedrockAnimator>();

            // 活动范围以落脚点为中心
            _area.Center = transform.position;

            if (_simpleBedrock == null)
            {
                _resolvedWalkClip = ResolveWalkClip();
            }

            _nextIdleVoiceAt = Random.Range(_idleVoiceMinDelay, _idleVoiceMaxDelay);
            EnterIdle();
        }

        void Start()
        {
            // 手动挂组件、Awake 时还没 SetClips 的情况，这里再找一次
            if (_simpleBedrock == null && string.IsNullOrEmpty(_resolvedWalkClip))
            {
                _resolvedWalkClip = ResolveWalkClip();
            }
        }

        void OnDisable()
        {
            EnsureSimpleBedrock();
            if (_simpleBedrock != null)
            {
                _simpleBedrock.SetMoving(false);
            }
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            if (_paused)
            {
                _controller.SimpleMove(Vector3.zero);
                return;
            }

            if (_state == State.Idle)
            {
                // 待机时也调一次 SimpleMove，让重力继续作用（生成时可能还在半空）
                _controller.SimpleMove(Vector3.zero);

                _idleTime += deltaTime;
                if (_idleTime >= _nextIdleVoiceAt)
                {
                    PlayIdleVoice();
                }

                _timer -= deltaTime;
                if (_timer <= 0f)
                {
                    TryStartWalk();
                }

                return;
            }

            UpdateWalk(deltaTime);
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused)
            {
                return;
            }

            _paused = paused;
            EnterIdle();
        }

        void EnterIdle()
        {
            _state = State.Idle;
            // 至少等 0.1 秒：两个字段要是都被设成 0，会变成每帧 Idle→Walk 来回切，
            // 动画被不停重启，看起来就是"卡在那儿抖"
            _timer = Mathf.Max(0.1f, Random.Range(_idleMinSeconds, _idleMaxSeconds));
            _hasTarget = false;
            if (_navigator != null)
            {
                _navigator.Clear();
            }

            PlayIdle();
        }

        void PlayIdleVoice()
        {
            if (_agent == null)
            {
                _agent = GetComponent<MaidAgent>();
            }

            if (_agent != null)
            {
                MaidAudioService.Play(_agent, MaidSoundId.Idle);
            }

            _idleTime = 0f;
            _nextIdleVoiceAt = Random.Range(_idleVoiceMinDelay, _idleVoiceMaxDelay);
        }

        void TryStartWalk()
        {
            if (!TryPickTarget())
            {
                // 挑不到目标就再等一轮，别原地打转
                EnterIdle();
                return;
            }

            _state = State.Walk;
            _timer = _walkTimeoutSeconds;
            _stuckTimer = 0f;
            _travelled = 0f;
            PlayWalk();
        }

        /// <summary>有房子的格表就按格寻路，没有就退回活动范围里直走。</summary>
        bool TryPickTarget()
        {
            AttachHouseIfAny();

            if (_navigator != null)
            {
                // 换了新路径，上一个目标作废
                _hasTarget = false;

                // 随机采到的点可能连不过去（被家具隔开），多试几次
                for (int i = 0; i < 8; i++)
                {
                    Vector3 destination;
                    if (!_navigator.TryPickRandomDestination(transform.position, _area.MinDistance, _rng, 16, out destination))
                    {
                        continue;
                    }

                    if (_navigator.SetDestination(transform.position, destination))
                    {
                        return true;
                    }
                }

                WarnCannotNavigate();
                return false;
            }

            Vector3 point;
            if (!_area.TryPick(transform.position, _rng, 8, out point))
            {
                return false;
            }

            _target = point;
            _hasTarget = true;
            return true;
        }

        void WarnCannotNavigate()
        {
            if (Time.unscaledTime < _nextNavigateWarning)
            {
                return;
            }

            _nextNavigateWarning = Time.unscaledTime + 10f;
            Debug.LogWarning(name + " 找不到能走到的目标（NavMesh 没烘出来，或者目标都不可达）", this);
        }

        /// <summary>场景里有烘好 NavMesh 的房子就接上。房子是异步加载的，所以每次找目标时都探一下。</summary>
        void AttachHouseIfAny()
        {
            if (_navigator != null)
            {
                return;
            }

            HouseNavMesh nav = FindObjectOfType<HouseNavMesh>();
            if (nav != null && nav.IsBuilt)
            {
                SetHouse(nav);
            }
        }

        /// <summary>接上房子的 NavMesh，顺便把出生点吸附到网上。</summary>
        public void SetHouse(HouseNavMesh nav)
        {
            if (nav == null || !nav.IsBuilt)
            {
                return;
            }

            _houseNav = nav;
            _navigator = new MaidNavigator(nav);

            Vector3 snapped;
            if (_navigator.TrySnap(transform.position, 8f, out snapped))
            {
                // 直接改 transform 会让 CharacterController 的内部状态跟不上
                _controller.enabled = false;
                transform.position = snapped;
                _controller.enabled = true;
            }
            else
            {
                Debug.LogWarning(name + " 附近 8 格内没有 NavMesh 可以落脚", this);
            }
        }

        void UpdateWalk(float deltaTime)
        {
            if (!TryTakeNextTarget())
            {
                EnterIdle();
                return;
            }

            Vector3 toTarget = _target - transform.position;
            toTarget.y = 0f;
            Vector3 moveDirection = toTarget.normalized;

            // 转身只负责好看，移动方向用"到目标的方向"。
            // 早先是沿 transform.forward 走，拐弯时会画弧线去切墙角——撞墙多半是这个。
            Quaternion look = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * deltaTime);

            Vector3 before = transform.position;
            _controller.SimpleMove(moveDirection * _moveSpeed);

            Vector3 moved = transform.position - before;
            moved.y = 0f;
            _travelled += moved.magnitude;

            _stuckTimer += deltaTime;
            if (_stuckTimer >= _stuckCheckSeconds)
            {
                if (_travelled < _stuckDistance)
                {
                    // 被挡住了，换个目标
                    if (Time.unscaledTime >= _nextStuckLog)
                    {
                        _nextStuckLog = Time.unscaledTime + 5f;
                        Debug.LogWarning(name + " 被挡住了，换个目标", this);
                    }

                    EnterIdle();
                    return;
                }

                _stuckTimer = 0f;
                _travelled = 0f;
            }

            _timer -= deltaTime;
            if (_timer <= 0f)
            {
                EnterIdle();
            }
        }

        /// <summary>取下一个要走的点。走格路径时会把已经贴脸的点直接跳过。</summary>
        bool TryTakeNextTarget()
        {
            if (_navigator != null)
            {
                // 当前这个拐点还没走到就继续走。这里是关键：拐点走到才消费，
                // 不能一取出来就 Advance —— 那样每个拐点只会被走一帧，表现为"抖一下停住"。
                if (_hasTarget && HorizontalDistance(_target) > _arriveDistance)
                {
                    return true;
                }

                while (_navigator.HasWaypoint)
                {
                    Vector3 point = _navigator.CurrentWaypoint;
                    _navigator.Advance();
                    if (HorizontalDistance(point) <= _arriveDistance)
                    {
                        continue;
                    }

                    _target = point;
                    _hasTarget = true;
                    return true;
                }

                _hasTarget = false;
                return false;
            }

            if (_hasTarget && HorizontalDistance(_target) > _arriveDistance)
            {
                return true;
            }

            _hasTarget = false;
            return false;
        }

        float HorizontalDistance(Vector3 point)
        {
            Vector3 delta = point - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        void PlayIdle()
        {
            EnsureSimpleBedrock();
            if (_simpleBedrock != null)
            {
                _simpleBedrock.SetMoving(false);
                _currentClip = null;
                return;
            }

            // 已经在播就不重启：idle 是 ClampForever，反复 Play 会把它一直摁在开头
            if (!string.IsNullOrEmpty(_idleClip) && _currentClip != _idleClip)
            {
                if (_player.Play(_idleClip))
                {
                    _currentClip = _idleClip;
                }
            }
        }

        void PlayWalk()
        {
            EnsureSimpleBedrock();
            if (_simpleBedrock != null)
            {
                _simpleBedrock.SetMoving(true);
                _currentClip = null;
                return;
            }

            if (string.IsNullOrEmpty(_resolvedWalkClip))
            {
                return;
            }

            if (_currentClip != _resolvedWalkClip)
            {
                if (!_player.Play(_resolvedWalkClip))
                {
                    return;
                }

                _currentClip = _resolvedWalkClip;
            }

            AnimationState state = _animation[_resolvedWalkClip];
            if (state != null && state.clip != null
                && state.clip.length > 0.0001f && _stridePerCycle > 0.0001f)
            {
                // 播放速率 = 实际速度 / 动画自然速度（一个循环走多远 / 循环多长）
                state.speed = _moveSpeed * state.clip.length / _stridePerCycle;
            }
        }

        string ResolveWalkClip()
        {
            if (!string.IsNullOrEmpty(_walkClip) && _player.HasClip(_walkClip))
            {
                return _walkClip;
            }

            if (!string.IsNullOrEmpty(_walkFallbackClip) && _player.HasClip(_walkFallbackClip))
            {
                return _walkFallbackClip;
            }

            Debug.LogWarning(name + " 没有走路动画，只会平移", this);
            return null;
        }

        void EnsureSimpleBedrock()
        {
            if (_simpleBedrock == null)
            {
                _simpleBedrock = GetComponent<MaidSimpleBedrockAnimator>();
            }
        }
    }
}
