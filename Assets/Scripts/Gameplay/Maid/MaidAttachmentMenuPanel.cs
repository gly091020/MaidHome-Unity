using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 第二层「挂件」面板：从女仆面板点「挂件」进来，选要挂哪一样（示众牌 / 面具，以后加别的也往这里放）。
    /// 自动生成时贴在屏幕下沿（和女仆面板同一个位置，进去时女仆面板收起来）。
    /// 想用自定义 UI 就把 Root / 标题 / 三个按钮接上；不接就自己搭一块。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidAttachmentMenuPanel : MonoBehaviour
    {
        public event Action SignRequested;
        public event Action MaskRequested;
        public event Action BackRequested;

        [Header("自定义 UI（不接就自动生成一块）")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _hintText;
        [Tooltip("「示众牌」按钮")]
        [SerializeField] private Button _signButton;
        [Tooltip("「面具」按钮")]
        [SerializeField] private Button _maskButton;
        [Tooltip("「返回」按钮（回到女仆面板）")]
        [SerializeField] private Button _backButton;

        const float PanelWidth = 760f;
        const float PanelHeight = 190f;

        bool _generated;

        public bool IsVisible
        {
            get { return _panelRoot != null && _panelRoot.activeSelf; }
        }

        void Awake()
        {
            Build();
            Hook();
            Hide();
        }

        void OnDestroy()
        {
            if (_panelRoot != null && _generated)
            {
                Destroy(_panelRoot);
            }

            SignRequested = null;
            MaskRequested = null;
            BackRequested = null;
        }

        public void Show(bool signAvailable, bool maskAvailable)
        {
            Build();
            Hook();
            if (_panelRoot == null)
            {
                return;
            }

            _panelRoot.SetActive(true);
            if (_signButton != null)
            {
                _signButton.interactable = signAvailable;
            }

            if (_maskButton != null)
            {
                _maskButton.interactable = maskAvailable;
            }
        }

        public void Hide()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        public void SetHint(string hint)
        {
            if (_hintText != null)
            {
                _hintText.text = hint ?? "";
            }
        }

        // ------------------------------------------------------------ 事件

        void Hook()
        {
            Hook(_signButton, OnSignClicked);
            Hook(_maskButton, OnMaskClicked);
            Hook(_backButton, OnBackClicked);
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

        void OnSignClicked()
        {
            if (SignRequested != null)
            {
                SignRequested();
            }
        }

        void OnMaskClicked()
        {
            if (MaskRequested != null)
            {
                MaskRequested();
            }
        }

        void OnBackClicked()
        {
            if (BackRequested != null)
            {
                BackRequested();
            }
        }

        // ------------------------------------------------------------ 生成

        void Build()
        {
            if (_panelRoot != null)
            {
                return;
            }

            Transform parent = UiParent();
            if (parent == null)
            {
                return;
            }

            GameObject panel = new GameObject("MaidAttachmentMenuPanel", typeof(RectTransform), typeof(Image));
            _panelRoot = panel;
            _generated = true;
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 40f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panel.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

            _titleText = CreateText(rect, "Title", new Vector2(0f, -4f), new Vector2(700f, 32f), 22,
                TextAnchor.MiddleCenter);
            _titleText.text = "挂件";
            _hintText = CreateText(rect, "Hint", new Vector2(0f, -40f), new Vector2(700f, 22f), 15,
                TextAnchor.MiddleCenter);
            _hintText.text = "选一样挂在她身上";
            _hintText.color = new Color(1f, 1f, 1f, 0.7f);

            _signButton = CreateButton(rect, "示众牌", new Vector2(-130f, -70f), new Vector2(200f, 54f), 22);
            _maskButton = CreateButton(rect, "面具", new Vector2(130f, -70f), new Vector2(200f, 54f), 22);
            _backButton = CreateButton(rect, "返回", new Vector2(0f, -134f), new Vector2(140f, 40f), 18);
        }

        Transform UiParent()
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

            return canvas == null ? null : canvas.transform;
        }

        static Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize,
            TextAnchor alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject go = new GameObject("Button " + label, typeof(RectTransform), typeof(Image),
                typeof(Button));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

            Text text = CreateText(rect, "Text", Vector2.zero, Vector2.zero, fontSize, TextAnchor.MiddleCenter);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = label;
            return go.GetComponent<Button>();
        }
    }
}
