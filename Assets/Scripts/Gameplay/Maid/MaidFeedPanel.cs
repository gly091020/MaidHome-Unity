using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 喂蛋糕模式的底栏：台词、提示、三个挡位（轻/中/重）、退出。
    /// 想用自定义 UI 就把这些引用接上，接上以后布局完全归你的 UI，这里只管开关、填字、转发按钮；
    /// 一个都不接就退回自动生成的那条。
    /// 注意本组件平时是 MaidFeedInteraction 在运行时 AddComponent 出来的，要接线得在场景的 GUI 上先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidFeedPanel : MonoBehaviour
    {
        /// <summary>选了哪个挡位，参数 1 轻 / 2 中 / 3 重（相当于单选按钮）</summary>
        public event Action<int> LevelSelected;
        public event Action ExitRequested;

        [SerializeField] private string _hint = "先选挡位，再点屏幕：轻=喂一口，中=扔蛋糕，重=更快的蛋糕（会算连击）";
        [SerializeField] private float _lineSeconds = 4f;
        [Header("选中样式")]
        [SerializeField] private Color _selectedColor = new Color(0.92f, 0.6f, 0.18f, 1f);
        [SerializeField] private Color _normalColor = new Color(0.25f, 0.25f, 0.3f, 1f);

        [Header("自定义 UI（不接就自动生成一条）")]
        [Tooltip("底栏根节点，Show/Hide 只开关它，布局不动")]
        [SerializeField] private GameObject _bar;
        [Tooltip("台词文本，留空就没有台词")]
        [SerializeField] private Text _lineText;
        [Tooltip("操作提示文本，留空就不提示")]
        [SerializeField] private Text _hintText;
        [Tooltip("「轻」挡按钮")]
        [SerializeField] private Button _lightButton;
        [Tooltip("「中」挡按钮")]
        [SerializeField] private Button _mediumButton;
        [Tooltip("「重」挡按钮")]
        [SerializeField] private Button _heavyButton;
        [Tooltip("「退出」按钮，自定义 UI 必须接，否则退不出喂蛋糕模式")]
        [SerializeField] private Button _exitButton;

        // 只给自动生成的那条用
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _lightRect;
        RectTransform _mediumRect;
        RectTransform _heavyRect;
        RectTransform _exitRect;
        bool _generated;
        int _selectedLevel = MaidFeedInteraction.LevelLight;

        public bool IsVisible { get; private set; }

        /// <summary>自己接了 bar（说明是手摆的那份，不是运行时自动生成的）</summary>
        public bool HasOwnBar
        {
            get { return _bar != null; }
        }

        void Awake()
        {
            Build();
            if (_bar != null && !_generated && _exitButton == null)
            {
                Debug.LogWarning("自定义喂蛋糕底栏没接「退出」按钮，退不出喂蛋糕模式", this);
            }

            // 自定义 bar 在场景里是勾着的，进 Play 先收起来
            Hide();
        }

        public void Show()
        {
            Build();
            if (_bar == null)
            {
                return;
            }

            IsVisible = true;
            _bar.SetActive(true);
            if (_generated)
            {
                ApplyLayout();
            }

            if (_hintText != null)
            {
                _hintText.text = _hint;
            }
        }

        public void Hide()
        {
            IsVisible = false;
            if (_bar != null)
            {
                _bar.SetActive(false);
            }
        }

        /// <summary>说一句话，过几秒自动消失</summary>
        public void ShowLine(string line, float seconds)
        {
            if (_lineText == null)
            {
                return;
            }

            _lineText.text = line;
            CancelInvoke("ClearLine");
            Invoke("ClearLine", Mathf.Max(0.5f, seconds > 0f ? seconds : _lineSeconds));
        }

        /// <summary>挡位能不能点：中/重还没做，先灰掉</summary>
        public void SetLevelInteractable(int level, bool interactable)
        {
            Button button = GetLevelButton(level);
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        /// <summary>把某个挡位显示成选中（另外两个恢复普通色）</summary>
        public void SetSelectedLevel(int level)
        {
            _selectedLevel = level;
            Tint(_lightButton, MaidFeedInteraction.LevelLight);
            Tint(_mediumButton, MaidFeedInteraction.LevelMedium);
            Tint(_heavyButton, MaidFeedInteraction.LevelHeavy);
        }

        void Tint(Button button, int level)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.targetGraphic as Image;
            if (image == null)
            {
                image = button.GetComponent<Image>();
            }

            if (image != null)
            {
                image.color = level == _selectedLevel ? _selectedColor : _normalColor;
            }
        }

        Button GetLevelButton(int level)
        {
            if (level == MaidFeedInteraction.LevelLight)
            {
                return _lightButton;
            }

            if (level == MaidFeedInteraction.LevelMedium)
            {
                return _mediumButton;
            }

            return level == MaidFeedInteraction.LevelHeavy ? _heavyButton : null;
        }

        void ClearLine()
        {
            if (_lineText != null)
            {
                _lineText.text = "";
            }
        }

        void OnDestroy()
        {
            RemoveButtons();
            LevelSelected = null;
            ExitRequested = null;
        }

        void Build()
        {
            if (_bar == null)
            {
                GenerateBar();
            }

            HookButtons();
        }

        void HookButtons()
        {
            Hook(_lightButton, OnLightClicked);
            Hook(_mediumButton, OnMediumClicked);
            Hook(_heavyButton, OnHeavyClicked);
            Hook(_exitButton, OnExitClicked);
        }

        void RemoveButtons()
        {
            Unhook(_lightButton, OnLightClicked);
            Unhook(_mediumButton, OnMediumClicked);
            Unhook(_heavyButton, OnHeavyClicked);
            Unhook(_exitButton, OnExitClicked);
        }

        static void Hook(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        static void Unhook(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        void OnLightClicked()
        {
            SelectLevel(MaidFeedInteraction.LevelLight);
        }

        void OnMediumClicked()
        {
            SelectLevel(MaidFeedInteraction.LevelMedium);
        }

        void OnHeavyClicked()
        {
            SelectLevel(MaidFeedInteraction.LevelHeavy);
        }

        void OnExitClicked()
        {
            if (ExitRequested != null)
            {
                ExitRequested();
            }
        }

        void SelectLevel(int level)
        {
            // 自己先变色（点了立刻有反馈），再通知外面
            SetSelectedLevel(level);
            if (LevelSelected != null)
            {
                LevelSelected(level);
            }
        }

        /// <summary>没有现成的底栏就自己搭一条：底框 + 台词 + 提示 + 三个挡位 + 退出。</summary>
        void GenerateBar()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                return;
            }

            // 名字特意带"(自动生成)"：和手摆的那份区分开
            _bar = new GameObject("MaidFeedBar (自动生成)", typeof(RectTransform), typeof(Image));
            _barRect = _bar.GetComponent<RectTransform>();
            _barRect.SetParent(canvas.transform, false);
            _barRect.anchorMin = new Vector2(0.5f, 0f);
            _barRect.anchorMax = new Vector2(0.5f, 0f);
            _barRect.pivot = new Vector2(0.5f, 0f);
            _bar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.78f);

            _lineText = CreateText(_barRect, "Line", 24, TextAnchor.MiddleCenter);
            RectTransform lineRect = _lineText.rectTransform;
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.anchoredPosition = new Vector2(0f, -14f);
            lineRect.sizeDelta = new Vector2(-40f, 54f);

            _hintText = CreateText(_barRect, "Hint", 18, TextAnchor.MiddleLeft);
            _hintRect = _hintText.rectTransform;
            _hintRect.anchorMin = new Vector2(0f, 0f);
            _hintRect.anchorMax = new Vector2(0f, 0f);
            _hintRect.pivot = new Vector2(0f, 0f);

            _lightRect = CreateButton(_barRect, "喂一口（轻）");
            _mediumRect = CreateButton(_barRect, "中");
            _heavyRect = CreateButton(_barRect, "重");
            _exitRect = CreateButton(_barRect, "退出");
            _lightButton = _lightRect.GetComponent<Button>();
            _mediumButton = _mediumRect.GetComponent<Button>();
            _heavyButton = _heavyRect.GetComponent<Button>();
            _exitButton = _exitRect.GetComponent<Button>();

            _generated = true;
            _bar.SetActive(false);
        }

        void ApplyLayout()
        {
            RectTransform parent = _barRect.parent as RectTransform;
            float available = parent != null && parent.rect.width > 1f ? parent.rect.width : 1080f;
            float width = Mathf.Clamp(available * 0.92f, 620f, 1100f);
            const float height = 190f;

            _barRect.sizeDelta = new Vector2(width, height);
            _barRect.anchoredPosition = new Vector2(0f, 32f);

            _hintRect.sizeDelta = new Vector2(width - 222f, 44f);
            _hintRect.anchoredPosition = new Vector2(24f, 100f);

            PlaceExit(_exitRect);
            PlaceLevel(_lightRect, 24f);
            PlaceLevel(_mediumRect, 194f);
            PlaceLevel(_heavyRect, 364f);
        }

        static void PlaceLevel(RectTransform rect, float offsetX)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(160f, 54f);
            rect.anchoredPosition = new Vector2(offsetX, 20f);
        }

        static void PlaceExit(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(150f, 54f);
            rect.anchoredPosition = new Vector2(-24f, 100f);
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

        static RectTransform CreateButton(RectTransform parent, string label)
        {
            GameObject buttonObject = new GameObject("Button " + label, typeof(RectTransform), typeof(Image),
                typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

            Text text = CreateText(buttonObject.GetComponent<RectTransform>(), "Text", 18,
                TextAnchor.MiddleCenter);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.text = label;
            return rect;
        }
    }
}
