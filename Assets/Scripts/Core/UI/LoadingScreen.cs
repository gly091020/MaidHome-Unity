using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Core.UI
{
    /// <summary>
    /// 加载进度条。谁要加载就 Register 一个 job，加载完 Complete：
    /// 有 job 才开始显示，全部完成 + 过了最短显示时间才淡出；超时（默认 30 秒）也会放行，
    /// 免得哪一步卡住把玩家关在加载界面里。没有 job 的时候它完全不出现在画面上。
    ///
    /// 场景里自己做了面板就把 Root / Label / Fill 接上，一个都不接就在运行时生成一个盖满全屏的
    /// （自带 Canvas，sortingOrder 很高，压过现有 GUI）。全屏那层吃射线，加载期间点画面不会误触。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadingScreen : MonoBehaviour
    {
        public const string HouseJob = "house";
        public const string MaidJob = "maids";
        /// <summary>从背包里放置东西（女仆要读缓存/第一次还要转换，慢的话好几秒）</summary>
        public const string PlaceJob = "place";
        /// <summary>切场景（异步加载超过阈值才会亮）</summary>
        public const string SceneJob = "scene";

        [Header("自定义 UI（不接就自动生成）")]
        [Tooltip("面板根节点，显示/隐藏只开关它")]
        [SerializeField] private GameObject _root;
        [Tooltip("提示文字，留空就不显示文字")]
        [SerializeField] private Text _label;
        [Tooltip("进度条填充。Filled 类型写 fillAmount；其它类型按宽度铺（改 anchorMax.x）")]
        [SerializeField] private Image _fill;

        [Header("节奏")]
        [Tooltip("再快也要显示这么久，免得闪一下")]
        [SerializeField] private float _minimumSeconds = 0.45f;
        [Tooltip("没完成也要放行的秒数")]
        [SerializeField] private float _timeoutSeconds = 30f;
        [Tooltip("忙的时候条最多先爬过目标进度多少")]
        [SerializeField] private float _creepAhead = 0.12f;
        [Tooltip("条的爬行速度（每秒）")]
        [SerializeField] private float _creepSpeed = 0.35f;
        [Tooltip("淡出秒数，0 = 直接关")]
        [SerializeField] private float _fadeSeconds = 0.25f;

        sealed class Job
        {
            public string Key;
            public string Label;
            public float Weight;
            public float Progress;
        }

        public static LoadingScreen Instance { get; private set; }

        /// <summary>加载面板挂在哪个 Canvas 上（切场景过场要跳过它，别把进度条一起收起来）</summary>
        public Canvas Canvas { get; private set; }

        readonly List<Job> _jobs = new List<Job>();
        CanvasGroup _group;
        RectTransform _fillRect;
        bool _showing;
        bool _finishing;
        float _shownAt;
        float _display;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // 场景里没人放就自己建一个。没有 job 注册时它一直藏着，所以放哪个场景都无所谓
            if (FindObjectOfType<LoadingScreen>() == null)
            {
                new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
            }
        }

        public static void Register(string key, float weight, string label)
        {
            LoadingScreen screen = Ensure();
            if (screen != null)
            {
                screen.RegisterJob(key, weight, label);
            }
        }

        /// <summary>一个 job 内部的进度（0~1），不报也没关系，完成时算 1</summary>
        public static void Report(string key, float progress)
        {
            if (Instance != null)
            {
                Instance.ReportJob(key, progress);
            }
        }

        public static void Complete(string key)
        {
            if (Instance != null)
            {
                Instance.CompleteJob(key);
            }
        }

        /// <summary>场景里没有就现建一个：AddComponent 会同步走 Awake，所以这里回来时 Instance 一定有了</summary>
        static LoadingScreen Ensure()
        {
            if (Instance == null)
            {
                new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
            }

            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Update()
        {
            if (!_showing || _finishing || _root == null)
            {
                return;
            }

            float target = TargetProgress();
            // 目标后面留一点余量：解析 glTF 这种没法报进度的长任务期间，条也能慢慢爬
            float goal = AllComplete ? 1f : Mathf.Min(target + _creepAhead, 1f);
            _display = Mathf.MoveTowards(_display, goal, Mathf.Max(0.0001f, _creepSpeed) * Time.unscaledDeltaTime);
            UpdateVisual();

            if (AllComplete)
            {
                if (Time.unscaledTime - _shownAt >= Mathf.Max(0f, _minimumSeconds))
                {
                    StartCoroutine(FinishRoutine());
                }
            }
            else if (Time.unscaledTime - _shownAt > Mathf.Max(1f, _timeoutSeconds))
            {
                Debug.LogWarning("加载超过 " + _timeoutSeconds + " 秒还没结束，先放行（剩下的继续在后台跑）");
                StartCoroutine(FinishRoutine());
            }
        }

        void RegisterJob(string key, float weight, string label)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (_finishing || AllComplete)
            {
                // 上一轮已经收尾了：整个重置，当作新的一轮加载（例如启动完又切房子）
                StopAllCoroutines();
                _finishing = false;
                _showing = false;
                _jobs.Clear();
                _display = 0f;
            }

            Job job = Find(key);
            if (job == null)
            {
                job = new Job();
                job.Key = key;
                _jobs.Add(job);
            }

            job.Weight = Mathf.Max(0.01f, weight);
            job.Label = label;
            job.Progress = 0f;
            Show();
        }

        void ReportJob(string key, float progress)
        {
            Job job = Find(key);
            if (job != null)
            {
                job.Progress = Mathf.Clamp01(progress);
            }
        }

        void CompleteJob(string key)
        {
            Job job = Find(key);
            if (job == null)
            {
                return;
            }

            job.Progress = 1f;
        }

        Job Find(string key)
        {
            for (int i = 0; i < _jobs.Count; i++)
            {
                if (_jobs[i].Key == key)
                {
                    return _jobs[i];
                }
            }

            return null;
        }

        bool AllComplete
        {
            get
            {
                if (_jobs.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < _jobs.Count; i++)
                {
                    if (_jobs[i].Progress < 1f)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        float TargetProgress()
        {
            float total = 0f;
            float done = 0f;
            for (int i = 0; i < _jobs.Count; i++)
            {
                total += _jobs[i].Weight;
                done += _jobs[i].Weight * Mathf.Clamp01(_jobs[i].Progress);
            }

            return total > 0f ? done / total : 0f;
        }

        void Show()
        {
            if (_root == null)
            {
                return;
            }

            if (!_showing)
            {
                _showing = true;
                _shownAt = Time.unscaledTime;
                _display = 0f;
            }

            _root.SetActive(true);
            if (_group != null)
            {
                _group.alpha = 1f;
            }

            UpdateVisual();
        }

        IEnumerator FinishRoutine()
        {
            if (_finishing)
            {
                yield break;
            }

            _finishing = true;

            // 先把条推到满，别在 90% 上直接淡出
            while (_display < 0.999f)
            {
                _display = Mathf.MoveTowards(_display, 1f,
                    Mathf.Max(1f, _creepSpeed * 4f) * Time.unscaledDeltaTime);
                UpdateVisual();
                yield return null;
            }

            float fade = Mathf.Max(0f, _fadeSeconds);
            while (fade > 0f && _group != null)
            {
                fade -= Time.unscaledDeltaTime;
                _group.alpha = Mathf.Clamp01(fade / Mathf.Max(0.0001f, _fadeSeconds));
                yield return null;
            }

            _jobs.Clear();
            _display = 0f;
            _showing = false;
            _finishing = false;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        void UpdateVisual()
        {
            if (_fill != null && _fillRect != null)
            {
                if (_fill.type == Image.Type.Filled)
                {
                    _fill.fillAmount = _display;
                }
                else
                {
                    Vector2 anchorMax = _fillRect.anchorMax;
                    anchorMax.x = Mathf.Clamp01(_display);
                    _fillRect.anchorMax = anchorMax;
                }
            }

            if (_label != null)
            {
                string text = CurrentLabel();
                if (_label.text != text)
                {
                    _label.text = text;
                }
            }
        }

        string CurrentLabel()
        {
            for (int i = 0; i < _jobs.Count; i++)
            {
                if (_jobs[i].Progress < 1f && !string.IsNullOrEmpty(_jobs[i].Label))
                {
                    return _jobs[i].Label;
                }
            }

            return _jobs.Count > 0 ? "马上就绪…" : "";
        }

        void Build()
        {
            if (_root == null)
            {
                Generate();
            }

            if (_root == null)
            {
                return;
            }

            _group = _root.GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = _root.AddComponent<CanvasGroup>();
            }

            _fillRect = _fill != null ? _fill.rectTransform : null;
            Canvas = FindParentCanvas(_root.transform);

            // 直接挂在 Canvas 上（没有父级）的情况
            if (Canvas == null)
            {
                Canvas = _root.GetComponent<Canvas>();
            }

            _root.SetActive(false);
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

        /// <summary>没有现成面板就自己搭一个：全屏底色 + 进度条 + 一行字。</summary>
        void Generate()
        {
            GameObject canvasObject = new GameObject("LoadingScreen (自动生成)", typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            RectTransform panelRect = _root.GetComponent<RectTransform>();
            panelRect.SetParent(canvasObject.transform, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0.04f, 0.045f, 0.06f, 1f);

            GameObject barObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            RectTransform barRect = barObject.GetComponent<RectTransform>();
            barRect.SetParent(panelRect, false);
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.pivot = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = new Vector2(560f, 24f);
            barRect.anchoredPosition = new Vector2(0f, -40f);
            barObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            _fillRect = fillObject.GetComponent<RectTransform>();
            _fillRect.SetParent(barRect, false);
            _fillRect.anchorMin = Vector2.zero;
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;
            _fill = fillObject.GetComponent<Image>();
            _fill.color = new Color(0.55f, 0.85f, 1f, 1f);

            _label = CreateText(panelRect, "Label", 28, TextAnchor.MiddleCenter);
            RectTransform labelRect = _label.rectTransform;
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(820f, 60f);
            labelRect.anchoredPosition = new Vector2(0f, 20f);
            _label.text = "正在加载…";

        }

        static Text CreateText(RectTransform parent, string name, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }
    }
}
