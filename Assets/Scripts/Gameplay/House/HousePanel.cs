using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.Input;
using MaidHome.Gameplay.Bag;
using MaidHome.Interop.House;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 房子列表面板：一栋一行，点一行就切过去（切换期间行会置灰）。
    /// 显隐和 BagPanel 一个套路：代码驱动滑出/滑入，**场景里摆的位置就是显示位置**，
    /// 滑完才 SetActive(false)。门口那个按钮的 onClick 接 Toggle() 就能展开/收起。
    ///
    /// 列表项优先用 _rowPrefab（挂 HouseRow 组件）；不接就按行高用代码生成，样式能看但不精致。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HousePanel : MonoBehaviour
    {
        public static HousePanel Instance { get; private set; }

        [Tooltip("房子数据来源，留空会在场景里找 HouseSwitcher")]
        [SerializeField] private HouseSwitcher _switcher;

        [Tooltip("面板根节点，显示/隐藏就是开关它")]
        [SerializeField] private GameObject _panelRoot;

        [Header("显隐动画（代码驱动，不用 Animator）")]
        [Tooltip("滑动的对象，留空就用 Panel Root")]
        [SerializeField] private RectTransform _slideRect;
        [Tooltip("隐藏时相对显示位置挪多少，负 y 是往下")]
        [SerializeField] private Vector2 _hiddenOffset = new Vector2(0f, -600f);
        [SerializeField] private float _slideSeconds = 0.2f;
        [Tooltip("展开时点面板外面也收起来")]
        [SerializeField] private bool _closeOnEmptyClick = true;
        [SerializeField] private bool _startVisible;

        [Header("列表")]
        [Tooltip("列表父节点，留空会在面板下自动建一个（带 VerticalLayoutGroup）")]
        [SerializeField] private RectTransform _content;
        [Tooltip("行预制体（挂 HouseRow），留空就用代码生成")]
        [SerializeField] private GameObject _rowPrefab;
        [SerializeField] private float _rowHeight = 72f;
        [SerializeField] private int _fontSize = 24;

        [Header("可选文字槽位")]
        [Tooltip("标题，例如显示房子数量")]
        [SerializeField] private Text _titleText;
        [Tooltip("状态文字：切换中 / 失败原因")]
        [SerializeField] private Text _statusText;

        [SerializeField] private Color _rowColor = new Color(0.16f, 0.16f, 0.2f, 0.9f);
        [SerializeField] private Color _currentRowColor = new Color(0.1f, 0.32f, 0.2f, 0.9f);
        [SerializeField] private Color _lockedRowColor = new Color(0.12f, 0.12f, 0.15f, 0.55f);

        readonly List<GameObject> _rows = new List<GameObject>();
        bool _visible;
        bool _listening;
        bool _warnedPrefab;
        Vector2 _shownPosition;
        Coroutine _slideRoutine;

        public bool IsVisible { get { return _visible; } }

        void Awake()
        {
            Instance = this;
            if (_panelRoot == null)
            {
                _panelRoot = gameObject;
            }

            if (_slideRect == null)
            {
                _slideRect = _panelRoot.transform as RectTransform;
            }

            if (_slideRect != null)
            {
                _shownPosition = _slideRect.anchoredPosition;
            }

            ResolveSwitcher();
        }

        void Start()
        {
            // 初始状态直接落位，不播一段
            SetVisible(_startVisible, true);
        }

        void OnEnable()
        {
            ResolveSwitcher();
            Subscribe();
            Refresh();
        }

        void OnDisable()
        {
            Unsubscribe();
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
            if (!_closeOnEmptyClick || !_visible)
            {
                return;
            }

            PointerInput.Pointer pointer = PointerInput.Primary;
            if (!pointer.Pressed || pointer.OverUi)
            {
                return;
            }

            if (RectContains(_panelRoot.transform as RectTransform, pointer.Position))
            {
                return;
            }

            SetVisible(false);
        }

        /// <summary>按钮上接这个：没展开就展开，展开了就收起来</summary>
        public void Toggle()
        {
            SetVisible(!_visible);
        }

        /// <summary>按钮上接这个：展开（已经展开就什么都不做）</summary>
        public void Expand()
        {
            SetVisible(true);
        }

        /// <summary>按钮上接这个：收起</summary>
        public void Collapse()
        {
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            SetVisible(visible, false);
        }

        public void SetVisible(bool visible, bool immediate)
        {
            if (_panelRoot == null)
            {
                return;
            }

            // 房子和背包面板互斥：展开一个就把另一个收起来
            // （已经收起来的就别再收一次：那个面板的物体是关着的，起不了协程，会报
            //   "Coroutine couldn't be started because the game object is inactive"）
            if (visible && BagPanel.Instance != null && BagPanel.Instance.IsVisible)
            {
                BagPanel.Instance.SetVisible(false);
            }

            _visible = visible;
            if (visible)
            {
                _panelRoot.SetActive(true);
                Refresh();
            }

            if (_slideRect == null || immediate || _slideSeconds <= 0.01f)
            {
                ApplySlide(visible ? 0f : 1f);
                FinishSlide(visible);
                return;
            }

            if (!_panelRoot.activeInHierarchy)
            {
                // 面板本来就是关着的（或者整个 Canvas 关着），没得滑：直接落位
                ApplySlide(visible ? 0f : 1f);
                FinishSlide(visible);
                return;
            }

            if (_slideRoutine != null)
            {
                StopCoroutine(_slideRoutine);
            }

            _slideRoutine = StartCoroutine(SlideRoutine(visible));
        }

        IEnumerator SlideRoutine(bool visible)
        {
            Vector2 start = _slideRect.anchoredPosition;
            Vector2 end = visible ? _shownPosition : _shownPosition + _hiddenOffset;
            float length = Mathf.Max(0.01f, _slideSeconds);
            float elapsed = 0f;
            while (elapsed < length)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                _slideRect.anchoredPosition = Vector2.Lerp(start, end, t);
                yield return null;
            }

            _slideRoutine = null;
            FinishSlide(visible);
        }

        void ApplySlide(float t)
        {
            if (_slideRect != null)
            {
                _slideRect.anchoredPosition = Vector2.Lerp(_shownPosition, _shownPosition + _hiddenOffset, t);
            }
        }

        /// <summary>收完才把面板关掉：让它在滑出去的整个过程里都是可见的</summary>
        void FinishSlide(bool visible)
        {
            if (!visible && _panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        void ResolveSwitcher()
        {
            if (_switcher != null)
            {
                return;
            }

            _switcher = HouseSwitcher.Instance;
            if (_switcher == null)
            {
                _switcher = FindObjectOfType<HouseSwitcher>();
            }
        }

        void Subscribe()
        {
            if (_listening || _switcher == null)
            {
                return;
            }

            _listening = true;
            _switcher.Changed += Refresh;
        }

        void Unsubscribe()
        {
            if (!_listening || _switcher == null)
            {
                return;
            }

            _listening = false;
            _switcher.Changed -= Refresh;
        }

        void Refresh()
        {
            ResolveSwitcher();
            Subscribe();
            ClearRows();

            if (_switcher == null || _panelRoot == null)
            {
                return;
            }

            IList<HouseSaveData> houses = _switcher.Houses;
            EnsureContent();

            for (int i = 0; i < houses.Count; i++)
            {
                CreateRow(houses[i]);
            }

            if (_titleText != null)
            {
                _titleText.text = "房子（" + houses.Count + "）";
            }

            if (_statusText != null)
            {
                _statusText.text = string.IsNullOrEmpty(_switcher.Message) ? "" : _switcher.Message;
            }
        }

        void CreateRow(HouseSaveData house)
        {
            bool isCurrent = _switcher.IsCurrent(house.Id);
            bool locked = _switcher.IsSwitching || isCurrent;
            string id = house.Id;

            if (_rowPrefab != null)
            {
                HouseRow prefab = _rowPrefab.GetComponent<HouseRow>();
                if (prefab != null)
                {
                    HouseRow instance = Instantiate(prefab, _content, false);
                    instance.Bind(house, isCurrent, locked, () => OnRowClicked(id), () => OnDeleteRequested(id));
                    _rows.Add(instance.gameObject);
                    return;
                }

                if (!_warnedPrefab)
                {
                    _warnedPrefab = true;
                    Debug.LogWarning("行预制体上没有 HouseRow 组件，改用代码生成的行：" + _rowPrefab.name,
                        _rowPrefab);
                }
            }

            GameObject row = new GameObject("House " + HouseSwitcher.DisplayName(house),
                typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(_content, false);

            Image background = row.GetComponent<Image>();
            background.color = isCurrent ? _currentRowColor : locked ? _lockedRowColor : _rowColor;

            LayoutElement element = row.AddComponent<LayoutElement>();
            element.minHeight = _rowHeight;
            element.preferredHeight = _rowHeight;

            Text label = CreateText(row.transform, "Label", _fontSize, TextAnchor.MiddleLeft);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 0f);
            // 右边给自动生成的「删除」按钮留出位置
            labelRect.offsetMax = new Vector2(-124f, 0f);
            label.text = HouseSwitcher.DisplayName(house) + (isCurrent ? "（当前）" : "");

            Button button = row.GetComponent<Button>();
            HouseRow view = row.AddComponent<HouseRow>();
            view.BindGenerated(background, label, button, null);
            view.Bind(house, isCurrent, locked, () => OnRowClicked(id), () => OnDeleteRequested(id));
            _rows.Add(row);
        }

        void OnRowClicked(string id)
        {
            if (_switcher == null)
            {
                return;
            }

            _switcher.Switch(id);
        }

        /// <summary>行上的「删除」已经点过两下确认了，这里只管删 + 把成败写进状态栏</summary>
        void OnDeleteRequested(string id)
        {
            if (_switcher == null)
            {
                return;
            }

            string error;
            if (!_switcher.Delete(id, out error))
            {
                if (_statusText != null)
                {
                    _statusText.text = error;
                }

                Debug.LogWarning("[房子] " + error);
                return;
            }

            Refresh();
        }

        void EnsureContent()
        {
            if (_content == null)
            {
                GameObject contentObject = new GameObject("HouseList", typeof(RectTransform));
                contentObject.transform.SetParent(_panelRoot.transform, false);
                _content = contentObject.GetComponent<RectTransform>();
                _content.anchorMin = Vector2.zero;
                _content.anchorMax = Vector2.one;
                _content.offsetMin = new Vector2(12f, 12f);
                _content.offsetMax = new Vector2(-12f, -12f);
            }

            VerticalLayoutGroup layout = _content.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(6, 6, 6, 6);
        }

        void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    _rows[i].transform.SetParent(null, false);
                    Destroy(_rows[i]);
                }
            }

            _rows.Clear();
        }

        static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment)
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

        static bool RectContains(RectTransform rect, Vector2 screenPoint)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera);
        }
    }
}
