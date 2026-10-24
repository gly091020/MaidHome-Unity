using System;
using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.UI;
using MaidHome.Gameplay.Bag;
using MaidHome.Gameplay.House;
using MaidHome.Gameplay.Maid;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MaidHome.Core.Scene
{
    /// <summary>
    /// 切场景的镜头过场：**先在原视角异步加载新场景 → 加载就绪后当前相机升到天上（默认 y +100）
    /// → 激活新场景 → 新场景的相机从同样的高度降回正常取景**。
    /// 加载期间镜头不动，避免资源还没准备好就把玩家留在天空视角干等。
    /// 开始切换时先把场景根下的 Canvas（含 Main 的侧边按钮）收起来，等整段走完再放出来；
    /// 镜头本身仍等目标场景资源加载完成后再移动。
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
            return Go(sceneName, null);
        }

        /// <summary>
        /// beforeMotion 在目标场景加载就绪、镜头尚未移动时调用。
        /// AR 退出用它先关闭会覆盖 transform 的姿态驱动。
        /// </summary>
        public static bool Go(string sceneName, Action beforeMotion)
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

            Instance.StartCoroutine(Instance.GoRoutine(sceneName, beforeMotion));
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

        IEnumerator GoRoutine(string sceneName, Action beforeMotion)
        {
            _busy = true;

            // 1. 先在原视角加载。Main 的侧边按钮先滑出，再把场景 UI 收起来。
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError("切场景失败：" + sceneName);
                _busy = false;
                yield break;
            }

            operation.allowSceneActivation = false;
            BagPanel bag = FindObjectOfType<BagPanel>();
            if (bag != null)
            {
                yield return bag.HideSideButtonsForSceneTransition();
            }

            HideSceneCanvases();
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

            // 2. 新场景资源已经就绪，此时才开始抬镜头。
            if (beforeMotion != null)
            {
                beforeMotion();
            }

            yield return null;
            ResolveCamera();
            yield return LiftRoutine(_liftHeight, _riseSeconds);

            // 3. 在最高点激活新场景（OnSceneLoaded 会把新相机顶到同样高度），等完全激活后再降。
            operation.allowSceneActivation = true;
            while (!operation.isDone)
            {
                yield return null;
            }

            // 场景激活不代表内容就绪：主场景还要等 HouseSpawner 完成 glTF、格表、NavMesh
            // 和女仆恢复。加载未完成时保持在最高处，别先降回地面。
            yield return null;
            HouseSpawner houseSpawner = FindObjectOfType<HouseSpawner>();
            while (houseSpawner != null && !houseSpawner.LoadFinished)
            {
                yield return null;
            }

            MaidManager maidManager = FindObjectOfType<MaidManager>();
            while (maidManager != null && !maidManager.RestoreFinished)
            {
                yield return null;
            }

            // 房屋任务完成后再结束场景任务，让进度条走到 100% 并完成淡出，
            // 避免镜头已经落地时屏幕上还残留未完成的进度。
            if (hintShown)
            {
                LoadingScreen.Complete(LoadingScreen.SceneJob);
            }

            while (LoadingScreen.IsShowing)
            {
                yield return null;
            }

            yield return LiftRoutine(0f, _descendSeconds);

            ShowSceneCanvases();

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
