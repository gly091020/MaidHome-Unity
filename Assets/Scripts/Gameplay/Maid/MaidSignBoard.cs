using System.Collections.Generic;
using System.Text;
using MaidHome.Interop.Maid;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 挂在女仆身上那块「游街示众」牌（照 Love-And-Loathe 的 MaidSignLayer / MaidSignRenderHelper）。
    /// 原模组是把原版告示牌的渲染器塞进 Gecko 骨位里画；Unity 这边没有告示牌方块，
    /// 所以改成一块世界空间 Canvas：程序化画出来的木板 + 四行文字。
    /// 挂点优先取背挂位（BackpackLocator），没有就退到躯干、头，位置按身高比例算，换模型不用改代码。
    /// 牌子跟着骨骼走（需要它的地方由 MaidSignInteraction / MaidSignDeterrence 拿）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidSignBoard : MonoBehaviour
    {
        /// <summary>场上所有牌子，「游街示众」要扫谁身上挂着牌子</summary>
        public static readonly List<MaidSignBoard> Active = new List<MaidSignBoard>();

        /// <summary>3D 牌子预制体的资源名（Resources/MaidAttachment/sign）</summary>
        public const string ResourceName = "sign";

        // 板面按 24×12 个"牌面像素"画，UI 再统一放大 _uiScale 倍（字号写小数会糊）
        const int BoardTexelsX = 24;
        const int BoardTexelsY = 12;
        const int UiScale = 8;
        const float FontTexels = 2f;

        static readonly string[] BackBones =
        {
            "backpacklocator", "backpack", "backpackbone", "backlocator", "backflag", "flag", "banner",
        };

        static readonly string[] BodyBones =
        {
            "body", "upperbody", "chest", "torso", "spine", "waist",
        };

        static readonly string[] HeadBones = { "head", "mhead", "allhead", "head2" };

        /// <summary>随包的那份板面贴图（`Tools/sign_board_texture.py` 生成，见该脚本改美术）</summary>
        const string BoardResourcePath = "MaidSignBoard/sign";

        static Sprite _sharedSprite;
        static Texture2D _spriteSource;
        static Texture2D _codeTexture;

        [Header("挂点（按身高比例算）")]
        [Tooltip("牌子挂在身体多高处：0 = 脚底，1 = 脖子（头骨骼原点）")]
        [SerializeField] private float _chestRatio = 0.72f;
        [Tooltip("牌子离身体中轴往前多远，按「脚底到脖子」的高度算")]
        [SerializeField] private float _forwardRatio = 0.12f;
        [Tooltip("手动微调，方向是女仆的右 / 上 / 前（米）")]
        [SerializeField] private Vector3 _anchorOffset = Vector3.zero;

        [Header("尺寸")]
        [Tooltip("牌子宽度是身高的多少倍（原版告示牌板面 0.75 格宽，模组又乘了 0.6）")]
        [SerializeField] private float _widthRatio = 0.30f;
        [Tooltip("四行文字离板面边缘留几牌面像素")]
        [SerializeField] private float _textPadTexels = 1.1f;
        [Tooltip("文字描边粗细（牌面像素），照原版发光文字那圈黑边")]
        [SerializeField] private float _outlineTexels = 0.3f;

        [Header("渲染")]
        [Tooltip("同排序层内的顺序：要比女仆模型的透明部分靠后画，比聊天气泡（100）靠前")]
        [SerializeField] private int _sortingOrder = 90;
        [Tooltip("板面贴图。留空就从 Resources/MaidSignBoard/sign 取（随包那份，脚本生成）；"
            + "再找不到才退回代码画的兜底。要换皮就把你的图拖这里，别改脚本")]
        [SerializeField] private Texture2D _boardTexture;

        MaidAgent _agent;
        MaidSignData _data = new MaidSignData();
        Transform _bone;
        Vector3 _localAnchor;
        MaidAttachmentInstance _instance;
        Canvas _canvas;
        /// <summary>
        /// 画布自己的 RectTransform。**注意本组件挂在女仆根节点上，`transform` 就是那只女仆**，
        /// 摆牌面必须写它、不能写 `transform`（踩过：写成 transform 把女仆本人每帧搬到挂点上，
        /// 挂点又是她的子物体，于是她的 Y 一路自增飞走）
        /// </summary>
        RectTransform _canvasRect;
        Text _text;
        Outline _outline;
        bool _built;
        bool _preview;
        float _width;
        float _height;

        public MaidAgent Agent
        {
            get { return _agent; }
        }

        public MaidSignData Data
        {
            get { return _data; }
        }

        /// <summary>她身上真的挂着牌子（编辑中的预览不算）</summary>
        public bool Worn
        {
            get { return _data.Enabled && !_preview && _canvas != null && _canvas.gameObject.activeSelf; }
        }

        /// <summary>
        /// 给这块牌子（没有就建），data 直接引用（存档那份或编辑中的工作副本）。
        /// 挂件配置按这只女仆的模型类型从 MaidAttachmentStyles 取（方块 / Gecko 各一套）。
        /// </summary>
        public static MaidSignBoard Attach(MaidAgent agent, MaidSignData data, bool preview)
        {
            if (agent == null || data == null)
            {
                return null;
            }

            MaidSignBoard board = agent.GetComponent<MaidSignBoard>();
            if (board == null)
            {
                board = agent.gameObject.AddComponent<MaidSignBoard>();
            }

            // 每次进编辑模式都按当前配置重搭一遍：Inspector 里改完大小/偏移，重进模式就能看到
            board.Rebuild();
            board.Build(agent, MaidAttachmentStyles.Sign);
            board.Apply(data, preview);
            return board;
        }

        /// <summary>把已经搭好的牌面拆掉，下一次 Build 会按最新配置重来</summary>
        void Rebuild()
        {
            if (_instance != null)
            {
                _instance.Destroy();
                _instance = null;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                _canvasRect = null;
            }

            _built = false;
        }

        public static MaidSignBoard Find(MaidAgent agent)
        {
            return agent == null ? null : agent.GetComponent<MaidSignBoard>();
        }

        /// <summary>把牌子摘下来（组件和它的画布一起销毁）</summary>
        public static void Detach(MaidAgent agent)
        {
            MaidSignBoard board = Find(agent);
            if (board != null)
            {
                // 注意：本组件挂在女仆根节点上，销毁 gameObject 等于把女仆销毁了
                Destroy(board);
            }
        }

        public void Apply(MaidSignData data, bool preview)
        {
            if (data == null || _canvas == null)
            {
                return;
            }

            _data = data;
            _preview = preview;
            bool show = data.Enabled || preview;
            if (_canvas.gameObject.activeSelf != show)
            {
                _canvas.gameObject.SetActive(show);
            }

            if (_instance != null && _instance.IsValid && _instance.Root.activeSelf != show)
            {
                // 3D 牌子是挂在骨骼下的独立物体，不在画布里，得单独开关
                _instance.Root.SetActive(show);
            }

            Refresh();
            if (show && !preview && data.Scare)
            {
                MaidSignDeterrence.Ensure();
            }
        }

        /// <summary>牌子在世界里占的那块（取景框它）</summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            Bounds board;
            if (_instance != null && _instance.TryGetWorldBounds(out board))
            {
                bounds = board;
                return true;
            }

            Vector3 center = _canvasRect != null ? _canvasRect.position : transform.position;
            bounds = new Bounds(center, new Vector3(_width, _height, 0.02f));
            return _canvas != null && _canvas.gameObject.activeSelf;
        }

        void OnEnable()
        {
            if (!Active.Contains(this))
            {
                Active.Add(this);
            }
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        void OnDestroy()
        {
            Active.Remove(this);

            if (_instance != null)
            {
                _instance.Destroy();
                _instance = null;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                _canvasRect = null;
            }
        }

        void LateUpdate()
        {
            ApplyPose();
        }

        /// <summary>摆到挂点上：位置跟着骨骼走（她躺下/转身牌子跟着），朝向按身体的正前方</summary>
        void ApplyPose()
        {
            // 有 3D 牌子时文字画布是牌子的子物体，刚性跟着走，不需要每帧摆
            if (_instance != null)
            {
                return;
            }

            if (_agent == null || _canvas == null || _canvasRect == null || !_canvas.gameObject.activeSelf)
            {
                return;
            }

            // 牌子挂在身前，板面（世界空间 Canvas 的可视面是 -Z）朝她的正前方
            Transform root = _agent.transform;
            Vector3 forward = root.forward;
            Vector3 up = root.up;
            if (forward.sqrMagnitude < 0.0001f || up.sqrMagnitude < 0.0001f
                || Mathf.Abs(Vector3.Dot(forward.normalized, up.normalized)) > 0.99f)
            {
                return;
            }

            Vector3 position = _bone != null ? _bone.TransformPoint(_localAnchor) : root.position;
            _canvasRect.position = position;
            _canvasRect.rotation = Quaternion.LookRotation(-forward, up);
        }

        // ------------------------------------------------------------ 建

        void Build(MaidAgent agent, MaidAttachmentStyle style)
        {
            if (_built || agent == null)
            {
                return;
            }

            _agent = agent;
            _built = true;

            Transform root = agent.transform;
            Bounds bounds = agent.GetBounds();
            Transform head = FindBone(root, HeadBones);
            float feetY = bounds.min.y;
            float headY = head != null ? head.position.y : bounds.max.y;
            float span = Mathf.Max(0.2f, headY - feetY);

            Vector3 up = root.up.sqrMagnitude < 0.0001f ? Vector3.up : root.up.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(root.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(up, forward).normalized;

            // 水平位置取头的正下方（比整体包围盒中心稳，头发/尾巴不会把中轴带偏）
            Vector3 axis = head != null
                ? new Vector3(head.position.x, 0f, head.position.z)
                : new Vector3(bounds.center.x, 0f, bounds.center.z);
            Vector3 anchor = new Vector3(axis.x, feetY + span * _chestRatio, axis.z)
                + forward * (span * _forwardRatio)
                + right * _anchorOffset.x + up * _anchorOffset.y + forward * _anchorOffset.z;

            _bone = FindBone(root, BackBones);
            if (_bone == null)
            {
                _bone = FindBone(root, BodyBones);
            }

            if (_bone == null)
            {
                _bone = head;
            }

            if (_bone == null)
            {
                _bone = root;
            }

            // 牌子本体：有 3D 预制体就用它（底部/中心在哪不用管，代码按包围盒把它摆正），
            // 没有才退回代码画的平铺板面 + 木纹贴图
            float thickness = 0f;
            bool simpleBedrock = MaidAttachmentStyles.IsSimpleBedrock(agent);
            MaidAttachmentStyle resolved = style != null ? style : MaidAttachmentStyles.Sign;
            Vector3 styleOffset = resolved.OffsetsFor(simpleBedrock);
            GameObject prefab = MaidAttachmentStyle.ResolvePrefab(style, ResourceName);
            if (prefab != null)
            {
                _instance = MaidAttachmentInstance.Create(_bone, prefab);
                if (_instance != null)
                {
                    float width = Mathf.Max(0.05f,
                        bounds.size.y * resolved.SizeRatio * resolved.MultiplierFor(simpleBedrock));
                    float placementScale = width / _instance.DesignWidth;
                    Quaternion facing = root.rotation * Quaternion.Euler(resolved.FacingFix);
                    Vector3 center = anchor
                        + right * (styleOffset.x * bounds.size.y)
                        + up * (styleOffset.y * bounds.size.y)
                        + forward * (styleOffset.z * bounds.size.y);
                    _instance.Place(facing, center, placementScale);

                    // 尺寸按预制体自己的设计包围盒 × 缩放算。别拿"摆好之后的世界 AABB"去投影：
                    // 她一转身 AABB 就会被撑大（薄板子最明显），文字会被推到身前老远
                    Bounds design = _instance.LocalBounds;
                    _width = Mathf.Max(0.02f, design.size.x * placementScale);
                    _height = Mathf.Max(0.02f, design.size.y * placementScale);
                    thickness = Mathf.Max(0f, design.size.z * placementScale);
                    anchor = center;
                }
            }

            if (_instance == null)
            {
                // 兜底：代码画的板面，宽高按身高比例算
                _width = Mathf.Max(0.05f, bounds.size.y * _widthRatio);
                _height = _width * BoardTexelsY / BoardTexelsX;
            }

            GameObject canvasObject = new GameObject("MaidSignBoard", typeof(RectTransform), typeof(Canvas));
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = false;
            _canvas.sortingOrder = _sortingOrder;

            RectTransform rect = (RectTransform)canvasObject.transform;
            _canvasRect = rect;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(BoardTexelsX * UiScale, BoardTexelsY * UiScale);

            // 横竖分开缩：预制体和 24×12 的牌面比例不一样时，文字也刚好铺满板面
            float textScale = resolved.TextMultiplier;
            Vector3 textOffset = resolved.TextOffset;
            if (_instance != null)
            {
                // ★ 文字画布直接挂在牌子下面（不是挂在女仆身上再跟着骨骼跑）：
                // 这样它和板面是刚性关系，骨骼动画怎么摆、换什么模型都不可能错开。
                // 位置/缩放都用牌子自己的设计坐标，画布的可视面（-Z）正好是模型的正面（-Z）。
                Bounds design = _instance.LocalBounds;
                float margin = 0.004f / Mathf.Max(0.0001f, _instance.Root.transform.lossyScale.y);
                Vector3 localRight = _instance.Root.transform.InverseTransformDirection(right);
                Vector3 localUp = _instance.Root.transform.InverseTransformDirection(up);
                Vector3 localForward = _instance.Root.transform.InverseTransformDirection(forward);
                rect.SetParent(_instance.Root.transform, false);
                rect.localPosition = design.center
                    + localForward * (design.size.z * 0.5f + margin)
                    + localRight * (textOffset.x * design.size.x)
                    + localUp * (textOffset.y * design.size.y)
                    + localForward * (textOffset.z * design.size.x);
                rect.localRotation = Quaternion.identity;
                rect.localScale = new Vector3(
                    design.size.x * textScale / (BoardTexelsX * UiScale),
                    design.size.y * textScale / (BoardTexelsY * UiScale),
                    1f);
            }
            else
            {
                // 兜底（没有 3D 预制体）：画布挂女仆根节点，位置靠骨骼跟，朝向保持世界直立
                rect.SetParent(root, false);
                Vector3 textAnchor = anchor + forward * (thickness * 0.5f + 0.004f)
                    + right * (textOffset.x * _width)
                    + up * (textOffset.y * _width)
                    + forward * (textOffset.z * _width);
                _localAnchor = _bone.InverseTransformPoint(textAnchor);
                // 骨位链上有零缩放（有些模型的 locator 是缩到 0 藏起来的）时这个换算不还原，
                // 牌子会被甩到天上去；往返对不上就退回模型根，宁可不跟着骨骼走
                if ((_bone.TransformPoint(_localAnchor) - textAnchor).sqrMagnitude > 0.0004f)
                {
                    _bone = root;
                    _localAnchor = root.InverseTransformPoint(textAnchor);
                }

                float parentScale = root.lossyScale.y;
                if (parentScale < 0.0001f)
                {
                    parentScale = 1f;
                }

                rect.localScale = new Vector3(
                    _width * textScale / (BoardTexelsX * UiScale) / parentScale,
                    _height * textScale / (BoardTexelsY * UiScale) / parentScale,
                    1f);
            }

            if (_instance == null)
            {
                CreateImage(rect, "Board");
                Image back = CreateImage(rect, "Back");
                // 背面也画一块，从她身后看过去才不是一个洞
                back.rectTransform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                back.rectTransform.localPosition = new Vector3(0f, 0f, 4f);
            }

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(rect, false);
            RectTransform textRect = (RectTransform)textObject.transform;
            float pad = _textPadTexels * UiScale;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(pad, pad);
            textRect.offsetMax = new Vector2(-pad, -pad);

            _text = textObject.GetComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.fontSize = Mathf.RoundToInt(FontTexels * UiScale);
            _text.fontStyle = FontStyle.Bold;
            // 四行整体垂直居中（不是贴顶）：只写一两行时也在板面中间，不会看着"字偏上"；
            // 字号按 12 个牌面像素高来配，四行刚好落在留白里
            _text.alignment = TextAnchor.MiddleCenter;
            _text.lineSpacing = 1.05f;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;

            _outline = textObject.AddComponent<Outline>();
            _outline.effectColor = new Color(0f, 0f, 0f, 1f);
            float outline = _outlineTexels * UiScale;
            _outline.effectDistance = new Vector2(outline, -outline);

            // 先把牌子摆到挂点上：取景要读它的位置，不能等到下一帧 LateUpdate
            ApplyPose();
            // 挂没挂上由 Apply 决定（编辑中的预览会自己打开）
            _canvas.gameObject.SetActive(false);
        }

        Image CreateImage(RectTransform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(BoardTexelsX * UiScale, BoardTexelsY * UiScale);

            Image image = go.GetComponent<Image>();
            image.sprite = SharedSprite(_boardTexture);
            image.raycastTarget = false;
            return image;
        }

        void Refresh()
        {
            if (_text == null)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < MaidSignData.MaxLines; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(_data.Lines[i] ?? "");
            }

            _text.text = builder.ToString();
            _text.color = MaidSignPalette.ColorOf(_data.Color);
        }

        // ------------------------------------------------------------ 骨位 / 板面

        static Transform FindBone(Transform root, string[] names)
        {
            List<Transform> all = new List<Transform>();
            Collect(root, all);
            for (int n = 0; n < names.Length; n++)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (Normalize(all[i].name) == names[n])
                    {
                        return all[i];
                    }
                }
            }

            return null;
        }

        static void Collect(Transform node, List<Transform> into)
        {
            if (node == null || !node.gameObject.activeInHierarchy)
            {
                return;
            }

            into.Add(node);
            for (int i = 0; i < node.childCount; i++)
            {
                Collect(node.GetChild(i), into);
            }
        }

        static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            return name.ToLowerInvariant()
                .Replace("_", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace(" ", "");
        }

        /// <summary>
        /// 板面的 Sprite，全项目共用一份。贴图来源依次是：接上的 _boardTexture →
        /// 随包的 Resources/MaidSignBoard/sign（PNG，`Tools/sign_board_texture.py` 生成）→ 代码画的兜底。
        /// 原模组那块牌子也是代码画的（三个盒子 + 木色），代码兜底留着是为了"图丢了也不白板"。
        /// </summary>
        static Sprite SharedSprite(Texture2D board)
        {
            Texture2D texture = board;
            if (texture == null)
            {
                texture = Resources.Load<Texture2D>(BoardResourcePath);
            }

            if (texture == null)
            {
                if (_codeTexture == null)
                {
                    _codeTexture = BuildBoardTexture();
                }

                texture = _codeTexture;
            }

            if (_sharedSprite != null && _spriteSource == texture)
            {
                return _sharedSprite;
            }

            // 像素风：点采样 + Clamp，缩到屏幕上也不会糊成一片
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            _spriteSource = texture;
            _sharedSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            _sharedSprite.name = "MaidSignBoard";
            return _sharedSprite;
        }

        /// <summary>兜底的那张板面：深色边框 + 木板条 + 几道木纹，24×12 逐像素画</summary>
        static Texture2D BuildBoardTexture()
        {
            Color32 frame = new Color32(0x6E, 0x4F, 0x2E, 0xFF);
            Color32 plank = new Color32(0xB5, 0x8A, 0x55, 0xFF);
            Color32 seam = new Color32(0x9C, 0x72, 0x41, 0xFF);
            Color32 grain = new Color32(0x8A, 0x63, 0x37, 0xFF);

            Texture2D texture = new Texture2D(BoardTexelsX, BoardTexelsY, TextureFormat.RGBA32, false);
            texture.name = "MaidSignBoardFallback";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[BoardTexelsX * BoardTexelsY];
            for (int y = 0; y < BoardTexelsY; y++)
            {
                for (int x = 0; x < BoardTexelsX; x++)
                {
                    bool border = x == 0 || y == 0 || x == BoardTexelsX - 1 || y == BoardTexelsY - 1;
                    Color32 color = border ? frame : plank;
                    if (!border)
                    {
                        // 两条板缝 + 一列竖向木纹，看着像钉起来的木板
                        if (y == 4 || y == 8)
                        {
                            color = seam;
                        }
                        else if ((x == 5 && y == 2) || (x == 6 && y == 7) || (x == 17 && y == 3)
                            || (x == 18 && y == 9) || (x == 11 && y == 10))
                        {
                            color = grain;
                        }
                    }

                    pixels[y * BoardTexelsX + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
