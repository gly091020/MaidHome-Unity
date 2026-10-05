using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 车万女仆聊天文字气泡的移植：只用文字气泡 + type2 背景，不搬表情/图片/进度那种。
    /// 尺寸和排布照 IChatBubbleRenderer / ChatBubbleRenderer：九宫格 8 像素、文字四周留 5 像素、
    /// 气泡之间 16 像素、换行宽 120（居中那条 200）、最多 5 条、奇数条最后一条居中、其余右左交替；
    /// 挂点照 EntityMaidRenderer：模型顶上方、朝相机、1 像素 = 0.025 格、默认活 15 秒；
    /// 唯一改动是尺寸还会按"最宽/最高占屏幕多少"再夹一次——TLM 是第三人称远景，本工程摸脸会贴到脸上。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidChatBubble : MonoBehaviour
    {
        public const float DefaultSeconds = 15f;
        public const int MaxBubbles = 4;

        [Header("外观")]
        [Tooltip("不接就从 Resources/MaidChatBubble/type2 取（车万女仆的 type2.png）")]
        [SerializeField] private Texture2D _background;
        [Tooltip("气泡 1 像素 = 多少格，TLM 是 0.025")]
        [SerializeField] private float _unitsPerPixel = 0.025f;
        [Tooltip("气泡底部离模型顶部多高，单位是气泡像素（TLM 的名字牌算 20，越小越贴着脑袋）")]
        [SerializeField] private float _anchorPixels = 10f;
        [Tooltip("UI 放大倍率：字按 9×这个值光栅化，世界空间 Canvas 里直接写 9 号字会糊")]
        [SerializeField] private int _uiScale = 8;
        [Tooltip("同排序层内的顺序基数，要比女仆模型的透明部分靠后画")]
        [SerializeField] private int _sortingOrder = 100;
        [Tooltip("最短显示时间：调用方传得比这个短就按这个算（0 = 完全听调用方的，TLM 默认一条活 15 秒）")]
        [SerializeField] private float _minSeconds = 4f;
        [Header("取景适配（摸脸那种贴脸镜头不缩的话气泡会飞出屏幕）")]
        [Tooltip("气泡最外沿离画面中心最多占屏幕宽度的多少")]
        [SerializeField] private float _maxHalfWidthFraction = 0.48f;
        [Tooltip("一行字最高占屏幕高度的多少")]
        [SerializeField] private float _maxLineHeightFraction = 0.07f;
        [Tooltip("整叠气泡最高占屏幕高度的多少")]
        [SerializeField] private float _maxStackHeightFraction = 0.5f;
        [Tooltip("整叠气泡顶到画面这个高度就整体往下压，别顶出屏幕")]
        [SerializeField] private float _maxTopViewport = 0.95f;

        // TLM 的常量，单位都是气泡像素
        const int SlicePixels = 8;
        const int PadPixels = 5;
        const int MarginPixels = 1;
        const int GapPixels = 16;
        const int WrapPixels = 120;
        const int CenterWrapPixels = 200;
        const int FontPixels = 12;
        const int BodyPixels = 24;
        const int TailPixels = 16;
        const int SourceWidth = 48;

        enum BubblePosition
        {
            Left,
            Right,
            Center
        }

        sealed class Bubble
        {
            public GameObject Root;
            public Canvas Canvas;
            public Image Body;
            public Image Tail;
            public Text Text;
            public float ExpireAt;
            public float AnchorTop;
            public float ExtentPixels;
            public float TopPixels;
            public int Sequence;
        }

        static MaidChatBubble _instance;

        readonly Dictionary<MaidAgent, List<Bubble>> _byMaid = new Dictionary<MaidAgent, List<Bubble>>();
        readonly List<MaidAgent> _owners = new List<MaidAgent>();

        Sprite _bodySprite;
        Sprite _tailLeft;
        Sprite _tailRight;
        Sprite _tailCenter;
        bool _spritesReady;
        int _sequence;

        /// <summary>让这只女仆头上冒一条气泡，到点自己消失</summary>
        public static void Show(MaidAgent maid, string line)
        {
            Show(maid, line, DefaultSeconds);
        }

        public static void Show(MaidAgent maid, string line, float seconds)
        {
            if (maid == null || string.IsNullOrEmpty(line))
            {
                return;
            }

            Instance.Say(maid, line, seconds);
        }

        /// <summary>立刻清掉这只女仆头上的所有气泡（退出交互模式时用）</summary>
        public static void Hide(MaidAgent maid)
        {
            if (maid == null || _instance == null)
            {
                return;
            }

            _instance.Clear(maid);
        }

        /// <summary>这只女仆头上现在没有气泡（闲置颜文字靠它避开正在说的台词）</summary>
        public static bool IsEmpty(MaidAgent maid)
        {
            if (maid == null || _instance == null)
            {
                return true;
            }

            List<Bubble> list;
            return !_instance._byMaid.TryGetValue(maid, out list) || list.Count == 0;
        }

        static MaidChatBubble Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<MaidChatBubble>();
                }

                if (_instance == null)
                {
                    GameObject host = new GameObject("MaidChatBubbles");
                    _instance = host.AddComponent<MaidChatBubble>();
                }

                return _instance;
            }
        }

        void Say(MaidAgent maid, string line, float seconds)
        {
            EnsureSprites();
            if (_bodySprite == null)
            {
                return;
            }

            List<Bubble> list;
            if (!_byMaid.TryGetValue(maid, out list))
            {
                list = new List<Bubble>();
                _byMaid.Add(maid, list);
            }

            // TLM 满了就挤掉最先到期的那条（优先级一样时就是这么选的）
            if (list.Count >= MaxBubbles)
            {
                Remove(list, Earliest(list));
            }

            Bubble bubble = Create(maid, line);
            float duration = seconds > 0f ? seconds : DefaultSeconds;
            bubble.ExpireAt = Time.unscaledTime + Mathf.Max(_minSeconds, duration);
            bubble.Sequence = _sequence++;
            list.Add(bubble);
            Relayout(list);
        }

        Bubble Create(MaidAgent maid, string line)
        {
            GameObject root = new GameObject("ChatBubble", typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = _sortingOrder;
            RectTransform rootRect = root.GetComponent<RectTransform>();

            Bubble bubble = new Bubble();
            bubble.Root = root;
            bubble.Canvas = canvas;
            bubble.Body = CreateImage(rootRect, "Body", _bodySprite);
            bubble.Body.type = Image.Type.Sliced;
            bubble.Tail = CreateImage(rootRect, "Tail", _tailLeft);
            bubble.Text = CreateText(bubble.Body.rectTransform, line);
            bubble.AnchorTop = MeasureAnchorTop(maid);
            return bubble;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            _owners.Clear();
            foreach (KeyValuePair<MaidAgent, List<Bubble>> pair in _byMaid)
            {
                _owners.Add(pair.Key);
            }

            for (int i = 0; i < _owners.Count; i++)
            {
                MaidAgent maid = _owners[i];
                List<Bubble> list = _byMaid[maid];
                bool changed = false;
                for (int j = list.Count - 1; j >= 0; j--)
                {
                    // 女仆被销毁/收回也要跟着清，不然会留着一条飘在原地的气泡
                    if (maid == null || !maid.gameObject.activeInHierarchy || list[j].ExpireAt <= now)
                    {
                        Remove(list, list[j]);
                        changed = true;
                    }
                }

                if (!changed)
                {
                    continue;
                }

                if (list.Count == 0)
                {
                    _byMaid.Remove(maid);
                }
                else
                {
                    Relayout(list);
                }
            }

            _owners.Clear();
        }

        void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Quaternion rotation = camera.transform.rotation;
            Vector3 cameraUp = rotation * Vector3.up;

            foreach (KeyValuePair<MaidAgent, List<Bubble>> pair in _byMaid)
            {
                List<Bubble> list = pair.Value;
                MaidAgent maid = pair.Key;
                if (maid == null)
                {
                    continue;
                }

                float extent = 1f;
                float top = 0f;
                for (int i = 0; i < list.Count; i++)
                {
                    extent = Mathf.Max(extent, list[i].ExtentPixels);
                    top = Mathf.Max(top, list[i].TopPixels);
                }

                // 挂点 = 模型顶部 + 固定几像素的间隙；间隙跟着气泡尺寸走，贴脸时才不会飘在天上
                Vector3 head = maid.transform.position + Vector3.up * list[0].AnchorTop;
                float worldPerPixel = FitWorldPerPixel(camera, head, extent, top);
                Vector3 anchor = head + Vector3.up * (_anchorPixels * worldPerPixel);
                anchor = ClampAnchor(camera, anchor, cameraUp, top * worldPerPixel);

                float scale = worldPerPixel / Mathf.Max(1, _uiScale);
                Vector3 localScale = new Vector3(scale, scale, scale);
                for (int i = 0; i < list.Count; i++)
                {
                    Bubble bubble = list[i];
                    Transform root = bubble.Root.transform;
                    root.SetPositionAndRotation(anchor, rotation);
                    root.localScale = localScale;
                }
            }
        }

        void Relayout(List<Bubble> list)
        {
            list.Sort(CompareBubbles);

            int count = list.Count;
            bool odd = count % 2 != 0;
            float leftY = 0f;
            float rightY = 0f;

            // 照 ChatBubbleRenderer：右、左、右、左……最后一条（总数为奇数）居中在两边更高的一侧
            for (int i = 0; i < count; i++)
            {
                Bubble bubble = list[i];
                if (odd && i == count - 1)
                {
                    ApplyLayout(bubble, BubblePosition.Center, Mathf.Max(leftY, rightY));
                }
                else if (i % 2 == 0)
                {
                    rightY += ApplyLayout(bubble, BubblePosition.Right, rightY) + GapPixels;
                }
                else
                {
                    leftY += ApplyLayout(bubble, BubblePosition.Left, leftY) + GapPixels;
                }

                bubble.Canvas.sortingOrder = _sortingOrder + i;
            }
        }

        /// <summary>摆好一条气泡，返回文字高度（和 TLM 一样，堆叠步长用的是文字高度不是气泡高度）</summary>
        float ApplyLayout(Bubble bubble, BubblePosition position, float stackY)
        {
            float ui = Mathf.Max(1, _uiScale);
            int wrap = position == BubblePosition.Center ? CenterWrapPixels : WrapPixels;
            float wrapWidth = wrap * ui;

            RectTransform textRect = bubble.Text.rectTransform;
            textRect.sizeDelta = new Vector2(wrapWidth, 0f);
            // Text 量出来的是"生成器单位"，除 pixelsPerUnit 才是 Canvas 单位，再除 _uiScale 才是气泡像素
            float toPixels = Mathf.Max(0.0001f, bubble.Text.pixelsPerUnit) * ui;
            float textWidth = Mathf.Max(1f, Mathf.Ceil(MeasureWrappedWidth(bubble.Text, wrapWidth) / toPixels));
            float textHeight = Mathf.Max(FontPixels, Mathf.Ceil(bubble.Text.preferredHeight / toPixels));
            float bodyWidth = textWidth + PadPixels * 2f;
            float bodyHeight = textHeight + PadPixels * 2f;

            float bodyX;
            float tailX;
            Sprite tail;
            if (position == BubblePosition.Right)
            {
                bodyX = MarginPixels;
                tailX = MarginPixels - TailPixels * 0.5f;
                tail = _tailRight;
            }
            else if (position == BubblePosition.Left)
            {
                bodyX = -MarginPixels - bodyWidth;
                tailX = -MarginPixels - TailPixels * 0.5f;
                tail = _tailLeft;
            }
            else
            {
                bodyX = -bodyWidth * 0.5f;
                tailX = -TailPixels * 0.5f;
                tail = _tailCenter;
            }

            RectTransform bodyRect = bubble.Body.rectTransform;
            bodyRect.sizeDelta = new Vector2(bodyWidth * ui, bodyHeight * ui);
            bodyRect.anchoredPosition = new Vector2(bodyX * ui, stackY * ui);

            bubble.Tail.sprite = tail;
            RectTransform tailRect = bubble.Tail.rectTransform;
            tailRect.sizeDelta = new Vector2(TailPixels * ui, TailPixels * ui);
            tailRect.anchoredPosition = new Vector2(tailX * ui, (stackY - TailPixels * 0.5f) * ui);

            textRect.anchoredPosition = new Vector2(PadPixels * ui, -PadPixels * ui);
            textRect.sizeDelta = new Vector2(wrapWidth, textHeight * ui);

            // 给"取景适配"用：这条气泡从挂点往外占多宽、往上顶多高（单位还是气泡像素）
            bubble.ExtentPixels = position == BubblePosition.Center
                ? bodyWidth * 0.5f
                : MarginPixels + bodyWidth;
            bubble.TopPixels = stackY + bodyHeight;
            return textHeight;
        }

        /// <summary>
        /// 气泡的世界尺寸。TLM 是写死的 1 像素 = 0.025 格，但本工程摸脸/喂蛋糕会把相机推到贴脸，
        /// 写死尺寸时气泡会又大又飞出屏幕，所以这里再按"最宽/最高占屏幕多少"夹一次，取小的那个。
        /// </summary>
        float FitWorldPerPixel(Camera camera, Vector3 anchor, float extentPixels, float topPixels)
        {
            float screenHeightWorld = ScreenHeightWorld(camera, anchor);
            float pixelsPerWorld = Screen.height / Mathf.Max(0.0001f, screenHeightWorld);
            float byWidth = _maxHalfWidthFraction * Screen.width / Mathf.Max(1f, extentPixels) / pixelsPerWorld;
            float byLine = _maxLineHeightFraction * Screen.height / FontPixels / pixelsPerWorld;
            float byStack = _maxStackHeightFraction * Screen.height / Mathf.Max(1f, topPixels) / pixelsPerWorld;
            return Mathf.Min(_unitsPerPixel, Mathf.Min(byWidth, Mathf.Min(byLine, byStack)));
        }

        static float ScreenHeightWorld(Camera camera, Vector3 point)
        {
            if (camera.orthographic)
            {
                return camera.orthographicSize * 2f;
            }

            float distance = Vector3.Dot(point - camera.transform.position, camera.transform.forward);
            return 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.1f, distance);
        }

        Vector3 ClampAnchor(Camera camera, Vector3 anchor, Vector3 cameraUp, float topWorld)
        {
            Vector3 top = anchor + cameraUp * topWorld;
            Vector3 viewport = camera.WorldToViewportPoint(top);
            if (viewport.z <= 0f || viewport.y <= _maxTopViewport)
            {
                return anchor;
            }

            float screenHeightWorld = ScreenHeightWorld(camera, anchor);
            return anchor - cameraUp * ((viewport.y - _maxTopViewport) * screenHeightWorld);
        }

        /// <summary>
        /// Text.preferredWidth 量的是不换行的整行宽度，这里自己排一遍拿"最长的一行"，
        /// 气泡才会像 TLM 那样贴着文字（不然换行后气泡永远有半行空白）。
        /// 返回值是 TextGenerator 的生成器单位，调用方自己除 pixelsPerUnit 换算。
        /// </summary>
        static float MeasureWrappedWidth(Text text, float wrapWidth)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(wrapWidth, 0f));
            settings.generateOutOfBounds = true;
            TextGenerator generator = new TextGenerator();
            generator.Populate(text.text, settings);

            IList<UICharInfo> characters = generator.characters;
            IList<UILineInfo> lines = generator.lines;
            float maxWidth = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                int start = lines[i].startCharIdx;
                int end = i + 1 < lines.Count ? lines[i + 1].startCharIdx : characters.Count;
                if (end <= start)
                {
                    continue;
                }

                UICharInfo first = characters[start];
                UICharInfo last = characters[end - 1];
                maxWidth = Mathf.Max(maxWidth, last.cursorPos.x + last.charWidth - first.cursorPos.x);
            }

            return maxWidth > 0f ? maxWidth : Mathf.Min(text.preferredWidth, wrapWidth);
        }

        void Clear(MaidAgent maid)
        {
            List<Bubble> list;
            if (!_byMaid.TryGetValue(maid, out list))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                DestroyBubble(list[i]);
            }

            list.Clear();
            _byMaid.Remove(maid);
        }

        static void Remove(List<Bubble> list, Bubble bubble)
        {
            if (bubble == null)
            {
                return;
            }

            DestroyBubble(bubble);
            list.Remove(bubble);
        }

        static void DestroyBubble(Bubble bubble)
        {
            if (bubble.Root != null)
            {
                Destroy(bubble.Root);
            }
        }

        static Bubble Earliest(List<Bubble> list)
        {
            Bubble earliest = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                if (CompareBubbles(list[i], earliest) < 0)
                {
                    earliest = list[i];
                }
            }

            return earliest;
        }

        static int CompareBubbles(Bubble left, Bubble right)
        {
            int byTime = left.ExpireAt.CompareTo(right.ExpireAt);
            return byTime != 0 ? byTime : left.Sequence.CompareTo(right.Sequence);
        }

        float MeasureAnchorTop(MaidAgent maid)
        {
            Bounds bounds = maid.GetBounds();
            return Mathf.Max(1.5f, bounds.max.y - maid.transform.position.y);
        }

        void EnsureSprites()
        {
            if (_spritesReady)
            {
                return;
            }

            Texture2D texture = _background;
            if (texture == null)
            {
                texture = Resources.Load<Texture2D>("MaidChatBubble/type2");
            }

            if (texture == null)
            {
                Debug.LogWarning("聊天气泡没有贴图：接上 _background，或放一张 Assets/Resources/MaidChatBubble/type2.png", this);
                return;
            }

            // TLM 那张是 256×256，内容都在左上角：主体 48×24，三条尾巴并排在 (0/16/32, 24)
            float top = texture.height;
            float pixelsPerUnit = 100f / Mathf.Max(1, _uiScale);
            _bodySprite = Sprite.Create(texture, new Rect(0f, top - BodyPixels, SourceWidth, BodyPixels),
                new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect,
                new Vector4(SlicePixels, SlicePixels, SlicePixels, SlicePixels));
            _tailLeft = CreateTailSprite(texture, TailPixels * 2f, top);
            _tailCenter = CreateTailSprite(texture, TailPixels, top);
            _tailRight = CreateTailSprite(texture, 0f, top);
            _spritesReady = true;
        }

        static Sprite CreateTailSprite(Texture2D texture, float x, float top)
        {
            return Sprite.Create(texture, new Rect(x, top - BodyPixels - TailPixels, TailPixels, TailPixels),
                new Vector2(0.5f, 0.5f), 100f);
        }

        static Image CreateImage(RectTransform parent, string name, Sprite sprite)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0f);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        Text CreateText(RectTransform parent, string line)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            // 贴着气泡主体左上角，往下排
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = FontPixels * Mathf.Max(1, _uiScale);
            text.alignment = TextAnchor.UpperLeft;
            // TLM 画的是 0x000000，不带阴影
            text.color = Color.black;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = line;
            return text;
        }

        void OnDestroy()
        {
            foreach (KeyValuePair<MaidAgent, List<Bubble>> pair in _byMaid)
            {
                List<Bubble> list = pair.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    DestroyBubble(list[i]);
                }
            }

            _byMaid.Clear();
            if (_bodySprite != null)
            {
                Destroy(_bodySprite);
            }

            if (_tailLeft != null)
            {
                Destroy(_tailLeft);
            }

            if (_tailRight != null)
            {
                Destroy(_tailRight);
            }

            if (_tailCenter != null)
            {
                Destroy(_tailCenter);
            }

            _bodySprite = null;
            _tailLeft = null;
            _tailRight = null;
            _tailCenter = null;
            _spritesReady = false;
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
