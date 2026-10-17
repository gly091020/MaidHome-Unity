using System;
using System.Collections.Generic;
using MaidHome.Core.Input;
using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 玩全身：进模式后把女仆临时换成布娃娃，手指按住哪块就拖哪块。
    /// 拖动只是给被抓住的刚体一个跟随速度，其余部位靠关节和重力自然摆动；
    /// 退出时恢复骨骼姿势、角色控制器和动画。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidBodyInteraction : MonoBehaviour
    {
        public event Action Ended;

        [Header("配置（接了就覆盖下面的默认参数）")]
        [SerializeField] private MaidBodySettings _settings;

        [Header("拖拽手感")]
        [Tooltip("SpringJoint 的弹簧强度。越大跟手越硬")]
        private float _spring = 1500f;
        [Tooltip("SpringJoint 的阻尼。越大越不容易来回弹")]
        private float _damper = 100f;
        [Tooltip("弹簧求解精度，越小越精确但越费性能")]
        private float _tolerance = 0.025f;
        [Tooltip("创建 SpringJoint 后延迟几帧再启用，先让抓取点和物理位置对齐")]
        private int _springDelayFrames = 3;
        [Tooltip("射线刚好落在关节缝里时，允许在屏幕这个像素范围内兜底抓最近的一块")]
        private float _pickFallbackPixels = 24f;
        [Tooltip("兜底范围按屏幕高度再给一份，适配高 DPI 手机：0.04 ≈ 屏幕高度的 4%")]
        private float _pickFallbackScreenRatio = 0.04f;
        private float _rayDistance = 500f;
        private LayerMask _dragMask = ~0;

        [Header("布娃娃")]
        [Tooltip("玩全身时两两取消自碰撞，头发 / 手脚 / 裙子才不会互相顶")]
        private bool _ignoreAllSelfCollision = true;
        [Tooltip("关掉关节角度限制（只剩位置约束），摆起来最软。默认关闭，也就是保留 10°/15° 限制")]
        private bool _disableAngleLimits;

        [Header("体感模式")]
        [Tooltip("传感器重力大小（米/秒²）")]
        private float _sensorGravity = 9.81f;
        [Tooltip("晃动产生的线性加速度倍率")]
        private float _sensorAccelerationScale = 4f;
        [Tooltip("加速度变化速度（jerk）的额外倍率，快速甩手机时更明显")]
        private float _sensorJerkScale = 0.12f;
        [Tooltip("重力方向低通滤波速度；越大越跟手，越小越稳")]
        private float _sensorSmoothing = 6f;
        [Tooltip("小于这个加速度就当没动，避免静止时抖动")]
        private float _sensorDeadzone = 0.01f;
        [Tooltip("屏幕盒的深度 / 屏幕高度。0.7 表示向镜头前后各留 35% 屏高")]
        private float _motionDepthRatio = 0.7f;
        private float _motionWallThickness = 0.08f;

        [Header("碰撞反馈")]
        [Tooltip("布娃娃撞到东西时，冲击速度超过这个值就闪红（米/秒）")]
        private float _impactFlashSpeed = 4f;
        [Tooltip("两次闪红之间至少隔多久")]
        private float _impactFlashCooldown = 0.6f;
        [Tooltip("连续碰撞里，间隔超过这个时间才再累加一次受伤动画")]
        private float _hurtRestartCooldown = 0.15f;
        [Tooltip("刚进布娃娃后忽略碰撞的时间，避免她倒下去砸到地板也闪红")]
        private float _impactWarmupSeconds = 0.4f;
        private Color _impactFlashColor = new Color(1f, 0f, 0f, 0.5f);
        private float _impactFlashSeconds = 0.5f;
        [Tooltip("碰撞受伤语音的音量倍率")]
        private float _impactVoiceVolume = 6f;
        public bool IsActive { get; private set; }

        sealed class Grab
        {
            public int FingerId;
            public Rigidbody Body;
            public Rigidbody Anchor;
            public SpringJoint Joint;
            public Plane Plane;
            public Vector3 TargetOffset;
            public int Frames;
            public bool SpringEnabled;
        }

        readonly List<Grab> _grabs = new List<Grab>();
        readonly List<PointerInput.Pointer> _pointers = new List<PointerInput.Pointer>();
        readonly List<Collider> _pickColliders = new List<Collider>();
        readonly RaycastHit[] _hits = new RaycastHit[128];

        MaidAgent _agent;
        MaidRagdoll _ragdoll;
        MaidBodyPanel _panel;
        float _impactIgnoreUntil;
        float _nextImpactFlashAt = -100f;
        float _lastHurtAt = -100f;
        Transform _headBone;
        bool _motionMode;
        bool _sensorInitialized;
        Vector3 _sensorLowPass;
        Vector3 _sensorPrevious;
        GameObject _motionSpace;
        PhysicMaterial _motionWallMaterial;
        Camera _projectionCamera;
        bool _projectionSaved;
        bool _savedOrthographic;
        float _savedFieldOfView;
        float _savedOrthographicSize;
        float _savedNear;
        float _savedFar;

        void Awake()
        {
            MaidInteractionConfigApplier.ApplyRequired(_settings, this, this, "MaidBodyInteraction");
        }

        /// <summary>这个女仆能不能玩全身。模型里至少要能认出物理块</summary>
        public bool Supports(MaidAgent agent)
        {
            if (_settings == null || agent == null)
            {
                return false;
            }

            MaidRagdollPlan plan = MaidRagdollPlanner.Plan(agent.transform);
            return plan != null && plan.Parts.Count > 0;
        }

        public bool Begin(MaidAgent agent)
        {
            if (_settings == null || agent == null)
            {
                return false;
            }

            Abort();

            _agent = agent;
            _ragdoll = agent.GetComponent<MaidRagdoll>();
            if (_ragdoll == null)
            {
                _ragdoll = agent.gameObject.AddComponent<MaidRagdoll>();
            }

            // 进模式时明确打开「全关自碰撞」，不管场景里那份组件之前是什么设置
            MaidRagdollPlan plan = _ragdoll.PrepareForInteraction(_ignoreAllSelfCollision, _disableAngleLimits);
            if (plan == null || plan.Parts.Count == 0)
            {
                Debug.LogWarning("这个模型认不出可用的物理块，玩不了全身: " + agent.name);
                _ragdoll.Clear();
                _ragdoll = null;
                _agent = null;
                return false;
            }

            _ragdoll.Enter();
            if (!_ragdoll.IsActive)
            {
                Debug.LogWarning("布娃娃没有成功接管，玩不了全身: " + agent.name);
                _ragdoll.Clear();
                _ragdoll = null;
                _agent = null;
                return false;
            }

            _ragdoll.ImpactDetected -= OnRagdollImpact;
            _ragdoll.ImpactDetected += OnRagdollImpact;
            _impactIgnoreUntil = Time.unscaledTime + Mathf.Max(0f, _impactWarmupSeconds);
            _nextImpactFlashAt = -100f;
            _lastHurtAt = -100f;
            _headBone = null;
            for (int i = 0; i < plan.Parts.Count; i++)
            {
                if (string.Equals(plan.Parts[i].Label, "head", StringComparison.Ordinal))
                {
                    _headBone = plan.Parts[i].Bone;
                    break;
                }
            }

            EnsurePanel();
            CachePickColliders();
            if (_panel != null)
            {
                _panel.SetMotion(false);
                _panel.SetHint("");
                _panel.Show();
            }

            IsActive = true;
            return true;
        }

        /// <summary>玩家主动退出：退回去重新打开女仆面板</summary>
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
            if (!IsActive && _ragdoll == null)
            {
                return;
            }

            Release();
        }

        void OnDisable()
        {
            Abort();
        }

        void OnDestroy()
        {
            Abort();
            Ended = null;
        }

        void Release()
        {
            SetMotionMode(false);
            for (int i = 0; i < _grabs.Count; i++)
            {
                DestroyGrab(_grabs[i]);
            }

            _grabs.Clear();
            _pointers.Clear();
            _pickColliders.Clear();

            if (_panel != null)
            {
                _panel.Hide();
            }

            if (_ragdoll != null)
            {
                _ragdoll.ImpactDetected -= OnRagdollImpact;
                // Exit 会把骨骼姿势写回、把动画 / 控制器 / 游走恢复；
                // Clear 再把这次临时生成的刚体和碰撞体拆干净，下次进模式重新识别
                _ragdoll.Exit();
                _ragdoll.Clear();
            }

            _ragdoll = null;
            _agent = null;
            _headBone = null;
            _motionMode = false;
            _sensorInitialized = false;
            IsActive = false;
        }

        void FixedUpdate()
        {
            if (!IsActive || !_motionMode || _ragdoll == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 raw = ReadAcceleration();
            Vector3 previous = _sensorPrevious;
            _sensorPrevious = raw;
            if (!_sensorInitialized)
            {
                _sensorLowPass = raw;
                _sensorInitialized = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, _sensorSmoothing) * Time.fixedDeltaTime);
                _sensorLowPass = Vector3.Lerp(_sensorLowPass, raw, blend);
            }

            Vector3 linear = raw - _sensorLowPass;
            if (linear.magnitude < Mathf.Max(0f, _sensorDeadzone))
            {
                linear = Vector3.zero;
            }

            linear = Vector3.ClampMagnitude(linear, 2f);
            float deltaTime = Mathf.Max(0.001f, Time.fixedDeltaTime);
            Vector3 jerk = Vector3.ClampMagnitude((raw - previous) / deltaTime, 30f);
            Vector3 gravity = GravityFromSensor(camera, _sensorLowPass);
            Vector3 linearWorld = camera.transform.right * linear.x
                + camera.transform.up * linear.y
                + camera.transform.forward * linear.z;
            Vector3 jerkWorld = camera.transform.right * jerk.x
                + camera.transform.up * jerk.y
                + camera.transform.forward * jerk.z;
            Vector3 acceleration = linearWorld * Mathf.Max(0f, _sensorAccelerationScale)
                + jerkWorld * Mathf.Max(0f, _sensorJerkScale);
            _ragdoll.SetSensorForces(true, gravity, acceleration);
        }

        void OnRagdollImpact(float speed)
        {
            if (!IsActive || _agent == null || speed < _impactFlashSpeed)
            {
                return;
            }

            if (!MaidInjuryRules.Allows(_agent))
            {
                return;
            }

            if (Time.unscaledTime < _impactIgnoreUntil)
            {
                return;
            }

            if (Time.unscaledTime - _lastHurtAt >= Mathf.Max(0f, _hurtRestartCooldown))
            {
                _lastHurtAt = Time.unscaledTime;
                MaidHurtAnimation.Hurt(_agent, false, _headBone);
            }

            if (Time.unscaledTime - _nextImpactFlashAt < _impactFlashCooldown)
            {
                return;
            }

            _nextImpactFlashAt = Time.unscaledTime;
            MaidDamageFlash.Flash(_agent.gameObject, _impactFlashColor, _impactFlashSeconds);
            MaidAudioService.Play(_agent, MaidSoundId.Hurt, false, _impactVoiceVolume);
            MaidTears.Hurt(_agent, 1f);
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

            if (PointerInput.BackPressed)
            {
                End();
                return;
            }

            PointerInput.CopyPointers(_pointers);
            for (int i = 0; i < _pointers.Count; i++)
            {
                PointerInput.Pointer pointer = _pointers[i];
                if (pointer.Pressed)
                {
                    BeginGrab(pointer);
                }

                if (pointer.Released)
                {
                    EndGrab(pointer.FingerId);
                }
                else if (!pointer.Pressed && pointer.Held)
                {
                    UpdateGrab(pointer);
                }
            }
        }

        void BeginGrab(PointerInput.Pointer pointer)
        {
            if (pointer.OverUi || FindGrab(pointer.FingerId) != null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Rigidbody body = PickBody(camera, pointer.Position, out _);
            if (body == null || HasGrab(body))
            {
                return;
            }

            Grab grab = new Grab();
            grab.FingerId = pointer.FingerId;
            grab.Body = body;
            // 平面过被抓部位的质心，深度固定在这个部位的中心，而不是射线命中的表面点
            Vector3 center = body.worldCenterOfMass;
            grab.Plane = new Plane(-camera.transform.forward, center);
            Ray startRay = camera.ScreenPointToRay(pointer.Position);
            float startDistance;
            if (grab.Plane.Raycast(startRay, out startDistance))
            {
                // 手指不一定正压在质心投影上，记下横向偏差，保证第一帧弹簧误差为 0
                grab.TargetOffset = center - startRay.GetPoint(startDistance);
            }

            grab.Anchor = CreateAnchor(body, out grab.Joint);
            _grabs.Add(grab);
            body.WakeUp();
        }

        void UpdateGrab(PointerInput.Pointer pointer)
        {
            Grab grab = FindGrab(pointer.FingerId);
            Camera camera = Camera.main;
            if (grab == null || grab.Body == null || grab.Anchor == null || camera == null)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(pointer.Position);
            float distance;
            if (!grab.Plane.Raycast(ray, out distance))
            {
                return;
            }

            Vector3 target = ray.GetPoint(distance);
            grab.Frames++;
            if (!grab.SpringEnabled)
            {
                if (grab.Frames >= Mathf.Max(0, _springDelayFrames))
                {
                    grab.SpringEnabled = true;
                    grab.Joint.spring = Mathf.Max(0f, _spring);
                    grab.Joint.damper = Mathf.Max(0f, _damper);
                    grab.TargetOffset = grab.Body.worldCenterOfMass - target;
                }
                else
                {
                    // 延迟期间锚点贴住当前质心，弹簧还没刚度，不会拉刚体
                    grab.TargetOffset = grab.Body.worldCenterOfMass - target;
                }
            }

            grab.Anchor.position = target + grab.TargetOffset;
            grab.Body.WakeUp();
        }

        void EndGrab(int fingerId)
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                if (_grabs[i].FingerId == fingerId)
                {
                    DestroyGrab(_grabs[i]);
                    _grabs.RemoveAt(i);
                    return;
                }
            }
        }

        Grab FindGrab(int fingerId)
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

        Rigidbody CreateAnchor(Rigidbody body, out SpringJoint spring)
        {
            Vector3 center = body.worldCenterOfMass;
            GameObject anchorObject = new GameObject("MaidBodyGrabAnchor");
            Rigidbody anchor = anchorObject.AddComponent<Rigidbody>();
            anchor.isKinematic = true;
            anchor.useGravity = false;
            anchor.interpolation = RigidbodyInterpolation.Interpolate;
            anchor.position = center;

            // 关节挂到被抓刚体上，力作用在抓取点；锚点只负责带着这个点走
            spring = body.gameObject.AddComponent<SpringJoint>();
            spring.autoConfigureConnectedAnchor = false;
            spring.connectedBody = anchor;
            // centerOfMass 本身就是刚体局部坐标，不走插值后的 Transform，避免点击时沿视线弹一下
            spring.anchor = body.centerOfMass;
            spring.connectedAnchor = Vector3.zero;
            spring.spring = 0f;
            spring.damper = 0f;
            spring.tolerance = Mathf.Max(0.001f, _tolerance);
            spring.enablePreprocessing = true;
            return anchor;
        }

        void DestroyGrab(Grab grab)
        {
            if (grab == null)
            {
                return;
            }

            if (grab.Joint != null)
            {
                grab.Joint.connectedBody = null;
                Destroy(grab.Joint);
            }

            if (grab.Anchor != null)
            {
                Destroy(grab.Anchor.gameObject);
            }
        }

        bool HasGrab(Rigidbody body)
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                if (_grabs[i].Body == body)
                {
                    return true;
                }
            }

            return false;
        }

        Rigidbody PickBody(Camera camera, Vector2 screenPosition, out Vector3 point)
        {
            point = Vector3.zero;
            Ray ray = camera.ScreenPointToRay(screenPosition);
            int count = Physics.RaycastNonAlloc(ray, _hits, _rayDistance, _dragMask,
                QueryTriggerInteraction.Ignore);
            if (count <= 0)
            {
                return null;
            }

            int nearest = -1;
            float nearestDistance = float.MaxValue;
            Rigidbody nearestBody = null;
            for (int i = 0; i < count; i++)
            {
                Collider collider = _hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (!collider.transform.IsChildOf(_agent.transform))
                {
                    continue;
                }

                Rigidbody hitBody = collider.attachedRigidbody;
                if (hitBody == null)
                {
                    hitBody = collider.GetComponentInParent<Rigidbody>();
                }

                if (hitBody == null || hitBody.isKinematic || !hitBody.transform.IsChildOf(_agent.transform))
                {
                    // 女仆自己的牌子 / 面具之类没有刚体的装饰碰撞体，跳过继续往后找
                    continue;
                }

                if (IsBackgroundPart(hitBody.name))
                {
                    // 部分模型里混着 Background1 / curtain 这种大装饰网格，
                    // 抓到以后看起来像"女仆完全拖不动"；衣摆等其它块仍然允许拖。
                    continue;
                }

                if (_hits[i].distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = _hits[i].distance;
                    nearestBody = hitBody;
                }
            }

            if (nearest >= 0 && nearestBody != null)
            {
                point = _hits[nearest].point;
                return nearestBody;
            }

            return PickNearScreen(camera, screenPosition, out point);
        }

        void CachePickColliders()
        {
            _pickColliders.Clear();
            if (_agent == null)
            {
                return;
            }

            Collider[] all = _agent.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Collider collider = all[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                Rigidbody body = collider.attachedRigidbody;
                if (body == null)
                {
                    body = collider.GetComponentInParent<Rigidbody>();
                }

                if (body == null || body.isKinematic || !body.transform.IsChildOf(_agent.transform)
                    || IsBackgroundPart(body.name))
                {
                    continue;
                }

                _pickColliders.Add(collider);
            }
        }

        Rigidbody PickNearScreen(Camera camera, Vector2 screenPosition, out Vector3 point)
        {
            point = Vector3.zero;
            float bestOutside = Mathf.Max(0f, _pickFallbackPixels,
                Screen.height * Mathf.Max(0f, _pickFallbackScreenRatio));
            float bestDepth = float.MaxValue;
            Rigidbody best = null;
            for (int i = 0; i < _pickColliders.Count; i++)
            {
                Collider collider = _pickColliders[i];
                if (collider == null)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                if (!TryScreenRect(camera, bounds, out Rect rect, out float depth))
                {
                    continue;
                }

                float dx = Mathf.Max(rect.xMin - screenPosition.x, 0f, screenPosition.x - rect.xMax);
                float dy = Mathf.Max(rect.yMin - screenPosition.y, 0f, screenPosition.y - rect.yMax);
                float outside = Mathf.Sqrt(dx * dx + dy * dy);
                if (outside > bestOutside || (Mathf.Abs(outside - bestOutside) < 0.01f && depth >= bestDepth))
                {
                    continue;
                }

                Rigidbody body = collider.attachedRigidbody;
                if (body == null)
                {
                    body = collider.GetComponentInParent<Rigidbody>();
                }

                if (body != null)
                {
                    best = body;
                    bestOutside = outside;
                    bestDepth = depth;
                }
            }

            if (best != null)
            {
                point = best.worldCenterOfMass;
            }

            return best;
        }

        static bool TryScreenRect(Camera camera, Bounds bounds, out Rect rect, out float depth)
        {
            rect = new Rect();
            depth = 0f;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            depth = 0f;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 world = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 screen = camera.WorldToScreenPoint(world);
                        if (screen.z <= 0f)
                        {
                            return false;
                        }

                        minX = Mathf.Min(minX, screen.x);
                        minY = Mathf.Min(minY, screen.y);
                        maxX = Mathf.Max(maxX, screen.x);
                        maxY = Mathf.Max(maxY, screen.y);
                        depth += screen.z;
                    }
                }
            }

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            depth *= 0.125f;
            return true;
        }

        static bool IsBackgroundPart(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("background") || lower.Contains("backdrop")
                || lower.Contains("curtain") || lower.Contains("screen")
                || lower.Contains("wall");
        }

        void ToggleMotionMode()
        {
            if (!IsActive)
            {
                return;
            }

            SetMotionMode(!_motionMode);
        }

        void SetMotionMode(bool on)
        {
            if (on && !IsActive)
            {
                return;
            }

            if (on && !HasAccelerometer())
            {
                if (_panel != null)
                {
                    _panel.SetMotion(false);
                    _panel.SetHint("当前设备没有加速度计，无法开启体感");
                }

                return;
            }

            if (_motionMode == on)
            {
                if (!on)
                {
                    if (_ragdoll != null)
                    {
                        _ragdoll.SetSensorForces(false, Vector3.zero, Vector3.zero);
                    }

                    DestroyMotionSpace();
                    RestoreProjection();
                    _sensorInitialized = false;
                }

                if (_panel != null)
                {
                    _panel.SetMotion(on);
                }

                return;
            }

            _motionMode = on;
            if (on)
            {
                Camera camera = Camera.main;
                if (camera == null)
                {
                    _motionMode = false;
                    return;
                }

                SaveProjection(camera);
                if (camera.orthographic)
                {
                    float distance = DistanceToAgent(camera);
                    float fov = 2f * Mathf.Atan(camera.orthographicSize / Mathf.Max(0.1f, distance)) * Mathf.Rad2Deg;
                    camera.orthographic = false;
                    camera.fieldOfView = Mathf.Clamp(fov, 5f, 120f);
                }

                BuildMotionSpace(camera);
                _sensorLowPass = ReadAcceleration();
                _sensorPrevious = _sensorLowPass;
                _sensorInitialized = true;
                if (_ragdoll != null)
                {
                    _ragdoll.SetSensorForces(true, GravityFromSensor(camera, _sensorLowPass), Vector3.zero);
                }

                if (_panel != null)
                {
                    _panel.SetMotion(true);
                    _panel.SetHint("体感开启：倾斜手机改变重力，晃动会给布娃娃加速度");
                }

                return;
            }

            if (_ragdoll != null)
            {
                _ragdoll.SetSensorForces(false, Vector3.zero, Vector3.zero);
            }

            DestroyMotionSpace();
            RestoreProjection();
            _sensorInitialized = false;
            if (_panel != null)
            {
                _panel.SetMotion(false);
                _panel.SetHint("");
            }
        }

        bool HasAccelerometer()
        {
            return PointerInput.SupportsAccelerometer || PointerInput.Acceleration.sqrMagnitude > 0.0001f;
        }

        Vector3 ReadAcceleration()
        {
            Vector3 raw = PointerInput.Acceleration;
            if (raw.sqrMagnitude < 0.0001f)
            {
                return Vector3.down;
            }

            switch (Screen.orientation)
            {
                case ScreenOrientation.PortraitUpsideDown:
                    return new Vector3(-raw.x, -raw.y, raw.z);
                case ScreenOrientation.LandscapeLeft:
                    return new Vector3(-raw.y, raw.x, raw.z);
                case ScreenOrientation.LandscapeRight:
                    return new Vector3(raw.y, -raw.x, raw.z);
                default:
                    return raw;
            }
        }

        Vector3 GravityFromSensor(Camera camera, Vector3 sensor)
        {
            Vector3 direction = camera.transform.right * sensor.x
                + camera.transform.up * sensor.y
                + camera.transform.forward * sensor.z;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = -camera.transform.up;
            }

            return direction.normalized * Mathf.Max(0.1f, _sensorGravity);
        }

        float DistanceToAgent(Camera camera)
        {
            if (_agent == null || camera == null)
            {
                return 2f;
            }

            return Mathf.Max(0.5f, Vector3.Dot(_agent.GetBounds().center - camera.transform.position,
                camera.transform.forward));
        }

        void SaveProjection(Camera camera)
        {
            if (camera == null || _projectionSaved)
            {
                return;
            }

            _projectionCamera = camera;
            _savedOrthographic = camera.orthographic;
            _savedFieldOfView = camera.fieldOfView;
            _savedOrthographicSize = camera.orthographicSize;
            _savedNear = camera.nearClipPlane;
            _savedFar = camera.farClipPlane;
            _projectionSaved = true;
        }

        void RestoreProjection()
        {
            if (!_projectionSaved)
            {
                return;
            }

            if (_projectionCamera != null)
            {
                _projectionCamera.orthographic = _savedOrthographic;
                _projectionCamera.fieldOfView = _savedFieldOfView;
                _projectionCamera.orthographicSize = _savedOrthographicSize;
                _projectionCamera.nearClipPlane = _savedNear;
                _projectionCamera.farClipPlane = _savedFar;
            }

            _projectionCamera = null;
            _projectionSaved = false;
        }

        void BuildMotionSpace(Camera camera)
        {
            DestroyMotionSpace();
            if (_agent == null || camera == null)
            {
                return;
            }

            float distance = DistanceToAgent(camera);

            float height;
            if (camera.orthographic)
            {
                height = camera.orthographicSize * 2f;
            }
            else
            {
                height = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }

            float width = height * Mathf.Max(0.1f, camera.aspect);
            float depth = Mathf.Max(0.6f, height * Mathf.Clamp(_motionDepthRatio, 0.2f, 3f));
            Vector3 center = camera.transform.position + camera.transform.forward * distance;

            _motionSpace = new GameObject("MaidBodyMotionSpace");
            _motionSpace.transform.SetPositionAndRotation(center, camera.transform.rotation);
            _motionWallMaterial = new PhysicMaterial("MaidBodyMotionWall");
            _motionWallMaterial.dynamicFriction = 0.4f;
            _motionWallMaterial.staticFriction = 0.4f;
            _motionWallMaterial.bounciness = 0f;

            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            // 后墙不能跑到相机后面：深度最多到相机前 near 处
            float halfDepth = Mathf.Min(depth * 0.5f,
                Mathf.Max(0.2f, distance - camera.nearClipPlane - 0.15f));
            depth = halfDepth * 2f;
            float thickness = Mathf.Max(0.02f, _motionWallThickness);
            AddMotionWall("Left", new Vector3(-halfWidth - thickness * 0.5f, 0f, 0f),
                new Vector3(thickness, height + thickness * 2f, depth + thickness * 2f));
            AddMotionWall("Right", new Vector3(halfWidth + thickness * 0.5f, 0f, 0f),
                new Vector3(thickness, height + thickness * 2f, depth + thickness * 2f));
            AddMotionWall("Bottom", new Vector3(0f, -halfHeight - thickness * 0.5f, 0f),
                new Vector3(width + thickness * 2f, thickness, depth + thickness * 2f));
            AddMotionWall("Top", new Vector3(0f, halfHeight + thickness * 0.5f, 0f),
                new Vector3(width + thickness * 2f, thickness, depth + thickness * 2f));
            AddMotionWall("Back", new Vector3(0f, 0f, -halfDepth - thickness * 0.5f),
                new Vector3(width + thickness * 2f, height + thickness * 2f, thickness));
            AddMotionWall("Front", new Vector3(0f, 0f, halfDepth + thickness * 0.5f),
                new Vector3(width + thickness * 2f, height + thickness * 2f, thickness));
        }

        void AddMotionWall(string name, Vector3 localPosition, Vector3 size)
        {
            if (_motionSpace == null)
            {
                return;
            }

            GameObject wall = new GameObject(name);
            wall.transform.SetParent(_motionSpace.transform, false);
            wall.transform.localPosition = localPosition;
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = size;
            collider.sharedMaterial = _motionWallMaterial;
        }

        void DestroyMotionSpace()
        {
            if (_motionSpace != null)
            {
                Destroy(_motionSpace);
                _motionSpace = null;
            }

            if (_motionWallMaterial != null)
            {
                Destroy(_motionWallMaterial);
                _motionWallMaterial = null;
            }
        }

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidBodyPanel>();
            }

            if (_panel == null)
            {
                _panel = FindScenePanel();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidBodyPanel>();
            }

            _panel.ExitRequested -= End;
            _panel.ExitRequested += End;
            _panel.MotionToggleRequested -= ToggleMotionMode;
            _panel.MotionToggleRequested += ToggleMotionMode;
        }

        /// <summary>场景里手摆的 MaidBodyPanel（挂在别的物体上、并且自己接了 bar）</summary>
        MaidBodyPanel FindScenePanel()
        {
            MaidBodyPanel[] all = FindObjectsOfType<MaidBodyPanel>(true);
            MaidBodyPanel found = null;
            for (int i = 0; i < all.Length; i++)
            {
                MaidBodyPanel panel = all[i];
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

            return found;
        }
    }
}
