using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸尾巴模式底部的条：台词、操作提示、「吸一口」「退出」。
    /// 全自动生成，以后要换正式 UI 就把这里的对象替掉。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidTailPanel : MonoBehaviour
    {
        public event Action SniffRequested;
        public event Action ExitRequested;

        [SerializeField] private string _hint = "按住尾巴拖动；拉出中间那块她会喊疼";
        [SerializeField] private float _lineSeconds = 4f;

        GameObject _bar;
        Text _lineText;
        Text _hintText;
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _sniffRect;
        RectTransform _exitRect;

        public bool IsVisible { get; private set; }

        void Awake()
        {
            Build();
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
            ApplyLayout();
            _hintText.text = _hint;
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
            SniffRequested = null;
            ExitRequested = null;
        }

        void Build()
        {
            if (_bar != null)
            {
                return;
            }

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

            _sniffRect = CreateButton(_barRect, "吸一口", () => Raise(SniffRequested));
            _exitRect = CreateButton(_barRect, "退出", () => Raise(ExitRequested));

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

        static RectTransform CreateButton(RectTransform parent, string label, Action onClick)
        {
            GameObject buttonObject = new GameObject("Button " + label, typeof(RectTransform), typeof(Image),
                typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);
            buttonObject.GetComponent<Button>().onClick.AddListener(() => onClick());

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
