using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 「玩全身」底栏：只放操作提示和「退出」。
    /// 想用自定义 UI 就接 Bar / Hint Text / Exit Button，不接就运行时自动生成一条。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidBodyPanel : MonoBehaviour
    {
        public event Action ExitRequested;

        [SerializeField] private string _hint = "按住身上任意部位拖动；松手后她会自然倒下，「退出」还原站姿";

        [Header("自定义 UI（不接就自动生成一条）")]
        [Tooltip("底栏根节点，Show/Hide 只开关它，布局不动")]
        [SerializeField] private GameObject _bar;
        [Tooltip("操作提示文本，留空就不提示")]
        [SerializeField] private Text _hintText;
        [Tooltip("「退出」按钮，自定义 UI 必须接，否则退不出玩全身模式")]
        [SerializeField] private Button _exitButton;

        bool _generated;
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _exitRect;

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

            ExitRequested = null;
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

            _generated = true;
            _bar.SetActive(false);
        }

        void ApplyLayout()
        {
            RectTransform parent = _barRect.parent as RectTransform;
            float available = parent != null && parent.rect.width > 1f ? parent.rect.width : 1080f;
            float width = Mathf.Clamp(available * 0.92f, 420f, 1100f);
            const float height = 92f;

            _barRect.sizeDelta = new Vector2(width, height);
            _barRect.anchoredPosition = new Vector2(0f, 32f);

            _hintRect.sizeDelta = new Vector2(width - 220f, 54f);
            _hintRect.anchoredPosition = new Vector2(24f, 18f);

            _exitRect.anchorMin = new Vector2(1f, 0f);
            _exitRect.anchorMax = new Vector2(1f, 0f);
            _exitRect.pivot = new Vector2(1f, 0f);
            _exitRect.sizeDelta = new Vector2(150f, 54f);
            _exitRect.anchoredPosition = new Vector2(-24f, 18f);
        }

        void OnExitClicked()
        {
            if (ExitRequested != null)
            {
                ExitRequested();
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
