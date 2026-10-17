using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆脸上那副面具：3D 预制体挂在头骨骼上、按头宽自动定大小；玩家选的照片贴在正面那块板的表面
    /// （一块很小的 quad，贴图就是她文件夹里的 mask.png），没选照片就只戴面具本身。
    /// 注意本组件挂在女仆根节点上：`transform` 是女仆，面具是它的子物体，别写错（示众牌那边踩过）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidMaskView : MonoBehaviour
    {
        public const string ResourceName = "mask";

        static readonly string[] HeadBones = { "head", "mhead", "allhead", "head2" };

        MaidAgent _agent;
        MaidMaskData _data = new MaidMaskData();
        MaidAttachmentInstance _instance;
        GameObject _photoQuad;
        Renderer _photoQuadRenderer;
        Material _photoMaterial;
        Texture2D _photo;
        bool _preview;

        public MaidMaskData Data
        {
            get { return _data; }
        }

        /// <summary>她真的戴着（编辑中的预览不算）</summary>
        public bool Worn
        {
            get { return _data.Enabled && !_preview && _instance != null && _instance.IsValid; }
        }

        public bool IsBuilt
        {
            get { return _instance != null && _instance.IsValid; }
        }

        public static MaidMaskView Find(MaidAgent agent)
        {
            return agent == null ? null : agent.GetComponent<MaidMaskView>();
        }

        /// <summary>有没有面具预制体可用（没有就把女仆面板的「面具」按钮灰掉）</summary>
        public static bool Supports()
        {
            return MaidAttachmentStyle.ResolvePrefab(MaidAttachmentStyles.Mask, ResourceName) != null;
        }

        public static MaidMaskView Attach(MaidAgent agent, MaidMaskData data, bool preview)
        {
            if (agent == null || data == null)
            {
                return null;
            }

            MaidMaskView view = Find(agent);
            if (view == null)
            {
                view = agent.gameObject.AddComponent<MaidMaskView>();
            }

            // 每次进编辑模式都按当前配置重搭一遍：Inspector 里改完大小/偏移，重进模式就能看到
            view.Rebuild();
            view.Build(agent);
            if (!view.IsBuilt)
            {
                return null;
            }

            view.Apply(data, preview);
            return view;
        }

        /// <summary>把已经搭好的面具拆掉，下一次 Build 会按最新配置重来</summary>
        void Rebuild()
        {
            if (_photoQuad != null)
            {
                Destroy(_photoQuad);
                _photoQuad = null;
                _photoQuadRenderer = null;
            }

            if (_photoMaterial != null)
            {
                Destroy(_photoMaterial);
                _photoMaterial = null;
            }

            if (_photo != null)
            {
                Destroy(_photo);
                _photo = null;
            }

            if (_instance != null)
            {
                _instance.Destroy();
                _instance = null;
            }

            _data = new MaidMaskData();
        }

        public void Apply(MaidMaskData data, bool preview)
        {
            if (data == null)
            {
                return;
            }

            _data = data;
            _preview = preview;
            bool show = data.Enabled || preview;
            if (_instance == null || !_instance.IsValid)
            {
                return;
            }

            _instance.Root.SetActive(show);
            if (show)
            {
                RefreshPhoto();
            }
        }

        /// <summary>换过照片之后把贴图重新读一遍（导入完调它，不然还挂着上一张）</summary>
        public void ReloadPhoto()
        {
            if (_photo != null)
            {
                Destroy(_photo);
                _photo = null;
            }

            if (_data != null && _data.Enabled && _instance != null && _instance.IsValid
                && _instance.Root.activeSelf)
            {
                RefreshPhoto();
            }
        }

        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_instance == null || !_instance.IsValid)
            {
                return false;
            }

            return _instance.TryGetWorldBounds(out bounds);
        }

        void OnDestroy()
        {
            if (_instance != null)
            {
                _instance.Destroy();
                _instance = null;
            }

            if (_photo != null)
            {
                Destroy(_photo);
                _photo = null;
            }

            if (_photoMaterial != null)
            {
                Destroy(_photoMaterial);
                _photoMaterial = null;
            }
        }

        void Build(MaidAgent agent)
        {
            if (_instance != null || agent == null)
            {
                return;
            }

            _agent = agent;
            MaidAttachmentStyle style = MaidAttachmentStyles.Mask;
            GameObject prefab = MaidAttachmentStyle.ResolvePrefab(style, ResourceName);
            if (prefab == null)
            {
                return;
            }

            Transform root = agent.transform;
            Transform head = FindBone(root, style.BoneNames(HeadBones));
            Bounds subtree;
            float maidHeight = Mathf.Max(0.2f, agent.GetBounds().size.y);
            if (!TryGetHeadBounds(head, root, maidHeight * 0.2f, out subtree))
            {
                Debug.LogWarning("面具找不到头骨骼的网格，先按女仆身高估算位置和大小：" + agent.name, this);
            }

            // ★ 只用头骨骼自己的网格，不含挂在头下面的头发 / 耳朵 / 单侧装饰：
            // 那些东西是不对称的（大正酒狐头上的小面具装饰让子树包围盒中心偏左 1.5px），
            // 用子树量会把面具带歪、还会被大发型撑大。
            Renderer ownHead = head != null ? head.GetComponent<Renderer>() : null;
            Bounds headBounds = ownHead != null && ownHead.enabled ? ownHead.bounds : subtree;

            _instance = MaidAttachmentInstance.Create(head != null ? head : root, prefab);
            if (_instance == null)
            {
                return;
            }

            Vector3 forward = root.forward;
            Vector3 up = root.up;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            // 头宽取水平两个方向里大的那个（不同模型头是朝 X 还是朝 Z 不一定）；
            // 世界 AABB 会被她转身撑大，所以按她自己的 right / forward 投影量
            Vector3 extents = headBounds.extents;
            float widthRight = 2f * (Mathf.Abs(extents.x * right.x)
                + Mathf.Abs(extents.y * right.y) + Mathf.Abs(extents.z * right.z));
            float widthForward = 2f * (Mathf.Abs(extents.x * forward.x)
                + Mathf.Abs(extents.y * forward.y) + Mathf.Abs(extents.z * forward.z));
            float headWidth = Mathf.Max(widthRight, widthForward);
            if (headWidth < 0.0001f)
            {
                headWidth = maidHeight * 0.2f;
            }

            bool simpleBedrock = MaidAttachmentStyles.IsSimpleBedrock(agent);
            float scale = headWidth * style.SizeRatio * style.MultiplierFor(simpleBedrock)
                / _instance.DesignWidth;
            Vector3 styleOffset = style.OffsetsFor(simpleBedrock);
            // 水平中心用头骨骼的中轴（模型的对称轴）：头上挂单侧装饰时包围盒中心会偏，
            // 骨骼位置不会。上下仍用头网格的中心。
            Vector3 center = headBounds.center;
            if (head != null)
            {
                Vector3 delta = head.position - center;
                center += right * Vector3.Dot(delta, right) + forward * Vector3.Dot(delta, forward);
            }

            center = center
                + right * (styleOffset.x * headWidth)
                + up * (styleOffset.y * headWidth)
                + forward * (styleOffset.z * headWidth);

            Quaternion facing = root.rotation * Quaternion.Euler(style.FacingFix);
            _instance.Place(facing, center, scale);
            BuildPhotoQuad();
        }

        /// <summary>照片贴在正面那块板的外表面上：找个 1×1 的 quad，按那块板的正面大小铺上去</summary>
        void BuildPhotoQuad()
        {
            Renderer plate = _instance.Biggest();
            if (plate == null)
            {
                return;
            }

            _photoQuad = new GameObject("Photo", typeof(MeshFilter), typeof(MeshRenderer));
            _photoQuad.transform.SetParent(plate.transform, false);

            MeshFilter filter = _photoQuad.GetComponent<MeshFilter>();
            filter.sharedMesh = BuildQuadMesh();

            Bounds local;
            if (!MaidAttachmentInstance.TryGetLocalBounds(plate, out local))
            {
                local = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            }

            _photoQuad.transform.localPosition = new Vector3(local.center.x, local.center.y, local.min.z - 0.01f);
            _photoQuad.transform.localRotation = Quaternion.identity;
            _photoQuad.transform.localScale = new Vector3(local.size.x, local.size.y, 1f);

            _photoQuadRenderer = _photoQuad.GetComponent<MeshRenderer>();
            // 材质照面具那块板来（同一个 MC 风格 shader），只把贴图换成照片
            if (plate.sharedMaterial == null)
            {
                Destroy(_photoQuad);
                _photoQuad = null;
                _photoQuadRenderer = null;
                return;
            }

            _photoMaterial = new Material(plate.sharedMaterial);
            _photoQuadRenderer.sharedMaterial = _photoMaterial;
            _photoQuadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _photoQuadRenderer.receiveShadows = false;
            _photoQuad.SetActive(false);
        }

        void RefreshPhoto()
        {
            if (_photoQuad == null || _photoQuadRenderer == null)
            {
                return;
            }

            if (!_data.HasPhoto)
            {
                _photoQuad.SetActive(false);
                return;
            }

            if (_photo == null)
            {
                _photo = MaidMaskPhoto.Load(_agent != null ? _agent.Save : null);
                if (_photo == null)
                {
                    _photoQuad.SetActive(false);
                    return;
                }

                _photoQuadRenderer.sharedMaterial.mainTexture = _photo;
            }

            _photoQuad.SetActive(true);
        }

        /// <summary>1×1 的四边形，法线朝 -Z（面具正面也是朝 -Z 的）</summary>
        static Mesh BuildQuadMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "MaidMaskPhoto";
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            // MC 风格 shader 会乘顶点色：没有颜色通道时部分平台给的是黑色，照片会整片发黑
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>头的世界包围盒：头骨骼底下所有渲染器合起来；一个都没有就按身高估</summary>
        static bool TryGetHeadBounds(Transform head, Transform root, float fallbackSize, out Bounds bounds)
        {
            bounds = new Bounds();
            float size = Mathf.Max(0.2f, fallbackSize);
            if (head != null)
            {
                Renderer[] renderers = head.GetComponentsInChildren<Renderer>(false);
                bool has = false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null || !renderers[i].enabled)
                    {
                        continue;
                    }

                    if (!has)
                    {
                        bounds = renderers[i].bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }

                if (has)
                {
                    return true;
                }

                bounds = new Bounds(head.position + Vector3.up * (size * 0.5f), Vector3.one * size);
                return false;
            }

            bounds = new Bounds(root.position + Vector3.up * size, new Vector3(size, size, size));
            return false;
        }

        static Transform FindBone(Transform root, string[] names)
        {
            if (root == null || names == null || names.Length == 0)
            {
                return null;
            }

            for (int n = 0; n < names.Length; n++)
            {
                Transform found = Search(root, Normalize(names[n]));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static Transform Search(Transform node, string name)
        {
            if (node == null || !node.gameObject.activeInHierarchy)
            {
                return null;
            }

            if (Normalize(node.name) == name)
            {
                return node;
            }

            for (int i = 0; i < node.childCount; i++)
            {
                Transform found = Search(node.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
    }
}
