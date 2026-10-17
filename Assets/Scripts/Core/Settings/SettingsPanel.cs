using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MaidHome.Core.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MaidHome.Core.Settings
{
    /// <summary>
    /// 反射式设置面板：字段上的 [Setting] 决定标题/说明/顺序/范围，方法上的
    /// [SettingButton] 生成按钮。支持 int / float / string / bool / enum / button。
    /// 顶栏右上角有一个「设置」按钮，F9 也可以开关；设置自动保存到 saves/config/settings.json。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Instance { get; private set; }

        [Header("开关动画")]
        [SerializeField] private float _animationSeconds = 0.3f;
        [SerializeField] private float _panelSlideOffset = 1100f;
        [SerializeField] private float _cameraLift = 3f;
        [SerializeField] private float _sideBarMargin = 40f;

        [Header("关于面板")]
        [Tooltip("单独的关于面板预制体；留空会尝试 Resources/Settings/AboutPanel，仍找不到就用内置面板")]
        [SerializeField] private GameObject _aboutPrefab;
        [SerializeField] private string _aboutPrefabResourcePath = "Settings/AboutPanel";

        sealed class RowEntry
        {
            public int Order;
            public string Label;
            public string Section;
            public FieldInfo Field;
            public MethodInfo Method;
        }

        readonly List<GameObject> _rows = new List<GameObject>();

        Font _font;
        GameObject _canvasObject;
        RectTransform _overlay;
        RectTransform _panel;
        RectTransform _aboutPanel;
        Vector2 _aboutShownPosition;
        RectTransform _content;
        ScrollRect _scroll;
        ScrollRect _aboutScroll;
        Text _status;
        Image _shade;
        Button _openButton;
        Button _sceneButton;
        bool _open;
        Coroutine _animation;
        RectTransform _sideBar;
        Vector2 _sideBarShown;
        Vector2 _sideBarHidden;
        Camera _camera;
        Vector3 _cameraBase;
        bool _cameraBound;
        bool _dirty;
        float _saveAt;
        bool _aboutOpen;
        Coroutine _aboutAnimation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindObjectsOfType<SettingsPanel>(true).Length > 0)
            {
                return;
            }

            GameObject root = new GameObject("Settings");
            root.AddComponent<SettingsPanel>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            Build();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_sceneButton != null)
            {
                _sceneButton.onClick.RemoveListener(Toggle);
            }
        }

        void Update()
        {
            if (PointerInput.SettingsTogglePressed)
            {
                Toggle();
            }

            if (_dirty && Time.unscaledTime >= _saveAt)
            {
                SaveAndShowStatus();
            }
        }

        public void Toggle()
        {
            if (_open)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            EnsureActive();
            if (_overlay == null)
            {
                Build();
            }

            if (_overlay == null)
            {
                return;
            }

            bool wasOpen = _open;
            _open = true;
            RebuildRows();
            if (_aboutPanel != null)
            {
                if (_aboutAnimation != null)
                {
                    StopCoroutine(_aboutAnimation);
                    _aboutAnimation = null;
                }

                _aboutOpen = false;
                _aboutPanel.anchoredPosition = _aboutShownPosition + new Vector2(0f, _panelSlideOffset);
                _aboutPanel.gameObject.SetActive(false);
            }

            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }

            if (!wasOpen)
            {
                BindSideBar();
                BindCamera();
            }

            Animate(true);
        }

        public void Close()
        {
            if (_overlay == null || !_open)
            {
                return;
            }

            if (_dirty)
            {
                SaveAndShowStatus();
            }

            if (_aboutOpen && _aboutPanel != null)
            {
                if (_aboutAnimation != null)
                {
                    StopCoroutine(_aboutAnimation);
                    _aboutAnimation = null;
                }

                _aboutOpen = false;
                _aboutPanel.gameObject.SetActive(false);
            }

            _open = false;
            Animate(false);
        }

        void Animate(bool opening)
        {
            EnsureActive();
            if (_animation != null)
            {
                StopCoroutine(_animation);
            }

            _animation = StartCoroutine(AnimateRoutine(opening));
        }

        IEnumerator AnimateRoutine(bool opening)
        {
            if (_overlay == null)
            {
                yield break;
            }

            _overlay.gameObject.SetActive(true);
            Vector2 panelStart = _panel.anchoredPosition;
            Vector2 panelEnd = opening ? Vector2.zero : new Vector2(0f, _panelSlideOffset);
            Vector2 sideStart = _sideBar != null ? _sideBar.anchoredPosition : Vector2.zero;
            Vector2 sideEnd = _sideBar != null
                ? (opening ? _sideBarHidden : _sideBarShown)
                : Vector2.zero;
            Vector3 cameraStart = _camera != null ? _camera.transform.position : Vector3.zero;
            Vector3 cameraEnd = _camera != null
                ? _cameraBase + Vector3.up * (opening ? _cameraLift : 0f)
                : Vector3.zero;
            Color shadeStart = _shade != null ? _shade.color : Color.clear;
            Color shadeEnd = new Color(0f, 0f, 0f, opening ? 0.62f : 0f);

            float duration = Mathf.Max(0f, _animationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                ApplyAnimation(panelStart, panelEnd, sideStart, sideEnd, cameraStart, cameraEnd,
                    shadeStart, shadeEnd, t);
                yield return null;
            }

            ApplyAnimation(panelStart, panelEnd, sideStart, sideEnd, cameraStart, cameraEnd,
                shadeStart, shadeEnd, 1f);
            if (!opening)
            {
                _overlay.gameObject.SetActive(false);
                if (_camera != null)
                {
                    _camera.transform.position = _cameraBase;
                }

                _cameraBound = false;
            }

            _animation = null;
        }

        void ApplyAnimation(Vector2 panelStart, Vector2 panelEnd, Vector2 sideStart, Vector2 sideEnd,
            Vector3 cameraStart, Vector3 cameraEnd, Color shadeStart, Color shadeEnd, float t)
        {
            if (_panel != null)
            {
                _panel.anchoredPosition = Vector2.Lerp(panelStart, panelEnd, t);
            }

            if (_sideBar != null)
            {
                _sideBar.anchoredPosition = Vector2.Lerp(sideStart, sideEnd, t);
            }

            if (_camera != null)
            {
                _camera.transform.position = Vector3.Lerp(cameraStart, cameraEnd, t);
            }

            if (_shade != null)
            {
                _shade.color = Color.Lerp(shadeStart, shadeEnd, t);
            }
        }

        void Build()
        {
            if (_canvasObject != null)
            {
                return;
            }

            EnsureEventSystem();
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            _canvasObject = new GameObject("SettingsCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _canvasObject.transform.SetParent(transform, false);
            Canvas canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _openButton = CreateButton(_canvasObject.transform, "OpenSettings", "设置",
                new Color(0.2f, 0.22f, 0.28f, 0.95f), 28);
            RectTransform openRect = _openButton.GetComponent<RectTransform>();
            openRect.anchorMin = new Vector2(1f, 1f);
            openRect.anchorMax = new Vector2(1f, 1f);
            openRect.pivot = new Vector2(1f, 1f);
            openRect.sizeDelta = new Vector2(160f, 70f);
            openRect.anchoredPosition = new Vector2(-32f, -32f);
            _openButton.onClick.AddListener(Toggle);

            _overlay = CreateRect("SettingsOverlay", _canvasObject.transform);
            _overlay.anchorMin = Vector2.zero;
            _overlay.anchorMax = Vector2.one;
            _overlay.offsetMin = Vector2.zero;
            _overlay.offsetMax = Vector2.zero;
            _shade = _overlay.gameObject.AddComponent<Image>();
            _shade.color = new Color(0f, 0f, 0f, 0f);

            _panel = CreateRect("SettingsPanel", _overlay);
            _panel.anchorMin = new Vector2(0.07f, 0.07f);
            _panel.anchorMax = new Vector2(0.93f, 0.93f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = Vector2.zero;
            _panel.anchoredPosition = new Vector2(0f, _panelSlideOffset);
            Image panelImage = _panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.075f, 0.08f, 0.11f, 1f);

            Text title = CreateText(_panel, "Title", "设置", 48, Color.white, TextAnchor.MiddleLeft);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(32f, -100f);
            titleRect.offsetMax = new Vector2(-220f, -20f);
            title.fontStyle = FontStyle.Bold;

            Button closeButton = CreateButton(_panel, "Close", "关闭",
                new Color(0.32f, 0.16f, 0.18f, 1f), 26);
            RectTransform closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(160f, 64f);
            closeRect.anchoredPosition = new Vector2(-28f, -20f);
            closeButton.onClick.AddListener(Close);

            RectTransform scrollRect = CreateRect("ScrollView", _panel);
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(32f, 80f);
            scrollRect.offsetMax = new Vector2(-32f, -116f);
            _scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 55f;

            RectTransform viewport = CreateRect("Viewport", scrollRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _content = CreateRect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(0, 0, 6, 20);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewport;
            _scroll.content = _content;

            _status = CreateText(_panel, "Status", "", 22, new Color(0.68f, 0.72f, 0.8f, 1f),
                TextAnchor.MiddleLeft);
            RectTransform statusRect = _status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0f, 0f);
            statusRect.offsetMin = new Vector2(32f, 18f);
            statusRect.offsetMax = new Vector2(-32f, 64f);

            EnsureAboutPanel();
            _overlay.gameObject.SetActive(false);
            BindOpenButton();
            BindSideBar();
        }

        void EnsureAboutPanel()
        {
            if (_aboutPanel != null)
            {
                return;
            }

            GameObject prefab = _aboutPrefab != null
                ? _aboutPrefab
                : Resources.Load<GameObject>(_aboutPrefabResourcePath);
            if (prefab != null && InstantiateAboutPrefab(prefab))
            {
                return;
            }

            BuildGeneratedAboutPanel();
        }

        bool InstantiateAboutPrefab(GameObject prefab)
        {
            GameObject instance = Instantiate(prefab, _overlay, false);
            instance.name = "AboutPanel";
            RectTransform rect = instance.GetComponent<RectTransform>();
            if (rect == null)
            {
                Debug.LogWarning("[Settings] 关于面板预制体没有 RectTransform，改用内置关于面板", instance);
                Destroy(instance);
                return false;
            }

            _aboutPanel = rect;
            _aboutShownPosition = rect.anchoredPosition;
            rect.anchoredPosition = _aboutShownPosition + new Vector2(0f, _panelSlideOffset);
            _aboutScroll = instance.GetComponentInChildren<ScrollRect>(true);

            Button[] buttons = instance.GetComponentsInChildren<Button>(true);
            bool foundBack = false;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!IsBackButton(buttons[i]))
                {
                    continue;
                }

                buttons[i].onClick.AddListener(CloseAbout);
                foundBack = true;
            }

            if (!foundBack && buttons.Length == 1)
            {
                buttons[0].onClick.AddListener(CloseAbout);
                foundBack = true;
            }

            if (!foundBack)
            {
                Debug.LogWarning("[Settings] 关于面板预制体里没找到返回/关闭按钮，只能靠设置面板的关闭按钮退出", instance);
            }

            instance.SetActive(false);
            return true;
        }

        static bool IsBackButton(Button button)
        {
            if (button == null)
            {
                return false;
            }

            string name = button.name.ToLowerInvariant();
            if (name.Contains("back") || name.Contains("close")
                || name.Contains("返回") || name.Contains("关闭"))
            {
                return true;
            }

            Text label = button.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                return false;
            }

            string text = label.text.ToLowerInvariant();
            return text.Contains("返回") || text.Contains("关闭")
                || text == "back" || text == "close";
        }

        void BuildGeneratedAboutPanel()
        {
            _aboutPanel = CreateRect("AboutPanel", _overlay);
            _aboutShownPosition = Vector2.zero;
            _aboutPanel.anchorMin = new Vector2(0.12f, 0.12f);
            _aboutPanel.anchorMax = new Vector2(0.88f, 0.88f);
            _aboutPanel.pivot = new Vector2(0.5f, 0.5f);
            _aboutPanel.sizeDelta = Vector2.zero;
            _aboutPanel.anchoredPosition = _aboutShownPosition + new Vector2(0f, _panelSlideOffset);
            Image aboutImage = _aboutPanel.gameObject.AddComponent<Image>();
            aboutImage.color = new Color(0.065f, 0.07f, 0.095f, 1f);

            Text title = CreateText(_aboutPanel, "Title", "关于", 48, Color.white, TextAnchor.MiddleLeft);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(32f, -100f);
            titleRect.offsetMax = new Vector2(-220f, -20f);
            title.fontStyle = FontStyle.Bold;

            Button closeButton = CreateButton(_aboutPanel, "Back", "返回",
                new Color(0.24f, 0.3f, 0.4f, 1f), 26);
            RectTransform closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(160f, 64f);
            closeRect.anchoredPosition = new Vector2(-28f, -20f);
            closeButton.onClick.AddListener(CloseAbout);

            RectTransform scrollRect = CreateRect("AboutScroll", _aboutPanel);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(32f, 28f);
            scrollRect.offsetMax = new Vector2(-32f, -116f);
            _aboutScroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            _aboutScroll.horizontal = false;
            _aboutScroll.vertical = true;
            _aboutScroll.movementType = ScrollRect.MovementType.Clamped;
            _aboutScroll.scrollSensitivity = 55f;

            RectTransform viewport = CreateRect("Viewport", scrollRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 24);
            layout.spacing = 0f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _aboutScroll.viewport = viewport;
            _aboutScroll.content = content;

            Text text = CreateText(content, "AboutText", "", 25, Color.white, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.2f;
            TextAsset asset = Resources.Load<TextAsset>("Settings/about");
            text.text = asset != null && !string.IsNullOrEmpty(asset.text)
                ? asset.text
                : "在 Assets/Resources/Settings/about.txt 里填写关于内容。";

            _aboutPanel.gameObject.SetActive(false);
        }

        public void OpenAbout()
        {
            if (_aboutPanel == null)
            {
                return;
            }

            _aboutOpen = true;
            _aboutPanel.gameObject.SetActive(true);
            if (_aboutScroll != null)
            {
                _aboutScroll.verticalNormalizedPosition = 1f;
            }

            AnimateAbout(true);
        }

        public void CloseAbout()
        {
            if (_aboutPanel == null || !_aboutOpen)
            {
                return;
            }

            _aboutOpen = false;
            AnimateAbout(false);
        }

        void AnimateAbout(bool opening)
        {
            if (_aboutAnimation != null)
            {
                StopCoroutine(_aboutAnimation);
            }

            _aboutAnimation = StartCoroutine(AnimateAboutRoutine(opening));
        }

        IEnumerator AnimateAboutRoutine(bool opening)
        {
            Vector2 start = _aboutPanel.anchoredPosition;
            Vector2 end = opening
                ? _aboutShownPosition
                : _aboutShownPosition + new Vector2(0f, _panelSlideOffset);
            float duration = Mathf.Max(0f, _animationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                _aboutPanel.anchoredPosition = Vector2.Lerp(start, end, t);
                yield return null;
            }

            _aboutPanel.anchoredPosition = end;
            if (!opening)
            {
                _aboutPanel.gameObject.SetActive(false);
            }

            _aboutAnimation = null;
        }

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            _sideBar = null;
            _sceneButton = null;
            _camera = null;
            _cameraBound = false;
            EnsureActive();
            StartCoroutine(BindAfterScene());
        }

        void EnsureActive()
        {
            Transform current = transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }

                current = current.parent;
            }

            if (_canvasObject != null && !_canvasObject.activeSelf)
            {
                _canvasObject.SetActive(true);
            }
        }

        IEnumerator BindAfterScene()
        {
            yield return null;
            BindOpenButton();
            BindSideBar();
        }

        void BindOpenButton()
        {
            if (_sceneButton != null)
            {
                return;
            }

            Button[] buttons = FindObjectsOfType<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null || button == _openButton)
                {
                    continue;
                }

                string objectName = button.name.ToLowerInvariant();
                Text label = button.GetComponentInChildren<Text>(true);
                string labelText = label != null ? label.text : "";
                if (objectName.Contains("setting") || objectName.Contains("设置")
                    || labelText.Contains("设置"))
                {
                    _sceneButton = button;
                    _sceneButton.onClick.RemoveListener(Toggle);
                    _sceneButton.onClick.AddListener(Toggle);
                    break;
                }
            }

            if (_openButton != null)
            {
                _openButton.gameObject.SetActive(_sceneButton == null);
            }
        }

        void BindSideBar()
        {
            if (_sideBar != null)
            {
                return;
            }

            string[] names = { "sidebutton", "sidebar", "侧边栏" };
            RectTransform[] all = FindObjectsOfType<RectTransform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string objectName = all[i].name.ToLowerInvariant();
                for (int n = 0; n < names.Length; n++)
                {
                    if (objectName.Contains(names[n]))
                    {
                        _sideBar = all[i];
                        break;
                    }
                }

                if (_sideBar != null)
                {
                    break;
                }
            }

            if (_sideBar != null)
            {
                _sideBarShown = _sideBar.anchoredPosition;
                ComputeSideBarHidden();
            }

            if (_openButton != null)
            {
                _openButton.gameObject.SetActive(_sceneButton == null);
            }
        }

        void ComputeSideBarHidden()
        {
            RectTransform parent = _sideBar != null ? _sideBar.parent as RectTransform : null;
            if (parent == null)
            {
                _sideBarHidden = _sideBarShown + new Vector2(600f, 0f);
                return;
            }

            Canvas.ForceUpdateCanvases();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, _sideBar);
            bool right = bounds.center.x >= parent.rect.center.x;
            float shift = right
                ? parent.rect.xMax - bounds.min.x + _sideBarMargin
                : parent.rect.xMin - bounds.max.x - _sideBarMargin;
            _sideBarHidden = _sideBarShown + new Vector2(shift, 0f);
        }

        void BindCamera()
        {
            if (_camera != null && _cameraBound)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                Camera[] cameras = FindObjectsOfType<Camera>(true);
                if (cameras.Length > 0)
                {
                    _camera = cameras[0];
                }
            }

            if (_camera != null && !_cameraBound)
            {
                _cameraBase = _camera.transform.position;
                _cameraBound = true;
            }
        }

        void RebuildRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    Destroy(_rows[i]);
                }
            }

            _rows.Clear();
            GameSettings settings = GameSettingsStore.Current;
            List<RowEntry> entries = CollectEntries();
            string lastSection = null;
            for (int i = 0; i < entries.Count; i++)
            {
                RowEntry entry = entries[i];
                if (!string.IsNullOrEmpty(entry.Section) && entry.Section != lastSection)
                {
                    BuildSectionHeader(entry.Section);
                    lastSection = entry.Section;
                }

                if (entry.Field != null)
                {
                    BuildFieldRow(settings, entry);
                }
                else if (entry.Method != null)
                {
                    BuildMethodRow(settings, entry);
                }
            }

            if (_status != null)
            {
                _status.text = "自动保存到: " + GameSettingsStore.FilePath;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            Canvas.ForceUpdateCanvases();
        }

        List<RowEntry> CollectEntries()
        {
            List<RowEntry> entries = new List<RowEntry>();
            FieldInfo[] fields = typeof(GameSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                SettingAttribute attribute = fields[i].GetCustomAttribute<SettingAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                RowEntry entry = new RowEntry();
                entry.Order = attribute.Order;
                entry.Label = attribute.Label;
                entry.Section = attribute.Section;
                entry.Field = fields[i];
                entries.Add(entry);
            }

            MethodInfo[] methods = typeof(GameSettings).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                SettingButtonAttribute attribute = methods[i].GetCustomAttribute<SettingButtonAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                if (methods[i].GetParameters().Length > 0)
                {
                    Debug.LogWarning("[Settings] 按钮方法不能有参数，已跳过: " + methods[i].Name);
                    continue;
                }

                RowEntry entry = new RowEntry();
                entry.Order = attribute.Order;
                entry.Label = attribute.Label;
                entry.Section = attribute.Section;
                entry.Method = methods[i];
                entries.Add(entry);
            }

            entries.Sort((a, b) =>
            {
                int order = a.Order.CompareTo(b.Order);
                return order != 0 ? order : string.Compare(a.Label, b.Label, StringComparison.Ordinal);
            });
            return entries;
        }

        void BuildSectionHeader(string title)
        {
            RectTransform row = CreateRow("Section_" + title, 64f);
            Image image = row.GetComponent<Image>();
            image.color = new Color(0.16f, 0.36f, 0.55f, 0.35f);
            Text text = CreateText(row, "Text", title, 30, new Color(0.75f, 0.9f, 1f, 1f),
                TextAnchor.MiddleLeft);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24f, 0f);
            rect.offsetMax = new Vector2(-24f, 0f);
            text.fontStyle = FontStyle.Bold;
        }

        void BuildFieldRow(GameSettings settings, RowEntry entry)
        {
            FieldInfo field = entry.Field;
            SettingAttribute attribute = field.GetCustomAttribute<SettingAttribute>();
            bool hasDescription = !string.IsNullOrEmpty(attribute.Description);
            RectTransform row = CreateRow(field.Name, hasDescription ? 150f : 112f);

            Text title = CreateText(row, "Label", attribute.Label, 30, Color.white, TextAnchor.MiddleLeft);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(24f, -54f);
            titleRect.offsetMax = new Vector2(-24f, -10f);

            if (hasDescription)
            {
                Text description = CreateText(row, "Description", attribute.Description, 21,
                    new Color(0.65f, 0.7f, 0.8f, 1f), TextAnchor.MiddleLeft);
                RectTransform descRect = description.rectTransform;
                descRect.anchorMin = new Vector2(0f, 1f);
                descRect.anchorMax = new Vector2(1f, 1f);
                descRect.pivot = new Vector2(0f, 1f);
                descRect.offsetMin = new Vector2(24f, -86f);
                descRect.offsetMax = new Vector2(-24f, -54f);
            }

            Type type = field.FieldType;
            if (type == typeof(bool))
            {
                BuildBoolControl(row, settings, field);
            }
            else if (type.IsEnum)
            {
                BuildEnumControl(row, settings, field, attribute);
            }
            else if (attribute.Slider && (type == typeof(int) || type == typeof(float)))
            {
                BuildSliderControl(row, settings, field, attribute);
            }
            else
            {
                BuildInputControl(row, settings, field, attribute);
            }
        }

        void BuildInputControl(RectTransform row, GameSettings settings, FieldInfo field,
            SettingAttribute attribute)
        {
            object value = field.GetValue(settings);
            string text = value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
            InputField input = CreateInput(row, field.Name, text);
            RectTransform rect = input.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.offsetMin = new Vector2(24f, 12f);
            rect.offsetMax = new Vector2(-24f, 66f);

            Type type = field.FieldType;
            if (type == typeof(int))
            {
                input.contentType = InputField.ContentType.IntegerNumber;
            }
            else if (type == typeof(float))
            {
                input.contentType = InputField.ContentType.DecimalNumber;
            }

            input.onEndEdit.AddListener(valueText =>
            {
                if (type == typeof(int))
                {
                    int parsed;
                    int current = value is int ? (int)value : 0;
                    if (!int.TryParse(valueText, out parsed))
                    {
                        input.text = current.ToString(CultureInfo.InvariantCulture);
                        return;
                    }

                    if (attribute.HasRange)
                    {
                        parsed = Mathf.Clamp(parsed, Mathf.RoundToInt(attribute.Min),
                            Mathf.RoundToInt(attribute.Max));
                    }

                    field.SetValue(settings, parsed);
                    input.text = parsed.ToString(CultureInfo.InvariantCulture);
                }
                else if (type == typeof(float))
                {
                    float parsed;
                    float current = value is float ? (float)value : 0f;
                    if (!float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                        && !float.TryParse(valueText, out parsed))
                    {
                        input.text = current.ToString("0.###", CultureInfo.InvariantCulture);
                        return;
                    }

                    if (attribute.HasRange)
                    {
                        parsed = Mathf.Clamp(parsed, attribute.Min, attribute.Max);
                    }

                    field.SetValue(settings, parsed);
                    input.text = parsed.ToString("0.###", CultureInfo.InvariantCulture);
                }
                else
                {
                    field.SetValue(settings, valueText ?? "");
                }

                SaveAndShowStatus();
            });
        }

        void BuildBoolControl(RectTransform row, GameSettings settings, FieldInfo field)
        {
            bool value = field.GetValue(settings) is bool && (bool)field.GetValue(settings);

            GameObject toggleObject = new GameObject("Toggle", typeof(RectTransform), typeof(Image),
                typeof(Toggle));
            toggleObject.transform.SetParent(row, false);
            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(0f, 0f);
            toggleRect.anchorMax = new Vector2(0f, 0f);
            toggleRect.pivot = new Vector2(0f, 0f);
            toggleRect.sizeDelta = new Vector2(56f, 44f);
            toggleRect.anchoredPosition = new Vector2(24f, 12f);
            Image background = toggleObject.GetComponent<Image>();
            background.color = new Color(0.18f, 0.2f, 0.26f, 1f);

            GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(toggleObject.transform, false);
            RectTransform checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(10f, 10f);
            checkRect.offsetMax = new Vector2(-10f, -10f);
            Image checkImage = check.GetComponent<Image>();
            checkImage.color = new Color(0.35f, 0.85f, 0.55f, 1f);
            checkImage.raycastTarget = false;

            Toggle toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkImage;
            toggle.isOn = value;

            Text state = CreateText(row, "State", value ? "开" : "关", 26,
                new Color(0.8f, 0.85f, 0.95f, 1f), TextAnchor.MiddleLeft);
            RectTransform stateRect = state.rectTransform;
            stateRect.anchorMin = new Vector2(0f, 0f);
            stateRect.anchorMax = new Vector2(0f, 0f);
            stateRect.pivot = new Vector2(0f, 0f);
            stateRect.sizeDelta = new Vector2(160f, 44f);
            stateRect.anchoredPosition = new Vector2(94f, 12f);

            toggle.onValueChanged.AddListener(on =>
            {
                field.SetValue(settings, on);
                state.text = on ? "开" : "关";
                SaveAndShowStatus();
            });
        }

        void BuildSliderControl(RectTransform row, GameSettings settings, FieldInfo field,
            SettingAttribute attribute)
        {
            bool integer = field.FieldType == typeof(int);
            object value = field.GetValue(settings);
            float current = integer
                ? (value is int ? (int)value : 0f)
                : (value is float ? (float)value : 0f);
            float min = attribute.HasRange ? attribute.Min : 0f;
            float max = attribute.HasRange ? attribute.Max : 100f;
            if (max <= min)
            {
                max = min + 1f;
            }

            GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Image),
                typeof(Slider));
            sliderObject.transform.SetParent(row, false);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 0f);
            sliderRect.pivot = new Vector2(0f, 0f);
            sliderRect.offsetMin = new Vector2(24f, 14f);
            sliderRect.offsetMax = new Vector2(-164f, 64f);

            Image background = sliderObject.GetComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.07f, 1f);

            RectTransform fillArea = CreateRect("Fill Area", sliderObject.transform);
            fillArea.anchorMin = new Vector2(0f, 0f);
            fillArea.anchorMax = new Vector2(1f, 1f);
            fillArea.offsetMin = new Vector2(2f, 2f);
            fillArea.offsetMax = new Vector2(-2f, -2f);
            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(fillArea, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillObject.GetComponent<Image>().color = new Color(0.28f, 0.62f, 0.95f, 1f);

            RectTransform handleArea = CreateRect("Handle Slide Area", sliderObject.transform);
            handleArea.anchorMin = new Vector2(0f, 0f);
            handleArea.anchorMax = new Vector2(1f, 1f);
            handleArea.offsetMin = new Vector2(14f, 0f);
            handleArea.offsetMax = new Vector2(-14f, 0f);
            GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(handleArea, false);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(30f, 30f);
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = Color.white;

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = integer;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = Mathf.Clamp(current, min, max);

            Text valueText = CreateText(row, "Value", FormatValue(field, attribute, current), 26,
                new Color(0.8f, 0.85f, 0.95f, 1f), TextAnchor.MiddleRight);
            RectTransform valueRect = valueText.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 0f);
            valueRect.pivot = new Vector2(1f, 0f);
            valueRect.sizeDelta = new Vector2(130f, 54f);
            valueRect.anchoredPosition = new Vector2(-24f, 14f);

            slider.onValueChanged.AddListener(v =>
            {
                if (integer)
                {
                    field.SetValue(settings, Mathf.RoundToInt(v));
                }
                else
                {
                    field.SetValue(settings, v);
                }

                valueText.text = FormatValue(field, attribute, v);
                GameSettingsStore.NotifyChanged();
                MarkDirty();
            });
        }

        static string FormatValue(FieldInfo field, SettingAttribute attribute, float value)
        {
            string text = field.FieldType == typeof(int)
                ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
            return text + (attribute.Suffix ?? "");
        }

        void MarkDirty()
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + 0.25f;
        }

        void BuildEnumControl(RectTransform row, GameSettings settings, FieldInfo field,
            SettingAttribute attribute)
        {
            Type type = field.FieldType;
            Array values = Enum.GetValues(type);
            object current = field.GetValue(settings);
            Button button = CreateButton(row, "Enum", "", new Color(0.18f, 0.22f, 0.3f, 1f), 26);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.offsetMin = new Vector2(24f, 12f);
            rect.offsetMax = new Vector2(-24f, 66f);
            Text label = button.GetComponentInChildren<Text>();
            label.text = EnumText(current, values, attribute);
            button.onClick.AddListener(() =>
            {
                int index = Array.IndexOf(values, field.GetValue(settings));
                index = values.Length == 0 ? -1 : (index + 1) % values.Length;
                if (index >= 0)
                {
                    object next = values.GetValue(index);
                    field.SetValue(settings, next);
                    label.text = EnumText(next, values, attribute);
                    SaveAndShowStatus();
                }
            });
        }

        static string EnumText(object value, Array values, SettingAttribute attribute)
        {
            if (value == null)
            {
                return "";
            }

            int index = Array.IndexOf(values, value);
            string label = attribute.EnumLabels != null && index >= 0 && index < attribute.EnumLabels.Length
                ? attribute.EnumLabels[index]
                : value.ToString();
            return "当前值：" + label + "（点击切换）";
        }

        void BuildMethodRow(GameSettings settings, RowEntry entry)
        {
            SettingButtonAttribute attribute = entry.Method.GetCustomAttribute<SettingButtonAttribute>();
            bool hasDescription = !string.IsNullOrEmpty(attribute.Description);
            RectTransform row = CreateRow(entry.Method.Name, hasDescription ? 140f : 100f);

            if (hasDescription)
            {
                Text description = CreateText(row, "Description", attribute.Description, 21,
                    new Color(0.65f, 0.7f, 0.8f, 1f), TextAnchor.MiddleLeft);
                RectTransform descRect = description.rectTransform;
                descRect.anchorMin = new Vector2(0f, 1f);
                descRect.anchorMax = new Vector2(1f, 1f);
                descRect.pivot = new Vector2(0f, 1f);
                descRect.offsetMin = new Vector2(24f, -42f);
                descRect.offsetMax = new Vector2(-24f, -6f);
            }

            Button button = CreateButton(row, "Action", attribute.Label,
                new Color(0.2f, 0.34f, 0.26f, 1f), 28);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.offsetMin = new Vector2(24f, 12f);
            rect.offsetMax = new Vector2(-24f, hasDescription ? 58f : 72f);
            button.onClick.AddListener(() =>
            {
                try
                {
                    entry.Method.Invoke(settings, null);
                    if (_status != null)
                    {
                        _status.text = "已执行: " + attribute.Label + "｜自动保存到: "
                            + GameSettingsStore.FilePath;
                    }
                }
                catch (Exception error)
                {
                    if (_status != null)
                    {
                        _status.text = "按钮执行失败: " + error.Message;
                    }

                    Debug.LogWarning("[Settings] 按钮执行失败: " + error);
                }
            });
        }

        void SaveAndShowStatus()
        {
            _dirty = false;
            string error;
            bool saved = GameSettingsStore.Save(out error);
            if (_status != null)
            {
                _status.text = saved
                    ? "已保存: " + GameSettingsStore.FilePath
                    : "保存失败: " + error;
            }
        }

        RectTransform CreateRow(string name, float height)
        {
            GameObject row = new GameObject("Setting_" + name, typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            row.transform.SetParent(_content, false);
            row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.045f);
            LayoutElement layout = row.GetComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            _rows.Add(row);
            return row.GetComponent<RectTransform>();
        }

        InputField CreateInput(Transform parent, string name, string value)
        {
            GameObject inputObject = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(InputField));
            inputObject.transform.SetParent(parent, false);
            Image background = inputObject.GetComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.07f, 1f);
            InputField input = inputObject.GetComponent<InputField>();
            input.targetGraphic = background;
            input.lineType = InputField.LineType.SingleLine;

            Text text = CreateText(inputObject.transform, "Text", "", 27, Color.white,
                TextAnchor.MiddleLeft);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 0f);
            textRect.offsetMax = new Vector2(-16f, 0f);
            text.raycastTarget = false;
            input.textComponent = text;

            Text placeholder = CreateText(inputObject.transform, "Placeholder", "请输入", 27,
                new Color(0.5f, 0.55f, 0.65f, 1f), TextAnchor.MiddleLeft);
            RectTransform placeholderRect = placeholder.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(16f, 0f);
            placeholderRect.offsetMax = new Vector2(-16f, 0f);
            placeholder.raycastTarget = false;
            input.placeholder = placeholder;
            input.text = value;
            return input;
        }

        Button CreateButton(Transform parent, string name, string label, Color color, int fontSize = 22)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = color;
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            Text text = CreateText(buttonObject.transform, "Text", label, fontSize, Color.white,
                TextAnchor.MiddleCenter);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.raycastTarget = false;
            return button;
        }

        Text CreateText(Transform parent, string name, string value, int size, Color color,
            TextAnchor anchor)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.text = value;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj.GetComponent<RectTransform>();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("SettingsEventSystem", typeof(EventSystem),
                typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }
    }
}
