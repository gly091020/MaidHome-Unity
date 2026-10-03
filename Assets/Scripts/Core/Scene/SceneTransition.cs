using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.UI;
using MaidHome.Gameplay.House;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MaidHome.Core.Scene
{
    /// <summary>
    /// 切场景的镜头过场：**当前相机先升到天上（默认 y +100）→ 异步加载新场景（加载期间停在高处，
    /// 画面是天空，不会看到黑屏或旧场景闪一下）→ 新场景的相机从同样的高度降回正常取景**。
    /// 开始移动前会把场景根下的 Canvas 全部收起来（屏幕空间覆盖的界面不会跟着飞），整段走完再放出来。
    ///
    /// 两个场景各有自己的主相机，所以升降只能分两段做，而且"抬了多少"必须记在 HouseCameraFitter 里
    /// （它每次 ApplyView 都会带上这个偏移）——否则过场期间房子/女仆重新取景，相机会被一下拽回地面。
    /// 新场景没有 HouseCameraFitter（例如 Portal）时就直接平移相机 transform。
    /// 自己带 DontDestroyOnLoad，所以跨场景活着。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneTransition : MonoBehaviour
    {
        public static SceneTransition Instance { get; private set; }

        [Tooltip("升到多高（世界 Y 方向额外抬高多少）")]
        [SerializeField] private float _liftHeight = 100f;
        [Tooltip("升上去 / 降下来各用多久")]
        [SerializeField] private float _riseSeconds = 0.35f;
        [SerializeField] private float _descendSeconds = 0.5f;
        [Tooltip("加载超过这个时间还没就绪，就把加载进度条亮出来（太快就闪一下反而难看）")]
        [SerializeField] private float _loadingScreenDelay = 0.5f;

        bool _busy;
        float _lift;
        float _appliedOnCamera;
        Camera _camera;
        HouseCameraFitter _fitter;
        readonly List<GameObject> _hiddenCanvases = new List<GameObject>();

        /// <summary>正在过场（按钮可以据此禁用）</summary>
        public bool IsBusy
        {
            get { return _busy; }
        }

        /// <summary>开始一次带动画的切场景。场景名必须是 Build Settings 里有的。</summary>
        public static bool Go(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("切场景：场景名是空的");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning("切场景：Build Settings 里没有这个场景 " + sceneName);
                return false;
            }

            EnsureInstance();
            if (Instance._busy)
            {
                return false;
            }

            Instance.StartCoroutine(Instance.GoRoutine(sceneName));
            return true;
        }

        static void EnsureInstance()
        {
            if (Instance != null)
            {
                return;
            }

            GameObject host = new GameObject("SceneTransition");
            host.AddComponent<SceneTransition>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            ResolveCamera();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
            }
        }

        /// <summary>新场景刚 Awake 完、还没画第一帧时回调：先把新相机顶到同样的高度</summary>
        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            if (!_busy)
            {
                return;
            }

            ResolveCamera();
            ApplyLift(_liftHeight);
            // 新场景的界面也先收起来，等降到位再一起放出来
            HideSceneCanvases();
        }

        IEnumerator GoRoutine(string sceneName)
        {
            _busy = true;
            ResolveCamera();
            HideSceneCanvases();

            // 1. 升上去
            yield return LiftRoutine(_liftHeight, _riseSeconds);

            // 2. 加载：allowSceneActivation=false 能把旧场景留在屏幕上，加载期间停在高处看天
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError("切场景失败：" + sceneName);
                yield return LiftRoutine(0f, _descendSeconds);
                ShowSceneCanvases();
                _busy = false;
                yield break;
            }

            operation.allowSceneActivation = false;
            float waitStart = Time.realtimeSinceStartup;
            bool hintShown = false;
            while (operation.progress < 0.9f)
            {
                if (!hintShown && Time.realtimeSinceStartup - waitStart > _loadingScreenDelay)
                {
                    hintShown = true;
                    LoadingScreen.Register(LoadingScreen.SceneJob, 1f, "正在切换场景…");
                }

                yield return null;
            }

            // 3. 激活新场景（OnSceneLoaded 会把新相机顶起来），然后降下去
            operation.allowSceneActivation = true;
            yield return null;
            yield return LiftRoutine(0f, _descendSeconds);

            ShowSceneCanvases();
            if (hintShown)
            {
                LoadingScreen.Complete(LoadingScreen.SceneJob);
            }

            _busy = false;
        }

        /// <summary>把相机从当前高度匀速挪到 height（按世界 Y 抬升，不改朝向和正交尺寸）</summary>
        IEnumerator LiftRoutine(float height, float seconds)
        {
            float start = _lift;
            float length = Mathf.Max(0.01f, seconds);
            float elapsed = 0f;
            while (elapsed < length)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                ApplyLift(Mathf.Lerp(start, height, t));
                yield return null;
            }

            ApplyLift(height);
        }

        void ResolveCamera()
        {
            Camera camera = Camera.main;
            if (camera != null && _camera != null && camera == _camera)
            {
                return;
            }

            // 换了一台相机（新场景）：_lift 是"这一趟要抬多高"，不跟着相机重置
            _camera = camera;
            _fitter = _camera != null ? _camera.GetComponent<HouseCameraFitter>() : null;
            _appliedOnCamera = _fitter != null ? _fitter.Lift : 0f;
        }

        void ApplyLift(float height)
        {
            if (_camera == null && _fitter == null)
            {
                // 新场景的相机可能晚一帧才出现，再找一次
                ResolveCamera();
            }

            if (_fitter != null)
            {
                _fitter.SetLift(height);
            }
            else if (_camera != null)
            {
                _camera.transform.position += Vector3.up * (height - _appliedOnCamera);
            }

            _appliedOnCamera = height;
            _lift = height;
        }

        /// <summary>
        /// 把当前场景根下的 Canvas 都收起来：镜头飞上去的时候它们（屏幕空间覆盖）还赖在画面上，
        /// 新场景的界面也一样，等整段过场走完再放出来。加载条的 Canvas 跳过（那是我们自己的提示）。
        /// </summary>
        void HideSceneCanvases()
        {
            ShowSceneCanvases();
            Canvas loading = LoadingScreen.Instance != null ? LoadingScreen.Instance.Canvas : null;
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Canvas[] canvases = roots[i].GetComponentsInChildren<Canvas>(true);
                for (int c = 0; c < canvases.Length; c++)
                {
                    Canvas canvas = canvases[c];
                    if (canvas == null || canvas == loading)
                    {
                        continue;
                    }

                    // 嵌套 Canvas 跟着外层一起消失，别重复记
                    if (FindParentCanvas(canvas.transform) != null)
                    {
                        continue;
                    }

                    if (canvas.gameObject.activeSelf)
                    {
                        canvas.gameObject.SetActive(false);
                        _hiddenCanvases.Add(canvas.gameObject);
                    }
                }
            }
        }

        void ShowSceneCanvases()
        {
            for (int i = 0; i < _hiddenCanvases.Count; i++)
            {
                if (_hiddenCanvases[i] != null)
                {
                    _hiddenCanvases[i].SetActive(true);
                }
            }

            _hiddenCanvases.Clear();
        }

        /// <summary>往上找最近的 Canvas（不用 GetComponentInParent 的重载，2020.3 上那些重载不齐）</summary>
        static Canvas FindParentCanvas(Transform transform)
        {
            Transform current = transform != null ? transform.parent : null;
            for (int i = 0; i < 64 && current != null; i++)
            {
                Canvas canvas = current.GetComponent<Canvas>();
                if (canvas != null)
                {
                    return canvas;
                }

                current = current.parent;
            }

            return null;
        }
    }
}
