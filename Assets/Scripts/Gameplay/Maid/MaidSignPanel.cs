using System;
using MaidHome.Interop.Maid;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 挂牌模式的编辑面板（照原模组 MaidSignEditScreen）：四行输入框 + 16 个染料色块 +
    /// 「完成 / 取下 / 威慑旁人 / 返回」。
    /// 面板贴在屏幕上方——Android 上是系统软键盘盖住下半屏，输入框必须在上面那一半。
    /// 想用自定义 UI 就把 Panel / 标题 / 提示 / 四个输入框 / 四个按钮接上，接上以后布局归你的 UI；
    /// 一个都不接就自动生成一块。色板（Swatches）能接 16 个按钮就用自己的，填不满就自动生成那一排。
    /// 注意本组件平时是 MaidSignInteraction 在运行时 AddComponent 出来的，要接线得在场景里先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidSignPanel : MonoBehaviour
    {
        /// <summary>点了「完成」（保存并退出）</summary>
        public event Action DoneRequested;
        /// <summary>点了「取下」（摘牌子并退出）</summary>
        public event Action RemoveRequested;
        /// <summary>点了「返回」（不保存退出）</summary>
        public event Action BackRequested;
        /// <summary>编辑中的内容变了：四行文字 / 色名 / 要威慑旁人 —— 用来实时预览</summary>
        public event Action<string[], string, bool> PreviewChanged;

        [Header("自定义 UI（不接就自动生成一块）")]
        [Tooltip("面板根节点。自定义的话四行输入框和色板仍按自动生成的排版挂上去，面板尺寸建议不小于 900×430")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _hintText;
        [Tooltip("四个输入框，按行序")]
        [SerializeField] private InputField[] _lineFields;
        [Tooltip("「完成」按钮，自定义面板必须接，否则退不出挂牌模式")]
        [SerializeField] private Button _doneButton;
        [Tooltip("「取下」按钮")]
        [SerializeField] private Button _removeButton;
        [Tooltip("「返回」按钮")]
        [SerializeField] private Button _backButton;
        [Tooltip("「威慑旁人」开关按钮")]
        [SerializeField] private Button _scareButton;
        [Tooltip("16 个色块按钮，顺序是 MaidSignPalette.All：白 橙 品红 淡蓝 黄 黄绿 粉 灰 淡灰 青 紫 蓝 棕 绿 红 黑。"
            + "按钮自己的 Image 当边框用（选中涂白、其余涂深灰），所以中间那块颜色方块自己放一个 Image 子物体；"
            + "填不满 16 个（或留空）就整排自动生成")]
        [SerializeField] private Button[] _swatches;

        const float PanelWidth = 900f;
        const float PanelHeight = 430f;
        const int SwatchColumns = 8;
        const float SwatchSize = 34f;
        const float SwatchGap = 10f;

        bool _generated;
        bool _loading;
        string _color = MaidSignPalette.Default;
        bool _scare = true;
        Image[] _swatchRings = new Image[0];
        UnityAction[] _swatchHandlers = new UnityAction[0];
        bool _swatchesGenerated;
        Outline[] _swatchMarkers = new Outline[0];
        Text _scareLabel;
        readonly string[] _buffer = new string[MaidSignData.MaxLines];

        public bool IsVisible
        {
            get { return _panelRoot != null && _panelRoot.activeSelf; }
        }

        public string ColorName
        {
            get { return _color; }
        }

        public bool Scare
        {
            get { return _scare; }
        }

        void Awake()
        {
            Build();
            Hide();
        }

        void OnDestroy()
        {
            if (_panelRoot != null && _generated)
            {
                Destroy(_panelRoot);
            }

            DoneRequested = null;
            RemoveRequested = null;
            BackRequested = null;
            PreviewChanged = null;
        }

        /// <summary>按当前存档内容填一遍再显示</summary>
        public void Show(MaidSignData data)
        {
            Build();
            if (_panelRoot == null)
            {
                return;
            }

            _loading = true;
            _color = MaidSignPalette.Normalize(data != null ? data.Color : MaidSignPalette.Default);
            _scare = data == null || data.Scare;
            for (int i = 0; i < MaidSignData.MaxLines; i++)
            {
                if (_lineFields != null && i < _lineFields.Length && _lineFields[i] != null)
                {
                    _lineFields[i].text = data != null ? data.Lines[i] ?? "" : "";
                }
            }

            _loading = false;
            RefreshSwatches();
            RefreshScareLabel();
            if (_removeButton != null)
            {
                _removeButton.interactable = data != null && data.Enabled;
            }

            _panelRoot.SetActive(true);
        }

        public void Hide()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        public void SetTitle(string title)
        {
            if (_titleText != null)
            {
                _titleText.text = title;
            }
        }

        public void SetHint(string hint)
        {
            if (_hintText != null)
            {
                _hintText.text = hint ?? "";
            }
        }

        /// <summary>当前四行输入（每次调用都重新读一遍）</summary>
        public string[] Lines()
        {
            for (int i = 0; i < MaidSignData.MaxLines; i++)
            {
                _buffer[i] = _lineFields != null && i < _lineFields.Length && _lineFields[i] != null
                    ? _lineFields[i].text
                    : "";
            }

            return _buffer;
        }

        // ------------------------------------------------------------ 事件

        void OnValueChanged(string _)
        {
            RaisePreview();
        }

        void OnSwatchClicked(int index)
        {
            _color = MaidSignPalette.All[index].Name;
            RefreshSwatches();
            RaisePreview();
        }

        void OnScareClicked()
        {
            _scare = !_scare;
            RefreshScareLabel();
            RaisePreview();
        }

        void RaisePreview()
        {
            if (_loading)
            {
                return;
            }

            if (PreviewChanged != null)
            {
                PreviewChanged(Lines(), _color, _scare);
            }
        }

        void RefreshSwatches()
        {
            EnsureSwatchVisuals();
            for (int i = 0; i < _swatchRings.Length && i < MaidSignPalette.All.Length; i++)
            {
                bool selected = MaidSignPalette.All[i].Name == _color;
                if (_swatchRings[i] != null)
                {
                    _swatchRings[i].color = selected ? Color.white : new Color(0.1f, 0.1f, 0.12f, 1f);
                }

                if (_swatchMarkers[i] != null)
                {
                    _swatchMarkers[i].enabled = selected;
                }
            }
        }

        /// <summary>
        /// 选中状态画在哪：自动生成的那种色块是"边框 Image + 里面的颜色方块"，就涂边框；
        /// 自己接的色块如果按钮里没有别的图（多半按钮本身就是那块颜色），就别动它的颜色，
        /// 改成选中时亮一圈白描边（描边是我们加的 Outline，不接的话也不会糊掉你自己上的色）
        /// </summary>
        void EnsureSwatchVisuals()
        {
            if (_swatchRings.Length == _swatches.Length && _swatchMarkers.Length == _swatches.Length)
            {
                return;
            }

            _swatchRings = new Image[_swatches.Length];
            _swatchMarkers = new Outline[_swatches.Length];
            for (int i = 0; i < _swatches.Length; i++)
            {
                Button button = _swatches[i];
                if (button == null)
                {
                    continue;
                }

                if (HasChildGraphic(button))
                {
                    _swatchRings[i] = button.image;
                }
                else
                {
                    _swatchMarkers[i] = EnsureMarker(button);
                }
            }
        }

        static bool HasChildGraphic(Button button)
        {
            for (int i = 0; i < button.transform.childCount; i++)
            {
                if (button.transform.GetChild(i).GetComponent<Graphic>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        static Outline EnsureMarker(Button button)
        {
            Outline outline = button.GetComponent<Outline>();
            if (outline == null)
            {
                outline = button.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.enabled = false;
            return outline;
        }

        void RefreshScareLabel()
        {
            if (_scareLabel != null)
            {
                _scareLabel.text = _scare ? "威慑旁人：开" : "威慑旁人：关";
            }
        }

        // ------------------------------------------------------------ 生成

        void Build()
        {
            if (_panelRoot != null)
            {
                EnsureSwatches();
                Hook();
                return;
            }

            if (GetComponent<Canvas>() == null && GetComponentInParent<Canvas>() == null
                && FindObjectOfType<Canvas>() == null)
            {
                return;
            }

            Generate();
            EnsureSwatches();
            Hook();
        }

        /// <summary>
        /// 16 个色块：接满了就用你的（顺序照 MaidSignPalette.All），填不满就整排自动生成。
        /// 点哪个由这里挂监听，选中状态是把按钮自己的 Image 涂白，所以那层应该当边框用。
        /// </summary>
        void EnsureSwatches()
        {
            int count = MaidSignPalette.All.Length;
            if (_swatches != null && _swatches.Length >= count)
            {
                bool complete = true;
                for (int i = 0; i < count; i++)
                {
                    if (_swatches[i] == null)
                    {
                        complete = false;
                        break;
                    }
                }

                if (complete)
                {
                    return;
                }

                Debug.LogWarning("挂牌面板的 Swatches 没填满 " + count
                    + " 个，整排改成自动生成的（顺序见 MaidSignPalette.All）", this);
            }

            if (_swatchesGenerated || _panelRoot == null)
            {
                return;
            }

            RectTransform panelRect = _panelRoot.transform as RectTransform;
            if (panelRect == null)
            {
                return;
            }

            _swatchesGenerated = true;
            _swatches = new Button[count];
            float rowWidth = SwatchColumns * SwatchSize + (SwatchColumns - 1) * SwatchGap;
            for (int i = 0; i < count; i++)
            {
                int column = i % SwatchColumns;
                int row = i / SwatchColumns;
                float x = -rowWidth * 0.5f + SwatchSize * 0.5f + column * (SwatchSize + SwatchGap);
                float y = -246f - row * (SwatchSize + SwatchGap);
                _swatches[i] = CreateSwatch(panelRect, MaidSignPalette.All[i], new Vector2(x, y));
            }

            _swatchRings = new Image[0];
            _swatchMarkers = new Outline[0];
        }

        void HookSwatches()
        {
            if (_swatches == null)
            {
                return;
            }

            if (_swatchHandlers.Length != _swatches.Length)
            {
                _swatchHandlers = new UnityAction[_swatches.Length];
            }

            for (int i = 0; i < _swatches.Length && i < MaidSignPalette.All.Length; i++)
            {
                Button button = _swatches[i];
                if (button == null)
                {
                    continue;
                }

                if (_swatchHandlers[i] == null)
                {
                    int captured = i;
                    _swatchHandlers[i] = delegate { OnSwatchClicked(captured); };
                }

                button.onClick.RemoveListener(_swatchHandlers[i]);
                button.onClick.AddListener(_swatchHandlers[i]);
            }
        }

        void Hook()
        {
            if (_doneButton != null)
            {
                _doneButton.onClick.RemoveListener(OnDoneClicked);
                _doneButton.onClick.AddListener(OnDoneClicked);
            }

            if (_removeButton != null)
            {
                _removeButton.onClick.RemoveListener(OnRemoveClicked);
                _removeButton.onClick.AddListener(OnRemoveClicked);
            }

            if (_backButton != null)
            {
                _backButton.onClick.RemoveListener(OnBackClicked);
                _backButton.onClick.AddListener(OnBackClicked);
            }

            if (_scareButton != null)
            {
                _scareButton.onClick.RemoveListener(OnScareClicked);
                _scareButton.onClick.AddListener(OnScareClicked);
                _scareLabel = _scareButton.GetComponentInChildren<Text>();
            }

            if (_lineFields != null)
            {
                for (int i = 0; i < _lineFields.Length; i++)
                {
                    if (_lineFields[i] == null)
                    {
                        continue;
                    }

                    _lineFields[i].onValueChanged.RemoveListener(OnValueChanged);
                    _lineFields[i].onValueChanged.AddListener(OnValueChanged);
                }
            }

            HookSwatches();
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

        /// <summary>没有现成面板就自己搭一块：标题 + 四行输入 + 色板 + 四个按钮 + 提示</summary>
        void Generate()
        {
            Transform parent = UiParent();
            if (parent == null)
            {
                return;
            }

            GameObject panel = new GameObject("MaidSignPanel", typeof(RectTransform), typeof(Image));
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
            _titleText.text = "编辑示众牌";

            _lineFields = new InputField[MaidSignData.MaxLines];
            for (int i = 0; i < MaidSignData.MaxLines; i++)
            {
                _lineFields[i] = CreateField(rect, "Line " + (i + 1),
                    new Vector2(0f, -46f - i * 48f), new Vector2(620f, 42f), "第 " + (i + 1) + " 行");
            }

            _doneButton = CreateButton(rect, "完成", new Vector2(-250f, -344f), new Vector2(140f, 46f), 20);
            _removeButton = CreateButton(rect, "取下", new Vector2(-100f, -344f), new Vector2(140f, 46f), 20);
            _backButton = CreateButton(rect, "返回", new Vector2(50f, -344f), new Vector2(140f, 46f), 20);
            _scareButton = CreateButton(rect, "威慑旁人：开", new Vector2(230f, -344f), new Vector2(180f, 46f),
                18);
            _hintText = CreateText(rect, "Hint", new Vector2(0f, -398f), new Vector2(860f, 28f), 16,
                TextAnchor.MiddleCenter);
            _hintText.color = new Color(1f, 1f, 1f, 0.75f);
            _scareLabel = _scareButton.GetComponentInChildren<Text>();
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
            textRect.anchoredPosition = Vector2.zero;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.text = label;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return go.GetComponent<Button>();
        }

        static InputField CreateField(Transform parent, string name, Vector2 position, Vector2 size,
            string placeholder)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image background = go.GetComponent<Image>();
            background.color = new Color(0.16f, 0.16f, 0.2f, 1f);

            Text placeholderText = CreateText(rect, "Placeholder", Vector2.zero, Vector2.zero, 20,
                TextAnchor.MiddleLeft);
            Stretch(placeholderText.rectTransform, 12f);
            placeholderText.text = placeholder;
            placeholderText.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderText.fontStyle = FontStyle.Italic;

            Text input = CreateText(rect, "Text", Vector2.zero, Vector2.zero, 20, TextAnchor.MiddleLeft);
            Stretch(input.rectTransform, 12f);
            input.supportRichText = false;

            InputField field = go.GetComponent<InputField>();
            field.targetGraphic = background;
            field.textComponent = input;
            field.placeholder = placeholderText;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = MaidSignData.MaxLineLength;
            field.text = "";
            return field;
        }

        static Button CreateSwatch(Transform parent, MaidSignPalette.Entry entry, Vector2 position)
        {
            GameObject go = new GameObject("Swatch " + entry.Name, typeof(RectTransform), typeof(Image),
                typeof(Button));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(SwatchSize, SwatchSize);

            // 按钮自己的 Image 就是那圈边框，里面再放一块染色方块
            Image ring = go.GetComponent<Image>();
            ring.color = new Color(0.1f, 0.1f, 0.12f, 1f);

            GameObject colorObject = new GameObject("Color", typeof(RectTransform), typeof(Image));
            RectTransform colorRect = colorObject.GetComponent<RectTransform>();
            colorRect.SetParent(rect, false);
            colorRect.anchorMin = Vector2.zero;
            colorRect.anchorMax = Vector2.one;
            colorRect.pivot = new Vector2(0.5f, 0.5f);
            colorRect.offsetMin = new Vector2(3f, 3f);
            colorRect.offsetMax = new Vector2(-3f, -3f);
            Image color = colorObject.GetComponent<Image>();
            color.color = entry.Text;
            color.raycastTarget = false;

            // 点击由 HookSwatches 统一挂（自动生成的和自己接的都走同一条路，免得挂两遍）
            return go.GetComponent<Button>();
        }

        static void Stretch(RectTransform rect, float horizontalPad)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(horizontalPad, 0f);
            rect.offsetMax = new Vector2(-horizontalPad, 0f);
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
