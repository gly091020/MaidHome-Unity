using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MaidHome.Gameplay.Maid;
using UnityEngine;
using UnityEngine.Networking;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>音频加载和播放的运行时宿主：缓存 AudioClip，管理 3D 音源池。</summary>
    [DisallowMultipleComponent]
    public sealed class MaidAudioRuntime : MonoBehaviour
    {
        public static MaidAudioRuntime Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject runtime = new GameObject("MaidAudioRuntime");
                    DontDestroyOnLoad(runtime);
                    _instance = runtime.AddComponent<MaidAudioRuntime>();
                }

                return _instance;
            }
        }

        static MaidAudioRuntime _instance;

        readonly Dictionary<string, AudioClip> _clips =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<Action<AudioClip>>> _pending =
            new Dictionary<string, List<Action<AudioClip>>>(StringComparer.OrdinalIgnoreCase);
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AudioSource _previewSource;
        bool _warmUpStarted;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            StartWarmUp();
        }

        /// <summary>
        /// 音频设备、解码器、声音包索引都是"第一次用到才初始化"，第一次播放声音会卡一下。
        /// 启动时就建出来，把这些一次性开销塞进加载流程里（Attribute 保证开场就执行一次）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            Instance.StartWarmUp();
        }

        void StartWarmUp()
        {
            if (_warmUpStarted)
            {
                return;
            }

            _warmUpStarted = true;
            StartCoroutine(WarmUpRoutine());
        }

        void Update()
        {
            MaidSoundLibrary.RefreshIndex();
        }

        IEnumerator WarmUpRoutine()
        {
            GameObject holder = new GameObject("MaidAudioWarmup");
            holder.transform.SetParent(transform, false);
            AudioSource source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;

            // 音频输出设备是第一次播放时才初始化的，先放一段静音把它唤醒
            AudioClip silence = AudioClip.Create("maidhome_warmup", 256, 1, 44100, false);
            silence.SetData(new float[256], 0);
            source.clip = silence;
            source.Play();
            // 顺手预热采样放大那条路（GetData / AudioClip.Create / SetData）
            AudioGain.Amplify(silence, 2f);

            yield return null;
            source.Stop();
            Destroy(holder);
            Destroy(silence);

            MaidSoundLibrary.RefreshIndex();

            // 先随便解码一条：解码器插件和 UnityWebRequest 的首次初始化也在这一步
            string probe = MaidSoundLibrary.PickAnyPath();
            if (!string.IsNullOrEmpty(probe) && File.Exists(probe))
            {
                LoadClip(probe, WarmUpClipReady);
            }
        }

        static void WarmUpClipReady(AudioClip clip)
        {
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public void PlayVoice(MaidAgent agent, string soundPackId, string eventId, bool test,
            float volumeScale = 1f)
        {
            string path = ResolvePath(soundPackId, eventId);
            if (path == null || agent == null)
            {
                return;
            }

            LoadClip(path, clip =>
            {
                if (clip == null || agent == null)
                {
                    return;
                }

                MaidAudioVoice voice = agent.GetComponent<MaidAudioVoice>();
                if (voice == null)
                {
                    voice = agent.gameObject.AddComponent<MaidAudioVoice>();
                }

                // 倍率走采样放大：AudioSource.volume 顶到 1 就上不去了
                voice.Play(AudioGain.Amplify(clip, volumeScale),
                    MaidAudioService.MasterVolume * MaidAudioService.VoiceVolume * MaidAudioService.VoiceGain, 1f);
            });
        }

        public void PlayAt(string soundPackId, string eventId, Vector3 position, float volume, float pitch,
            bool test)
        {
            string path = ResolvePath(soundPackId, eventId);
            if (path == null)
            {
                return;
            }

            LoadClip(path, clip =>
            {
                if (clip == null)
                {
                    return;
                }

                AudioSource source = GetPooledSource();
                source.transform.position = position;
                source.volume = Mathf.Clamp01(volume * MaidAudioService.MasterVolume
                    * MaidAudioService.PositionalVolume);
                source.pitch = pitch;
                source.PlayOneShot(clip);
            });
        }

        public void Preview(string soundPackId, string eventId)
        {
            string path = ResolvePath(soundPackId, eventId);
            if (path == null)
            {
                return;
            }

            LoadClip(path, clip =>
            {
                if (clip == null)
                {
                    return;
                }

                if (_previewSource == null)
                {
                    GameObject preview = new GameObject("MaidAudioPreview");
                    preview.transform.SetParent(transform, false);
                    _previewSource = preview.AddComponent<AudioSource>();
                    _previewSource.playOnAwake = false;
                    _previewSource.spatialBlend = 0f;
                    _previewSource.volume = MaidAudioService.PreviewVolume;
                }

                _previewSource.PlayOneShot(clip);
            });
        }

        string ResolvePath(string soundPackId, string eventId)
        {
            List<string> paths;
            if (!MaidSoundLibrary.TryGetPaths(soundPackId, eventId, out paths) || paths.Count == 0)
            {
                string key = soundPackId + "|" + eventId;
                if (_warned.Add(key))
                {
                    Debug.LogWarning("女仆声音包缺少事件: " + soundPackId + " / " + eventId);
                }

                return null;
            }

            return paths[UnityEngine.Random.Range(0, paths.Count)];
        }

        void LoadClip(string path, Action<AudioClip> callback)
        {
            AudioClip cached;
            if (_clips.TryGetValue(path, out cached))
            {
                callback(cached);
                return;
            }

            List<Action<AudioClip>> waiters;
            if (_pending.TryGetValue(path, out waiters))
            {
                waiters.Add(callback);
                return;
            }

            waiters = new List<Action<AudioClip>>();
            waiters.Add(callback);
            _pending[path] = waiters;
            StartCoroutine(LoadClipRoutine(path));
        }

        IEnumerator LoadClipRoutine(string path)
        {
            AudioClip clip = null;
            string url = new Uri(path).AbsoluteUri;
            DownloadHandlerAudioClip handler = new DownloadHandlerAudioClip(url, AudioType.OGGVORBIS);
            // 语音很短，直接整段解码；流式 clip 在 request 释放后可能变成空文件
#pragma warning disable 618
            handler.streamAudio = false;
#pragma warning restore 618
            using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET,
                handler, null))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    clip = handler.audioClip;
                }
                else
                {
                    Debug.LogWarning("加载女仆语音失败: " + path + " / " + request.error);
                }
            }

            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
                while (clip != null && clip.loadState == AudioDataLoadState.Loading)
                {
                    yield return null;
                }
            }

            if (clip != null && (clip.loadState == AudioDataLoadState.Failed || clip.samples <= 0))
            {
                Debug.LogWarning("女仆语音解码后为空: " + path + " / state=" + clip.loadState
                    + " samples=" + clip.samples);
                clip = null;
            }

            if (clip != null)
            {
                clip.name = Path.GetFileNameWithoutExtension(path);
                _clips[path] = clip;
            }

            List<Action<AudioClip>> waiters = _pending[path];
            _pending.Remove(path);
            for (int i = 0; i < waiters.Count; i++)
            {
                waiters[i](clip);
            }
        }

        AudioSource GetPooledSource()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (!_pool[i].isPlaying)
                {
                    return _pool[i];
                }
            }

            GameObject sourceObject = new GameObject("MaidAudioSource");
            sourceObject.transform.SetParent(transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 2f;
            source.maxDistance = 16f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            _pool.Add(source);
            return source;
        }
    }
}
