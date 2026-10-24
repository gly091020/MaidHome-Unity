using System;
using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.Input;
using MaidHome.Interop.Ai;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    [DisallowMultipleComponent]
    public sealed class MaidAiChatPanel : MonoBehaviour
    {
        const int PageSize = 20;

        sealed class MessageLayout
        {
            public RectTransform Bubble;
            public Text Text;
            public LayoutElement Row;
        }

        public event Action CloseRequested;

        MaidAiChatController _controller;
        Canvas _hostCanvas;
        GameObject _root;
        GameObject _backdrop;
        GameObject _historyRoot;
        GameObject _bottomBar;
        GameObject _previewBubble;
        RectTransform _bottomRect;
        CanvasGroup _backdropGroup;
        CanvasGroup _historyGroup;
        CanvasGroup _previewGroup;
        RectTransform _content;
        ScrollRect _scroll;
        InputField _input;
        Button _sendButton;
        Button _historyButton;
        Text _title;
        Text _status;
        Text _previewText;
        bool _initialized;
        bool _expanded;
        int _loadedCount = PageSize;
        Coroutine _scrollRoutine;
        Coroutine _modeRoutine;

        public bool IsOpen
        {
            get { return _root != null && _root.activeSelf; }
        }

        public void Initialize(MaidAiChatController controller)
        {
            if (_initialized && _controller == controller) return;
            if (_controller != null) _controller.Changed -= Refresh;
            _controller = controller;
            if (_controller != null) _controller.Changed += Refresh;
            _initialized = true;
        }

        public void Show(Canvas hostCanvas)
        {
            _hostCanvas = hostCanvas;
            Build();
            if (_root == null) return;
            _expanded = false;
            _loadedCount = PageSize;
            _root.SetActive(true);
            SetModeImmediate(false);
            Canvas.ForceUpdateCanvases();
            Refresh();
        }

        public void Hide()
        {
            if (_modeRoutine != null)
            {
                StopCoroutine(_modeRoutine);
                _modeRoutine = null;
            }
            if (_root != null) _root.SetActive(false);
        }

        void Update()
        {
            if (!IsOpen || !PointerInput.BackPressed) return;
            if (_expanded) BeginModeChange(false);
            else RequestClose();
        }

        void OnDestroy()
        {
            if (_controller != null) _controller.Changed -= Refresh;
            if (_root != null) Destroy(_root);
            CloseRequested = null;
        }

        void Build()
        {
            if (_controller == null || _hostCanvas == null || _root != null) return;
            EnsureEventSystem();
            RectTransform root = CreateRect("MaidAiChatPanel", _hostCanvas.transform);
            SetRect(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _root = root.gameObject;

            RectTransform backdrop = CreateRect("Backdrop", root);
            SetRect(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = new Color(0.01f, 0.015f, 0.025f, 0.94f);
            _backdropGroup = backdrop.gameObject.AddComponent<CanvasGroup>();
            _backdrop = backdrop.gameObject;

            RectTransform history = CreateRect("History", root);
            SetRect(history, new Vector2(0.02f, 0.035f), new Vector2(0.98f, 0.975f),
                Vector2.zero, Vector2.zero);
            _historyGroup = history.gameObject.AddComponent<CanvasGroup>();
            _historyRoot = history.gameObject;

            _title = CreateText(history, "Title", "聊天记录", 40, TextAnchor.MiddleLeft);
            SetRect(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12f, -72f), new Vector2(-12f, -12f));

            _status = CreateText(history, "Status", "", 24, TextAnchor.MiddleLeft);
            _status.color = new Color(0.68f, 0.74f, 0.84f, 1f);
            SetRect(_status.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12f, -126f), new Vector2(-12f, -78f));

            RectTransform scrollRect = CreateRect("Scroll", history);
            SetRect(scrollRect, Vector2.zero, Vector2.one,
                new Vector2(0f, 120f), new Vector2(0f, -136f));
            _scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 55f;

            RectTransform viewport = CreateRect("Viewport", scrollRect);
            SetRect(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.1f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _content = CreateRect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(8, 8, 10, 18);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.viewport = viewport;
            _scroll.content = _content;

            RectTransform bar = CreateRect("BottomBar", root);
            bar.pivot = new Vector2(0.5f, 0f);
            SetRect(bar, new Vector2(0.025f, 0f), new Vector2(0.975f, 0f),
                new Vector2(0f, 24f), new Vector2(0f, 284f));
            bar.gameObject.AddComponent<Image>().color = new Color(0.045f, 0.055f, 0.075f, 0.96f);
            _bottomBar = bar.gameObject;
            _bottomRect = bar;

            RectTransform preview = CreateRect("MaidMessage", bar);
            SetRect(preview, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(24f, 124f), new Vector2(-24f, -24f));
            preview.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.16f, 0.2f, 0.96f);
            _previewGroup = preview.gameObject.AddComponent<CanvasGroup>();
            _previewGroup.blocksRaycasts = false;
            _previewBubble = preview.gameObject;
            _previewText = CreateText(preview, "Text", "还没有聊天，点「历史」查看记录。", 26,
                TextAnchor.MiddleLeft);
            SetRect(_previewText.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(18f, 10f), new Vector2(-18f, -10f));

            _input = CreateInput(bar);
            RectTransform inputRect = _input.GetComponent<RectTransform>();
            SetRect(inputRect, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 20f), new Vector2(-560f, 104f));

            _sendButton = CreateButton(bar, "Send", "发送", new Color(0.2f, 0.52f, 0.34f, 1f));
            SetRect(_sendButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-548f, 20f), new Vector2(-408f, 104f));
            _sendButton.onClick.AddListener(Send);

            _historyButton = CreateButton(bar, "HistoryButton", "历史", new Color(0.2f, 0.3f, 0.42f, 1f));
            SetRect(_historyButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-396f, 20f), new Vector2(-260f, 104f));
            _historyButton.onClick.AddListener(ToggleHistory);

            Button config = CreateButton(bar, "Config", "配置", new Color(0.2f, 0.3f, 0.42f, 1f));
            SetRect(config.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-248f, 20f), new Vector2(-132f, 104f));
            config.onClick.AddListener(OpenSettings);

            Button close = CreateButton(bar, "Close", "关闭", new Color(0.34f, 0.15f, 0.18f, 1f));
            SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-120f, 20f), new Vector2(-24f, 104f));
            close.onClick.AddListener(RequestClose);

            _backdrop.SetActive(false);
            _historyRoot.SetActive(false);
            _root.SetActive(false);
        }

        void SetModeImmediate(bool expanded)
        {
            if (_root == null) return;
            _expanded = expanded;
            _backdrop.SetActive(expanded);
            _historyRoot.SetActive(expanded);
            _previewBubble.SetActive(!expanded);
            _backdropGroup.alpha = expanded ? 1f : 0f;
            _historyGroup.alpha = expanded ? 1f : 0f;
            _previewGroup.alpha = expanded ? 0f : 1f;
            _historyRoot.transform.localScale = Vector3.one;
            _bottomRect.sizeDelta = new Vector2(_bottomRect.sizeDelta.x, expanded ? 150f : 260f);
            SetButtonText(_historyButton, expanded ? "收起" : "历史");
            _historyButton.interactable = true;
            Canvas.ForceUpdateCanvases();
        }

        void ToggleHistory()
        {
            if (_modeRoutine != null) return;
            BeginModeChange(!_expanded);
        }

        void BeginModeChange(bool expand)
        {
            if (_root == null) return;
            if (_modeRoutine != null) StopCoroutine(_modeRoutine);
            _expanded = expand;
            _backdrop.SetActive(true);
            _historyRoot.SetActive(true);
            _previewBubble.SetActive(true);
            if (expand) _loadedCount = Mathf.Max(PageSize, _loadedCount);
            SetButtonText(_historyButton, expand ? "收起" : "历史");
            _historyButton.interactable = false;
            Refresh();
            _modeRoutine = StartCoroutine(AnimateMode(expand));
            if (expand) ScrollToBottom();
        }

        IEnumerator AnimateMode(bool expand)
        {
            float fromBackdrop = _backdropGroup.alpha;
            float fromHistory = _historyGroup.alpha;
            float fromPreview = _previewGroup.alpha;
            float fromScale = _historyRoot.transform.localScale.x;
            float fromBarHeight = _bottomRect.sizeDelta.y;
            float toBackdrop = expand ? 1f : 0f;
            float toHistory = expand ? 1f : 0f;
            float toPreview = expand ? 0f : 1f;
            float toScale = expand ? 1f : 0.97f;
            float toBarHeight = expand ? 150f : 260f;
            const float duration = 0.24f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                _backdropGroup.alpha = Mathf.Lerp(fromBackdrop, toBackdrop, t);
                _historyGroup.alpha = Mathf.Lerp(fromHistory, toHistory, t);
                _previewGroup.alpha = Mathf.Lerp(fromPreview, toPreview, t);
                float scale = Mathf.Lerp(fromScale, toScale, t);
                _historyRoot.transform.localScale = new Vector3(scale, scale, 1f);
                _bottomRect.sizeDelta = new Vector2(_bottomRect.sizeDelta.x,
                    Mathf.Lerp(fromBarHeight, toBarHeight, t));
                yield return null;
            }

            _backdropGroup.alpha = toBackdrop;
            _historyGroup.alpha = toHistory;
            _previewGroup.alpha = toPreview;
            _historyRoot.transform.localScale = Vector3.one;
            _bottomRect.sizeDelta = new Vector2(_bottomRect.sizeDelta.x, toBarHeight);
            _backdrop.SetActive(expand);
            _historyRoot.SetActive(expand);
            _previewBubble.SetActive(!expand);
            _historyButton.interactable = true;
            _modeRoutine = null;
            Canvas.ForceUpdateCanvases();
        }

        void Refresh()
        {
            if (_controller == null || _root == null || !IsOpen) return;
            AiChatRecord record = _controller.Record;
            string name = string.IsNullOrEmpty(record.Profile.MaidName) ? "女仆" : record.Profile.MaidName;
            _title.text = "与 " + name + " 的聊天记录";
            string status = "站点：" + _controller.SiteSummary()
                + "｜人设：" + _controller.PersonaSummary();
            if (!string.IsNullOrEmpty(_controller.Status)) status += "\n" + _controller.Status;
            _status.text = status;
            _sendButton.interactable = !_controller.Busy;
            UpdatePreview(record);
            if (_expanded) RebuildMessages(record);
        }

        void UpdatePreview(AiChatRecord record)
        {
            string text = "";
            if (!string.IsNullOrEmpty(_controller.Status)
                && (_controller.Status.Contains("失败") || _controller.Status.Contains("没有")
                    || _controller.Status.Contains("设置")))
            {
                text = _controller.Status;
            }
            else if (_controller.Busy)
            {
                text = "……";
            }
            else
            {
                for (int i = record.Messages.Count - 1; i >= 0; i--)
                {
                    AiChatMessage message = record.Messages[i];
                    if (message.Role != "assistant" || message.Status != "complete") continue;
                    string candidate = string.IsNullOrWhiteSpace(message.Text) ? message.RawText : message.Text;
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    text = candidate;
                    break;
                }
            }

            if (string.IsNullOrEmpty(text)) text = "还没有聊天，点「历史」查看记录。";
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > 60) text = text.Substring(0, 60) + "…";
            _previewText.text = "女仆： " + text;
        }

        void RebuildMessages(AiChatRecord record)
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Transform child = _content.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            List<AiChatMessage> visible = new List<AiChatMessage>();
            for (int i = 0; i < record.Messages.Count; i++)
            {
                AiChatMessage message = record.Messages[i];
                if (message.Role != "user" && message.Role != "assistant") continue;
                if (string.IsNullOrWhiteSpace(message.Text) && string.IsNullOrWhiteSpace(message.RawText)) continue;
                visible.Add(message);
            }

            if (visible.Count == 0)
            {
                Text empty = CreateText(_content, "Empty", "还没有聊天记录。", 27, TextAnchor.MiddleCenter);
                LayoutElement emptyLayout = empty.gameObject.AddComponent<LayoutElement>();
                emptyLayout.preferredHeight = 100f;
                return;
            }

            int start = Mathf.Max(0, visible.Count - _loadedCount);
            if (start > 0)
            {
                Button older = CreateButton(_content, "Older",
                    "加载更早的 " + Mathf.Min(PageSize, start) + " 条",
                    new Color(0.18f, 0.22f, 0.3f, 1f));
                LayoutElement olderLayout = older.gameObject.AddComponent<LayoutElement>();
                olderLayout.preferredHeight = 52f;
                olderLayout.minHeight = 52f;
                older.onClick.AddListener(LoadOlder);
            }

            List<MessageLayout> layouts = new List<MessageLayout>();
            for (int i = start; i < visible.Count; i++)
            {
                layouts.Add(AddMessageRow(visible[i]));
            }

            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < layouts.Count; i++)
            {
                MessageLayout item = layouts[i];
                float textHeight = Mathf.Max(34f, item.Text.preferredHeight);
                float bubbleHeight = Mathf.Max(58f, textHeight + 28f);
                item.Bubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);
                item.Row.preferredHeight = bubbleHeight + 12f;
                item.Row.minHeight = bubbleHeight + 12f;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            Canvas.ForceUpdateCanvases();

            // 再量一次：LayoutGroup 第一次赋宽后，长文本的实际行数才稳定。
            for (int i = 0; i < layouts.Count; i++)
            {
                MessageLayout item = layouts[i];
                float needed = Mathf.Max(58f, item.Text.preferredHeight + 28f);
                if (needed > item.Bubble.rect.height + 0.5f)
                {
                    item.Bubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, needed);
                    item.Row.preferredHeight = needed + 12f;
                    item.Row.minHeight = needed + 12f;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        MessageLayout AddMessageRow(AiChatMessage message)
        {
            bool user = message.Role == "user";
            GameObject row = new GameObject(user ? "UserMessageRow" : "MaidMessageRow",
                typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(_content, false);
            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.preferredHeight = 76f;
            rowLayout.minHeight = 76f;

            GameObject bubble = new GameObject(user ? "UserMessage" : "MaidMessage",
                typeof(RectTransform), typeof(Image));
            bubble.transform.SetParent(row.transform, false);
            RectTransform bubbleRect = bubble.GetComponent<RectTransform>();
            bubbleRect.anchorMin = new Vector2(user ? 0.22f : 0f, 1f);
            bubbleRect.anchorMax = new Vector2(user ? 1f : 0.78f, 1f);
            bubbleRect.pivot = new Vector2(user ? 1f : 0f, 1f);
            bubbleRect.anchoredPosition = Vector2.zero;
            bubbleRect.sizeDelta = new Vector2(0f, 64f);
            bubble.GetComponent<Image>().color = user
                ? new Color(0.16f, 0.25f, 0.36f, 0.96f)
                : new Color(0.13f, 0.16f, 0.2f, 0.97f);

            string value = string.IsNullOrEmpty(message.Text) ? message.RawText : message.Text;
            string marker = message.Status == "pending" ? "  …" : message.Status == "failed" ? "  [失败]" : "";
            Text label = CreateText(bubble.transform, "Text", value + marker, 27, TextAnchor.UpperLeft);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(18f, 14f), new Vector2(-18f, -14f));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            MessageLayout result = new MessageLayout();
            result.Bubble = bubbleRect;
            result.Text = label;
            result.Row = rowLayout;
            return result;
        }

        void LoadOlder()
        {
            _loadedCount += PageSize;
            Refresh();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        void Send()
        {
            if (_controller == null || _input == null) return;
            string text = _input.text.Trim();
            if (text.Length == 0) return;
            _input.text = "";
            _controller.SendText(text);
            if (_expanded) ScrollToBottom();
        }

        void ScrollToBottom()
        {
            if (!IsOpen || _scroll == null) return;
            if (_scrollRoutine != null) StopCoroutine(_scrollRoutine);
            _scrollRoutine = StartCoroutine(ScrollToBottomRoutine());
        }

        IEnumerator ScrollToBottomRoutine()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
            _scrollRoutine = null;
        }

        void OpenSettings()
        {
            if (_controller != null) _controller.OpenSettings();
        }

        void RequestClose()
        {
            if (CloseRequested != null) CloseRequested();
        }

        static InputField CreateInput(Transform parent)
        {
            GameObject root = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            root.transform.SetParent(parent, false);
            Image image = root.GetComponent<Image>();
            image.color = new Color(0.035f, 0.045f, 0.06f, 1f);
            InputField input = root.GetComponent<InputField>();
            input.targetGraphic = image;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 512;

            Text text = CreateText(root.transform, "Text", "", 28, TextAnchor.MiddleLeft);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(18f, 0f), new Vector2(-18f, 0f));
            input.textComponent = text;

            Text placeholder = CreateText(root.transform, "Placeholder", "输入要对女仆说的话…", 28,
                TextAnchor.MiddleLeft);
            placeholder.color = new Color(0.5f, 0.55f, 0.65f, 1f);
            SetRect(placeholder.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(18f, 0f), new Vector2(-18f, 0f));
            input.placeholder = placeholder;
            return input;
        }

        static Text CreateText(Transform parent, string name, string value, int size, TextAnchor alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.text = value;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static Button CreateButton(Transform parent, string name, string label, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            Text text = CreateText(go.transform, "Text", label, 26, TextAnchor.MiddleCenter);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        static void SetButtonText(Button button, string value)
        {
            if (button == null) return;
            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = value;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindObjectOfType<EventSystem>() != null) return;
            GameObject system = new GameObject("MaidAiChatEventSystem", typeof(EventSystem),
                typeof(StandaloneInputModule));
            DontDestroyOnLoad(system);
        }
    }
}
