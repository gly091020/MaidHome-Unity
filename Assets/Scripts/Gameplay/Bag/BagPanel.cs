using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.Input;
using MaidHome.Gameplay.House;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Bag
{
    /// <summary>
    /// 通用背包面板。只负责列 item、点“取出”后进入放置模式、点“收回”通知 provider。
    /// 女仆以外的物品以后注册一个新的 IBagItemProvider 就能出现在这里。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BagPanel : MonoBehaviour
    {
        public static BagPanel Instance { get; private set; }

        [Tooltip("面板里被 Show/Hide 动画控制的对象，建议拖 Slot")]
        [SerializeField] private GameObject _panelRoot;

        [Header("显隐动画（代码驱动，不用 Animator）")]
        [Tooltip("滑动的对象，留空就用 Panel Root。**场景里摆的位置就是显示位置**")]
        [SerializeField] private RectTransform _slideRect;
        [Tooltip("隐藏时相对显示位置挪多少，负 y 是往下")]
        [SerializeField] private Vector2 _hiddenOffset = new Vector2(0f, -550f);
        [Tooltip("开关时一起挪的按钮（以前那条动画里的 InvButton），留空就不动它")]
        [SerializeField] private RectTransform _toggleRect;
        [Tooltip("那个按钮隐藏时相对显示位置挪多少")]
        [SerializeField] private Vector2 _hiddenToggleOffset = Vector2.zero;
        [Tooltip("滑一段多久，0 = 直接开关")]
        [SerializeField] private float _slideSeconds = 0.2f;

        [Tooltip("列表父节点，留空会在面板下自动建一个")]
        [SerializeField] private RectTransform _content;

        [Tooltip("拖实现 IBagItemProvider 的组件，例如 MaidManager")]
        [SerializeField] private MonoBehaviour[] _providerBehaviours;

        [SerializeField] private float _rowHeight = 36f;
        [SerializeField] private Color _rowColor = new Color(0f, 0f, 0f, 0.45f);
        [SerializeField] private int _fontSize = 14;
        [SerializeField] private bool _startVisible;
        [SerializeField] private bool _closeOnEmptyClick = true;

        [Header("格子布局")]
        [SerializeField] private int _columns = 9;
        [SerializeField] private int _rows = 4;
        [SerializeField] private int _padding = 4;
        [SerializeField] private Vector2 _spacing = new Vector2(2f, 2f);
        [SerializeField] private Color _emptySlotColor = new Color(1f, 1f, 1f, 0.06f);

        readonly List<IBagItemProvider> _providers = new List<IBagItemProvider>();
        readonly List<BagItemInfo> _items = new List<BagItemInfo>();
        readonly List<GameObject> _slots = new List<GameObject>();

        public BagPlacementController _placement;
        bool _listening;
        bool _visible;
        Vector2 _shownPosition;
        Vector2 _shownTogglePosition;
        Coroutine _slideRoutine;

        void Awake()
        {
            Instance = this;
            _placement = GetComponent<BagPlacementController>();
            if (_placement == null)
            {
                _placement = gameObject.AddComponent<BagPlacementController>();
            }

            if (_panelRoot == null)
            {
                GameObject panelObject = new GameObject("BagPanelRoot", typeof(RectTransform));
                panelObject.transform.SetParent(transform, false);
                RectTransform panelRect = panelObject.GetComponent<RectTransform>();
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
                _panelRoot = panelObject;
            }

            if (_slideRect == null && _panelRoot != null)
            {
                _slideRect = _panelRoot.transform as RectTransform;
            }

            if (_slideRect != null)
            {
                _shownPosition = _slideRect.anchoredPosition;
            }

            if (_toggleRect != null)
            {
                _shownTogglePosition = _toggleRect.anchoredPosition;
            }

            RegisterProviders();
        }

        void Start()
        {
            // 初始状态直接落位，不播一段
            SetVisible(_startVisible, true);
        }

        void OnEnable()
        {
            RegisterProviders();
            Subscribe();
            Refresh();
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

        public void Toggle()
        {
            if (_panelRoot == null)
            {
                return;
            }

            SetVisible(!_visible);
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

            // 背包和房子面板互斥：展开一个就把另一个收起来，别叠在一起
            if (visible && HousePanel.Instance != null)
            {
                HousePanel.Instance.SetVisible(false);
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

            if (_slideRoutine != null)
            {
                StopCoroutine(_slideRoutine);
            }

            _slideRoutine = StartCoroutine(SlideRoutine(visible));
        }

        IEnumerator SlideRoutine(bool visible)
        {
            Vector2 startPosition = _slideRect.anchoredPosition;
            Vector2 endPosition = visible ? _shownPosition : _shownPosition + _hiddenOffset;
            Vector2 startToggle = _toggleRect != null ? _toggleRect.anchoredPosition : Vector2.zero;
            Vector2 endToggle = _toggleRect != null
                ? (visible ? _shownTogglePosition : _shownTogglePosition + _hiddenToggleOffset)
                : Vector2.zero;

            float length = Mathf.Max(0.01f, _slideSeconds);
            float elapsed = 0f;
            while (elapsed < length)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                _slideRect.anchoredPosition = Vector2.Lerp(startPosition, endPosition, t);
                if (_toggleRect != null)
                {
                    _toggleRect.anchoredPosition = Vector2.Lerp(startToggle, endToggle, t);
                }

                yield return null;
            }

            _slideRoutine = null;
            FinishSlide(visible);
        }

        void ApplySlide(float t)
        {
            if (_slideRect != null)
            {
                _slideRect.anchoredPosition = Vector2.Lerp(_shownPosition,
                    _shownPosition + _hiddenOffset, t);
            }

            if (_toggleRect != null)
            {
                _toggleRect.anchoredPosition = Vector2.Lerp(_shownTogglePosition,
                    _shownTogglePosition + _hiddenToggleOffset, t);
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

        void RegisterProviders()
        {
            _providers.Clear();
            if (_providerBehaviours == null)
            {
                return;
            }

            for (int i = 0; i < _providerBehaviours.Length; i++)
            {
                IBagItemProvider provider = _providerBehaviours[i] as IBagItemProvider;
                if (provider != null && !_providers.Contains(provider))
                {
                    _providers.Add(provider);
                }
            }
        }

        void Subscribe()
        {
            if (_listening)
            {
                return;
            }

            _listening = true;
            for (int i = 0; i < _providers.Count; i++)
            {
                _providers[i].Changed += OnProviderChanged;
            }
        }

        void Unsubscribe()
        {
            if (!_listening)
            {
                return;
            }

            _listening = false;
            for (int i = 0; i < _providers.Count; i++)
            {
                _providers[i].Changed -= OnProviderChanged;
            }
        }

        void OnProviderChanged()
        {
            if (isActiveAndEnabled && _panelRoot != null && _panelRoot.activeInHierarchy)
            {
                Refresh();
            }
        }

        void Refresh()
        {
            RegisterProviders();

            _items.Clear();
            for (int i = 0; i < _providers.Count; i++)
            {
                _providers[i].GetItems(_items);
            }

            ClearRows();
            if (_panelRoot == null)
            {
                return;
            }

            EnsureContent();

            int slotCount = Mathf.Max(1, _columns * _rows);
            for (int i = 0; i < slotCount; i++)
            {
                CreateSlot(i, i < _items.Count ? _items[i] : null);
            }
        }

        void EnsureContent()
        {
            if (_content == null)
            {
                _content = _panelRoot.transform as RectTransform;
            }

            if (_content == null)
            {
                GameObject contentObject = new GameObject("BagList", typeof(RectTransform));
                contentObject.transform.SetParent(_panelRoot.transform, false);
                _content = contentObject.GetComponent<RectTransform>();
                _content.anchorMin = Vector2.zero;
                _content.anchorMax = Vector2.one;
                _content.offsetMin = new Vector2(8f, 8f);
                _content.offsetMax = new Vector2(-8f, -8f);
            }

            bool useGrid = _columns > 1 && _rows > 1;
            if (useGrid)
            {
                VerticalLayoutGroup vertical = _content.GetComponent<VerticalLayoutGroup>();
                if (vertical != null)
                {
                    vertical.enabled = false;
                }

                GridLayoutGroup grid = _content.GetComponent<GridLayoutGroup>();
                if (grid == null)
                {
                    grid = _content.gameObject.AddComponent<GridLayoutGroup>();
                    ConfigureGrid(grid);
                }
                else
                {
                    grid.enabled = true;
                    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    grid.constraintCount = _columns;
                    grid.cellSize = CalculateCellSize();
                }

                return;
            }

            VerticalLayoutGroup layout = _content.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.enabled = true;
            GridLayoutGroup oldGrid = _content.GetComponent<GridLayoutGroup>();
            if (oldGrid != null)
            {
                oldGrid.enabled = false;
            }

            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(4, 4, 4, 4);
        }

        void ConfigureGrid(GridLayoutGroup grid)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = _columns;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.spacing = _spacing;
            grid.padding = new RectOffset(_padding, _padding, _padding, _padding);
            grid.cellSize = CalculateCellSize();
        }

        Vector2 CalculateCellSize()
        {
            float width = _content.rect.width;
            float height = _content.rect.height;
            if (width <= 1f || height <= 1f)
            {
                return new Vector2(32f, 32f);
            }

            float cellWidth = (width - _padding * 2f - _spacing.x * (_columns - 1)) / _columns;
            float cellHeight = (height - _padding * 2f - _spacing.y * (_rows - 1)) / _rows;
            float size = Mathf.Max(1f, Mathf.Min(cellWidth, cellHeight));
            return new Vector2(size, size);
        }

        void CreateSlot(int index, BagItemInfo item)
        {
            GameObject slot = new GameObject("Slot " + index, typeof(RectTransform), typeof(Image));
            slot.transform.SetParent(_content, false);

            Image image = slot.GetComponent<Image>();
            image.color = item == null ? _emptySlotColor : _rowColor;
            image.raycastTarget = item != null;

            if (item == null)
            {
                _slots.Add(slot);
                return;
            }

            bool useGrid = _columns > 1 && _rows > 1;
            if (!useGrid)
            {
                LayoutElement element = slot.AddComponent<LayoutElement>();
                element.minHeight = _rowHeight;
                element.preferredHeight = _rowHeight;
            }

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(slot.transform, false);
            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = _fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = _fontSize;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.text = useGrid ? BuildCompactLabel(item) : BuildLabel(item);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(2f, 2f);
            labelRect.offsetMax = new Vector2(-2f, -2f);

            Button button = slot.AddComponent<Button>();
            button.onClick.AddListener(() => OnItemClicked(item));
            _slots.Add(slot);
        }

        static string BuildLabel(BagItemInfo item)
        {
            string action = item.InBag ? "[取出] " : "[收回] ";
            string state = item.InBag ? "  在背包" : "  已放置";
            return action + item.DisplayName + state + "  " + item.Subtitle;
        }

        static string BuildCompactLabel(BagItemInfo item)
        {
            return (item.InBag ? "取 " : "收 ") + item.DisplayName;
        }

        void OnItemClicked(BagItemInfo item)
        {
            IBagItemProvider provider = FindProvider(item.Kind);
            if (provider == null || _placement == null)
            {
                return;
            }

            if (!item.InBag)
            {
                provider.TryPutAway(item.Id);
                return;
            }

            if (!provider.TryBeginPlacement(item.Id))
            {
                return;
            }

            bool wasVisible = _visible;
            SetVisible(false);
            IBagSlowPlacement slow = provider as IBagSlowPlacement;
            if (!_placement.Begin(item.Kind, item.Id, item.DisplayName,
                slow != null && slow.NeedsLoading(item.Id),
                (feet, yaw) =>
                {
                    provider.TryPlaceAt(item.Id, feet, yaw);
                    if (wasVisible)
                    {
                        SetVisible(true);
                    }
                    else
                    {
                        Refresh();
                    }
                },
                () =>
                {
                    if (wasVisible)
                    {
                        SetVisible(true);
                    }
                }))
            {
                if (wasVisible)
                {
                    SetVisible(true);
                }
            }
        }

        IBagItemProvider FindProvider(string kind)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].Kind == kind)
                {
                    return _providers[i];
                }
            }

            return null;
        }

        void ClearRows()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] != null)
                {
                    _slots[i].transform.SetParent(null, false);
                    Destroy(_slots[i]);
                }
            }

            _slots.Clear();
        }
    }
}
