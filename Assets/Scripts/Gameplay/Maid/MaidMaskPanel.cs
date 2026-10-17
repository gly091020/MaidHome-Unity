using System;
using MaidHome.Interop.Maid;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 面具面板：看出现在脸上的是哪张照片，以及「选照片 / 去掉照片 / 完成 / 取下 / 返回」。
    /// 和示众牌那块一样贴屏幕上方（Android 软键盘 / 系统 UI 都在下面），想用自己的 UI 就把这几个槽位接上。
    /// 注意本组件平时是 MaidMaskInteraction 在运行时 AddComponent 出来的，要接线得在场景里先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidMaskPanel : MonoBehaviour
    {
        public event Action PickRequested;
        public event Action ClearPhotoRequested;
        public event Action DoneRequested;
        public event Action RemoveRequested;
        public event Action BackRequested;

        [Header("自定义 UI（不接就自动生成一块）")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _titleText;
        [Tooltip("显示「贴的是哪张照片」那行字")]
        [SerializeField] private Text _photoText;
        [SerializeField] private Text _hintText;
        [SerializeField] private Button _pickButton;
        [SerializeField] private Button _clearButton;
        [SerializeField] private Button _doneButton;
        [SerializeField] private Button _removeButton;
        [SerializeField] private Button _backButton;

        const float PanelWidth = 900f;
        const float PanelHeight = 240f;

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

            PickRequested = null;
            ClearPhotoRequested = null;
            DoneRequested = null;
            RemoveRequested = null;
            BackRequested = null;
        }

        public void Show(MaidMaskData data)
        {
            Build();
            Hook();
            if (_panelRoot == null)
            {
                return;
            }

            _panelRoot.SetActive(true);
            SetPhoto(data);
            if (_clearButton != null)
            {
                _clearButton.interactable = data != null && data.HasPhoto;
            }
        }

        public void Hide()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        public void SetPhoto(MaidMaskData data)
        {
            if (_photoText == null)
            {
                return;
            }

            if (data == null || !data.HasPhoto)
            {
                _photoText.text = "照片：还没选（只戴面具）";
                return;
            }

            _photoText.text = "照片：已贴好";
        }

        public void SetHint(string hint)
        {
            if (_hintText != null)
            {
                _hintText.text = hint ?? "";
            }
        }

        public void SetPhotoLabel(string label)
        {
            if (_photoText != null)
            {
                _photoText.text = label ?? "";
            }
        }

        // ------------------------------------------------------------ 事件

        void Hook()
        {
            Hook(_pickButton, OnPickClicked);
            Hook(_clearButton, OnClearClicked);
            Hook(_doneButton, OnDoneClicked);
            Hook(_removeButton, OnRemoveClicked);
            Hook(_backButton, OnBackClicked);
        }

        void Hook(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        void OnPickClicked()
        {
            if (PickRequested != null)
            {
                PickRequested();
            }
        }

        void OnClearClicked()
        {
            if (ClearPhotoRequested != null)
            {
                ClearPhotoRequested();
            }
        }

        void OnDoneClicked()
        {
            if (DoneRequested != null)
            {
                DoneRequested();
            }
        }

        void OnRemoveClicked()
        {
            if (RemoveRequested != null)
            {
                RemoveRequested();
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

            GameObject panel = new GameObject("MaidMaskPanel", typeof(RectTransform), typeof(Image));
            _panelRoot = panel;
            _generated = true;
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -14f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panel.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

            _titleText = CreateText(rect, "Title", new Vector2(0f, -6f), new Vector2(700f, 34f), 22,
                TextAnchor.MiddleCenter);
            _titleText.text = "面具";
            _photoText = CreateText(rect, "Photo", new Vector2(0f, -50f), new Vector2(840f, 34f), 20,
                TextAnchor.MiddleCenter);
            _photoText.text = "照片：还没选（只戴面具）";
            _hintText = CreateText(rect, "Hint", new Vector2(0f, -94f), new Vector2(860f, 56f), 16,
                TextAnchor.UpperCenter);
            _hintText.color = new Color(1f, 1f, 1f, 0.75f);

            _pickButton = CreateButton(rect, "选照片", new Vector2(-250f, -176f), new Vector2(160f, 46f), 20);
            _clearButton = CreateButton(rect, "去掉照片", new Vector2(-78f, -176f), new Vector2(140f, 46f), 18);
            _doneButton = CreateButton(rect, "完成", new Vector2(72f, -176f), new Vector2(120f, 46f), 20);
            _removeButton = CreateButton(rect, "取下", new Vector2(204f, -176f), new Vector2(120f, 46f), 20);
            _backButton = CreateButton(rect, "返回", new Vector2(336f, -176f), new Vector2(120f, 46f), 20);
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
