using System;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 火焰贴图切换：默认自动收集子物体的所有 Renderer，用 MaterialPropertyBlock 改 _MainTex，
    /// 不复制/破坏原材质。默认自动加载 FireFrames 并按帧率循环播放。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireTextureSwitcher : MonoBehaviour
    {
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [Tooltip("可直接在 Inspector 选一张火焰贴图；留空保持预制体原贴图")]
        [SerializeField] private Texture2D _texture;
        [Tooltip("可选的多张火焰贴图，SetTextureIndex 用")]
        [SerializeField] private Texture2D[] _textures;
        [Tooltip("_textures 为空时从 Resources 的这个目录自动加载，按文件名排序")]
        [SerializeField] private string _resourceFolder = "FireFrames";
        [SerializeField] private bool _autoLoadTextures = true;
        [Tooltip("自动加载后默认显示第一帧")]
        [SerializeField] private bool _useFirstTextureOnAwake = true;
        [Header("自动播放")]
        [SerializeField] private bool _autoPlay = true;
        [SerializeField] private float _framesPerSecond = 20f;
        [SerializeField] private bool _useUnscaledTime;
        [Tooltip("留空就自动收集子物体的 Renderer")]
        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private bool _applyOnEnable = true;

        MaterialPropertyBlock _block;
        bool _hasOverride;
        int _index;
        float _timer;

        public Texture2D CurrentTexture
        {
            get { return _texture; }
        }

        public int TextureCount
        {
            get { return _textures != null ? _textures.Length : 0; }
        }

        void Awake()
        {
            LoadTextures();
            if (_texture == null && (_autoPlay || _useFirstTextureOnAwake)
                && _textures != null && _textures.Length > 0)
            {
                _texture = _textures[0];
                _index = 0;
            }

            CollectRenderers();
            Apply();
        }

        void OnEnable()
        {
            if (_applyOnEnable)
            {
                CollectRenderers();
                LoadTextures();
                Apply();
            }
        }

        void Update()
        {
            if (!_autoPlay || _textures == null || _textures.Length < 2 || _framesPerSecond <= 0f)
            {
                return;
            }

            _timer += _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float interval = 1f / _framesPerSecond;
            if (_timer < interval)
            {
                return;
            }

            int steps = Mathf.Max(1, Mathf.FloorToInt(_timer / interval));
            _timer -= steps * interval;
            _index = (_index + steps) % _textures.Length;
            _texture = _textures[_index];
            Apply();
        }

        /// <summary>切换火焰贴图；传 null 会清除覆盖，回到预制体原贴图。</summary>
        public void SetTexture(Texture2D texture)
        {
            _texture = texture;
            if (_textures != null)
            {
                int index = Array.IndexOf(_textures, texture);
                if (index >= 0)
                {
                    _index = index;
                }
            }

            _timer = 0f;
            Apply();
        }

        /// <summary>从 _textures 里选一张。</summary>
        public void SetTextureIndex(int index)
        {
            if (_textures == null || index < 0 || index >= _textures.Length)
            {
                Debug.LogWarning("火焰贴图索引越界: " + index, this);
                return;
            }

            _index = index;
            SetTexture(_textures[index]);
        }

        /// <summary>循环切到下一张，方便做火焰帧动画。</summary>
        public void NextTexture()
        {
            LoadTextures();
            if (_textures == null || _textures.Length == 0)
            {
                return;
            }

            SetTextureIndex((_index + 1) % _textures.Length);
        }

        public void SetAutoPlay(bool autoPlay)
        {
            _autoPlay = autoPlay;
            _timer = 0f;
        }

        public void ClearTexture()
        {
            SetTexture(null);
        }

        [ContextMenu("应用火焰贴图")]
        void ApplyFromInspector()
        {
            CollectRenderers();
            Apply();
        }

        void CollectRenderers()
        {
            if (_renderers != null && _renderers.Length > 0)
            {
                return;
            }

            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        void LoadTextures()
        {
            if (!_autoLoadTextures || (_textures != null && _textures.Length > 0)
                || string.IsNullOrEmpty(_resourceFolder))
            {
                return;
            }

            _textures = Resources.LoadAll<Texture2D>(_resourceFolder);
            Array.Sort(_textures, (a, b) => string.CompareOrdinal(a.name, b.name));
            _index = 0;
            if (_texture != null)
            {
                int index = Array.IndexOf(_textures, _texture);
                if (index >= 0)
                {
                    _index = index;
                }
            }
        }

        void Apply()
        {
            CollectRenderers();
            if (_renderers == null || _renderers.Length == 0)
            {
                return;
            }

            if (_texture == null)
            {
                if (!_hasOverride)
                {
                    return;
                }

                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                    {
                        _renderers[i].SetPropertyBlock(null);
                    }
                }

                _hasOverride = false;
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                _block.Clear();
                renderer.GetPropertyBlock(_block);
                _block.SetTexture(MainTexId, _texture);
                renderer.SetPropertyBlock(_block);
            }

            _hasOverride = true;
        }
    }
}
