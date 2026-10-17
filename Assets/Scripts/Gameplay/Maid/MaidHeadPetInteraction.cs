using System;
using System.Collections.Generic;
using MaidHome.Core.Input;
using MaidHome.Core.Settings;
using MaidHome.Gameplay.Audio;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸摸模式：直接拖动手指判定。
    /// 左右模式：横着来回拖，头跟着轻轻侧倒；竖直模式：从按下点往下拖，头被压扁并播放配置里的声音。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidHeadPetInteraction : MonoBehaviour
    {
        public enum PetMode
        {
            Rub,
            Press
        }

        public event Action Ended;

        [SerializeField] private MaidHeadPetConfig _config;
        [SerializeField] private MaidHeadPetPanel _panel;

        public bool IsActive { get; private set; }

        public PetMode Mode
        {
            get { return _mode; }
        }

        readonly List<PointerInput.Pointer> _pointers = new List<PointerInput.Pointer>();

        MaidAgent _agent;
        MaidFaceRig _rig;
        MaidHeadPetConfig _cfg;
        Camera _camera;
        Transform _head;
        Transform _headParent;
        int _headSibling;
        Transform _pivot;
        Vector3 _pivotBaseLocalPosition;
        Quaternion _pivotBaseLocalRotation = Quaternion.identity;
        Vector3 _restHeadScale = Vector3.one;
        Vector3[] _headLocalCorners;
        Vector3[] _headMeshLocalCorners;
        float _headWorldHeight = 0.3f;
        AudioSource _audio;
        bool _audioCreated;
        BedrockAnimationPlayer _player;
        MaidWanderer _wanderer;
        bool _petClipPlayed;
        float _petReactionEndsAt = -1f;
        bool _reactionPlaying;
        bool _reactionPlayed;
        float _reactionEndsAt;
        readonly List<BedrockBoneTrack> _wuyuTracks = new List<BedrockBoneTrack>();
        readonly List<Transform> _wuyuBones = new List<Transform>();
        Vector3[] _wuyuBasePositions;
        Quaternion[] _wuyuBaseRotations;
        Vector3[] _wuyuBaseScales;
        bool _wuyuActive;
        float _wuyuWeight;
        readonly List<BedrockBoneTrack> _dizzyTracks = new List<BedrockBoneTrack>();
        readonly List<Transform> _dizzyBones = new List<Transform>();
        Vector3[] _dizzyBasePositions;
        Quaternion[] _dizzyBaseRotations;
        Vector3[] _dizzyBaseScales;
        float _dizzyTime;
        Transform _surpriseMouth;
        Vector3 _surpriseMouthBaseScale;
        Transform[] _otherMouths;
        Vector3[] _otherMouthBaseScales;
        bool _reactionMouthApplied;

        PetMode _mode = PetMode.Rub;
        int _fingerId = -1;
        bool _dragging;
        Vector2 _dragOrigin;
        Vector2 _lastScreen;
        Vector2 _pointerScreen;
        float _press;
        float _pressTarget;
        bool _pressSoundPlayed;
        float _pressHeld;
        bool _pressHurtTriggered;
        bool _dizzinessHeld;
        MaidHeadStars _stars;
        float _lastNormalLineAt = -100f;
        float _rubDistance;
        float _rubYaw;
        float _rubRoll;
        float _rubYawTarget;
        float _rubRollTarget;
        int _squashAxis = -1;
        float _heat;
        bool _fireLit;
        float _fireEndsAt;
        GameObject _fireInstance;
        Transform _hairRoot;
        Renderer[] _burnRenderers;
        MaterialPropertyBlock[] _burnOriginalBlocks;
        MaterialPropertyBlock[] _burnApplyBlocks;
        Color[] _burnOriginalColors;
        bool[] _burnHasOverlay;
        float _burnProgress;
        bool _burnCached;

        void Awake()
        {
            if (_config == null)
            {
                Debug.LogError("MaidHeadPetInteraction 没有挂 MaidHeadPetConfig，"
                    + "请把 Assets/Resources/Settings/MaidHeadPetConfig 拖到 Config 字段", this);
            }
        }

        public bool Supports(MaidAgent agent)
        {
            if (_config == null || agent == null)
            {
                return false;
            }

            MaidFaceRig rig = MaidFaceRig.Build(agent.transform);
            return rig.Supports;
        }

        public bool Begin(MaidAgent agent)
        {
            if (_config == null)
            {
                Debug.LogError("MaidHeadPetInteraction 没有挂 MaidHeadPetConfig，"
                    + "请把 Assets/Resources/Settings/MaidHeadPetConfig 拖到 Config 字段", this);
                return false;
            }

            if (agent == null)
            {
                return false;
            }

            Abort();

            _rig = MaidFaceRig.Build(agent.transform);
            if (!_rig.Supports)
            {
                Debug.LogWarning("这个模型没找到头骨骼，摸摸模式用不了: " + agent.name);
                return false;
            }

            _camera = Camera.main;
            if (_camera == null)
            {
                Debug.LogWarning("摸摸模式需要主相机", this);
                return false;
            }

            _cfg = _config;

            _agent = agent;
            _head = _rig.Head;
            _player = agent.GetComponent<BedrockAnimationPlayer>();
            _wanderer = agent.Wanderer;
            CacheWuyuBase();
            CacheDizzinessBase();
            CacheReactionMouth();
            WrapHead();
            CaptureHeadGeometry();
            CaptureHeadMeshGeometry();
            EnsurePanel();
            EnsureAudio();
            BeginMaidPetAnimation();

            _mode = PetMode.Rub;
            _fingerId = -1;
            _dragging = false;
            _press = 0f;
            _pressTarget = 0f;
            _pressSoundPlayed = false;
            _pressHeld = 0f;
            _pressHurtTriggered = false;
            _dizzinessHeld = false;
            _stars = null;
            _lastNormalLineAt = -100f;
            _rubDistance = 0f;
            _rubYaw = 0f;
            _rubRoll = 0f;
            _rubYawTarget = 0f;
            _rubRollTarget = 0f;
            _dragOrigin = Vector2.zero;
            _pointerScreen = Vector2.zero;
            _lastScreen = Vector2.zero;
            _squashAxis = -1;
            _heat = 0f;
            _fireLit = false;
            _fireEndsAt = 0f;
            _fireInstance = null;
            _hairRoot = null;
            _reactionPlaying = false;
            _reactionPlayed = false;
            _reactionEndsAt = 0f;
            _wuyuActive = false;
            _wuyuWeight = 0f;
            _reactionMouthApplied = false;

            IsActive = true;
            if (_panel != null)
            {
                _panel.SetMode(false);
                _panel.Show();
                _panel.SetHeat(0f);
            }

            return true;
        }

        /// <summary>玩家自己退出：回到女仆面板</summary>
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

        /// <summary>外层关界面：不回调</summary>
        public void Abort()
        {
            Release();
        }

        void OnDestroy()
        {
            if (_panel != null)
            {
                _panel.ExitRequested -= End;
                _panel.RubModeRequested -= SelectRub;
                _panel.PressModeRequested -= SelectPress;
            }

            Release();
            Ended = null;
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

            if (_petReactionEndsAt > 0f && Time.unscaledTime >= _petReactionEndsAt)
            {
                _petReactionEndsAt = -1f;
                PlayPetHold();
            }

            if (_reactionPlaying && Time.unscaledTime >= _reactionEndsAt)
            {
                if (_fireLit)
                {
                    // 火还在烧：继续演 !??!，别停在中途
                    PlayBurnReaction();
                }
                else
                {
                    _reactionPlaying = false;
                    if (_wanderer != null)
                    {
                        _wanderer.ReplayAnimation();
                    }
                }
            }

            PointerInput.CopyPointers(_pointers);
            PointerInput.Pointer active = default(PointerInput.Pointer);
            bool hasActive = false;
            for (int i = 0; i < _pointers.Count; i++)
            {
                if (_pointers[i].FingerId == _fingerId)
                {
                    active = _pointers[i];
                    hasActive = true;
                    break;
                }
            }

            if (_fingerId >= 0 && !hasActive)
            {
                // 系统把手指吞掉时 PointerInput 会补 Released；这里再兜一层
                _fingerId = -1;
                _dragging = false;
            }

            if (_fingerId < 0)
            {
                for (int i = 0; i < _pointers.Count; i++)
                {
                    PointerInput.Pointer pointer = _pointers[i];
                    if (!pointer.Pressed || pointer.OverUi)
                    {
                        continue;
                    }

                    // 左右摸必须从头顶起手；按一按不限制起手位置
                    if (_mode == PetMode.Rub && !IsOnHeadTop(pointer.Position))
                    {
                        continue;
                    }

                    _fingerId = pointer.FingerId;
                    _dragging = true;
                    _dragOrigin = pointer.Position;
                    _lastScreen = pointer.Position;
                    _pointerScreen = pointer.Position;
                    active = pointer;
                    hasActive = true;
                    break;
                }
            }

            if (hasActive && _dragging)
            {
                Vector2 position = active.Position;
                Vector2 delta = position - _lastScreen;
                _lastScreen = position;
                _pointerScreen = position;
                if (_mode == PetMode.Rub)
                {
                    TrackRub(delta);
                }

                if (active.Released)
                {
                    _fingerId = -1;
                    _dragging = false;
                }
            }

            UpdateTargets();
            UpdatePressHurt();
            UpdateHeatAndFire();
        }

        void LateUpdate()
        {
            if (!IsActive)
            {
                return;
            }

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            float pressK = 1f - Mathf.Exp(-Mathf.Max(0.01f, _cfg.PressSmooth) * dt);
            float rubK = 1f - Mathf.Exp(-Mathf.Max(0.01f, _cfg.RubSmooth) * dt);
            _press = Mathf.Lerp(_press, _pressTarget, pressK);
            _rubYaw = Mathf.LerpAngle(_rubYaw, _rubYawTarget, rubK);
            _rubRoll = Mathf.LerpAngle(_rubRoll, _rubRollTarget, rubK);

            ApplyHead();
            ApplyWuyu(dt);
            ApplyDizziness(dt);
        }

        public bool TryGetHeadBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_head == null || _headLocalCorners == null || _headLocalCorners.Length < 8)
            {
                return false;
            }

            bounds = BoundsFromLocalCorners();
            return true;
        }

        void TrackRub(Vector2 delta)
        {
            float dx = delta.x;
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            float speed = Mathf.Abs(dx) / Mathf.Max(1f, Screen.height) / dt;

            if (!_fireLit)
            {
                if (speed >= _cfg.RubIgniteSpeed)
                {
                    _heat = Mathf.Min(1f, _heat + _cfg.RubHeatRate * dt);
                }
                else
                {
                    _heat = Mathf.Max(0f, _heat - _cfg.RubHeatDecay * dt);
                }

                if (_heat >= 1f)
                {
                    Ignite();
                }
            }

            if (Mathf.Abs(dx) < 0.01f)
            {
                return;
            }

            float step = Mathf.Max(0.005f, _cfg.RubStep);
            _rubDistance += Mathf.Abs(dx) / Mathf.Max(1f, Screen.height);
            if (_rubDistance < step)
            {
                return;
            }

            _rubDistance = 0f;
            PlayClip(_cfg.RubClip, _cfg.RubVolume);
            PlayNormalLine(_cfg.RubLines);
        }

        void UpdateHeatAndFire()
        {
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            if (!_fireLit && (_mode != PetMode.Rub || !_dragging))
            {
                _heat = Mathf.Max(0f, _heat - _cfg.RubHeatDecay * dt);
            }

            if (_fireLit && Time.unscaledTime >= _fireEndsAt)
            {
                ResolveFire();
            }

            if (_fireLit && _burnCached)
            {
                float total = Mathf.Max(0.1f, _cfg.FireSeconds);
                float progress = Mathf.Clamp01(1f - (_fireEndsAt - Time.unscaledTime) / total);
                if (progress > _burnProgress)
                {
                    _burnProgress = progress;
                    ApplyBurn(_burnProgress);
                }
            }

            if (_panel != null)
            {
                _panel.SetHeat(_fireLit ? 1f : _heat);
            }
        }

        void Ignite()
        {
            _fireLit = true;
            _heat = 1f;
            _fireEndsAt = Time.unscaledTime + Mathf.Max(0.1f, _cfg.FireSeconds);
            // 着火反应直接接管，别再被摸头的 pet_reaction_hold 定时器顶回去
            _petReactionEndsAt = -1f;
            _hairRoot = FindHairRoot();
            if (!_burnCached)
            {
                CacheBurnTargets();
            }
            _reactionPlayed = false;
            PlayBurnReaction();
            SpawnFire();
        }

        void SpawnFire()
        {
            if (_cfg.FirePrefab == null)
            {
                Debug.LogWarning("摸摸模式没有火焰预制体，搓着了但不会显示火", this);
                return;
            }

            Transform parent = _head;
            if (parent == null)
            {
                return;
            }

            _fireInstance = Instantiate(_cfg.FirePrefab, parent);
            _fireInstance.name = "MaidHeadPetFire (Runtime)";
            // 挂在 Head 下面，局部旋转归零 = 跟着女仆/头部一起转
            _fireInstance.transform.localRotation = Quaternion.identity;
            _fireInstance.transform.localScale = Vector3.one * Mathf.Max(0.01f, _cfg.FireScale);

            // 先放到 Head 顶部，再按火焰自己的包围盒把底面对齐到头顶中心
            Vector3 top = HeadTopWorld();
            _fireInstance.transform.position = top;
            Bounds fireBounds;
            if (TryFireBounds(_fireInstance, out fireBounds))
            {
                Vector3 up = _head.TransformDirection(Axis(SquashAxis())).normalized;
                Vector3 planar = top - fireBounds.center;
                planar -= Vector3.Project(planar, up);
                float minDot = float.MaxValue;
                Vector3 min = fireBounds.min;
                Vector3 max = fireBounds.max;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 corner = new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z);
                            minDot = Mathf.Min(minDot, Vector3.Dot(corner, up));
                        }
                    }
                }

                _fireInstance.transform.position += planar
                    + up * (Vector3.Dot(top, up) - minDot);
            }

            _fireInstance.transform.position += _camera.transform.right * _cfg.FireCameraOffset.x
                + _camera.transform.up * _cfg.FireCameraOffset.y
                + _camera.transform.forward * _cfg.FireCameraOffset.z;
        }

        void ResolveFire()
        {
            bool hadHair = _hairRoot != null;
            if (_reactionPlaying)
            {
                _reactionPlaying = false;
            }

            RestoreReactionMouth();
            // 先回到 idle；wuyu 只盖眼睛/眉毛，嘴巴和身体继续由 idle 驱动
            if (!_dizzinessHeld && _wanderer != null)
            {
                _wanderer.ReplayAnimation();
            }

            _fireLit = false;
            _heat = 0f;
            _fireEndsAt = 0f;

            if (_burnCached)
            {
                _burnProgress = 1f;
                ApplyBurn(1f);
            }

            // 先隐藏，再留一点时间让残影消失，最后删掉
            if (_fireInstance != null)
            {
                _fireInstance.SetActive(false);
                Destroy(_fireInstance, 0.6f);
                _fireInstance = null;
            }

            PlayClip(_cfg.FireEndClip, _cfg.FireEndVolume, false);

            if (hadHair && _agent != null && _cfg.FireLines != null && _cfg.FireLines.Length > 0)
            {
                string line = _cfg.FireLines[UnityEngine.Random.Range(0, _cfg.FireLines.Length)];
                MaidChatBubble.Show(_agent, line, _cfg.FireLineSeconds);
            }

            _hairRoot = null;
            if (_panel != null)
            {
                _panel.SetHeat(0f);
            }

            if (!_dizzinessHeld && hadHair && !_wuyuActive && _wuyuBones.Count > 0)
            {
                _wuyuActive = true;
                _wuyuWeight = 0f;
            }
        }

        void DestroyFire()
        {
            if (_fireInstance != null)
            {
                Destroy(_fireInstance);
                _fireInstance = null;
            }

            _fireLit = false;
            _heat = 0f;
            _fireEndsAt = 0f;
            _hairRoot = null;
        }

        void CacheBurnTargets()
        {
            RestoreBurn();
            if (_hairRoot == null)
            {
                return;
            }

            List<Renderer> renderers = new List<Renderer>();
            List<MaterialPropertyBlock> originals = new List<MaterialPropertyBlock>();
            List<MaterialPropertyBlock> applies = new List<MaterialPropertyBlock>();
            List<Color> colors = new List<Color>();
            List<bool> overlays = new List<bool>();
            HashSet<Renderer> seen = new HashSet<Renderer>();
            Transform[] nodes = _agent.GetComponentsInChildren<Transform>(true);
            for (int n = 0; n < nodes.Length; n++)
            {
                Transform node = nodes[n];
                if (node == null || !node.gameObject.activeInHierarchy || !IsHairName(node.name))
                {
                    continue;
                }

                Renderer[] found = node.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < found.Length; i++)
                {
                    Renderer renderer = found[i];
                    if (renderer == null || !renderer.enabled
                        || !renderer.gameObject.activeInHierarchy || !seen.Add(renderer))
                    {
                        continue;
                    }

                    MaterialPropertyBlock original = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original);
                    MaterialPropertyBlock apply = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(apply);
                    Material material = renderer.sharedMaterial;
                    Color color = material != null && material.HasProperty("_Color")
                        ? material.GetColor("_Color")
                        : Color.white;
                    bool overlay = material != null && material.HasProperty("_BurnColor");

                    renderers.Add(renderer);
                    originals.Add(original);
                    applies.Add(apply);
                    colors.Add(color);
                    overlays.Add(overlay);
                }
            }

            _burnRenderers = renderers.ToArray();
            _burnOriginalBlocks = originals.ToArray();
            _burnApplyBlocks = applies.ToArray();
            _burnOriginalColors = colors.ToArray();
            _burnHasOverlay = overlays.ToArray();
            _burnCached = _burnRenderers.Length > 0;
            _burnProgress = 0f;
        }

        void ApplyBurn(float progress)
        {
            if (!_burnCached || _burnRenderers == null)
            {
                return;
            }

            for (int i = 0; i < _burnRenderers.Length; i++)
            {
                Renderer renderer = _burnRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                MaterialPropertyBlock block = _burnApplyBlocks[i];
                if (_burnHasOverlay != null && _burnHasOverlay[i])
                {
                    block.SetColor("_BurnColor", _cfg.BurnColor);
                    block.SetFloat("_BurnStrength", Mathf.Clamp01(progress));
                }
                else
                {
                    // 原材质不是 MinecraftBlock：退回乘色，alpha 当叠加强度用
                    Color tint = Color.Lerp(_burnOriginalColors[i], _cfg.BurnColor,
                        Mathf.Clamp01(progress * _cfg.BurnColor.a));
                    tint.a = 1f;
                    block.SetColor("_Color", tint);
                    block.SetColor("_BaseColor", tint);
                }

                renderer.SetPropertyBlock(block);
            }
        }

        void RestoreBurn()
        {
            if (_burnRenderers != null && _burnOriginalBlocks != null)
            {
                for (int i = 0; i < _burnRenderers.Length; i++)
                {
                    Renderer renderer = _burnRenderers[i];
                    if (renderer != null)
                    {
                        renderer.SetPropertyBlock(_burnOriginalBlocks[i]);
                    }
                }
            }

            _burnRenderers = null;
            _burnOriginalBlocks = null;
            _burnApplyBlocks = null;
            _burnOriginalColors = null;
            _burnHasOverlay = null;
            _burnCached = false;
            _burnProgress = 0f;
        }

        bool IsOnHeadTop(Vector2 screen)
        {
            HeadScreenMetrics metrics = MeasureHeadOnScreen();
            float halfWidth = metrics.Width * 0.5f * Mathf.Clamp01(_cfg.HeadStartWidthRatio);
            float minY = metrics.TopY - metrics.Height * Mathf.Clamp01(_cfg.HeadStartDepthRatio);
            float maxY = metrics.TopY + metrics.Height * Mathf.Clamp01(_cfg.HeadStartAboveRatio);
            return Mathf.Abs(screen.x - metrics.Center.x) <= halfWidth
                && screen.y >= minY && screen.y <= maxY;
        }

        Vector3 HeadTopWorld()
        {
            Vector3[] corners = _headMeshLocalCorners ?? _headLocalCorners;
            if (_head == null || corners == null)
            {
                return _head != null ? _head.position : Vector3.zero;
            }

            Vector3 min = corners[0];
            Vector3 max = corners[0];
            for (int i = 0; i < corners.Length; i++)
            {
                min = Vector3.Min(min, corners[i]);
                max = Vector3.Max(max, corners[i]);
            }

            Vector3 center = (min + max) * 0.5f;
            int axis = SquashAxis();
            center[axis] = max[axis] + 0.01f;
            return _head.TransformPoint(center);
        }

        static bool TryFireBounds(GameObject fire, out Bounds bounds)
        {
            bounds = new Bounds();
            Renderer[] renderers = fire.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (any)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    any = true;
                }
            }

            return any;
        }

        Transform FindHairRoot()
        {
            if (_agent == null)
            {
                return null;
            }

            Transform[] all = _agent.GetComponentsInChildren<Transform>(true);
            Transform fallback = null;
            for (int i = 0; i < all.Length; i++)
            {
                Transform node = all[i];
                if (node == null || !node.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!IsHairName(node.name))
                {
                    continue;
                }

                if (_head != null && (node == _head || node.IsChildOf(_head)))
                {
                    return node;
                }

                if (fallback == null)
                {
                    fallback = node;
                }
            }

            return fallback;
        }

        static bool IsHairName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string normalized = NormalizeBoneName(name);
            return normalized.Contains("hair") || normalized.Contains("bang")
                || normalized.Contains("fringe") || normalized.Contains("ponytail")
                || normalized.Contains("ahoge");
        }

        void UpdateTargets()
        {
            HeadScreenMetrics metrics = MeasureHeadOnScreen();
            if (_mode == PetMode.Press)
            {
                _rubYawTarget = 0f;
                _rubRollTarget = 0f;

                if (!_dragging)
                {
                    _pressTarget = 0f;
                }
                else
                {
                    float range = Mathf.Max(1f, Screen.height * Mathf.Max(0.01f, _cfg.PressRange));
                    float start = _dragOrigin.y + Screen.height * _cfg.PressStartOffset;
                    _pressTarget = Mathf.Clamp01((start - _pointerScreen.y) / range);
                }

                if (_pressTarget >= _cfg.PressSoundAt)
                {
                    if (!_pressSoundPlayed)
                    {
                        _pressSoundPlayed = true;
                        PlayClip(_cfg.PressClip, _cfg.PressVolume);
                        PlayNormalLine(_cfg.PressLines);
                    }
                }
                else if (_pressTarget < _cfg.PressSoundAt * 0.6f)
                {
                    _pressSoundPlayed = false;
                }
            }
            else
            {
                _pressTarget = 0f;
                if (_press < 0.02f)
                {
                    _pressSoundPlayed = false;
                }

                if (!_dragging)
                {
                    _rubYawTarget = 0f;
                    _rubRollTarget = 0f;
                    return;
                }

                float half = Mathf.Max(1f, metrics.Width * 0.5f);
                float normalized = Mathf.Clamp((_pointerScreen.x - _dragOrigin.x) / half, -1f, 1f);
                _rubYawTarget = normalized * _cfg.RubYawDegrees;
                _rubRollTarget = -normalized * _cfg.RubRollDegrees;
            }
        }

        void UpdatePressHurt()
        {
            if (_mode != PetMode.Press || !_dragging
                || _press < Mathf.Clamp01(_cfg.PressHurtThreshold))
            {
                _pressHeld = 0f;
                _pressHurtTriggered = false;
                return;
            }

            if (_pressHurtTriggered)
            {
                return;
            }

            _pressHeld += Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            if (_pressHeld >= Mathf.Max(0.1f, _cfg.PressHurtSeconds))
            {
                TriggerPressHurt();
            }
        }

        void TriggerPressHurt()
        {
            _pressHurtTriggered = true;
            if (!MaidInjuryRules.Allows(_agent))
            {
                return;
            }

            string line = "";
            if (_cfg.PressTooLongLines != null && _cfg.PressTooLongLines.Length > 0)
            {
                line = _cfg.PressTooLongLines[
                    UnityEngine.Random.Range(0, _cfg.PressTooLongLines.Length)];
            }

            MaidDamageFlash.Flash(_agent.gameObject, _cfg.PressFlashColor, _cfg.PressFlashSeconds);
            MaidAudioService.Play(_agent, MaidSoundId.Hurt, false, _cfg.PressVoiceVolume);
            if (!string.IsNullOrEmpty(line))
            {
                MaidChatBubble.Show(_agent, line, _cfg.PressLineSeconds);
            }

            MaidTears.Hurt(_agent, 0.5f);
            StartStars();

            bool dizzy = _player != null && _dizzyBones.Count > 0;
            if (dizzy)
            {
                // dizziness 只叠 Head 子树；身体继续 idle，保持到退出交互
                _reactionPlaying = false;
                _petReactionEndsAt = -1f;
                RestoreReactionMouth();
                ClearWuyu();
                MaidHaptics.Hurt(_agent);
                MaidMysterySounds.Hurt(_agent, false);
                MaidHurtAnimation hurt = MaidHurtAnimation.Find(_agent);
                if (hurt != null)
                {
                    hurt.StopNow();
                }

                _dizzinessHeld = true;
                _dizzyTime = 0f;
                if (_wanderer != null)
                {
                    _wanderer.ReplayAnimation();
                }
            }
            else
            {
                MaidHurtAnimation.Hurt(_agent, true, _head);
            }
        }

        void StartStars()
        {
            if (_stars != null || _head == null)
            {
                return;
            }

            Material material = Resources.Load<Material>("MaidStars/star_material");
            if (material == null)
            {
                Debug.LogWarning("找不到毛发星星材质 Resources/MaidStars/star_material", this);
                return;
            }

            Texture2D texture = Resources.Load<Texture2D>("MaidStars/star_texture");
            if (texture != null)
            {
                material.mainTexture = texture;
            }

            Vector3 localTop = _head.InverseTransformPoint(HeadTopWorld());
            GameObject root = new GameObject("MaidHeadStars");
            root.transform.SetParent(_head, false);
            root.transform.localPosition = localTop;
            _stars = root.AddComponent<MaidHeadStars>();
            _stars.Initialize(material, _cfg.StarCount, _cfg.StarRadius, _cfg.StarSize,
                _cfg.StarHeight, _cfg.StarSpeed);
        }

        void PlayNormalLine(string[] lines)
        {
            if (_agent == null || _fireLit || _dizzinessHeld
                || lines == null || lines.Length == 0)
            {
                return;
            }

            if (Time.unscaledTime - _lastNormalLineAt < Mathf.Max(0f, _cfg.NormalLineCooldownSeconds))
            {
                return;
            }

            _lastNormalLineAt = Time.unscaledTime;
            string line = lines[UnityEngine.Random.Range(0, lines.Length)];
            MaidChatBubble.Show(_agent, line, _cfg.NormalLineSeconds);
        }

        void ApplyHead()
        {
            if (_head == null)
            {
                return;
            }

            int axis = SquashAxis();
            float squash = Mathf.Clamp(_cfg.PressSquash, 0f, 0.9f);
            float bulge = Mathf.Clamp(_cfg.PressBulge, 0f, 0.9f);
            Vector3 scale = _restHeadScale;
            scale[axis] *= 1f - _press * squash;
            scale[(axis + 1) % 3] *= 1f + _press * bulge;
            scale[(axis + 2) % 3] *= 1f + _press * bulge;
            _head.localScale = scale;

            if (_pivot != null)
            {
                Vector3 baseWorld = _pivot.parent != null
                    ? _pivot.parent.TransformPoint(_pivotBaseLocalPosition)
                    : _pivotBaseLocalPosition;
                _pivot.position = baseWorld - _camera.transform.up
                    * (_press * _headWorldHeight * 0.12f);

                Quaternion restWorld = _pivot.parent != null
                    ? _pivot.parent.rotation * _pivotBaseLocalRotation
                    : _pivotBaseLocalRotation;
                Quaternion delta = Quaternion.AngleAxis(_rubYaw, _camera.transform.up)
                    * Quaternion.AngleAxis(_rubRoll, _camera.transform.forward);
                _pivot.rotation = delta * restWorld;
            }
        }

        int SquashAxis()
        {
            if (_squashAxis >= 0)
            {
                return _squashAxis;
            }

            Vector3 up = _camera != null ? _camera.transform.up : Vector3.up;
            float best = -1f;
            int axis = 1;
            for (int i = 0; i < 3; i++)
            {
                Vector3 direction = _head.TransformDirection(Axis(i));
                float score = Mathf.Abs(Vector3.Dot(direction.normalized, up));
                if (score > best)
                {
                    best = score;
                    axis = i;
                }
            }

            _squashAxis = axis;
            return axis;
        }

        static Vector3 Axis(int axis)
        {
            return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        }

        void WrapHead()
        {
            if (_head == null || _head.parent == null)
            {
                _restHeadScale = _head != null ? _head.localScale : Vector3.one;
                return;
            }

            _headParent = _head.parent;
            _headSibling = _head.GetSiblingIndex();
            GameObject pivotObject = new GameObject("MaidHeadPetPivot");
            _pivot = pivotObject.transform;
            _pivot.SetParent(_headParent, false);
            _pivot.localPosition = _head.localPosition;
            _pivot.localRotation = _head.localRotation;
            _pivot.localScale = _head.localScale;
            _pivotBaseLocalPosition = _pivot.localPosition;
            _pivotBaseLocalRotation = _pivot.localRotation;
            _head.SetParent(_pivot, true);
            _restHeadScale = _head.localScale;
        }

        void RestoreHead()
        {
            if (_head == null)
            {
                return;
            }

            _head.localScale = _restHeadScale;
            if (_pivot == null)
            {
                return;
            }

            _pivot.localPosition = _pivotBaseLocalPosition;
            _pivot.localRotation = _pivotBaseLocalRotation;
            if (_headParent != null)
            {
                _head.SetParent(_headParent, true);
                _head.SetSiblingIndex(Mathf.Clamp(_headSibling, 0, Mathf.Max(0, _headParent.childCount - 1)));
            }

            Destroy(_pivot.gameObject);
            _pivot = null;
            _headParent = null;
        }

        void CaptureHeadGeometry()
        {
            if (_head == null)
            {
                return;
            }

            Bounds bounds = ComputeHeadBounds();
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            _headLocalCorners = new Vector3[8];
            int index = 0;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        _headLocalCorners[index++] = _head.InverseTransformPoint(corner);
                    }
                }
            }

            _headWorldHeight = Mathf.Max(0.05f, bounds.size.y);
        }

        /// <summary>只取 Head 自己的 Mesh，不把耳朵/头发等子物体算进火焰顶部。</summary>
        void CaptureHeadMeshGeometry()
        {
            _headMeshLocalCorners = null;
            if (_head == null || _rig == null || _rig.HeadMesh == null)
            {
                return;
            }

            Renderer renderer = _rig.HeadMesh;
            Bounds localBounds;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                localBounds = filter.sharedMesh.bounds;
            }
            else if (renderer is SkinnedMeshRenderer skinned)
            {
                localBounds = skinned.localBounds;
            }
            else
            {
                return;
            }

            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            _headMeshLocalCorners = new Vector3[8];
            int index = 0;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 world = renderer.transform.TransformPoint(corner);
                        _headMeshLocalCorners[index++] = _head.InverseTransformPoint(world);
                    }
                }
            }
        }

        Bounds ComputeHeadBounds()
        {
            bool any = false;
            Bounds bounds = new Bounds();
            Renderer[] renderers = _head.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || IsUnderEar(renderer.transform))
                {
                    continue;
                }

                if (any)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    any = true;
                }
            }

            if (any)
            {
                return bounds;
            }

            if (_rig != null && _rig.HeadMesh != null)
            {
                return _rig.HeadMesh.bounds;
            }

            return new Bounds(_head.position + Vector3.up * 0.15f, Vector3.one * 0.3f);
        }

        bool IsUnderEar(Transform node)
        {
            if (_rig == null || node == null)
            {
                return false;
            }

            for (int i = 0; i < _rig.LeftEar.Count; i++)
            {
                Transform ear = _rig.LeftEar[i];
                if (ear != null && (node == ear || node.IsChildOf(ear)))
                {
                    return true;
                }
            }

            for (int i = 0; i < _rig.RightEar.Count; i++)
            {
                Transform ear = _rig.RightEar[i];
                if (ear != null && (node == ear || node.IsChildOf(ear)))
                {
                    return true;
                }
            }

            return false;
        }

        Bounds BoundsFromLocalCorners()
        {
            Vector3 first = _head.TransformPoint(_headLocalCorners[0]);
            Bounds bounds = new Bounds(first, Vector3.zero);
            for (int i = 1; i < _headLocalCorners.Length; i++)
            {
                bounds.Encapsulate(_head.TransformPoint(_headLocalCorners[i]));
            }

            return bounds;
        }

        struct HeadScreenMetrics
        {
            public Vector2 Center;
            public float Width;
            public float Height;
            public float TopY;
        }

        HeadScreenMetrics MeasureHeadOnScreen()
        {
            HeadScreenMetrics metrics = new HeadScreenMetrics();
            if (_head == null || _camera == null || _headLocalCorners == null)
            {
                metrics.Center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                metrics.Width = Screen.width * 0.3f;
                metrics.Height = Screen.height * 0.3f;
                metrics.TopY = metrics.Center.y + metrics.Height * 0.5f;
                return metrics;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < _headLocalCorners.Length; i++)
            {
                Vector3 world = _head.TransformPoint(_headLocalCorners[i]);
                Vector3 screen = _camera.WorldToScreenPoint(world);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }

            metrics.Center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            metrics.Width = Mathf.Max(1f, maxX - minX);
            metrics.Height = Mathf.Max(1f, maxY - minY);
            metrics.TopY = maxY;
            return metrics;
        }

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidHeadPetPanel>();
            }

            if (_panel == null)
            {
                _panel = FindScenePanel();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidHeadPetPanel>();
            }

            _panel.ExitRequested -= End;
            _panel.ExitRequested += End;
            _panel.RubModeRequested -= SelectRub;
            _panel.RubModeRequested += SelectRub;
            _panel.PressModeRequested -= SelectPress;
            _panel.PressModeRequested += SelectPress;
        }

        MaidHeadPetPanel FindScenePanel()
        {
            MaidHeadPetPanel[] all = FindObjectsOfType<MaidHeadPetPanel>(true);
            MaidHeadPetPanel found = null;
            for (int i = 0; i < all.Length; i++)
            {
                MaidHeadPetPanel panel = all[i];
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

        void SelectRub()
        {
            SetMode(PetMode.Rub);
        }

        void SelectPress()
        {
            SetMode(PetMode.Press);
        }

        void SetMode(PetMode mode)
        {
            if (!IsActive)
            {
                return;
            }

            _mode = mode;
            _fingerId = -1;
            _dragging = false;
            _press = 0f;
            _pressTarget = 0f;
            _rubDistance = 0f;
            _rubYaw = 0f;
            _rubRoll = 0f;
            _rubYawTarget = 0f;
            _rubRollTarget = 0f;
            _pressSoundPlayed = false;
            _pressHeld = 0f;
            _pressHurtTriggered = false;
            _dragOrigin = Vector2.zero;
            _pointerScreen = Vector2.zero;
            _lastScreen = Vector2.zero;
            if (!_fireLit)
            {
                _heat = 0f;
            }
            if (_panel != null)
            {
                _panel.SetMode(mode == PetMode.Press);
                _panel.SetHeat(_fireLit ? 1f : _heat);
            }

        }

        void EnsureAudio()
        {
            _audio = GetComponent<AudioSource>();
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audioCreated = true;
            }

            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 1f;
        }

        /// <summary>
        /// 有 pet_reaction 的模型先播一遍反应，再切到可循环的 hold；没有这套动画的模型
        /// （方块模型等）就只走程序化的侧倒 / 压扁。
        /// </summary>
        void BeginMaidPetAnimation()
        {
            _petClipPlayed = false;
            _petReactionEndsAt = -1f;
            if (!_cfg.PlayMaidPetAnimation || _player == null)
            {
                return;
            }

            if (_player.HasClip(_cfg.PetReactionClip) && _player.Play(_cfg.PetReactionClip))
            {
                _petClipPlayed = true;
                float length = _player.ClipLength(_cfg.PetReactionClip);
                _petReactionEndsAt = Time.unscaledTime + Mathf.Max(0.1f, length);
                return;
            }

            PlayPetHold();
        }

        void PlayPetHold()
        {
            if (_player != null && _player.HasClip(_cfg.PetHoldClip) && _player.Play(_cfg.PetHoldClip))
            {
                _petClipPlayed = true;
            }
        }

        void PlayBurnReaction()
        {
            if (_player == null || string.IsNullOrEmpty(_cfg.BurnReactionClip)
                || !_player.HasClip(_cfg.BurnReactionClip))
            {
                _reactionPlaying = false;
                RestoreReactionMouth();
                return;
            }

            // !??! 允许直接接管整条主动画
            if (!_player.Play(_cfg.BurnReactionClip))
            {
                _reactionPlaying = false;
                RestoreReactionMouth();
                return;
            }

            ApplyReactionMouth();
            _reactionPlayed = true;
            _reactionPlaying = true;
            float length = _player.ClipLength(_cfg.BurnReactionClip);
            _reactionEndsAt = Time.unscaledTime + Mathf.Max(0.1f, length > 0.01f ? length : 1.25f);
        }

        /// <summary>
        /// wuyu 是只有眼睛 / 眉毛几条骨骼的表情姿势。缓存它写到的骨骼和正常动画下的基准姿势，
        /// 烧完后按权重叠在正常动画上，而不是把整条主动画切过去。
        /// </summary>
        void CacheWuyuBase()
        {
            _wuyuTracks.Clear();
            _wuyuBones.Clear();
            _wuyuBasePositions = null;
            _wuyuBaseRotations = null;
            _wuyuBaseScales = null;

            if (_player == null || _head == null || string.IsNullOrEmpty(_cfg.WuyuClip)
                || !_player.TryGetTracksUnder(_cfg.WuyuClip, _head, _wuyuTracks, _wuyuBones))
            {
                return;
            }

            // TryGetTracksUnder 会把没动过的骨骼也返回（它们是为了切动画时复位写的 rest 轨道）。
            // wuyu 只该盖眼睛/眉毛，不能连嘴巴、头、头发一起钉住，否则 idle 就没法继续动。
            for (int i = _wuyuBones.Count - 1; i >= 0; i--)
            {
                if (_wuyuBones[i] != null && IsWuyuBone(_wuyuBones[i].name))
                {
                    continue;
                }

                _wuyuBones.RemoveAt(i);
                _wuyuTracks.RemoveAt(i);
            }

            if (_wuyuBones.Count == 0)
            {
                return;
            }

            int count = _wuyuBones.Count;
            _wuyuBasePositions = new Vector3[count];
            _wuyuBaseRotations = new Quaternion[count];
            _wuyuBaseScales = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Transform bone = _wuyuBones[i];
                if (bone == null)
                {
                    continue;
                }

                _wuyuBasePositions[i] = bone.localPosition;
                _wuyuBaseRotations[i] = bone.localRotation;
                _wuyuBaseScales[i] = bone.localScale;
            }
        }

        /// <summary>dizziness 只叠 Head 子树，不接管整条主动画。</summary>
        void CacheDizzinessBase()
        {
            _dizzyTracks.Clear();
            _dizzyBones.Clear();
            _dizzyBasePositions = null;
            _dizzyBaseRotations = null;
            _dizzyBaseScales = null;
            _dizzyTime = 0f;

            if (_player == null || _head == null || string.IsNullOrEmpty(_cfg.DizzinessClip)
                || !_player.TryGetTracksUnder(_cfg.DizzinessClip, _head, _dizzyTracks, _dizzyBones))
            {
                return;
            }

            int count = _dizzyBones.Count;
            _dizzyBasePositions = new Vector3[count];
            _dizzyBaseRotations = new Quaternion[count];
            _dizzyBaseScales = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Transform bone = _dizzyBones[i];
                if (bone == null)
                {
                    continue;
                }

                _dizzyBasePositions[i] = bone.localPosition;
                _dizzyBaseRotations[i] = bone.localRotation;
                _dizzyBaseScales[i] = bone.localScale;
            }
        }

        void ApplyDizziness(float dt)
        {
            if (!_dizzinessHeld || _player == null || _dizzyBones.Count == 0)
            {
                return;
            }

            float length = Mathf.Max(0.1f, _player.ClipLength(_cfg.DizzinessClip));
            _dizzyTime = Mathf.Repeat(_dizzyTime + dt, length);
            for (int i = 0; i < _dizzyBones.Count; i++)
            {
                Transform bone = _dizzyBones[i];
                if (bone == null)
                {
                    continue;
                }

                Vector3 position;
                Quaternion rotation;
                Vector3 scale;
                BedrockAnimationPlayer.Sample(_dizzyTracks[i], _dizzyTime,
                    out position, out rotation, out scale);
                bone.localPosition = position;
                bone.localRotation = rotation;
                bone.localScale = scale;
            }
        }

        void ClearDizziness()
        {
            if (_dizzyBones.Count > 0 && _dizzyBasePositions != null)
            {
                for (int i = 0; i < _dizzyBones.Count; i++)
                {
                    Transform bone = _dizzyBones[i];
                    if (bone == null)
                    {
                        continue;
                    }

                    bone.localPosition = _dizzyBasePositions[i];
                    bone.localRotation = _dizzyBaseRotations[i];
                    bone.localScale = _dizzyBaseScales[i];
                }
            }

            _dizzinessHeld = false;
            _dizzyTime = 0f;
            _dizzyTracks.Clear();
            _dizzyBones.Clear();
            _dizzyBasePositions = null;
            _dizzyBaseRotations = null;
            _dizzyBaseScales = null;
        }

        static bool IsWuyuBone(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string normalized = name.ToLowerInvariant()
                .Replace("_", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace(" ", "");
            if (normalized.Contains("mouth") || normalized.Contains("head")
                || normalized.Contains("hair") || normalized.Contains("ear")
                || normalized.Contains("body"))
            {
                return false;
            }

            return normalized.Contains("eye") || normalized.Contains("brow")
                || normalized.Contains("eyelid") || normalized.Contains("pupil");
        }

        /// <summary>
        /// 有些模型的惊讶嘴不叫 Mouth_surprise，而是 jingya；!??! 只写 position 不写 scale，
        /// 从 idle 切过来时那个嘴可能还是 scale 0。这里在 !??! 期间强制显示惊讶嘴，隐藏其它嘴型。
        /// </summary>
        void CacheReactionMouth()
        {
            _surpriseMouth = null;
            _otherMouths = null;
            _otherMouthBaseScales = null;
            if (_head == null)
            {
                return;
            }

            List<Transform> others = new List<Transform>();
            List<Vector3> othersScales = new List<Vector3>();
            Transform[] all = _head.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform node = all[i];
                if (node == null || !node.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string n = NormalizeBoneName(node.name);
                if ((n.Contains("jingya") || n.Contains("surprise")) && _surpriseMouth == null)
                {
                    _surpriseMouth = node;
                    _surpriseMouthBaseScale = node.localScale;
                    continue;
                }

                if (n.Contains("smile") || n.Contains("xiao") || n.Contains("chill"))
                {
                    others.Add(node);
                    othersScales.Add(node.localScale);
                }
            }

            _otherMouths = others.ToArray();
            _otherMouthBaseScales = othersScales.ToArray();
        }

        void ApplyReactionMouth()
        {
            if (_surpriseMouth == null)
            {
                return;
            }

            _surpriseMouth.localScale = Vector3.one;
            if (_otherMouths != null)
            {
                for (int i = 0; i < _otherMouths.Length; i++)
                {
                    if (_otherMouths[i] != null)
                    {
                        _otherMouths[i].localScale = Vector3.zero;
                    }
                }
            }

            _reactionMouthApplied = true;
        }

        void RestoreReactionMouth()
        {
            if (!_reactionMouthApplied)
            {
                return;
            }

            if (_surpriseMouth != null)
            {
                _surpriseMouth.localScale = _surpriseMouthBaseScale;
            }

            if (_otherMouths != null && _otherMouthBaseScales != null)
            {
                for (int i = 0; i < _otherMouths.Length; i++)
                {
                    if (_otherMouths[i] != null)
                    {
                        _otherMouths[i].localScale = _otherMouthBaseScales[i];
                    }
                }
            }

            _reactionMouthApplied = false;
        }

        static string NormalizeBoneName(string name)
        {
            return string.IsNullOrEmpty(name) ? "" : name.ToLowerInvariant()
                .Replace("_", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace(" ", "");
        }

        void ApplyWuyu(float dt)
        {
            if (!_wuyuActive || _wuyuBones.Count == 0 || _wuyuBasePositions == null)
            {
                return;
            }

            float target = Mathf.Clamp01(_cfg.WuyuWeight);
            float speed = 1f / Mathf.Max(0.01f, _cfg.WuyuFadeSeconds);
            _wuyuWeight = Mathf.MoveTowards(_wuyuWeight, target, speed * dt);
            for (int i = 0; i < _wuyuBones.Count; i++)
            {
                Transform bone = _wuyuBones[i];
                if (bone == null)
                {
                    continue;
                }

                Vector3 position;
                Quaternion rotation;
                Vector3 scale;
                BedrockAnimationPlayer.Sample(_wuyuTracks[i], 0f, out position, out rotation, out scale);
                bone.localPosition = Vector3.Lerp(_wuyuBasePositions[i], position, _wuyuWeight);
                bone.localRotation = Quaternion.Slerp(_wuyuBaseRotations[i], rotation, _wuyuWeight);
                bone.localScale = Vector3.Lerp(_wuyuBaseScales[i], scale, _wuyuWeight);
            }
        }

        void ClearWuyu()
        {
            if (_wuyuBones.Count > 0 && _wuyuBasePositions != null)
            {
                for (int i = 0; i < _wuyuBones.Count; i++)
                {
                    Transform bone = _wuyuBones[i];
                    if (bone == null)
                    {
                        continue;
                    }

                    bone.localPosition = _wuyuBasePositions[i];
                    bone.localRotation = _wuyuBaseRotations[i];
                    bone.localScale = _wuyuBaseScales[i];
                }
            }

            _wuyuActive = false;
            _wuyuWeight = 0f;
            _wuyuTracks.Clear();
            _wuyuBones.Clear();
            _wuyuBasePositions = null;
            _wuyuBaseRotations = null;
            _wuyuBaseScales = null;
        }

        void PlayClip(AudioClip clip, float volume, bool varyPitch = true)
        {
            if (clip == null || _audio == null)
            {
                return;
            }

            float scale = Mathf.Clamp01(GameSettingsStore.Current.MaidVolume / 100f) * Mathf.Max(0f, volume);
            if (scale <= 0.0001f)
            {
                return;
            }

            _audio.pitch = varyPitch ? UnityEngine.Random.Range(0.97f, 1.03f) : 1f;
            _audio.PlayOneShot(clip, scale);
        }

        void Release()
        {
            IsActive = false;
            _fingerId = -1;
            _dragging = false;
            _press = 0f;
            _pressTarget = 0f;
            _rubYaw = 0f;
            _rubRoll = 0f;
            _rubYawTarget = 0f;
            _rubRollTarget = 0f;
            _rubDistance = 0f;
            _pressSoundPlayed = false;
            _pressHeld = 0f;
            _pressHurtTriggered = false;
            _squashAxis = -1;

            bool borrowedAnimation = _petClipPlayed || _reactionPlayed || _wuyuActive || _dizzinessHeld;
            _reactionPlaying = false;
            DestroyFire();
            RestoreBurn();
            RestoreReactionMouth();
            ClearDizziness();
            ClearWuyu();
            if (_stars != null)
            {
                Destroy(_stars.gameObject);
                _stars = null;
            }
            _lastNormalLineAt = -100f;
            RestoreHead();

            // 借来的摸头 / 着火 / wuyu 姿势演完要还回去，不然会停在反应姿势
            if (borrowedAnimation && _wanderer != null)
            {
                _wanderer.ReplayAnimation();
            }

            if (_panel != null)
            {
                _panel.SetHeat(0f);
                _panel.Hide();
            }

            if (_audio != null)
            {
                _audio.Stop();
                _audio.pitch = 1f;
                if (_audioCreated)
                {
                    Destroy(_audio);
                }
            }

            _audio = null;
            _audioCreated = false;
            _cfg = null;
            _player = null;
            _wanderer = null;
            _petClipPlayed = false;
            _petReactionEndsAt = -1f;
            _reactionPlayed = false;
            _reactionEndsAt = 0f;
            _agent = null;
            _rig = null;
            _head = null;
            _headLocalCorners = null;
            _camera = null;
        }
    }
}
