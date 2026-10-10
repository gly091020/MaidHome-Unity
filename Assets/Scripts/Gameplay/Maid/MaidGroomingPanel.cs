using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 梳毛模式底部的条：台词、操作提示、进度条、「毛刷 / 手」切换、「固定尾巴」、「结束」。
    /// 想用自定义 UI 就把 Bar / Line / Hint / 三个按钮 / 进度条接上，接上以后布局完全归你的 UI，
    /// 这里只管开关、填字、转发按钮；一个都不接就退回自动生成的那条。
    /// 注意本组件平时是 MaidGroomingInteraction 在运行时 AddComponent 出来的，要接线得在场景里先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidGroomingPanel : MonoBehaviour
    {
        public event Action ExitRequested;
        public event Action ModeToggled;
        public event Action FixToggled;
        public event Action FlipRequested;

        [Header("自定义 UI（不接就自动生成一条）")]
        [Tooltip("底栏根节点，Show/Hide 只开关它，布局不动")]
        [SerializeField] private GameObject _bar;
        [Tooltip("台词文本，留空就没有台词")]
        [SerializeField] private Text _lineText;
        [Tooltip("操作提示文本，留空就不提示")]
        [SerializeField] private Text _hintText;
        [Tooltip("「毛刷 / 手」切换按钮")]
        [SerializeField] private Button _toolButton;
        [Tooltip("「固定尾巴」按钮")]
        [SerializeField] private Button _fixButton;
        [Tooltip("「转向」按钮（照抄模组的翻转：躺着的时候把她掉个头）")]
        [SerializeField] private Button _flipButton;
        [Tooltip("「结束」按钮，自定义 UI 必须接，否则退不出梳毛模式")]
        [SerializeField] private Button _exitButton;
        [Tooltip("梳毛进度条（0~1 的 Fill Image），不接就没有进度显示")]
        [SerializeField] private Image _progressFill;
        [Tooltip("自动生成的底栏要不要画那条进度条（累计有效梳毛到 20 秒满一次，满了她会满足地夸一句）")]
        [SerializeField] private bool _showProgress = true;

        bool _generated;
        RectTransform _barRect;
        RectTransform _hintRect;
        RectTransform _toolRect;
        RectTransform _fixRect;
        RectTransform _flipRect;
        RectTransform _exitRect;
        RectTransform _progressRect;
        Text _toolLabel;
        bool _hasTail = true;
        MaidGroomingInteraction.Tool _tool = MaidGroomingInteraction.Tool.Brush;

        public bool IsVisible { get; private set; }

        void Awake()
        {
            Build();
            if (_bar != null && !_generated && _exitButton == null)
            {
                Debug.LogWarning("自定义梳毛底栏没接「结束」按钮，会退不出梳毛模式", this);
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
        }

        public void Hide()
        {
            IsVisible = false;
            if (_bar != null)
            {
                _bar.SetActive(false);
            }
        }

        /// <summary>说一句话，过几秒自动消失</summary>
        public void ShowLine(string line, float seconds)
        {
            if (_lineText == null)
            {
                return;
            }

            _lineText.text = line;
            CancelInvoke("ClearLine");
            Invoke("ClearLine", Mathf.Max(0.5f, seconds));
        }

        void ClearLine()
        {
            if (_lineText != null)
            {
                _lineText.text = "";
            }
        }

        public void SetHint(string hint)
        {
            if (_hintText != null)
            {
                _hintText.text = hint;
            }
        }

        public void SetTool(MaidGroomingInteraction.Tool tool)
        {
            _tool = tool;
            if (_toolLabel != null)
            {
                _toolLabel.text = tool == MaidGroomingInteraction.Tool.Brush ? "工具：毛刷" : "工具：手";
            }

            RefreshFixButton();
        }

        /// <summary>模型认不出尾巴就把「固定尾巴」灰掉（而不是点了没反应）</summary>
        public void SetHasTail(bool hasTail)
        {
            _hasTail = hasTail;
            RefreshFixButton();
        }

        /// <summary>尾巴要先用「手」按住才谈得上固定，模型没尾巴的话这个按钮也没意义</summary>
        void RefreshFixButton()
        {
            if (_fixButton != null)
            {
                _fixButton.interactable = _hasTail && _tool == MaidGroomingInteraction.Tool.Hand;
            }
        }

        public void SetProgress(float value)
        {
            if (_progressFill != null)
            {
                _progressFill.fillAmount = Mathf.Clamp01(value);
            }
        }

        /// <summary>没躺下就没有「转向」可言</summary>
        public void SetFlipEnabled(bool enabled)
        {
            if (_flipButton != null)
            {
                _flipButton.interactable = enabled;
            }
        }

        void OnDestroy()
        {
            if (_toolButton != null)
            {
                _toolButton.onClick.RemoveListener(OnToolClicked);
            }

            if (_fixButton != null)
            {
                _fixButton.onClick.RemoveListener(OnFixClicked);
            }

            if (_flipButton != null)
            {
                _flipButton.onClick.RemoveListener(OnFlipClicked);
            }

            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
            }

            ExitRequested = null;
            ModeToggled = null;
            FixToggled = null;
            FlipRequested = null;
        }

        void OnToolClicked()
        {
            if (ModeToggled != null)
            {
                ModeToggled();
            }
        }

        void OnFixClicked()
        {
            if (FixToggled != null)
            {
                FixToggled();
            }
        }

        void OnExitClicked()
        {
            if (ExitRequested != null)
            {
                ExitRequested();
            }
        }

        void OnFlipClicked()
        {
            if (FlipRequested != null)
            {
                FlipRequested();
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
            if (_toolButton != null)
            {
                _toolButton.onClick.RemoveListener(OnToolClicked);
                _toolButton.onClick.AddListener(OnToolClicked);
                _toolLabel = _toolButton.GetComponentInChildren<Text>();
            }

            if (_fixButton != null)
            {
                _fixButton.onClick.RemoveListener(OnFixClicked);
                _fixButton.onClick.AddListener(OnFixClicked);
            }

            if (_flipButton != null)
            {
                _flipButton.onClick.RemoveListener(OnFlipClicked);
                _flipButton.onClick.AddListener(OnFlipClicked);
            }

            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveListener(OnExitClicked);
                _exitButton.onClick.AddListener(OnExitClicked);
            }
        }

        /// <summary>没有现成的底栏就自己搭一条：台词 + 提示 + 进度条 + 三个按钮，全挂在 Canvas 下。</summary>
        void GenerateBar()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                // 兜底：女仆交互控制器不一定挂在 UI 层级里，找不到父 Canvas 就随手挑一个
                canvas = FindObjectOfType<Canvas>();
            }

            if (canvas == null)
            {
                return;
            }

            _bar = new GameObject("MaidGroomingBar", typeof(RectTransform), typeof(Image));
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

            if (_showProgress)
            {
                // 进度条：底槽（ProgressTrack）+ 填充（ProgressFill），只占提示文字上面那条窄带
                GameObject track = new GameObject("ProgressTrack", typeof(RectTransform), typeof(Image));
                _progressRect = track.GetComponent<RectTransform>();
                _progressRect.SetParent(_barRect, false);
                _progressRect.anchorMin = new Vector2(0f, 0f);
                _progressRect.anchorMax = new Vector2(0f, 0f);
                _progressRect.pivot = new Vector2(0f, 0f);
                track.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

                GameObject fill = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
                RectTransform fillRect = fill.GetComponent<RectTransform>();
                fillRect.SetParent(_progressRect, false);
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.pivot = new Vector2(0f, 0.5f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
                _progressFill = fill.GetComponent<Image>();
                _progressFill.color = new Color(0.95f, 0.75f, 0.45f, 0.85f);
                _progressFill.type = Image.Type.Filled;
                _progressFill.fillMethod = Image.FillMethod.Horizontal;
                _progressFill.fillAmount = 0f;
            }

            _toolRect = CreateButton(_barRect, "毛刷");
            _fixRect = CreateButton(_barRect, "固定尾巴");
            _flipRect = CreateButton(_barRect, "转向");
            _exitRect = CreateButton(_barRect, "结束");
            _toolButton = _toolRect.GetComponent<Button>();
            _fixButton = _fixRect.GetComponent<Button>();
            _flipButton = _flipRect.GetComponent<Button>();
            _exitButton = _exitRect.GetComponent<Button>();
            _toolLabel = _toolRect.GetComponentInChildren<Text>();

            _generated = true;
            _bar.SetActive(false);
        }

        void ApplyLayout()
        {
            RectTransform parent = _barRect.parent as RectTransform;
            float available = parent != null && parent.rect.width > 1f ? parent.rect.width : 1080f;
            float width = Mathf.Clamp(available * 0.92f, 460f, 1100f);
            const float height = 170f;

            _barRect.sizeDelta = new Vector2(width, height);
            _barRect.anchoredPosition = new Vector2(0f, 32f);

            _hintRect.sizeDelta = new Vector2(Mathf.Max(120f, width - 600f), 46f);
            _hintRect.anchoredPosition = new Vector2(24f, 18f);
            if (_progressRect != null)
            {
                _progressRect.sizeDelta = new Vector2(Mathf.Max(120f, width - 600f), 8f);
                _progressRect.anchoredPosition = new Vector2(24f, 74f);
            }

            PlaceButton(_flipRect, -444f);
            PlaceButton(_toolRect, -304f);
            PlaceButton(_fixRect, -164f);
            PlaceButton(_exitRect, -24f);
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
            rect.sizeDelta = new Vector2(132f, 54f);
            rect.anchoredPosition = new Vector2(offsetX, 18f);
        }

        static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment)
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

        static RectTransform CreateButton(Transform parent, string label)
        {
            GameObject buttonObject = new GameObject("Button " + label, typeof(RectTransform), typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(132f, 54f);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.25f, 0.25f, 0.3f, 1f);
            Text text = CreateText(buttonObject.transform, "Text", 18, TextAnchor.MiddleCenter);
            text.text = label;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return rect;
        }
    }
}
