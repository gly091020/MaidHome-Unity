using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸脸模式底部的条：台词、操作提示、连击数、「退出」。
    /// 想用自定义 UI 就把 Bar / Line Text / Hint Text / Combo Text / Exit Button 五个引用接上，
    /// 接上以后布局完全归你的 UI，这里只管开关、填字、转发按钮；一个都不接就退回自动生成的那条。
    /// 注意本组件平时是 MaidFaceInteraction 在运行时 AddComponent 出来的，要接线得在场景的 GUI 上先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidFacePanel : MonoBehaviour
    {
        public event Action ExitRequested;

        [SerializeField] private string _hint = "两指可拽耳朵；脸上横划=扇脸，点眼睛=戳；空白处拖=拖脸";
        [SerializeField] private float _lineSeconds = 4f;

        [Header("自定义 UI（不接就自动生成一条）")]
        [Tooltip("底栏根节点，Show/Hide 只开关它，布局不动")]
        [SerializeField] private GameObject _bar;
        [Tooltip("台词文本，留空就没有台词")]
        [SerializeField] private Text _lineText;
        [Tooltip("操作提示文本，留空就不提示")]
        [SerializeField] private Text _hintText;
        [Tooltip("连击数文本（可选）：接上就除了屏幕上的彩虹连击字以外，这里也显示一份纯文本")]
        [SerializeField] private Text _comboText;
        [Tooltip("「退出」按钮，自定义 UI 必须接，否则退不出摸脸模式")]
        [SerializeField] private Button _exitButton;

        // 只给自动生成的那条用
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _exitRect;
        bool _generated;

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
                Debug.LogWarning("自定义摸脸底栏没接「退出」按钮，退不出摸脸模式", this);
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

        /// <summary>连击数，传 0 就清掉</summary>
        public void SetCombo(int count)
        {
            if (_comboText == null)
            {
                return;
            }

            _comboText.text = count > 0 ? "连击 ×" + count : "";
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
            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
            }

            ExitRequested = null;
        }

        void Build()
        {
            if (_bar != null)
            {
                HookButton();
                return;
            }

            GenerateBar();
            HookButton();
        }

        void HookButton()
        {
            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
                _exitButton.onClick.AddListener(OnExitClicked);
            }
        }

        void OnExitClicked()
        {
            if (ExitRequested != null)
            {
                ExitRequested();
            }
        }

        /// <summary>没有现成的底栏就自己搭一条：底框 + 台词 + 提示 + 连击 + 退出。</summary>
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

            // 名字特意带"(自动生成)"：和手摆的那份区分开，免得以为是两份
            _bar = new GameObject("MaidFaceBar (自动生成)", typeof(RectTransform), typeof(Image));
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

            _exitRect = CreateButton(_barRect, "退出");
            _exitButton = _exitRect.GetComponent<Button>();

            _generated = true;
            _bar.SetActive(false);
        }

        void ApplyLayout()
        {
            RectTransform parent = _barRect.parent as RectTransform;
            float available = parent != null && parent.rect.width > 1f ? parent.rect.width : 1080f;
            float width = Mathf.Clamp(available * 0.92f, 420f, 1100f);
            const float height = 170f;

            _barRect.sizeDelta = new Vector2(width, height);
            _barRect.anchoredPosition = new Vector2(0f, 32f);

            _hintRect.sizeDelta = new Vector2(width - 420f, 46f);
            _hintRect.anchoredPosition = new Vector2(24f, 18f);

            PlaceExit(_exitRect);
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
            rect.anchoredPosition = new Vector2(-24f, 18f);
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

            Text text = CreateText(buttonObject.GetComponent<RectTransform>(), "Text", 20,
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
