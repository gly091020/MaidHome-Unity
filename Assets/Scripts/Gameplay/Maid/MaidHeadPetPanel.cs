using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸摸模式底栏：左右摸 / 按一按 / 退出。想用自定义 UI 就把四个槽接上；
    /// 一个都不接就自动在屏幕底部生成一条。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidHeadPetPanel : MonoBehaviour
    {
        public event Action ExitRequested;
        public event Action RubModeRequested;
        public event Action PressModeRequested;

        [SerializeField] private string _hint = "左右拖动摸头顶；切到「按一按」后从按下点往下拖是按压";

        [Header("自定义 UI（不接就自动生成一条）")]
        [SerializeField] private GameObject _bar;
        [SerializeField] private Text _hintText;
        [Tooltip("搓热进度条填充（可选，不接自动生成）")]
        [SerializeField] private Image _heatFill;
        [Tooltip("搓热进度文字（可选）")]
        [SerializeField] private Text _heatText;
        [SerializeField] private Button _rubButton;
        [SerializeField] private Button _pressButton;
        [SerializeField] private Button _exitButton;

        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _heatBgRect;
        RectTransform _heatFillRect;
        RectTransform _heatTextRect;
        RectTransform _rubRect;
        RectTransform _pressRect;
        RectTransform _exitRect;
        bool _generated;

        public bool IsVisible { get; private set; }

        public bool HasOwnBar
        {
            get { return _bar != null; }
        }

        void Awake()
        {
            Build();
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

            SetHeat(0f);
        }

        public void Hide()
        {
            IsVisible = false;
            if (_bar != null)
            {
                _bar.SetActive(false);
            }
        }

        /// <summary>按钮文字直接显示当前模式，免得玩家看不出在哪种模式里。</summary>
        public void SetMode(bool press)
        {
            SetLabel(_rubButton, press ? "左右摸" : "左右摸 ✓");
            SetLabel(_pressButton, press ? "按一按 ✓" : "按一按");
        }

        /// <summary>搓热进度 0~1；满了以后显示着火。</summary>
        public void SetHeat(float value)
        {
            value = Mathf.Clamp01(value);
            if (_heatFill != null)
            {
                if (_heatFillRect == null)
                {
                    _heatFillRect = _heatFill.rectTransform;
                }

                _heatFillRect.anchorMin = Vector2.zero;
                _heatFillRect.anchorMax = new Vector2(value, 1f);
                _heatFillRect.offsetMin = Vector2.zero;
                _heatFillRect.offsetMax = Vector2.zero;
                _heatFill.color = Color.Lerp(new Color(1f, 0.62f, 0.12f, 1f),
                    new Color(1f, 0.08f, 0.04f, 1f), value);
            }

            if (_heatText != null)
            {
                _heatText.text = value >= 0.999f ? "着火了！"
                    : "搓热 " + Mathf.RoundToInt(value * 100f) + "%";
            }
        }

        void OnDestroy()
        {
            Unhook();
            ExitRequested = null;
            RubModeRequested = null;
            PressModeRequested = null;
        }

        void Build()
        {
            if (_bar == null)
            {
                GenerateBar();
            }

            Hook();
        }

        void Hook()
        {
            Unhook();
            if (_exitButton != null)
            {
                _exitButton.onClick.AddListener(OnExitClicked);
            }

            if (_rubButton != null)
            {
                _rubButton.onClick.AddListener(OnRubClicked);
            }

            if (_pressButton != null)
            {
                _pressButton.onClick.AddListener(OnPressClicked);
            }
        }

        void Unhook()
        {
            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
            }

            if (_rubButton != null)
            {
                _rubButton.onClick.RemoveListener(OnRubClicked);
            }

            if (_pressButton != null)
            {
                _pressButton.onClick.RemoveListener(OnPressClicked);
            }
        }

        void OnExitClicked()
        {
            if (ExitRequested != null)
            {
                ExitRequested();
            }
        }

        void OnRubClicked()
        {
            if (RubModeRequested != null)
            {
                RubModeRequested();
            }
        }

        void OnPressClicked()
        {
            if (PressModeRequested != null)
            {
                PressModeRequested();
            }
        }

        static void SetLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
            }
        }

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

            _bar = new GameObject("MaidHeadPetBar (自动生成)", typeof(RectTransform), typeof(Image));
            _barRect = _bar.GetComponent<RectTransform>();
            _barRect.SetParent(canvas.transform, false);
            _barRect.anchorMin = new Vector2(0.5f, 0f);
            _barRect.anchorMax = new Vector2(0.5f, 0f);
            _barRect.pivot = new Vector2(0.5f, 0f);
            _bar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.8f);

            _hintText = CreateText(_barRect, "Hint", 20, TextAnchor.MiddleLeft);
            _hintRect = _hintText.rectTransform;
            _hintRect.anchorMin = new Vector2(0f, 1f);
            _hintRect.anchorMax = new Vector2(1f, 1f);
            _hintRect.pivot = new Vector2(0.5f, 1f);
            _hintRect.anchoredPosition = new Vector2(0f, -12f);

            GameObject heatBackground = new GameObject("Heat", typeof(RectTransform), typeof(Image));
            _heatBgRect = heatBackground.GetComponent<RectTransform>();
            _heatBgRect.SetParent(_barRect, false);
            heatBackground.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.2f, 0.95f);

            GameObject heatFillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            _heatFillRect = heatFillObject.GetComponent<RectTransform>();
            _heatFillRect.SetParent(_heatBgRect, false);
            _heatFillRect.anchorMin = Vector2.zero;
            _heatFillRect.anchorMax = Vector2.zero;
            _heatFillRect.offsetMin = Vector2.zero;
            _heatFillRect.offsetMax = Vector2.zero;
            _heatFill = heatFillObject.GetComponent<Image>();
            _heatFill.type = Image.Type.Simple;
            _heatFill.color = new Color(1f, 0.62f, 0.12f, 1f);

            _heatText = CreateText(_barRect, "HeatText", 18, TextAnchor.MiddleLeft);
            _heatTextRect = _heatText.rectTransform;
            _heatText.raycastTarget = false;

            _rubRect = CreateButton(_barRect, "左右摸 ✓");
            _rubButton = _rubRect.GetComponent<Button>();
            _pressRect = CreateButton(_barRect, "按一按");
            _pressButton = _pressRect.GetComponent<Button>();
            _exitRect = CreateButton(_barRect, "退出");
            _exitButton = _exitRect.GetComponent<Button>();

            _generated = true;
            _bar.SetActive(false);
        }

        void ApplyLayout()
        {
            RectTransform parent = _barRect.parent as RectTransform;
            float available = parent != null && parent.rect.width > 1f ? parent.rect.width : 1080f;
            float width = Mathf.Clamp(available * 0.9f, 420f, 980f);

            _barRect.sizeDelta = new Vector2(width, 176f);
            _barRect.anchoredPosition = new Vector2(0f, 32f);
            _hintRect.sizeDelta = new Vector2(width - 40f, 40f);

            _heatBgRect.anchorMin = new Vector2(0f, 0f);
            _heatBgRect.anchorMax = new Vector2(1f, 0f);
            _heatBgRect.pivot = new Vector2(0.5f, 0f);
            _heatBgRect.sizeDelta = new Vector2(-48f, 14f);
            _heatBgRect.anchoredPosition = new Vector2(0f, 76f);

            _heatTextRect.anchorMin = new Vector2(0f, 0f);
            _heatTextRect.anchorMax = new Vector2(0f, 0f);
            _heatTextRect.pivot = new Vector2(0f, 0f);
            _heatTextRect.sizeDelta = new Vector2(width - 48f, 20f);
            _heatTextRect.anchoredPosition = new Vector2(24f, 92f);

            PlaceButton(_rubRect, -336f);
            PlaceButton(_pressRect, -174f);
            PlaceExit(_exitRect);
        }

        static void PlaceButton(RectTransform rect, float x)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(150f, 54f);
            rect.anchoredPosition = new Vector2(x, 16f);
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
            rect.anchoredPosition = new Vector2(-24f, 16f);
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
