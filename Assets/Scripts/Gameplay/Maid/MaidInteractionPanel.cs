using System;
using System.Text;
using MaidHome.Interop.Maid;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆交互面板。想用自定义 UI 就把 Panel Root / 文字 / 按钮拖进来，接上以后布局完全归你的 UI，
    /// 这里只管开关和填字；不接就自动生成一个占位面板。
    /// 注意本组件平时是 MaidInteractionController 在运行时 AddComponent 出来的，要接线得在场景里先手动挂一份。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidInteractionPanel : MonoBehaviour
    {
        public event Action CloseRequested;
        /// 点了「摸尾巴」
        public event Action TailRequested;
        /// 点了「摸脸」
        public event Action FaceRequested;
        /// 点了「喂蛋糕」
        public event Action FeedRequested;

        [Tooltip("正式面板根节点。留空会自动生成一个占位面板")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _infoText;
        [SerializeField] private Text _optionsText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _tailButton;
        [Tooltip("「摸脸」按钮。自定义面板留空的话，运行时会照「摸尾巴」按钮的样子自动加一个")]
        [SerializeField] private Button _faceButton;
        [Tooltip("「喂蛋糕」按钮。自定义面板留空的话，运行时会在「摸脸」右边自动加一个")]
        [SerializeField] private Button _feedButton;
        [Tooltip("只对自动生成的面板生效：贴到屏幕下方。自定义面板不会被动")]
        [SerializeField] private bool _placeAtBottom = true;
        [SerializeField] private float _bottomOffset = 40f;

        bool _generated;

        public bool IsOpen
        {
            get { return _panelRoot != null && _panelRoot.activeSelf; }
        }

        void Awake()
        {
            EnsureCreated();
            PlacePanel();
            if (_panelRoot != null && !_generated && (_titleText == null || _closeButton == null))
            {
                Debug.LogWarning("自定义女仆交互面板没接全 Title / Close Button 之类的引用，对应部分不会更新", this);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(RequestClose);
            }

            if (_tailButton != null)
            {
                _tailButton.onClick.AddListener(RequestTail);
            }

            EnsureFaceButton();
            if (_faceButton != null)
            {
                _faceButton.onClick.AddListener(RequestFace);
            }

            EnsureFeedButton();
            if (_feedButton != null)
            {
                _feedButton.onClick.AddListener(RequestFeed);
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

            if (_faceButton != null)
            {
                _faceButton.onClick.RemoveListener(RequestFace);
            }

            if (_feedButton != null)
            {
                _feedButton.onClick.RemoveListener(RequestFeed);
            }

            CloseRequested = null;
            TailRequested = null;
            FaceRequested = null;
            FeedRequested = null;
        }

        public void Open(MaidSaveData maid, bool tailAvailable)
        {
            Open(maid, tailAvailable, true, true);
        }

        public void Open(MaidSaveData maid, bool tailAvailable, bool faceAvailable)
        {
            Open(maid, tailAvailable, faceAvailable, true);
        }

        public void Open(MaidSaveData maid, bool tailAvailable, bool faceAvailable, bool feedAvailable)
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
            SetFaceAvailable(faceAvailable);
            SetFeedAvailable(feedAvailable);
            if (_optionsText != null)
            {
                _optionsText.text = BuildOptions(maid, tailAvailable, faceAvailable, feedAvailable);
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

        /// 模型没有头骨骼就把「摸脸」灰掉
        public void SetFaceAvailable(bool available)
        {
            if (_faceButton != null)
            {
                _faceButton.interactable = available;
            }
        }

        /// <summary>喂蛋糕对模型没要求，一般都给点</summary>
        public void SetFeedAvailable(bool available)
        {
            if (_feedButton != null)
            {
                _feedButton.interactable = available;
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

        public void RequestFace()
        {
            if (FaceRequested != null)
            {
                FaceRequested();
            }
        }

        public void RequestFeed()
        {
            if (FeedRequested != null)
            {
                FeedRequested();
            }
        }

        static string BuildOptions(MaidSaveData maid, bool tailAvailable, bool faceAvailable, bool feedAvailable)
        {
            string body;
            if (tailAvailable && faceAvailable)
            {
                body = "「摸尾巴」按住尾巴拖；「摸脸」拽耳朵、戳眼睛、横着划脸";
            }
            else if (maid != null && maid.SimpleBedrockModel)
            {
                body = "方块模型的女仆不支持摸尾巴和摸脸";
            }
            else if (tailAvailable)
            {
                body = "「摸尾巴」按住尾巴拖动；这个模型摸不了脸";
            }
            else if (faceAvailable)
            {
                body = "「摸脸」拽耳朵、戳眼睛、横着划脸；这个模型没有尾巴";
            }
            else
            {
                body = "这个模型的骨架里没有尾巴，也没找到头";
            }

            return feedAvailable ? "「喂蛋糕」选挡位喂她吃东西\n" + body : body;
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
            panelRect.sizeDelta = new Vector2(560f, 300f);
            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

            _panelRoot = panel;
            _titleText = CreateText(panel.transform, "Title", new Vector2(0f, 118f), new Vector2(520f, 40f),
                22, TextAnchor.MiddleCenter);
            _infoText = CreateText(panel.transform, "Info", new Vector2(0f, 28f), new Vector2(520f, 120f),
                16, TextAnchor.UpperLeft);
            _optionsText = CreateText(panel.transform, "Options", new Vector2(0f, -68f), new Vector2(520f, 60f),
                16, TextAnchor.UpperLeft);
            _closeButton = CreateButton(panel.transform, "关闭", new Vector2(225f, -122f),
                new Vector2(90f, 34f));
            _tailButton = CreateButton(panel.transform, "摸尾巴", new Vector2(-190f, -122f),
                new Vector2(140f, 34f));
            _faceButton = CreateButton(panel.transform, "摸脸", new Vector2(-30f, -122f),
                new Vector2(110f, 34f));
            _feedButton = CreateButton(panel.transform, "喂蛋糕", new Vector2(105f, -122f),
                new Vector2(130f, 34f));
            _generated = true;
        }

        /// <summary>
        /// 自定义面板没接「摸脸」按钮时，照「摸尾巴」按钮的位置/尺寸自动补一个，
        /// 摆在面板底部中间（接了自己的按钮就不会走到这里）。
        /// </summary>
        void EnsureFaceButton()
        {
            if (_faceButton != null || _panelRoot == null)
            {
                return;
            }

            Vector2 position = new Vector2(0f, -112f);
            Vector2 size = new Vector2(150f, 34f);
            if (_tailButton != null)
            {
                RectTransform tailRect = _tailButton.transform as RectTransform;
                if (tailRect != null)
                {
                    position = new Vector2(0f, tailRect.anchoredPosition.y);
                    size = tailRect.sizeDelta;
                }
            }

            _faceButton = CreateButton(_panelRoot.transform, "摸脸", position, size);
        }

        /// <summary>
        /// 自定义面板没接「喂蛋糕」按钮时自动补一个：挂在面板上沿外面。
        /// 自定义面板底下那排通常已经排满了（摸尾巴 / 摸脸 / 关闭），压在别人的按钮上更糟，
        /// 所以兜底放面板上方，然后提示玩家自己加一个接到本字段。
        /// </summary>
        void EnsureFeedButton()
        {
            if (_feedButton != null || _panelRoot == null)
            {
                return;
            }

            _feedButton = CreateButton(_panelRoot.transform, "喂蛋糕", Vector2.zero, new Vector2(200f, 60f));
            RectTransform rect = _feedButton.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(24f, 8f);
            }

            Debug.LogWarning("自定义女仆交互面板没接「喂蛋糕」按钮，先自动挂在面板上沿了；"
                + "建议自己加一个按钮接到 MaidInteractionPanel 的 Feed Button", this);
        }

        void PlacePanel()
        {
            if (!_generated || !_placeAtBottom || _panelRoot == null)
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
