using System;
using System.Text;
using MaidHome.Interop.Maid;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆交互面板。默认会自动生成一个占位面板显示基础信息，
    /// 以后设计正式 UI 时把 Panel Root/文字/按钮拖进来覆盖即可。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidInteractionPanel : MonoBehaviour
    {
        public event Action CloseRequested;
        /// 点了「摸尾巴」
        public event Action TailRequested;

        [Tooltip("正式面板根节点。留空会自动生成一个占位面板")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _infoText;
        [SerializeField] private Text _optionsText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _tailButton;
        [Tooltip("自动把面板贴到屏幕下方")]
        [SerializeField] private bool _placeAtBottom = true;
        [SerializeField] private float _bottomOffset = 40f;

        public bool IsOpen
        {
            get { return _panelRoot != null && _panelRoot.activeSelf; }
        }

        void Awake()
        {
            EnsureCreated();
            PlacePanel();
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(RequestClose);
            }

            if (_tailButton != null)
            {
                _tailButton.onClick.AddListener(RequestTail);
            }

            Close();
        }

        void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(RequestClose);
            }

            if (_tailButton != null)
            {
                _tailButton.onClick.RemoveListener(RequestTail);
            }

            CloseRequested = null;
            TailRequested = null;
        }

        public void Open(MaidSaveData maid, bool tailAvailable)
        {
            EnsureCreated();
            PlacePanel();
            if (_panelRoot == null)
            {
                return;
            }

            _panelRoot.SetActive(true);
            if (_titleText != null)
            {
                _titleText.text = maid == null || string.IsNullOrEmpty(maid.Name) ? "女仆" : maid.Name;
            }

            if (_infoText != null)
            {
                _infoText.text = BuildInfo(maid);
            }

            SetTailAvailable(tailAvailable);
            if (_optionsText != null)
            {
                if (tailAvailable)
                {
                    _optionsText.text = "点下面的「摸尾巴」，按住尾巴拖动试试";
                }
                else if (maid != null && maid.SimpleBedrockModel)
                {
                    _optionsText.text = "方块模型的女仆不支持摸尾巴";
                }
                else
                {
                    _optionsText.text = "这个模型的骨架里没有尾巴";
                }
            }
        }

        /// 模型没有尾巴（例如方块酒狐）就把按钮灰掉，而不是点了才报错
        public void SetTailAvailable(bool available)
        {
            if (_tailButton != null)
            {
                _tailButton.interactable = available;
            }
        }

        public void Close()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        public void RequestClose()
        {
            if (CloseRequested != null)
            {
                CloseRequested();
            }
        }

        public void RequestTail()
        {
            if (TailRequested != null)
            {
                TailRequested();
            }
        }

        static string BuildInfo(MaidSaveData maid)
        {
            if (maid == null)
            {
                return "女仆数据缺失";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("等级: ").Append(maid.Level).Append('\n');
            builder.Append("主人: ").Append(string.IsNullOrEmpty(maid.OwnerName) ? "-" : maid.OwnerName).Append('\n');
            builder.Append("模型: ").Append(maid.ModelFile).Append('\n');
            builder.Append("类型: ").Append(maid.SimpleBedrockModel ? "SimpleBedrockModel" : "GeckoLib");
            return builder.ToString();
        }

        void EnsureCreated()
        {
            if (_panelRoot != null)
            {
                return;
            }

            GameObject panel = new GameObject("MaidInteractionPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(440f, 280f);
            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

            _panelRoot = panel;
            _titleText = CreateText(panel.transform, "Title", new Vector2(0f, 108f), new Vector2(400f, 40f),
                22, TextAnchor.MiddleCenter);
            _infoText = CreateText(panel.transform, "Info", new Vector2(0f, 18f), new Vector2(400f, 120f),
                16, TextAnchor.UpperLeft);
            _optionsText = CreateText(panel.transform, "Options", new Vector2(0f, -78f), new Vector2(400f, 60f),
                16, TextAnchor.UpperLeft);
            _closeButton = CreateButton(panel.transform, "关闭", new Vector2(160f, -112f),
                new Vector2(110f, 34f));
            _tailButton = CreateButton(panel.transform, "摸尾巴", new Vector2(-100f, -112f),
                new Vector2(150f, 34f));
        }

        void PlacePanel()
        {
            if (!_placeAtBottom || _panelRoot == null)
            {
                return;
            }

            RectTransform rect = _panelRoot.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, _bottomOffset);
        }

        static Text CreateText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size,
            int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

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

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject buttonObject = new GameObject("Button " + label, typeof(RectTransform), typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.25f, 0.25f, 0.3f, 1f);

            Text text = CreateText(buttonObject.transform, "Text", Vector2.zero, size, 16,
                TextAnchor.MiddleCenter);
            text.text = label;
            return buttonObject.GetComponent<Button>();
        }
    }
}
