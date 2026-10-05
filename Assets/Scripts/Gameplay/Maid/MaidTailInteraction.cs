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
    /// 摸尾巴模式：机制和参数照抄 moreanimation 的 TailInteractionState / TailSniffCamera。
    /// 点住尾巴 → 拖动改变目标角度（每帧最多变 18°）→ 7 段弹簧跟着走 → 松手弹回；
    /// 指针拉出屏幕中央 70%×65% 的安全区算"拉太狠"，她会喊疼；
    /// 「吸一口」播 tail_sniff.ogg（最多 2 秒）；正交相机推近看不出远近，所以"凑近"用
    /// 正交 size 缩放代替（相机位置全程不动）。
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

        [Header("转身")]
        [Tooltip("进模式 / 退出模式时她转身要多久。相机全程不动，靠她转过来把尾巴露给镜头")]
        [SerializeField] private float _turnSeconds = 0.35f;

        [Header("吸一口")]
        [SerializeField] private float _sniffTravelSeconds = 0.3f;
        [SerializeField] private float _sniffMaxSeconds = 2f;
        [Tooltip("吸的时候正交 size 缩到这个比例（0.6 = 画面放大 1.67 倍）。正交下移动相机看不出远近，所以用 size 代替推近")]
        [SerializeField] private float _sniffZoomScale = 0.6f;
        [Tooltip("size 再小也不低于这个值，免得糊到看不清")]
        [SerializeField] private float _sniffMinSize = 0.35f;

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
        readonly List<TailGrab> _grabs = new List<TailGrab>();
        readonly List<PointerInput.Pointer> _pointers = new List<PointerInput.Pointer>();

        MaidAgent _agent;
        MaidTailChain _lastTouched;
        public MaidTailPanel _panel;
        AudioSource _source;
        AudioClip _sniffClip;
        Coroutine _sniffRoutine;

        bool _sniffing;
        // 吸一口前的正交 size，退出/中断时要还原，不然相机会一直停在放大状态
        bool _hasCameraSize;
        float _cameraBaseSize;
        float _simAccumulator;
        float _lastComplainTime = -100f;

        /// <summary>一根手指对应一条尾巴；多指时各拖各的，互不干扰</summary>
        sealed class TailGrab
        {
            public int FingerId;
            public MaidTailChain Chain;
            public Vector2 Origin;
            public float StartYaw;
            public float StartPitch;
            public float TargetYaw;
            public float TargetPitch;
            public bool Overstretched;
        }

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
            _lastTouched = _chains[0];
            _simAccumulator = 0f;
            EnsurePanel();
            if (_panel != null)
            {
                _panel.Show();
            }

            IsActive = true;
            // 相机不动，她自己转过去把尾巴露给镜头
            TurnTailToCamera();
            SetParallelLayer(false);
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

            if (_hasCameraSize)
            {
                // 吸到一半被中断：把正交 size 还原，别让相机停在放大状态
                Camera camera = Camera.main;
                if (camera != null && camera.orthographic)
                {
                    camera.orthographicSize = _cameraBaseSize;
                }

                _hasCameraSize = false;
            }

            // 退出时让她转回来看镜头（女仆交互面板接着要开）
            TurnToCamera();
            SetParallelLayer(true);
            ReleaseAllGrabs();

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
            _lastTouched = null;
            _agent = null;
            _sniffing = false;
            _simAccumulator = 0f;
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

            PointerInput.CopyPointers(_pointers);
            // 先处理已经在拖的手指：只认自己那根 fingerId，别的手指按下/抬起都不影响
            UpdateGrabs();

            if (_sniffing)
            {
                return;
            }

            for (int i = 0; i < _pointers.Count; i++)
            {
                PointerInput.Pointer pointer = _pointers[i];
                if (!pointer.Pressed || pointer.OverUi || FindGrab(pointer.FingerId) != null)
                {
                    continue;
                }

                MaidTailChain chain;
                int bone;
                if (TryHit(pointer.Position, out chain, out bone))
                {
                    Grab(chain, pointer);
                }
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
                    ReleaseGrab(grab);
                    _grabs.RemoveAt(i);
                    continue;
                }

                Drag(grab, pointer.Position);
            }
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

        void Grab(MaidTailChain chain, PointerInput.Pointer pointer)
        {
            // 同一根尾巴被第二根手指按住：把这条链交给新手指，别两个抢着写
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
            chain.SetTarget(grab.TargetYaw, grab.TargetPitch, true);
            _grabs.Add(grab);
            _lastTouched = chain;
            _simAccumulator = 0f;
        }

        void Drag(TailGrab grab, Vector2 pointer)
        {
            if (grab == null || grab.Chain == null)
            {
                return;
            }

            Vector2 delta = pointer - grab.Origin;
            float yaw = grab.StartYaw
                + Mathf.Atan2(delta.x, _dragReferencePixels) * Mathf.Rad2Deg * _dragSensitivity;
            float pitch = grab.StartPitch
                + Mathf.Atan2(delta.y, _dragReferencePixels) * Mathf.Rad2Deg * _dragSensitivity;
            yaw = SoftLimit(yaw, MaidTailChain.MaxYaw, MaidTailChain.MaxYaw);
            pitch = SoftLimit(pitch, MaidTailChain.MinPitch, MaidTailChain.MaxPitch);
            grab.TargetYaw = LimitStep(grab.TargetYaw, yaw);
            grab.TargetPitch = LimitStep(grab.TargetPitch, pitch);
            grab.Chain.SetTarget(grab.TargetYaw, grab.TargetPitch, true);

            // 拉太狠的判定：指针跑出屏幕中央那块，或者从抓住的地方拖了很长的距离
            bool over = !InSafeZone(pointer)
                || (pointer - grab.Origin).magnitude > _overstretchDragFraction * Screen.height;
            if (over != grab.Overstretched)
            {
                grab.Overstretched = over;
                if (over)
                {
                    Complain();
                }
            }
        }

        void ReleaseGrab(TailGrab grab)
        {
            if (grab != null && grab.Chain != null)
            {
                grab.Chain.SetTarget(0f, 0f, false);
            }
        }

        void ReleaseAllGrabs()
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                ReleaseGrab(_grabs[i]);
            }

            _grabs.Clear();
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
            // 拉太狠了，放一声女仆自己的受伤语音（来自她的声音包 maid.ai.hurt）
            MaidAudioService.Play(_agent, MaidSoundId.Hurt, false, _soundVolume);
            MaidChatBubble.Show(_agent, MaidEasterEgg.PickHurt(_agent, OverstretchLines), _lineSeconds);
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
            MaidTailChain chain = _lastTouched != null
                ? _lastTouched
                : (_chains.Count > 0 ? _chains[0] : null);
            if (chain == null)
            {
                _sniffRoutine = null;
                yield break;
            }

            _sniffing = true;
            ReleaseAllGrabs();
            chain.Freeze(true);

            // 正交相机沿视线移动看不出"凑近"，改成把 size 缩小（画面放大）
            Camera camera = Camera.main;
            _hasCameraSize = camera != null && camera.orthographic;
            _cameraBaseSize = _hasCameraSize ? camera.orthographicSize : 0f;
            float zoomedSize = _hasCameraSize
                ? Mathf.Max(_sniffMinSize, _cameraBaseSize * Mathf.Clamp(_sniffZoomScale, 0.1f, 1f))
                : 0f;
            if (_hasCameraSize)
            {
                yield return StartCoroutine(SizeRoutine(zoomedSize, _sniffTravelSeconds));
            }

            PlaySniffSound();
            MaidChatBubble.Show(_agent, Pick(SniffLines), _lineSeconds);

            float wait = _sniffClip != null ? Mathf.Min(_sniffClip.length, _sniffMaxSeconds) : 1f;
            yield return new WaitForSeconds(wait);

            if (_hasCameraSize)
            {
                yield return StartCoroutine(SizeRoutine(_cameraBaseSize, _sniffTravelSeconds));
                _hasCameraSize = false;
            }

            chain.Freeze(false);
            chain.SetTarget(0f, 0f, false);
            _sniffing = false;
            _sniffRoutine = null;
        }

        /// <summary>正交相机"凑近"只能靠改 size：变小 = 画面放大（位置全程不动）</summary>
        IEnumerator SizeRoutine(float target, float seconds)
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.orthographic)
            {
                yield break;
            }

            float start = camera.orthographicSize;
            float length = Mathf.Max(0.01f, seconds);
            float elapsed = 0f;
            while (elapsed < length)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                camera.orthographicSize = Mathf.Lerp(start, target, t);
                yield return null;
            }

            camera.orthographicSize = target;
        }

        /// <summary>背对相机，把尾巴露给镜头。相机全程不动，所以只能她转。</summary>
        void TurnTailToCamera()
        {
            Turn(false);
        }

        /// <summary>转回来面对相机，退出摸尾巴后接着看正脸。</summary>
        void TurnToCamera()
        {
            Turn(true);
        }

        void Turn(bool faceCamera)
        {
            Camera camera = Camera.main;
            MaidWanderer wanderer = _agent != null ? _agent.Wanderer : null;
            if (camera == null || wanderer == null || !_agent.gameObject.activeInHierarchy)
            {
                return;
            }

            Vector3 toCamera = camera.transform.position - _agent.transform.position;
            wanderer.FaceDirection(faceCamera ? toCamera : -toCamera, _turnSeconds);
        }

        /// <summary>摸尾巴期间关掉常驻摆动：尾巴这几根交给弹簧链写，两边都写会互相盖。</summary>
        void SetParallelLayer(bool enabled)
        {
            if (_agent == null)
            {
                return;
            }

            BedrockAnimationPlayer player = _agent.GetComponent<BedrockAnimationPlayer>();
            if (player != null)
            {
                player.ParallelEnabled = enabled;
            }
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
