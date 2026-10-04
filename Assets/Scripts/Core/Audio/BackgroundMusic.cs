using System.Collections;
using UnityEngine;

namespace MaidHome.Core.Audio
{
    /// <summary>
    /// 背景音乐：循环播一条 clip，自己 DontDestroyOnLoad，所以切场景不会断。
    /// 场景里放一份就行（多放的会在 Awake 里自己退场）；音量 Inspector 可调，运行时改 Volume 立即生效。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BackgroundMusic : MonoBehaviour
    {
        public static BackgroundMusic Instance { get; private set; }

        [Tooltip("循环播放的曲子")]
        [SerializeField] private AudioClip _clip;
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 0.6f;
        [Tooltip("进 Play 就自动播；关掉就等外部调 Play()")]
        [SerializeField] private bool _playOnStart = true;
        [Tooltip("淡入秒数，0 = 直接满音量")]
        [SerializeField] private float _fadeInSeconds = 0.5f;

        AudioSource _source;
        Coroutine _fadeRoutine;
        float _fade = 1f;

        public bool IsPlaying
        {
            get { return _source != null && _source.isPlaying; }
        }

        /// <summary>曲子。正在播的时候换一条会从头重播。</summary>
        public AudioClip Clip
        {
            get { return _clip; }
            set
            {
                if (_clip == value)
                {
                    return;
                }

                _clip = value;
                if (IsPlaying)
                {
                    Play();
                }
            }
        }

        /// <summary>音量 0~1，立即生效（淡入途中也按这个乘）</summary>
        public float Volume
        {
            get { return _volume; }
            set
            {
                _volume = Mathf.Clamp01(value);
                ApplyVolume();
            }
        }

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            if (_source != null)
            {
                // 场景里手挂的 AudioSource 默认 playOnAwake，会绕过这里的音量直接响
                _source.playOnAwake = false;
                _source.loop = true;
                _source.spatialBlend = 0f;
            }

            if (Instance != null && Instance != this)
            {
                // 场景里又放了一份：只退掉自己这个组件，别把整个物体连别的组件一起删了
                if (_source != null)
                {
                    _source.Stop();
                }

                _source = null;
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ApplyVolume();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Start()
        {
            if (_playOnStart)
            {
                Play();
            }
        }

        /// <summary>从头播，已经在播也会重头来</summary>
        public void Play()
        {
            if (_source == null)
            {
                return;
            }

            if (_clip == null)
            {
                Debug.LogWarning("BackgroundMusic 没接 AudioClip，不播", this);
                return;
            }

            _source.clip = _clip;
            _fade = _fadeInSeconds > 0f ? 0f : 1f;
            ApplyVolume();
            _source.Play();

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = _fadeInSeconds > 0f ? StartCoroutine(FadeInRoutine()) : null;
        }

        public void Stop()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (_source != null)
            {
                _source.Stop();
            }
        }

        void ApplyVolume()
        {
            if (_source != null)
            {
                _source.volume = _volume * _fade;
            }
        }

        IEnumerator FadeInRoutine()
        {
            float length = Mathf.Max(0.01f, _fadeInSeconds);
            float elapsed = 0f;
            while (elapsed < length)
            {
                elapsed += Time.unscaledDeltaTime;
                _fade = Mathf.Clamp01(elapsed / length);
                ApplyVolume();
                yield return null;
            }

            _fade = 1f;
            ApplyVolume();
            _fadeRoutine = null;
        }
    }
}
