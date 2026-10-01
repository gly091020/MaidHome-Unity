using System;
using System.Collections;
using System.Collections.Generic;
using MaidHome.Gameplay.Audio;
using MaidHome.Gameplay.House;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸尾巴模式：机制和参数照抄 moreanimation 的 TailInteractionState / TailSniffCamera。
    /// 点住尾巴 → 拖动改变目标角度（每帧最多变 18°）→ 7 段弹簧跟着走 → 松手弹回；
    /// 指针拉出屏幕中央 70%×65% 的安全区算"拉太狠"，她会喊疼；
    /// 「吸一口」把相机凑到尾巴前，播 tail_sniff.ogg（最多 2 秒）再退回来。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidTailInteraction : MonoBehaviour
    {
        public event Action Ended;

        [Header("拖尾巴手感")]
        [SerializeField] private float _dragSensitivity = 1.85f;
        [Tooltip("拖动多少像素相当于 45°，越大越迟钝")]
        [SerializeField] private float _dragReferencePixels = 400f;
        [Tooltip("拖起来方向反了就把某个改成 -1")]
        [SerializeField] private float _yawSign = 1f;
        [SerializeField] private float _pitchSign = -1f;
        [Tooltip("侧向拉扯时尾巴跟着扭一点的比例")]
        [SerializeField] private float _zFollow = 0.08f;
        [SerializeField] private float _safeZoneWidth = 0.70f;
        [SerializeField] private float _safeZoneHeight = 0.65f;
        [SerializeField] private float _hitPaddingPixels = 8f;
        [Tooltip("从抓住的位置往外拖这么多屏幕高度就算拉太狠")]
        [SerializeField] private float _overstretchDragFraction = 0.3f;

        [Header("镜头")]
        [Tooltip("进摸尾巴模式时，相机绕到尾巴背面、离尾巴多远")]
        [SerializeField] private float _viewDistance = 1.5f;
        [Tooltip("进模式时相机比尾巴高多少")]
        [SerializeField] private float _viewHeight = 0.9f;
        [Tooltip("背后视角框住整只女仆时留的余量")]
        [SerializeField] private float _viewMargin = 1.1f;
        [SerializeField] private float _sniffTravelSeconds = 0.3f;

        [Header("吸一口")]
        [SerializeField] private float _sniffMaxSeconds = 2f;
        [Tooltip("吸的时候再往里推多远")]
        [SerializeField] private float _sniffDistance = 0.65f;
        [SerializeField] private float _sniffHeight = 0.35f;
        [Tooltip("吸的时候只框住尾巴那条包围盒")]
        [SerializeField] private float _sniffMargin = 1.15f;

        [Header("音量")]
        [Tooltip("摸尾巴这块的音效和语音统一放大倍率。原文件本身录音小就往上调，最大 8")]
        [SerializeField] private float _soundVolume = 6f;

        [Header("闪红")]
        [Tooltip("拉太狠时闪的乘色。Alpha 当强度用：1 = 纯红（原版受伤），0.5 = 半透明红，0 = 不闪")]
        [SerializeField] private Color _flashColor = new Color(1f, 0f, 0f, 0.5f);
        [Tooltip("原版受伤是 hurtTime = 10 tick，也就是 0.5 秒")]
        [SerializeField] private float _flashSeconds = 0.5f;

        [Header("台词")]
        [SerializeField] private float _lineSeconds = 1f;
        [SerializeField] private float _complainCooldownSeconds = 1f;

        // 台词照抄 moreanimation 的 zh_cn
        static readonly string[] OverstretchLines =
        {
            "呜呜……别这么用力，尾巴会断的……",
            "疼疼疼！轻一点呀！",
            "尾巴要被你拽掉了……",
            "呜……不要再往那边拉了！",
            "轻一点……真的很疼的……",
            "等、等等……尾巴不是这么玩的呀……",
            "呜呜……再拉就真的要断掉了……"
        };

        static readonly string[] SniffLines =
        {
            "主人，尾巴有那么好闻吗？",
            "呀，靠得太近啦，毛毛会蹭到鼻子的！",
            "刚梳好的尾巴，可别弄乱了哦。",
            "唔……闻一下就好，不许咬！",
            "嘿嘿，毛茸茸的，很舒服吧？",
            "主人，闻够了就陪我把尾巴梳顺，好不好？",
            "再凑这么近，我可要拿尾巴挠你痒痒啦！"
        };

        public bool IsActive { get; private set; }

        readonly List<MaidTailChain> _chains = new List<MaidTailChain>();

        MaidAgent _agent;
        MaidTailChain _selected;
        MaidTailPanel _panel;
        AudioSource _source;
        AudioClip _sniffClip;
        Coroutine _sniffRoutine;

        bool _grabbed;
        bool _sniffing;
        bool _overstretched;
        bool _hasAnchor;
        Vector3 _anchorPoint;
        Vector3 _anchorDirection;
        Vector2 _dragOrigin;
        float _startYaw;
        float _startPitch;
        float _targetYaw;
        float _targetPitch;
        float _simAccumulator;
        float _lastComplainTime = -100f;

        public bool Begin(MaidAgent agent, AudioClip sniffClip)
        {
            if (agent == null)
            {
                return false;
            }

            Abort();

            // SimpleBedrockModel 的女仆（方块酒狐这种）在 TLM 里没有 GeckoLib 模型，
            // 原版摸尾巴就是只支持 GeckoLib 模型
            if (agent.Save != null && agent.Save.SimpleBedrockModel)
            {
                Debug.LogWarning("SimpleBedrockModel 的女仆不支持摸尾巴: " + agent.name);
                return false;
            }

            List<MaidTailChain> found = MaidTailChain.Build(agent.transform);
            for (int i = 0; i < found.Count; i++)
            {
                // 被 HiddenNodes 藏起来的骨骼（例如酒狐的小狐狸尾巴）不算
                if (found[i].HasVisibleGeometry())
                {
                    _chains.Add(found[i]);
                }
            }

            if (_chains.Count == 0)
            {
                Debug.LogWarning("这个模型的骨架里没找到尾巴，摸不了: " + agent.name);
                return false;
            }

            _agent = agent;
            _sniffClip = sniffClip;
            _selected = _chains[0];
            _simAccumulator = 0f;
            CacheAnchor();
            EnsurePanel();
            if (_panel != null)
            {
                _panel.Show();
            }

            IsActive = true;
            EnterTailView();
            return true;
        }

        /// 这个女仆的模型有没有能摸的尾巴，用来决定面板按钮能不能点
        public bool Supports(MaidAgent agent)
        {
            if (agent == null || (agent.Save != null && agent.Save.SimpleBedrockModel))
            {
                return false;
            }

            List<MaidTailChain> found = MaidTailChain.Build(agent.transform);
            for (int i = 0; i < found.Count; i++)
            {
                if (found[i].HasVisibleGeometry())
                {
                    return true;
                }
            }

            return false;
        }

        /// 玩家自己退出：退回去重新打开女仆面板
        public void End()
        {
            if (!IsActive)
            {
                return;
            }

            ReleaseAll();
            if (Ended != null)
            {
                Ended();
            }
        }

        /// 被外层关掉：不回调
        public void Abort()
        {
            ReleaseAll();
        }

        void OnDestroy()
        {
            ReleaseAll();
            Ended = null;
        }

        void ReleaseAll()
        {
            bool wasSniffing = _sniffing;
            if (_sniffRoutine != null)
            {
                StopCoroutine(_sniffRoutine);
                _sniffRoutine = null;
            }

            // 进模式时镜头绕到了背面，不管是不是吸到一半退出，都得先把它拉回来
            RestoreMaidView();

            if (_panel != null)
            {
                _panel.Hide();
            }

            for (int i = 0; i < _chains.Count; i++)
            {
                _chains[i].ResetAll();
                // 立刻把尾巴放回动画姿势，不然会停在拉歪的状态
                _chains[i].Apply(_pitchSign, _yawSign, _zFollow, 1f);
            }

            _chains.Clear();
            _selected = null;
            _agent = null;
            _grabbed = false;
            _sniffing = false;
            _overstretched = false;
            _simAccumulator = 0f;
            _hasAnchor = false;
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

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                End();
                return;
            }

            Vector2 pointer = Input.mousePosition;

            if (_grabbed)
            {
                // 松手判断放在最前面，免得指针抬在按钮上就卡住不放
                if (Input.GetMouseButtonUp(0))
                {
                    Release();
                }
                else
                {
                    Drag(pointer);
                }

                return;
            }

            if (_sniffing || IsPointerOverUi())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                MaidTailChain chain;
                int bone;
                if (TryHit(pointer, out chain, out bone))
                {
                    Grab(chain, pointer);
                }
            }
        }

        void LateUpdate()
        {
            if (!IsActive)
            {
                return;
            }

            if (!_sniffing)
            {
                // 原版弹簧是按客户端 tick（20Hz）跑的，这里补一个固定步长
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
            }

            // 弹簧只有 20Hz，这里在两次之间插值，不然尾巴看起来像 20 帧
            float alpha = Mathf.Clamp01(_simAccumulator / MaidTailChain.StepSeconds);
            for (int i = 0; i < _chains.Count; i++)
            {
                _chains[i].Apply(_pitchSign, _yawSign, _zFollow, alpha);
            }
        }

        void Grab(MaidTailChain chain, Vector2 pointer)
        {
            _selected = chain;
            _grabbed = true;
            _overstretched = false;
            _dragOrigin = pointer;
            chain.SnapshotStart(out _startYaw, out _startPitch);
            _targetYaw = _startYaw;
            _targetPitch = _startPitch;
            chain.SetTarget(_targetYaw, _targetPitch, true);
            _simAccumulator = 0f;
        }

        void Drag(Vector2 pointer)
        {
            if (_selected == null)
            {
                return;
            }

            Vector2 delta = pointer - _dragOrigin;
            float yaw = _startYaw
                + Mathf.Atan2(delta.x, _dragReferencePixels) * Mathf.Rad2Deg * _dragSensitivity;
            float pitch = _startPitch
                + Mathf.Atan2(delta.y, _dragReferencePixels) * Mathf.Rad2Deg * _dragSensitivity;
            yaw = SoftLimit(yaw, MaidTailChain.MaxYaw, MaidTailChain.MaxYaw);
            pitch = SoftLimit(pitch, MaidTailChain.MinPitch, MaidTailChain.MaxPitch);
            _targetYaw = LimitStep(_targetYaw, yaw);
            _targetPitch = LimitStep(_targetPitch, pitch);
            _selected.SetTarget(_targetYaw, _targetPitch, true);

            // 拉太狠的判定：指针跑出屏幕中央那块，或者从抓住的地方拖了很长的距离
            bool over = !InSafeZone(pointer)
                || (pointer - _dragOrigin).magnitude > _overstretchDragFraction * Screen.height;
            if (over != _overstretched)
            {
                _overstretched = over;
                if (over)
                {
                    Complain();
                }
            }
        }

        void Release()
        {
            _grabbed = false;
            _overstretched = false;
            _targetYaw = 0f;
            _targetPitch = 0f;
            if (_selected != null)
            {
                _selected.SetTarget(0f, 0f, false);
            }
        }

        void Complain()
        {
            if (Time.unscaledTime - _lastComplainTime < _complainCooldownSeconds)
            {
                return;
            }

            _lastComplainTime = Time.unscaledTime;
            // 闪红、台词、受伤语音三者共用同一个冷却
            MaidDamageFlash.Flash(_agent != null ? _agent.gameObject : null, _flashColor, _flashSeconds);
            if (_panel != null)
            {
                _panel.ShowLine(Pick(OverstretchLines), _lineSeconds);
            }

            // 拉太狠了，放一声女仆自己的受伤语音（来自她的声音包 maid.ai.hurt）
            MaidAudioService.Play(_agent, MaidSoundId.Hurt, false, _soundVolume);
        }

        void OnSniff()
        {
            if (!IsActive || _sniffing)
            {
                return;
            }

            _sniffRoutine = StartCoroutine(SniffRoutine());
        }

        IEnumerator SniffRoutine()
        {
            MaidTailChain chain = _selected != null
                ? _selected
                : (_chains.Count > 0 ? _chains[0] : null);
            if (chain == null)
            {
                _sniffRoutine = null;
                yield break;
            }

            _sniffing = true;
            _grabbed = false;
            _overstretched = false;
            chain.Freeze(true);

            HouseCameraFitter fitter = HouseCameraFitter.Instance;
            if (fitter != null)
            {
                if (!_hasAnchor)
                {
                    CacheAnchor();
                }

                fitter.FocusOnPoint(_anchorPoint, _anchorDirection, _sniffDistance, _sniffHeight,
                    _agent != null ? _agent.transform : null, chain.GetBounds(), _sniffMargin);
            }

            yield return new WaitForSeconds(_sniffTravelSeconds);
            PlaySniffSound();
            if (_panel != null)
            {
                _panel.ShowLine(Pick(SniffLines), _lineSeconds);
            }

            float wait = _sniffClip != null ? Mathf.Min(_sniffClip.length, _sniffMaxSeconds) : 1f;
            yield return new WaitForSeconds(wait);

            if (fitter != null && _agent != null)
            {
                EnterTailView();
            }

            yield return new WaitForSeconds(_sniffTravelSeconds);

            chain.Freeze(false);
            chain.SetTarget(0f, 0f, false);
            _sniffing = false;
            _sniffRoutine = null;
        }

        /// 相机该待在尾巴哪一侧：从女仆身体指向尾巴的那一边（尾巴长在屁股后面，所以就是背面）
        Vector3 ResolveBackDirection(Vector3 tailPoint)
        {
            if (_agent == null)
            {
                return Vector3.back;
            }

            Vector3 direction = tailPoint - _agent.GetBounds().center;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = -_agent.transform.forward;
                direction.y = 0f;
            }

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.back;
            }

            return direction.normalized;
        }

        void RestoreMaidView()
        {
            HouseCameraFitter fitter = HouseCameraFitter.Instance;
            if (fitter == null || _agent == null || !_agent.gameObject.activeInHierarchy)
            {
                return;
            }

            fitter.FocusOn(_agent.GetBounds(), _agent.transform);
        }

        /// 进模式 / 吸完回来：相机待在尾巴背面看整条尾巴
        void EnterTailView()
        {
            HouseCameraFitter fitter = HouseCameraFitter.Instance;
            if (fitter == null || _agent == null || _selected == null)
            {
                return;
            }

            if (!_hasAnchor)
            {
                CacheAnchor();
            }

            // 背后视角按整只女仆重算视野角
            fitter.FocusOnPoint(_anchorPoint, _anchorDirection, _viewDistance, _viewHeight, _agent.transform,
                _agent.GetBounds(), _viewMargin);
        }

        /// 进模式时把尾巴的位置和朝向记下来：后面吸一口、退回都复用同一个点，
        /// 不然尾巴一摆、镜头就跟着重算，看起来就是一会儿近一会儿远
        void CacheAnchor()
        {
            if (_selected == null || _agent == null)
            {
                _hasAnchor = false;
                return;
            }

            _anchorPoint = FocusPoint(_selected);
            _anchorDirection = ResolveBackDirection(_anchorPoint);
            _hasAnchor = true;
        }

        Vector3 FocusPoint(MaidTailChain chain)
        {
            Renderer renderer = chain.FocusRenderer();
            return renderer != null ? renderer.bounds.center : chain.FocusPoint();
        }

        void PlaySniffSound()
        {
            if (_sniffClip == null)
            {
                return;
            }

            if (_source == null)
            {
                GameObject holder = new GameObject("MaidTailAudio");
                holder.transform.SetParent(transform, false);
                _source = holder.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
            }

            // 倍率走采样放大，AudioSource.volume 和 PlayOneShot 的倍率参数都上不了 1
            _source.volume = Mathf.Clamp(MaidAudioService.MasterVolume, 0f, MaidAudioVoice.MaxVolume);
            _source.PlayOneShot(AudioGain.Amplify(_sniffClip, _soundVolume), 1f);
        }

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidTailPanel>();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidTailPanel>();
            }

            _panel.SniffRequested -= OnSniff;
            _panel.SniffRequested += OnSniff;
            _panel.ExitRequested -= End;
            _panel.ExitRequested += End;
        }

        bool TryHit(Vector2 screenPoint, out MaidTailChain chain, out int boneIndex)
        {
            chain = null;
            boneIndex = -1;
            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            float best = float.MaxValue;
            for (int c = 0; c < _chains.Count; c++)
            {
                MaidTailChain current = _chains[c];
                Transform[] bones = current.Bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform bone = bones[i];
                    if (bone == null || !bone.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Renderer renderer = bone.GetComponent<Renderer>();
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    Rect rect;
                    if (!TryProject(renderer.bounds, camera, out rect))
                    {
                        continue;
                    }

                    rect.xMin -= _hitPaddingPixels;
                    rect.yMin -= _hitPaddingPixels;
                    rect.xMax += _hitPaddingPixels;
                    rect.yMax += _hitPaddingPixels;
                    if (!rect.Contains(screenPoint))
                    {
                        continue;
                    }

                    float distance = (rect.center - screenPoint).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        chain = current;
                        boneIndex = i;
                    }
                }
            }

            return chain != null;
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

        bool InSafeZone(Vector2 pointer)
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return true;
            }

            float x = Mathf.Abs(pointer.x / Screen.width - 0.5f);
            float y = Mathf.Abs(pointer.y / Screen.height - 0.5f);
            return x <= _safeZoneWidth * 0.5f && y <= _safeZoneHeight * 0.5f;
        }

        static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        static float SoftLimit(float value, float negative, float positive)
        {
            float limit = value < 0f ? negative : positive;
            return limit * (float)Math.Tanh(value / limit);
        }

        static float LimitStep(float current, float value)
        {
            // 原版 MAX_TARGET_CHANGE_PER_UPDATE = 18°
            return Mathf.Clamp(value, current - 18f, current + 18f);
        }

        static string Pick(string[] lines)
        {
            return lines[UnityEngine.Random.Range(0, lines.Length)];
        }
    }
}
