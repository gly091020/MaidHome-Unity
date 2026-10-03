using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Bag
{
    /// <summary>
    /// 放置模式底部的按钮条：手指拖动选位置，这里调朝向、确认、退出。
    /// 想用自定义 UI 就把 Bar / Hint Text / 三个 Button 接上，接上以后布局完全归你的 UI，
    /// 这里只管开关、填提示、转发按钮；一个都不接就退回自动生成的那条。
    /// 注意本组件平时是 BagPlacementController 在运行时 AddComponent 出来的，要接线得在场景里先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BagPlacementPanel : MonoBehaviour
    {
        public event Action RotateRequested;
        public event Action CancelRequested;
        public event Action ConfirmRequested;

        [SerializeField] private string _hint = "拖动选位置，「旋转」调朝向，点「放置」放下";

        [Header("自定义 UI（不接就自动生成一条）")]
        [Tooltip("按钮条根节点，Show/Hide 只开关它，布局不动")]
        [SerializeField] private GameObject _bar;
        [Tooltip("操作提示文本，留空就不提示")]
        [SerializeField] private Text _hintText;
        [Tooltip("「旋转」按钮，自定义 UI 必须接")]
        [SerializeField] private Button _rotateButton;
        [Tooltip("「取消」按钮，自定义 UI 必须接，否则退不出放置模式")]
        [SerializeField] private Button _cancelButton;
        [Tooltip("「放置」按钮，自定义 UI 必须接，否则放不下去")]
        [SerializeField] private Button _confirmButton;

        // 只给自动生成的那条用
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _rotateRect;
        RectTransform _cancelRect;
        RectTransform _confirmRect;
        bool _generated;

        public bool IsVisible { get; private set; }

        void Awake()
        {
            Build();
            if (_bar != null && !_generated && (_rotateButton == null || _cancelButton == null || _confirmButton == null))
            {
                Debug.LogWarning("自定义放置条没接全「旋转」「取消」「放置」按钮，这几个会点不动", this);
            }

            // 自定义 bar 在场景里可能是勾着的，进 Play 先收起来
            Hide();
        }

        void OnDestroy()
        {
            if (_rotateButton != null)
            {
                _rotateButton.onClick.RemoveListener(OnRotateClicked);
            }

            if (_cancelButton != null)
            {
                _cancelButton.onClick.RemoveListener(OnCancelClicked);
            }

            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(OnConfirmClicked);
            }

            RotateRequested = null;
            CancelRequested = null;
            ConfirmRequested = null;
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
            if (_rotateButton != null)
            {
                _rotateButton.onClick.RemoveListener(OnRotateClicked);
                _rotateButton.onClick.AddListener(OnRotateClicked);
            }

            if (_cancelButton != null)
            {
                _cancelButton.onClick.RemoveListener(OnCancelClicked);
                _cancelButton.onClick.AddListener(OnCancelClicked);
            }

            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(OnConfirmClicked);
                _confirmButton.onClick.AddListener(OnConfirmClicked);
            }
        }

        void OnRotateClicked()
        {
            Raise(RotateRequested);
        }

        void OnCancelClicked()
        {
            Raise(CancelRequested);
        }

        void OnConfirmClicked()
        {
            Raise(ConfirmRequested);
        }

        /// <summary>没有现成的按钮条就自己搭一条：底框 + 提示 + 三个按钮，全挂在 Canvas 下。</summary>
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

            _bar = new GameObject("BagPlacementBar", typeof(RectTransform), typeof(Image));
            _barRect = _bar.GetComponent<RectTransform>();
            _barRect.SetParent(canvas.transform, false);
            _barRect.anchorMin = new Vector2(0.5f, 0f);
            _barRect.anchorMax = new Vector2(0.5f, 0f);
            _barRect.pivot = new Vector2(0.5f, 0f);
            _bar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.78f);

            Text hint = CreateText(_barRect, "Hint", 18, TextAnchor.MiddleLeft);
            _hintRect = hint.rectTransform;
            _hintRect.anchorMin = new Vector2(0f, 0f);
            _hintRect.anchorMax = new Vector2(0f, 0f);
            _hintRect.pivot = new Vector2(0f, 0f);

            _rotateRect = CreateButton(_barRect, "旋转");
            _cancelRect = CreateButton(_barRect, "取消");
            _confirmRect = CreateButton(_barRect, "放置");
            _rotateButton = _rotateRect.GetComponent<Button>();
            _cancelButton = _cancelRect.GetComponent<Button>();
            _confirmButton = _confirmRect.GetComponent<Button>();

            _generated = true;
            _bar.SetActive(false);
        }

        void ApplyLayout()
        {
            RectTransform parent = _barRect.parent as RectTransform;
            float available = parent != null && parent.rect.width > 1f ? parent.rect.width : 1080f;
            float width = Mathf.Clamp(available * 0.92f, 420f, 1100f);
            const float height = 170f;
            const float buttonY = 58f;

            _barRect.sizeDelta = new Vector2(width, height);
            _barRect.anchoredPosition = new Vector2(0f, 32f);

            _hintRect.sizeDelta = new Vector2(width - 540f, 60f);
            _hintRect.anchoredPosition = new Vector2(24f, 30f);

            PlaceButton(_confirmRect, -24f, buttonY);
            PlaceButton(_rotateRect, -188f, buttonY);
            PlaceButton(_cancelRect, -352f, buttonY);
        }

        static void PlaceButton(RectTransform rect, float offsetX, float offsetY)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(150f, 54f);
            rect.anchoredPosition = new Vector2(offsetX, offsetY);
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
