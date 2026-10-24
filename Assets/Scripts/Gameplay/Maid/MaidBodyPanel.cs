using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 「玩全身」底栏：操作提示、体感开关、视角调整和「退出」。
    /// 想用自定义 UI 就接 Bar / Hint Text / Exit Button，不接就运行时自动生成一条。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidBodyPanel : MonoBehaviour
    {
        public event Action ExitRequested;
        public event Action MotionToggleRequested;
        public event Action ViewToggleRequested;

        [SerializeField] private string _hint = "按住身体拖动；「调整视角」后可拖动屏幕旋转镜头";

        [Header("自定义 UI（不接就自动生成一条）")]
        [Tooltip("底栏根节点，Show/Hide 只开关它，布局不动")]
        [SerializeField] private GameObject _bar;
        [Tooltip("操作提示文本，留空就不提示")]
        [SerializeField] private Text _hintText;
        [Tooltip("「退出」按钮，自定义 UI 必须接，否则退不出玩全身模式")]
        [SerializeField] private Button _exitButton;
        [Tooltip("「体感」开关按钮；自定义 UI 不接就没有这个开关")]
        [SerializeField] private Button _motionButton;
        [Tooltip("「调整视角」开关按钮；自定义 UI 不接就没有这个开关")]
        [SerializeField] private Button _viewButton;

        bool _generated;
        bool _motionOn;
        bool _viewOn;
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _exitRect;
        RectTransform _motionRect;
        RectTransform _viewRect;

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
                Debug.LogWarning("自定义玩全身底栏没接「退出」按钮，会退不出玩全身模式", this);
            }

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

            UpdateMotionText();
            UpdateViewText();
        }

        public void Hide()
        {
            IsVisible = false;
            if (_bar != null)
            {
                _bar.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
            }

            if (_motionButton != null)
            {
                _motionButton.onClick.RemoveListener(OnMotionClicked);
            }

            if (_viewButton != null)
            {
                _viewButton.onClick.RemoveListener(OnViewClicked);
            }

            ExitRequested = null;
            MotionToggleRequested = null;
            ViewToggleRequested = null;
        }

        void Build()
        {
            if (_bar == null)
            {
                GenerateBar();
            }

            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
                _exitButton.onClick.AddListener(OnExitClicked);
            }

            if (_motionButton != null)
            {
                _motionButton.onClick.RemoveListener(OnMotionClicked);
                _motionButton.onClick.AddListener(OnMotionClicked);
            }

            if (_viewButton != null)
            {
                _viewButton.onClick.RemoveListener(OnViewClicked);
                _viewButton.onClick.AddListener(OnViewClicked);
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
                canvas = FindObjectOfType<Canvas>();
            }

            if (canvas == null)
            {
                return;
            }

            _bar = new GameObject("MaidBodyBar", typeof(RectTransform), typeof(Image));
            _barRect = _bar.GetComponent<RectTransform>();
            _barRect.SetParent(canvas.transform, false);
            _barRect.anchorMin = new Vector2(0.5f, 0f);
            _barRect.anchorMax = new Vector2(0.5f, 0f);
            _barRect.pivot = new Vector2(0.5f, 0f);
            _bar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.78f);

            _hintText = CreateText(_barRect, "Hint", 18, TextAnchor.MiddleLeft);
            _hintRect = _hintText.rectTransform;
            _hintRect.anchorMin = new Vector2(0f, 0f);
            _hintRect.anchorMax = new Vector2(0f, 0f);
            _hintRect.pivot = new Vector2(0f, 0f);

            _exitRect = CreateButton(_barRect, "退出");
            _exitButton = _exitRect.GetComponent<Button>();
            _motionRect = CreateButton(_barRect, "体感：关");
            _motionButton = _motionRect.GetComponent<Button>();
            _viewRect = CreateButton(_barRect, "调整视角");
            _viewButton = _viewRect.GetComponent<Button>();

            _generated = true;
            _bar.SetActive(false);
        }

        void ApplyLayout()
        {
            RectTransform parent = _barRect.parent as RectTransform;
            float available = parent != null && parent.rect.width > 1f ? parent.rect.width : 1080f;
            float width = Mathf.Clamp(available * 0.94f, 620f, 1180f);
            const float height = 92f;

            _barRect.sizeDelta = new Vector2(width, height);
            _barRect.anchoredPosition = new Vector2(0f, 32f);

            _hintRect.sizeDelta = new Vector2(width - 560f, 54f);
            _hintRect.anchoredPosition = new Vector2(24f, 18f);

            _exitRect.anchorMin = new Vector2(1f, 0f);
            _exitRect.anchorMax = new Vector2(1f, 0f);
            _exitRect.pivot = new Vector2(1f, 0f);
            _exitRect.sizeDelta = new Vector2(140f, 54f);
            _exitRect.anchoredPosition = new Vector2(-20f, 18f);

            _motionRect.anchorMin = new Vector2(1f, 0f);
            _motionRect.anchorMax = new Vector2(1f, 0f);
            _motionRect.pivot = new Vector2(1f, 0f);
            _motionRect.sizeDelta = new Vector2(160f, 54f);
            _motionRect.anchoredPosition = new Vector2(-172f, 18f);

            _viewRect.anchorMin = new Vector2(1f, 0f);
            _viewRect.anchorMax = new Vector2(1f, 0f);
            _viewRect.pivot = new Vector2(1f, 0f);
            _viewRect.sizeDelta = new Vector2(170f, 54f);
            _viewRect.anchoredPosition = new Vector2(-344f, 18f);
        }

        public void SetMotion(bool on)
        {
            _motionOn = on;
            UpdateMotionText();
        }

        public void SetMotionAvailable(bool available)
        {
            if (_motionButton != null)
            {
                _motionButton.interactable = available;
            }
        }

        public void SetView(bool on)
        {
            _viewOn = on;
            UpdateViewText();
        }

        public void SetViewAvailable(bool available)
        {
            if (_viewButton != null)
            {
                _viewButton.interactable = available;
            }
        }

        public void SetHint(string hint)
        {
            if (_hintText != null)
            {
                _hintText.text = string.IsNullOrEmpty(hint) ? _hint : hint;
            }
        }

        void UpdateMotionText()
        {
            if (_motionButton == null)
            {
                return;
            }

            Text label = _motionButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = _motionOn ? "体感：开" : "体感：关";
            }
        }

        void UpdateViewText()
        {
            if (_viewButton == null)
            {
                return;
            }

            Text label = _viewButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = _viewOn ? "返回拖拽" : "调整视角";
            }
        }

        void OnExitClicked()
        {
            if (ExitRequested != null)
            {
                ExitRequested();
            }
        }

        void OnMotionClicked()
        {
            if (MotionToggleRequested != null)
            {
                MotionToggleRequested();
            }
        }

        void OnViewClicked()
        {
            if (ViewToggleRequested != null)
            {
                ViewToggleRequested();
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

            Text text = CreateText(rect, "Text", 20, TextAnchor.MiddleCenter);
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
