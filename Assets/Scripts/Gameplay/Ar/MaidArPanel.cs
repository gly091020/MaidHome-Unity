using System;
using System.Collections.Generic;
using MaidHome.Interop.Maid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Ar
{
    /// <summary>
    /// AR 界面。所有引用都在场景里接，**运行时不生成任何物体**：没接的东西就当没有这个功能。
    /// 面板只负责显示和发事件，怎么加载、怎么放置由 MaidArSession 决定。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidArPanel : MonoBehaviour
    {
        [Header("女仆列表：场景里摆好的格子，用不到的会被关掉")]
        [SerializeField] private MaidArSlot[] _slots;

        [Header("按钮")]
        [SerializeField] private Button _standButton;
        [SerializeField] private Button _sitButton;
        [SerializeField] private Button _sleepButton;
        [SerializeField] private Button _rotateLeftButton;
        [SerializeField] private Button _rotateRightButton;
        [SerializeField] private Button _shrinkButton;
        [SerializeField] private Button _growButton;
        [SerializeField] private Button _clearButton;
        [SerializeField] private Button _exitButton;

        [Header("显示")]
        [Tooltip("放下之后才出现的按钮组（坐/站、转身、清除），留空就不管")]
        [SerializeField] private GameObject _placedGroup;
        [Tooltip("放下之后才出现的姿势侧边栏（站/坐/睡），留空就不管")]
        [SerializeField] private GameObject _poseGroup;
        [SerializeField] private Text _statusText;

        [Header("文案与参数")]
        [SerializeField] private string _pickHint = "选一只背包里的女仆，再点一下地面放下她";
        [SerializeField] private string _placedHint = "单指拖动移动 · 双指捏合缩放";
        [SerializeField] private string _noSitHint = "这只模型没有坐姿";
        [SerializeField] private float _rotateStepDegrees = 15f;
        [Tooltip("按一下缩小到多少倍（放大就是它的倒数），0.8 = 每按一次小 20%")]
        [SerializeField] private float _scaleStep = 0.8f;

        [Header("自动生成（一个引用都不接的时候才会用）")]
        [Tooltip("自动生成时女仆格子一行几个")]
        [SerializeField] private int _generatedColumns = 5;
        [Tooltip("自动生成时摆几行（格子总数 = 列 × 行）")]
        [SerializeField] private int _generatedRows = 2;

        public event Action<string> MaidChosen;
        public event Action StandRequested;
        public event Action SitRequested;
        public event Action SleepRequested;
        public event Action<float> RotateRequested;
        public event Action<float> ScaleRequested;
        public event Action ClearRequested;
        public event Action ExitRequested;

        readonly List<MaidSaveData> _candidates = new List<MaidSaveData>();
        string _selectedId = "";
        RectTransform _barRect;

        void Awake()
        {
            if (NeedsGeneratedUi())
            {
                GeneratePanel();
                Debug.Log("AR 面板一个引用都没接，已自动生成一条底栏", this);
            }

            if (_slots == null)
            {
                _slots = new MaidArSlot[0];
                Debug.LogWarning("AR 面板没接女仆格子，列表里点不了人", this);
            }
            else if (_slots.Length == 0)
            {
                Debug.LogWarning("AR 面板没接女仆格子，列表里点不了人", this);
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null || !_slots[i].IsUsable)
                {
                    continue;
                }

                // 闭包要的是这一格的序号，别直接用循环变量
                int index = i;
                _slots[i].Button.onClick.AddListener(() => ChooseAt(index));
                _slots[i].SetVisible(false);
            }

            AddListener(_standButton, () => Raise(StandRequested));
            AddListener(_sitButton, () => Raise(SitRequested));
            AddListener(_sleepButton, () => Raise(SleepRequested));
            AddListener(_rotateLeftButton, () => RaiseRotate(-_rotateStepDegrees));
            AddListener(_rotateRightButton, () => RaiseRotate(_rotateStepDegrees));
            AddListener(_shrinkButton, () => RaiseScale(_scaleStep));
            AddListener(_growButton, () => RaiseScale(1f / Mathf.Max(0.05f, _scaleStep)));
            AddListener(_clearButton, () => Raise(ClearRequested));
            AddListener(_exitButton, () => Raise(ExitRequested));

            SetPlaced(false, false);
        }

        bool NeedsGeneratedUi()
        {
            return (_slots == null || _slots.Length == 0)
                && _standButton == null
                && _sitButton == null
                && _sleepButton == null
                && _rotateLeftButton == null
                && _rotateRightButton == null
                && _shrinkButton == null
                && _growButton == null
                && _clearButton == null
                && _exitButton == null
                && _statusText == null;
        }

        /// <summary>刷新列表。传进来的应该只有「在背包里」的女仆。</summary>
        public void SetCandidates(IList<MaidSaveData> maids)
        {
            _candidates.Clear();
            if (maids != null)
            {
                for (int i = 0; i < maids.Count; i++)
                {
                    _candidates.Add(maids[i]);
                }
            }

            int shown = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null || !_slots[i].IsUsable)
                {
                    continue;
                }

                bool used = i < _candidates.Count;
                _slots[i].SetVisible(used);
                if (!used)
                {
                    continue;
                }

                _slots[i].Show(DisplayName(_candidates[i]), _candidates[i].Id == _selectedId);
                shown++;
            }

            if (_candidates.Count > shown)
            {
                Debug.LogWarning("AR 面板的格子不够：背包里有 " + _candidates.Count
                    + " 只女仆，只显示了 " + shown + " 只（多摆几格就行）", this);
            }
        }

        public void SetSelected(string maidId)
        {
            _selectedId = maidId;
            for (int i = 0; i < _slots.Length && i < _candidates.Count; i++)
            {
                if (_slots[i] != null && _slots[i].IsUsable)
                {
                    _slots[i].Show(DisplayName(_candidates[i]), _candidates[i].Id == _selectedId);
                }
            }
        }

        /// <summary>放置状态变了：管一下按钮的可用性和默认提示。</summary>
        public void SetPlaced(bool placed, bool canSit)
        {
            if (_placedGroup != null)
            {
                _placedGroup.SetActive(placed);
            }

            if (_poseGroup != null)
            {
                _poseGroup.SetActive(placed);
            }

            if (_standButton != null)
            {
                _standButton.interactable = placed;
            }

            if (_sitButton != null)
            {
                _sitButton.interactable = placed && canSit;
            }

            if (placed && !canSit)
            {
                SetStatus(_noSitHint + "\n" + _placedHint);
                return;
            }

            SetStatus(placed ? _placedHint : _pickHint);
        }

        public void SetStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.text = text;
            }
        }

        /// <summary>到最小/最大了就按住对应的按钮不让点，免得按了没反应。</summary>
        public void SetScale(float scale, float min, float max)
        {
            if (_shrinkButton != null)
            {
                _shrinkButton.interactable = scale > min * 1.001f;
            }

            if (_growButton != null)
            {
                _growButton.interactable = scale < max * 0.999f;
            }
        }

        void ChooseAt(int index)
        {
            if (index < 0 || index >= _candidates.Count)
            {
                return;
            }

            Raise(MaidChosen, _candidates[index].Id);
        }

        void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }

        void Raise(Action<string> handler, string value)
        {
            if (handler != null)
            {
                handler(value);
            }
        }

        void RaiseRotate(float degrees)
        {
            if (RotateRequested != null)
            {
                RotateRequested(degrees);
            }
        }

        void RaiseScale(float factor)
        {
            if (ScaleRequested != null)
            {
                ScaleRequested(factor);
            }
        }

        static void AddListener(Button button, Action action)
        {
            if (button != null)
            {
                button.onClick.AddListener(() => action());
            }
        }

        static string DisplayName(MaidSaveData maid)
        {
            return string.IsNullOrEmpty(maid.Name) ? maid.Id : maid.Name;
        }

        /// <summary>
        /// 一个引用都没接就自己搭一条底栏（场景里连 Canvas 都没有的话连 Canvas 一起建）。
        /// 接了任意一个引用就走上面的自定义路径，这里不碰你的布局。
        /// </summary>
        void GeneratePanel()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = CreateCanvas();
            }

            EnsureEventSystem();

            GameObject bar = new GameObject("MaidArBar", typeof(RectTransform), typeof(Image));
            _barRect = bar.GetComponent<RectTransform>();
            _barRect.SetParent(canvas.transform, false);
            _barRect.anchorMin = new Vector2(0f, 0f);
            _barRect.anchorMax = new Vector2(1f, 0f);
            _barRect.pivot = new Vector2(0.5f, 0f);
            _barRect.anchoredPosition = new Vector2(0f, 24f);
            bar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.78f);

            int columns = Mathf.Max(1, _generatedColumns);
            int rows = Mathf.Max(1, _generatedRows);
            const float slotHeight = 80f;
            const float slotStride = 84f;
            const float buttonRowHeight = 64f;
            const float statusHeight = 44f;
            float gridBottom = 20f + buttonRowHeight + 16f;
            float barHeight = gridBottom + rows * slotStride + 12f + statusHeight + 8f;
            _barRect.sizeDelta = new Vector2(-40f, barHeight);

            _statusText = CreateText(_barRect, "Status", 20, TextAnchor.MiddleCenter);
            RectTransform statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -8f);
            statusRect.sizeDelta = new Vector2(-140f, statusHeight);

            // 退出单独摆：它不属于 Placed Group，任何时候都能点
            _exitButton = CreateButton(_barRect, "退出").GetComponent<Button>();
            RectTransform exitRect = _exitButton.GetComponent<RectTransform>();
            exitRect.anchorMin = new Vector2(1f, 1f);
            exitRect.anchorMax = new Vector2(1f, 1f);
            exitRect.pivot = new Vector2(1f, 1f);
            exitRect.anchoredPosition = new Vector2(-10f, -8f);
            exitRect.sizeDelta = new Vector2(110f, 44f);

            int count = columns * rows;
            _slots = new MaidArSlot[count];
            for (int i = 0; i < count; i++)
            {
                int row = i / columns;          // 0 = 最上面那行
                int column = i % columns;

                RectTransform slotRect = CreateButton(_barRect, "女仆 " + (i + 1));
                slotRect.anchorMin = new Vector2((float)column / columns, 0f);
                slotRect.anchorMax = new Vector2((float)(column + 1) / columns, 0f);
                slotRect.pivot = new Vector2(0.5f, 0f);
                slotRect.anchoredPosition = new Vector2(0f, gridBottom + (rows - 1 - row) * slotStride);
                slotRect.sizeDelta = new Vector2(-10f, slotHeight);

                MaidArSlot slot = new MaidArSlot();
                slot.Bind(slotRect.GetComponent<Button>(), slotRect.GetComponentInChildren<Text>(),
                    slotRect.GetComponent<Image>());
                _slots[i] = slot;
            }

            _placedGroup = new GameObject("Placed Group", typeof(RectTransform));
            RectTransform groupRect = _placedGroup.GetComponent<RectTransform>();
            groupRect.SetParent(_barRect, false);
            groupRect.anchorMin = new Vector2(0f, 0f);
            groupRect.anchorMax = new Vector2(1f, 0f);
            groupRect.pivot = new Vector2(0.5f, 0f);
            groupRect.anchoredPosition = new Vector2(0f, 20f);
            groupRect.sizeDelta = new Vector2(-20f, buttonRowHeight);

            _rotateLeftButton = CreateRowButton(groupRect, 0, 5, "左转");
            _rotateRightButton = CreateRowButton(groupRect, 1, 5, "右转");
            _shrinkButton = CreateRowButton(groupRect, 2, 5, "缩小");
            _growButton = CreateRowButton(groupRect, 3, 5, "放大");
            _clearButton = CreateRowButton(groupRect, 4, 5, "清除");

            // 姿势（站/坐/睡）单独一条竖向侧边栏摆屏幕左侧：底栏那行已经放不下了，
            // 而且姿势是"切换状态"，和左转/缩放这些"按住调"的操作本来也不该挤在一起
            _poseGroup = new GameObject("Pose Group", typeof(RectTransform));
            RectTransform poseRect = _poseGroup.GetComponent<RectTransform>();
            poseRect.SetParent(canvas.transform, false);
            poseRect.anchorMin = new Vector2(0f, 0.5f);
            poseRect.anchorMax = new Vector2(0f, 0.5f);
            poseRect.pivot = new Vector2(0f, 0.5f);
            poseRect.anchoredPosition = new Vector2(12f, 0f);
            poseRect.sizeDelta = new Vector2(150f, 3f * 64f + 2f * 8f);

            _standButton = CreateSideButton(poseRect, 0, 3, "站");
            _sitButton = CreateSideButton(poseRect, 1, 3, "坐");
            _sleepButton = CreateSideButton(poseRect, 2, 3, "睡");
        }

        static Button CreateRowButton(RectTransform parent, int index, int total, string label)
        {
            RectTransform rect = CreateButton(parent, label);
            rect.anchorMin = new Vector2((float)index / total, 0f);
            rect.anchorMax = new Vector2((float)(index + 1) / total, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-8f, 0f);
            return rect.GetComponent<Button>();
        }

        /// <summary>侧边栏里的竖排按钮：从上往下第 index 个</summary>
        static Button CreateSideButton(RectTransform parent, int index, int total, string label)
        {
            RectTransform rect = CreateButton(parent, label);
            rect.anchorMin = new Vector2(0f, 1f - (float)(index + 1) / total);
            rect.anchorMax = new Vector2(1f, 1f - (float)index / total);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, -8f);
            return rect.GetComponent<Button>();
        }

        static Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("AR Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>没有 EventSystem 按钮点不动，而且 PointerInput 判「压在 UI 上」也要靠它。</summary>
        static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
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
