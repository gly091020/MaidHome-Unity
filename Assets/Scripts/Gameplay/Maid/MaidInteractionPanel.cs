using System;
using System.Collections;
using System.Text;
using MaidHome.Core.Settings;
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
        /// 点了「摸摸」
        public event Action PetRequested;
        /// 点了「喂蛋糕」
        public event Action FeedRequested;
        /// 点了「梳毛」
        public event Action GroomRequested;
        /// 点了「玩全身」
        public event Action BodyRequested;
        /// 点了「挂件」（打开第二层：选挂牌 / 面具）
        public event Action AttachRequested;
        /// 点了「聊天」
        public event Action ChatRequested;

        [Tooltip("正式面板根节点。留空会自动生成一个占位面板")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _infoText;
        [SerializeField] private Text _optionsText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _tailButton;
        [Tooltip("「摸脸」按钮。自定义面板留空的话，运行时会照「摸尾巴」按钮的样子自动加一个")]
        [SerializeField] private Button _faceButton;
        [Tooltip("「摸摸」按钮。自定义面板留空的话，运行时会在面板上方补一个")]
        [SerializeField] private Button _petButton;
        [Tooltip("「喂蛋糕」按钮。自定义面板留空的话，运行时会在「摸脸」右边自动加一个")]
        [SerializeField] private Button _feedButton;
        [Tooltip("「梳毛」按钮。自定义面板留空的话，运行时会照「摸尾巴」按钮的样子在左边补一个")]
        [SerializeField] private Button _groomButton;
        [Tooltip("「玩全身」按钮。自定义面板留空的话，运行时会在「梳毛」按钮上方补一个")]
        [SerializeField] private Button _bodyButton;
        [Tooltip("「挂件」按钮（打开第二层选挂牌 / 面具）。字段名是旧的，别改——场景里已经接在这上面了")]
        [SerializeField] private Button _signButton;
        [Tooltip("「聊天」按钮。留空会自动补一个")]
        [SerializeField] private Button _chatButton;
        [Tooltip("只对自动生成的面板生效：贴到屏幕下方。自定义面板不会被动")]
        [SerializeField] private bool _placeAtBottom = true;
        [SerializeField] private float _bottomOffset = 40f;
        [Header("进入动画")]
        [SerializeField] private float _enterSeconds = 0.22f;
        [SerializeField] private float _exitSeconds = 0.18f;
        [SerializeField] private float _enterScale = 0.92f;
        [Tooltip("完全滑出屏幕下边缘后再多留的距离")]
        [SerializeField] private float _enterBottomGapPixels = 32f;

        bool _generated;
        CanvasGroup _canvasGroup;
        RectTransform _panelRect;
        Coroutine _enterRoutine;
        Coroutine _exitRoutine;
        Vector2 _shownPosition;
        Vector3 _shownScale = Vector3.one;
        bool _shownPoseCaptured;
        bool _closing;

        public bool IsOpen
        {
            get { return _panelRoot != null && _panelRoot.activeSelf && !_closing; }
        }

        void Awake()
        {
            EnsureCreated();
            PlacePanel();
            EnsureAnimationRefs();
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

            EnsurePetButton();
            if (_petButton != null)
            {
                _petButton.onClick.AddListener(RequestPet);
            }

            EnsureFeedButton();
            if (_feedButton != null)
            {
                _feedButton.onClick.AddListener(RequestFeed);
            }

            EnsureGroomButton();
            if (_groomButton != null)
            {
                _groomButton.onClick.AddListener(RequestGroom);
            }

            EnsureBodyButton();
            if (_bodyButton != null)
            {
                _bodyButton.onClick.AddListener(RequestBody);
            }

            EnsureSignButton();
            if (_signButton != null)
            {
                _signButton.onClick.AddListener(RequestAttach);
            }

            EnsureChatButton();
            if (_chatButton != null)
            {
                _chatButton.onClick.AddListener(RequestChat);
            }

            Close();
        }

        void OnDestroy()
        {
            if (_enterRoutine != null)
            {
                StopCoroutine(_enterRoutine);
                _enterRoutine = null;
            }

            if (_exitRoutine != null)
            {
                StopCoroutine(_exitRoutine);
                _exitRoutine = null;
            }

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

            if (_petButton != null)
            {
                _petButton.onClick.RemoveListener(RequestPet);
            }

            if (_feedButton != null)
            {
                _feedButton.onClick.RemoveListener(RequestFeed);
            }

            if (_groomButton != null)
            {
                _groomButton.onClick.RemoveListener(RequestGroom);
            }

            if (_bodyButton != null)
            {
                _bodyButton.onClick.RemoveListener(RequestBody);
            }

            if (_signButton != null)
            {
                _signButton.onClick.RemoveListener(RequestAttach);
            }

            if (_chatButton != null)
            {
                _chatButton.onClick.RemoveListener(RequestChat);
            }

            CloseRequested = null;
            TailRequested = null;
            FaceRequested = null;
            PetRequested = null;
            FeedRequested = null;
            GroomRequested = null;
            BodyRequested = null;
            AttachRequested = null;
            ChatRequested = null;
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
            Open(maid, tailAvailable, faceAvailable, feedAvailable, true);
        }

        public void Open(MaidSaveData maid, bool tailAvailable, bool faceAvailable, bool feedAvailable,
            bool groomAvailable)
        {
            Open(maid, tailAvailable, faceAvailable, feedAvailable, groomAvailable, true);
        }

        public void Open(MaidSaveData maid, bool tailAvailable, bool faceAvailable, bool feedAvailable,
            bool groomAvailable, bool bodyAvailable, bool petAvailable = true)
        {
            bool wasOpen = IsOpen;
            StopMotion();
            EnsureCreated();
            PlacePanel();
            EnsureAnimationRefs();
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
            SetPetAvailable(petAvailable);
            SetFeedAvailable(feedAvailable);
            SetGroomAvailable(groomAvailable);
            SetBodyAvailable(bodyAvailable);
            // 挂件入口一直可点：里面挂牌 / 面具各自可不可用由第二层面板判断
            SetAttachAvailable(true);
            SetAiAvailable(GameSettingsStore.Current.AiEnabled);
            if (_optionsText != null)
            {
                _optionsText.text = BuildOptions(maid, tailAvailable, faceAvailable, feedAvailable,
                    groomAvailable, bodyAvailable, petAvailable);
            }

            if (wasOpen)
            {
                SnapOpen();
            }
            else
            {
                PlayEnterAnimation();
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

        /// 模型没有头骨骼就把「摸摸」灰掉
        public void SetPetAvailable(bool available)
        {
            if (_petButton != null)
            {
                _petButton.interactable = available;
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

        /// 模型认不出头发 / 耳朵 / 尾巴就把「梳毛」灰掉
        public void SetGroomAvailable(bool available)
        {
            if (_groomButton != null)
            {
                _groomButton.interactable = available;
            }
        }

        /// 模型认不出可用的物理块就把「玩全身」灰掉
        public void SetBodyAvailable(bool available)
        {
            if (_bodyButton != null)
            {
                _bodyButton.interactable = available;
            }
        }

        public void Close()
        {
            StopMotion();
            SnapOpen();
            _closing = false;
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        /// <summary>正常退出女仆交互时使用：向下滑出并淡出后再停用面板。</summary>
        public void CloseAnimated()
        {
            if (_panelRoot == null || !_panelRoot.activeSelf || _closing)
            {
                return;
            }

            StopMotion();
            _closing = true;
            _exitRoutine = StartCoroutine(ExitRoutine());
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

        public void RequestPet()
        {
            if (PetRequested != null)
            {
                PetRequested();
            }
        }

        public void RequestFeed()
        {
            if (FeedRequested != null)
            {
                FeedRequested();
            }
        }

        public void RequestGroom()
        {
            if (GroomRequested != null)
            {
                GroomRequested();
            }
        }

        public void RequestBody()
        {
            if (BodyRequested != null)
            {
                BodyRequested();
            }
        }

        public void RequestAttach()
        {
            if (AttachRequested != null)
            {
                AttachRequested();
            }
        }

        public void RequestChat()
        {
            if (GameSettingsStore.Current.AiEnabled && ChatRequested != null)
            {
                ChatRequested();
            }
        }

        public void SetAiAvailable(bool available)
        {
            EnsureChatButton();
            if (_chatButton != null)
            {
                _chatButton.gameObject.SetActive(true);
                _chatButton.interactable = available;
            }
        }

        /// 挂件入口默认一直可点（具体哪样能挂由第二层面板判断）
        public void SetAttachAvailable(bool available)
        {
            if (_signButton != null)
            {
                _signButton.interactable = available;
            }
        }

        static string BuildOptions(MaidSaveData maid, bool tailAvailable, bool faceAvailable, bool feedAvailable,
            bool groomAvailable, bool bodyAvailable, bool petAvailable)
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

            string food = feedAvailable ? "「喂蛋糕」选挡位喂她吃东西\n" : "";
            string pet = petAvailable ? "「摸摸」拖动屏幕：左右摸头 / 往下按\n" : "";
            string groom = groomAvailable ? "「梳毛」沿头发、耳朵、尾巴刷；刷反了会疼\n" : "";
            string bodyLine = bodyAvailable ? "「玩全身」按住任意部位拖动；退出会还原站姿\n" : "";
            string attach = "「挂件」选挂牌子（写字、吓唬别的女仆）或戴面具（贴照片）\n";
            string chat = "「聊天」打开 AI 聊天记录；回复也会显示在她头顶\n";
            return food + pet + groom + bodyLine + attach + chat + body;
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
            panelRect.sizeDelta = new Vector2(840f, 380f);
            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

            _panelRoot = panel;
            _titleText = CreateText(panel.transform, "Title", new Vector2(0f, 140f), new Vector2(800f, 40f),
                22, TextAnchor.MiddleCenter);
            _infoText = CreateText(panel.transform, "Info", new Vector2(0f, 40f), new Vector2(800f, 120f),
                16, TextAnchor.UpperLeft);
            _optionsText = CreateText(panel.transform, "Options", new Vector2(0f, -70f), new Vector2(800f, 100f),
                16, TextAnchor.UpperLeft);
            // 底下这一排共 8 个：从 -390 排到 410
            _tailButton = CreateButton(panel.transform, "摸尾巴", new Vector2(-336f, -160f),
                new Vector2(108f, 34f));
            _faceButton = CreateButton(panel.transform, "摸脸", new Vector2(-238f, -160f),
                new Vector2(92f, 34f));
            _groomButton = CreateButton(panel.transform, "梳毛", new Vector2(-138f, -160f),
                new Vector2(92f, 34f));
            _bodyButton = CreateButton(panel.transform, "玩全身", new Vector2(-30f, -160f),
                new Vector2(112f, 34f));
            _feedButton = CreateButton(panel.transform, "喂蛋糕", new Vector2(82f, -160f),
                new Vector2(110f, 34f));
            _signButton = CreateButton(panel.transform, "挂件", new Vector2(190f, -160f),
                new Vector2(100f, 34f));
            _closeButton = CreateButton(panel.transform, "关闭", new Vector2(290f, -160f),
                new Vector2(80f, 34f));
            _chatButton = CreateButton(panel.transform, "聊天", new Vector2(380f, -160f),
                new Vector2(80f, 34f));
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
        /// 自定义面板没接「摸摸」按钮时，挂在面板上沿右侧。
        /// 底下那排通常已经排满，压进别人按钮里更糟；建议自己加一个接到 Pet Button。
        /// </summary>
        void EnsurePetButton()
        {
            if (_petButton != null || _panelRoot == null)
            {
                return;
            }

            _petButton = CreateButton(_panelRoot.transform, "摸摸", Vector2.zero, new Vector2(180f, 56f));
            RectTransform rect = _petButton.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-24f, 8f);
            }

            Debug.LogWarning("自定义女仆交互面板没接「摸摸」按钮，先自动挂在面板上沿了；"
                + "建议自己加一个按钮接到 MaidInteractionPanel 的 Pet Button", this);
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

        /// <summary>
        /// 自定义面板没接「梳毛」按钮时，照「摸尾巴」/「摸脸」按钮的样子往左边补一个。
        /// 底下那排通常已经排满了，所以只是兜底，建议自己加一个接到 Groom Button。
        /// </summary>
        void EnsureGroomButton()
        {
            if (_groomButton != null || _panelRoot == null || _generated)
            {
                // 自动生成的那份在 EnsureCreated 里已经排好了位置
                return;
            }

            Button template = _tailButton != null ? _tailButton : _faceButton;
            Vector2 position = new Vector2(0f, -152f);
            Vector2 size = new Vector2(110f, 34f);
            if (template != null)
            {
                RectTransform rect = template.transform as RectTransform;
                if (rect != null)
                {
                    position = new Vector2(rect.anchoredPosition.x - rect.sizeDelta.x - 12f,
                        rect.anchoredPosition.y);
                    size = rect.sizeDelta;
                }
            }

            _groomButton = CreateButton(_panelRoot.transform, "梳毛", position, size);
            Debug.LogWarning("自定义女仆交互面板没接「梳毛」按钮，先照「摸尾巴」的位置往左补了一个；"
                + "建议自己加一个按钮接到 MaidInteractionPanel 的 Groom Button", this);
        }

        /// <summary>
        /// 自定义面板没接「玩全身」按钮时，挂在「梳毛」按钮正上方。
        /// 底下那一排通常已经排满，往上方补不容易压到别的按钮；建议自己接线。
        /// </summary>
        void EnsureBodyButton()
        {
            if (_bodyButton != null || _panelRoot == null || _generated)
            {
                // 自动生成的那份在 EnsureCreated 里已经排好了位置
                return;
            }

            Button template = _groomButton != null ? _groomButton : _tailButton;
            Vector2 position = new Vector2(0f, -110f);
            Vector2 size = new Vector2(110f, 34f);
            if (template != null)
            {
                RectTransform rect = template.transform as RectTransform;
                if (rect != null)
                {
                    position = new Vector2(rect.anchoredPosition.x,
                        rect.anchoredPosition.y + rect.sizeDelta.y + 8f);
                    size = rect.sizeDelta;
                }
            }

            _bodyButton = CreateButton(_panelRoot.transform, "玩全身", position, size);
            Debug.LogWarning("自定义女仆交互面板没接「玩全身」按钮，先挂在「梳毛」上方了；"
                + "建议自己加一个按钮接到 MaidInteractionPanel 的 Body Button", this);
        }

        /// <summary>
        /// 自定义面板没接「挂牌」按钮时，照「梳毛」按钮的样子再往左补一个（兜底，建议自己接线）。
        /// </summary>
        void EnsureSignButton()
        {
            if (_signButton != null || _panelRoot == null || _generated)
            {
                // 自动生成的那份在 EnsureCreated 里已经排好了位置
                return;
            }

            Button template = _groomButton != null ? _groomButton : _tailButton != null ? _tailButton : _faceButton;
            Vector2 position = new Vector2(0f, -152f);
            Vector2 size = new Vector2(100f, 34f);
            if (template != null)
            {
                RectTransform rect = template.transform as RectTransform;
                if (rect != null)
                {
                    position = new Vector2(rect.anchoredPosition.x - rect.sizeDelta.x - 12f,
                        rect.anchoredPosition.y);
                    size = rect.sizeDelta;
                }
            }

            _signButton = CreateButton(_panelRoot.transform, "挂件", position, size);
            Debug.LogWarning("自定义女仆交互面板没接「挂件」按钮，先照「梳毛」的位置往左补了一个；"
                + "建议自己加一个按钮接到 MaidInteractionPanel 的 Sign Button（字段名是旧的）", this);
        }

        void EnsureChatButton()
        {
            if (_chatButton != null || _panelRoot == null || _generated)
            {
                return;
            }

            Button template = _petButton != null ? _petButton
                : _signButton != null ? _signButton
                : _tailButton;
            if (template != null && template.transform.parent != null)
            {
                RectTransform templateRect = template.transform as RectTransform;
                Transform content = template.transform.parent;
                _chatButton = CreateButton(content, "聊天", Vector2.zero,
                    templateRect != null ? templateRect.sizeDelta : new Vector2(314f, 100f));
                RectTransform rect = _chatButton.transform as RectTransform;
                if (rect != null && templateRect != null)
                {
                    rect.anchorMin = templateRect.anchorMin;
                    rect.anchorMax = templateRect.anchorMax;
                    rect.pivot = templateRect.pivot;
                    rect.anchoredPosition = new Vector2(
                        templateRect.anchoredPosition.x + templateRect.sizeDelta.x,
                        templateRect.anchoredPosition.y);
                }

                CopyButtonStyle(template, _chatButton);
                Debug.LogWarning("自定义女仆交互面板没接「聊天」按钮，已在现有按钮网格中补一个；"
                    + "建议自己接线到 MaidInteractionPanel 的 Chat Button", this);
                return;
            }

            _chatButton = CreateButton(_panelRoot.transform, "聊天", new Vector2(0f, 110f),
                new Vector2(180f, 56f));
            Debug.LogWarning("自定义女仆交互面板没接「聊天」按钮，自动补在面板上方；"
                + "建议自己接线到 MaidInteractionPanel 的 Chat Button", this);
        }

        static void CopyButtonStyle(Button source, Button target)
        {
            if (source == null || target == null) return;
            Image sourceImage = source.targetGraphic as Image;
            Image targetImage = target.targetGraphic as Image;
            if (sourceImage != null && targetImage != null)
            {
                targetImage.sprite = sourceImage.sprite;
                targetImage.type = sourceImage.type;
                targetImage.color = sourceImage.color;
            }

            Text sourceText = source.GetComponentInChildren<Text>(true);
            Text targetText = target.GetComponentInChildren<Text>(true);
            if (sourceText != null && targetText != null)
            {
                targetText.font = sourceText.font;
                targetText.fontSize = sourceText.fontSize;
                targetText.color = sourceText.color;
            }
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

        void EnsureAnimationRefs()
        {
            if (_panelRoot == null)
            {
                return;
            }

            if (_panelRect == null)
            {
                _panelRect = _panelRoot.transform as RectTransform;
            }

            if (_canvasGroup == null)
            {
                _canvasGroup = _panelRoot.GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = _panelRoot.AddComponent<CanvasGroup>();
                }
            }

            if (_panelRect != null && !_shownPoseCaptured)
            {
                _shownPosition = _panelRect.anchoredPosition;
                _shownScale = _panelRect.localScale;
                _shownPoseCaptured = true;
            }
        }

        void PlayEnterAnimation()
        {
            if (_panelRect == null)
            {
                return;
            }

            if (_enterRoutine != null)
            {
                StopCoroutine(_enterRoutine);
            }

            _enterRoutine = StartCoroutine(EnterRoutine());
        }

        IEnumerator EnterRoutine()
        {
            float panelHeight = _panelRect.rect.height > 1f ? _panelRect.rect.height : 380f;
            float distance = Mathf.Max(0f, _shownPosition.y) + panelHeight + _enterBottomGapPixels;
            Vector2 fromPosition = _shownPosition + Vector2.down * distance;
            Vector3 fromScale = _shownScale * Mathf.Clamp(_enterScale, 0.5f, 1f);
            float length = Mathf.Max(0.01f, _enterSeconds);
            float elapsed = 0f;
            while (elapsed < length && _panelRoot != null && _panelRoot.activeSelf)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                _panelRect.anchoredPosition = Vector2.LerpUnclamped(fromPosition, _shownPosition, t);
                _panelRect.localScale = Vector3.LerpUnclamped(fromScale, _shownScale, t);
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = Mathf.LerpUnclamped(0.15f, 1f, t);
                }

                yield return null;
            }

            SnapOpen();
            _enterRoutine = null;
        }

        IEnumerator ExitRoutine()
        {
            float panelHeight = _panelRect != null && _panelRect.rect.height > 1f
                ? _panelRect.rect.height
                : 380f;
            float distance = Mathf.Max(0f, _shownPosition.y) + panelHeight + _enterBottomGapPixels;
            Vector2 toPosition = _shownPosition + Vector2.down * distance;
            Vector2 fromPosition = _panelRect != null ? _panelRect.anchoredPosition : _shownPosition;
            Vector3 fromScale = _panelRect != null ? _panelRect.localScale : _shownScale;
            Vector3 toScale = _shownScale * Mathf.Clamp(_enterScale, 0.5f, 1f);
            float fromAlpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;
            float length = Mathf.Max(0.01f, _exitSeconds);
            float elapsed = 0f;
            while (elapsed < length && _panelRoot != null && _panelRoot.activeSelf)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                if (_panelRect != null)
                {
                    _panelRect.anchoredPosition = Vector2.LerpUnclamped(fromPosition, toPosition, t);
                    _panelRect.localScale = Vector3.LerpUnclamped(fromScale, toScale, t);
                }

                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = Mathf.LerpUnclamped(fromAlpha, 0f, t);
                }

                yield return null;
            }

            _exitRoutine = null;
            _closing = false;
            SnapOpen();
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        void StopMotion()
        {
            if (_enterRoutine != null)
            {
                StopCoroutine(_enterRoutine);
                _enterRoutine = null;
            }

            if (_exitRoutine != null)
            {
                StopCoroutine(_exitRoutine);
                _exitRoutine = null;
            }

            _closing = false;
        }

        void SnapOpen()
        {
            if (_panelRect != null)
            {
                _panelRect.anchoredPosition = _shownPosition;
                _panelRect.localScale = _shownScale;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
            }
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
