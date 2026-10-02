using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸尾巴模式底部的条：台词、操作提示、「吸一口」「退出」。
    /// 想用自定义 UI 就把这件事的五个引用（Bar / Line Text / Hint Text / Sniff Button / Exit Button）接上，
    /// 接上以后布局完全归你的 UI，这里只管开关、填字、转发按钮；一个都不接就退回自动生成的那条。
    /// 注意本组件平时是 MaidTailInteraction 在运行时 AddComponent 出来的，要接线得在场景的 GUI 上先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidTailPanel : MonoBehaviour
    {
        public event Action SniffRequested;
        public event Action ExitRequested;

        [SerializeField] private string _hint = "按住尾巴拖动；拉出中间那块她会喊疼";
        [SerializeField] private float _lineSeconds = 4f;

        [Header("自定义 UI（不接就自动生成一条）")]
        [Tooltip("底栏根节点，Show/Hide 只开关它，布局不动")]
        [SerializeField] private GameObject _bar;
        [Tooltip("台词文本，留空就没有台词")]
        [SerializeField] private Text _lineText;
        [Tooltip("操作提示文本，留空就不提示")]
        [SerializeField] private Text _hintText;
        [Tooltip("「吸一口」按钮，自定义 UI 必须接，否则点不了")]
        [SerializeField] private Button _sniffButton;
        [Tooltip("「退出」按钮，自定义 UI 必须接，否则退不出摸尾巴模式")]
        [SerializeField] private Button _exitButton;

        // 只给自动生成的那条用
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _sniffRect;
        RectTransform _exitRect;
        bool _generated;

        public bool IsVisible { get; private set; }

        void Awake()
        {
            Build();
            if (_bar != null && !_generated && (_sniffButton == null || _exitButton == null))
            {
                Debug.LogWarning("自定义摸尾巴底栏没接全「吸一口」「退出」按钮，这两个会点不动", this);
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

        /// 说一句话，过几秒自动消失
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

        void ClearLine()
        {
            if (_lineText != null)
            {
                _lineText.text = "";
            }
        }

        void OnDestroy()
        {
            if (_sniffButton != null)
            {
                _sniffButton.onClick.RemoveListener(OnSniffClicked);
            }

            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
            }

            SniffRequested = null;
            ExitRequested = null;
        }

        void Build()
        {
            if (_bar != null)
            {
                HookButtons();
                return;
            }

            GenerateBar();
            HookButtons();
        }

        void HookButtons()
        {
            if (_sniffButton != null)
            {
                _sniffButton.onClick.RemoveListener(OnSniffClicked);
                _sniffButton.onClick.AddListener(OnSniffClicked);
            }

            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
                _exitButton.onClick.AddListener(OnExitClicked);
            }
        }

        void OnSniffClicked()
        {
            Raise(SniffRequested);
        }

        void OnExitClicked()
        {
            Raise(ExitRequested);
        }

        /// <summary>没有现成的底栏就自己搭一条：底框 + 台词 + 提示 + 两个按钮，全挂在 Canvas 下。</summary>
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

            _bar = new GameObject("MaidTailBar", typeof(RectTransform), typeof(Image));
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

            _sniffRect = CreateButton(_barRect, "吸一口");
            _exitRect = CreateButton(_barRect, "退出");
            _sniffButton = _sniffRect.GetComponent<Button>();
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

            PlaceButton(_sniffRect, -24f);
            PlaceButton(_exitRect, -188f);
        }

        static void PlaceButton(RectTransform rect, float offsetX)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(150f, 54f);
            rect.anchoredPosition = new Vector2(offsetX, 18f);
        }

        void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
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
