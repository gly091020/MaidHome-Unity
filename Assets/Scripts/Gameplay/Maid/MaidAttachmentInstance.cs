using UnityEngine;
using System.Collections.Generic;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 一个挂件的实例（预制体的一份拷贝）。量出它自己多大、把包围盒中心摆到指定的世界点、
    /// 按朝向修正转正、按需要的世界宽度缩放 —— 示众牌和面具都走它。
    /// 摆好以后局部变换就不动了，位置/朝向靠父级骨骼自己跟（她转身、低头、躺下都跟着）。
    /// </summary>
    public sealed class MaidAttachmentInstance
    {
        readonly GameObject _root;
        readonly Renderer[] _renderers;
        readonly Bounds _localBounds;
        readonly List<Material> _ownedMaterials = new List<Material>();

        MaidAttachmentInstance(GameObject root, Renderer[] renderers, Bounds bounds)
        {
            _root = root;
            _renderers = renderers;
            _localBounds = bounds;
        }

        public GameObject Root
        {
            get { return _root; }
        }

        public Bounds LocalBounds
        {
            get { return _localBounds; }
        }

        /// <summary>预制体自己有多宽（缩放前的设计尺寸）</summary>
        public float DesignWidth
        {
            get { return Mathf.Max(0.0001f, _localBounds.size.x); }
        }

        public bool IsValid
        {
            get { return _root != null; }
        }

        public static MaidAttachmentInstance Create(Transform parent, GameObject prefab)
        {
            if (parent == null || prefab == null)
            {
                return null;
            }

            GameObject root = Object.Instantiate(prefab, parent, false);
            root.name = prefab.name;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = Measure(renderers, root.transform);
            MaidAttachmentInstance instance = new MaidAttachmentInstance(root, renderers, bounds);
            instance.NormalizeVertexColors();
            return instance;
        }

        /// <summary>
        /// 摆好：包围盒中心落到 centerWorld，朝向按 facing（调用方已经把 FacingFix 乘进去了），
        /// 世界宽度 = scale × 设计宽度。
        /// </summary>
        public void Place(Quaternion facing, Vector3 centerWorld, float scale)
        {
            if (_root == null)
            {
                return;
            }

            Transform transform = _root.transform;
            float parentScale = UniformScale(transform.parent);
            float localScale = Mathf.Max(0.0001f, scale) / parentScale;
            transform.localScale = new Vector3(localScale, localScale, localScale);

            Vector3 offset = facing * (_localBounds.center * localScale);
            transform.position = centerWorld - offset;
            transform.rotation = facing;
        }

        /// <summary>最大的那一块子网格（面具正面那块板就是最大的）</summary>
        public Renderer Biggest()
        {
            Renderer best = null;
            float bestValue = -1f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds local;
                Vector3 size = TryGetLocalBounds(renderer, out local) ? local.size : renderer.bounds.size;
                float value = size.x * size.y + size.y * size.z + size.z * size.x;
                if (value > bestValue)
                {
                    bestValue = value;
                    best = renderer;
                }
            }

            return best;
        }

        public bool TryGetWorldBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_root == null || _renderers.Length == 0 || !_root.activeInHierarchy)
            {
                return false;
            }

            bool has = false;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return has;
        }

        public void Destroy()
        {
            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                if (_ownedMaterials[i] != null)
                {
                    Object.Destroy(_ownedMaterials[i]);
                }
            }

            _ownedMaterials.Clear();
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }

        /// <summary>
        /// 这两个挂件模型是 glTF 导出的，没有 COLOR_0；MC 风格 shader 会乘顶点色，
        /// 个别平台缺这个通道时给的是黑色，模型会整块发黑。没有顶点色就克隆一份材质，
        /// 用 `_TintStrength = 1` + 白色 `_TintColor` 把顶点色钉成白色（有顶点色的模型不动）。
        /// </summary>
        void NormalizeVertexColors()
        {
            if (HasVertexColors())
            {
                return;
            }

            Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material source = renderer.sharedMaterial;
                if (source == null)
                {
                    continue;
                }

                Material clone;
                if (!clones.TryGetValue(source, out clone))
                {
                    clone = new Material(source);
                    if (clone.HasProperty("_TintColor"))
                    {
                        clone.SetColor("_TintColor", Color.white);
                    }

                    if (clone.HasProperty("_TintStrength"))
                    {
                        clone.SetFloat("_TintStrength", 1f);
                    }

                    clones[source] = clone;
                    _ownedMaterials.Add(clone);
                }

                renderer.sharedMaterial = clone;
            }
        }

        bool HasVertexColors()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.colors.Length > 0)
                {
                    return true;
                }

                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null && skinned.sharedMesh.colors.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        static Bounds Measure(Renderer[] renderers, Transform root)
        {
            bool has = false;
            Bounds bounds = new Bounds();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds local;
                if (!TryGetLocalBounds(renderer, out local))
                {
                    continue;
                }

                Vector3 center = local.center;
                Vector3 extents = local.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        center.x + ((corner & 1) == 0 ? -extents.x : extents.x),
                        center.y + ((corner & 2) == 0 ? -extents.y : extents.y),
                        center.z + ((corner & 4) == 0 ? -extents.z : extents.z));
                    Vector3 inRoot = root.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    if (!has)
                    {
                        bounds = new Bounds(inRoot, Vector3.zero);
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(inRoot);
                    }
                }
            }

            return bounds;
        }

        static float UniformScale(Transform parent)
        {
            if (parent == null)
            {
                return 1f;
            }

            Vector3 scale = parent.lossyScale;
            float uniform = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return uniform < 0.0001f ? 1f : uniform;
        }

        /// <summary>
        /// 渲染器的本地包围盒。Unity 2020 的基类 Renderer 上还没有 localBounds（2021 才有），
        /// 所以网格渲染器读 mesh.bounds、蒙皮读 SkinnedMeshRenderer.localBounds。
        /// </summary>
        public static bool TryGetLocalBounds(Renderer renderer, out Bounds bounds)
        {
            bounds = new Bounds();
            if (renderer == null)
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                bounds = filter.sharedMesh.bounds;
                return true;
            }

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                bounds = skinned.localBounds;
                return true;
            }

            return false;
        }
    }
}
