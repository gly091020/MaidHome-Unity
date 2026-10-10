using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>可梳的东西：头发、头顶头皮、耳朵、尾巴</summary>
    public enum MaidGroomKind
    {
        Hair,
        Scalp,
        Ear,
        Tail
    }

    /// <summary>
    /// 一个可梳部位。分段型的（尾巴每节、分段头发）每节一份，驱动骨骼就是那一节；
    /// 整块型的（第一段头发、头皮）驱动骨骼是头，刷它只是让头轻轻让一下。
    /// </summary>
    public sealed class MaidGroomPart
    {
        public string Id;
        public MaidGroomKind Kind;
        /// <summary>反应写在这根骨骼上（模组里的 driver）</summary>
        public Transform Driver;
        /// <summary>链式部位的第一根（耳朵）/ 这一节的骨骼（尾巴、头发）</summary>
        public Transform Tip;
        /// <summary>同一根尾巴 / 同一条耳朵链共用，用来判断"这条链现在被别人拿着"</summary>
        public string Group;
        public int Segment = -1;
        /// <summary>模组 movable()：链的第 0 节不弯，免得根部一转整条尾巴跟着飞</summary>
        public bool Movable = true;
        /// <summary>沿毛流方向的总长，用来把命中点换算成 0~1 的进度（顺逆毛判定用）</summary>
        public float FlowLength = 0.02f;
        /// <summary>尾巴才有：拖动/固定走这套弹簧</summary>
        public MaidTailChain Tail;
        /// <summary>尾巴：整条链的中轴（世界坐标，每帧刷一次，同一帧内共用）</summary>
        public Vector3 Axis;
        public int AxisStamp = -1;

        public bool IsHeadDriven
        {
            get { return Kind == MaidGroomKind.Scalp || Kind == MaidGroomKind.Hair && !Movable; }
        }
    }

    /// <summary>一块可拾取的表面对应一根骨骼的一块网格（骨骼挂 MeshFilter 的那种）</summary>
    public sealed class MaidGroomSurface
    {
        public MaidGroomPart Part;
        public Transform Bone;
        public Renderer Renderer;
        public Vector3[] Vertices;
        public int[] Triangles;
        public Matrix4x4 ToWorld;
        /// <summary>毛流方向（世界）：顺毛 = 沿着它往前走</summary>
        public Vector3 Flow;
        /// <summary>这一束毛的根（头/尾巴根）：顺逆毛的进度从这里起算</summary>
        public Transform StrandRoot;
        /// <summary>顺逆毛进度的起点（世界坐标，每帧刷）</summary>
        public Vector3 GrainOrigin;
        /// <summary>顺逆毛判定用：沿 Flow 数多远算 0~1（按几何量，不靠骨骼 pivot）</summary>
        public float GrainRange = 0.05f;
        /// <summary>头发和尾巴才判顺逆毛</summary>
        public bool GrainEligible;
    }

    /// <summary>一次拾取结果</summary>
    public sealed class MaidGroomHit
    {
        public MaidGroomSurface Surface;
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;

        public MaidGroomPart Part { get { return Surface != null ? Surface.Part : null; } }
    }

    /// <summary>
    /// 梳毛的部位模型：按名字认头发 / 耳朵 / 头顶 / 尾巴，采样每根骨骼的网格做射线拾取，
    /// 再维护"刷一下就让这块毛让一让"的弹性反应。
    /// 部位规则照抄 moreanimation 的 GroomingGeometry（名字优先，认不出来就几何兜底，实在认不出来就不支持）。
    /// 没有模仿模组的姿态冻结：这边女仆是活的，反应都是叠在当帧动画姿势上的。
    /// </summary>
    public sealed class MaidGroomingRig
    {
        /// <summary>反应弹簧照抄模组：一步最多把当前值往目标拉这么多</summary>
        const float ResponseLerp = 0.38f;
        /// <summary>安静几步之后目标开始缩，刷完自己回到原姿势</summary>
        const float ResponseDecay = 0.86f;
        const int QuietStepsBeforeDecay = 3;

        /// <summary>模组里驱动骨骼是头的时候，反应上限只有 0.055 弧度（约 3°）</summary>
        public const float HeadReactionMax = 0.055f;
        /// <summary>其它骨骼的反应上限 0.52 弧度（约 30°）</summary>
        public const float BendReactionMax = 0.52f;
        /// <summary>压耳最多压 55°，和模组 earTarget 的上限一致</summary>
        public const float EarPressMaxDegrees = 55f;

        /// <summary>这条链现在被拖动 / 固定，反应先让位，别和弹簧链抢同一根骨骼</summary>
        public readonly HashSet<string> BusyGroups = new HashSet<string>();

        readonly List<MaidGroomPart> _parts = new List<MaidGroomPart>();
        readonly List<MaidGroomSurface> _surfaces = new List<MaidGroomSurface>();
        readonly Dictionary<Transform, MaidGroomPart> _byBone = new Dictionary<Transform, MaidGroomPart>();
        readonly Dictionary<string, Reaction> _reactions = new Dictionary<string, Reaction>();
        readonly List<Reaction> _reactionOrder = new List<Reaction>();
        readonly List<MaidTailChain> _tails = new List<MaidTailChain>();
        readonly List<MaidGroomPart> _ears = new List<MaidGroomPart>();
        /// <summary>所有算头发的骨骼，含"名字不叫 hair 但挂在头发底下"的后代</summary>
        readonly HashSet<Transform> _hairBones = new HashSet<Transform>();

        Transform _head;
        float _stepAccumulator;
        /// <summary>每刷一次 +1：同一帧里同一条尾巴的中轴只算一次</summary>
        int _axisStamp;

        /// <summary>刷一下就想让这块毛让多少：目标值在驱动骨骼的父空间里，用"轴 × 角度"表示</summary>
        sealed class Reaction
        {
            public MaidGroomPart Part;
            public Transform Bone;
            public Vector3 Previous;
            public Vector3 Current;
            public Vector3 Target;
            public int QuietSteps;
            public Quaternion Animated;
            public Quaternion Written;
            public bool HasAnimated;
        }

        public bool Supports { get { return _parts.Count > 0; } }
        public Transform Head { get { return _head; } }
        public List<MaidTailChain> Tails { get { return _tails; } }
        public List<MaidGroomPart> Parts { get { return _parts; } }

        public static MaidGroomingRig Build(MaidAgent agent, MaidFaceRig face, List<MaidTailChain> tails)
        {
            MaidGroomingRig rig = new MaidGroomingRig();
            if (agent == null)
            {
                return rig;
            }

            rig._head = face != null ? face.Head : null;
            List<Transform> all = new List<Transform>();
            Collect(agent.transform, all);

            rig.BuildTails(tails);
            rig.BuildEars(face, all);
            rig.BuildHair(all);
            rig.BuildScalp();
            rig.BuildSurfaces(all);
            return rig;
        }

        // ---------- 部位识别 ----------

        /// <summary>
        /// 尾巴每节一份。第 0 节不算 movable（模组 movable()），刷根部时把反应转给下一节，
        /// 不然根部一转整条尾巴一起甩。
        /// </summary>
        void BuildTails(List<MaidTailChain> tails)
        {
            if (tails == null)
            {
                return;
            }

            for (int c = 0; c < tails.Count; c++)
            {
                MaidTailChain chain = tails[c];
                if (chain == null || chain.Bones == null || chain.Bones.Length == 0)
                {
                    continue;
                }

                _tails.Add(chain);
                float length = ChainLength(chain.Bones);
                for (int i = 0; i < chain.Bones.Length; i++)
                {
                    Transform bone = chain.Bones[i];
                    if (bone == null)
                    {
                        continue;
                    }

                    MaidGroomPart part = new MaidGroomPart();
                    part.Id = "tail/" + chain.Id + "#" + i;
                    part.Kind = MaidGroomKind.Tail;
                    part.Driver = bone;
                    part.Tip = bone;
                    part.Group = "tail/" + chain.Id;
                    part.Segment = i;
                    part.Movable = i > 0;
                    part.FlowLength = length;
                    part.Tail = chain;
                    AddPart(part);
                }
            }
        }

        /// <summary>耳朵：整条链一份，反应只写在耳根那根（和模组一致）</summary>
        void BuildEars(MaidFaceRig face, List<Transform> all)
        {
            if (face == null)
            {
                return;
            }

            AddEar(face.LeftEar, "ear/left");
            AddEar(face.RightEar, "ear/right");
        }

        void AddEar(List<Transform> chain, string id)
        {
            if (chain == null || chain.Count == 0 || chain[0] == null)
            {
                return;
            }

            MaidGroomPart part = new MaidGroomPart();
            part.Id = id;
            part.Kind = MaidGroomKind.Ear;
            part.Driver = chain[0];
            part.Tip = chain[chain.Count - 1] != null ? chain[chain.Count - 1] : chain[0];
            part.Group = id;
            part.Movable = true;
            part.FlowLength = 0.05f;
            AddPart(part);
            _ears.Add(part);
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i] != null && !_byBone.ContainsKey(chain[i]))
                {
                    _byBone.Add(chain[i], part);
                }
            }
        }

        /// <summary>
        /// 头发：名字里带 hair / bang / fringe / ponytail 的骨骼。
        /// 上一节也是头发（真正的分束）就每节独立弯；挂在头上的第一段算"整块头发"，随头轻动。
        /// </summary>
        void BuildHair(List<Transform> all)
        {
            List<Transform> hair = new List<Transform>();
            for (int i = 0; i < all.Count; i++)
            {
                if (IsHair(all[i].name))
                {
                    hair.Add(all[i]);
                }
            }

            // 名字不叫 hair、但挂在头发底下的后代（方块模型里常见：hair -> bone7 / bone8 才是真正那几撮）
            // 一样算头发，否则最大那几块头发网格根本刷不到
            for (int i = 0; i < hair.Count; i++)
            {
                _hairBones.Add(hair[i]);
                CollectHairDescendants(hair[i], _hairBones);
            }

            for (int i = 0; i < hair.Count; i++)
            {
                Transform bone = hair[i];
                Transform parent = bone.parent;
                bool segmented = parent != null && _hairBones.Contains(parent) && IsUnder(bone, _head);
                MaidGroomPart part = new MaidGroomPart();
                part.Id = segmented ? "hair/" + bone.name : "hair/head";
                part.Kind = MaidGroomKind.Hair;
                part.Driver = segmented || _head == null ? bone : _head;
                part.Tip = bone;
                part.Group = part.Id;
                part.Segment = segmented ? 1 : 0;
                part.Movable = segmented;
                part.FlowLength = segmented ? StrandLength(bone) : 0.05f;
                AddPart(part);
            }

            // 只在自己不在 parts 里的那种后代（bone7/bone8）：挂到最近的头发祖先那一份上，
            // 它自己不能弯（骨骼 pivot 常常和父骨骼重合），但至少刷得到、也有顺逆毛
            for (int i = 0; i < all.Count; i++)
            {
                Transform bone = all[i];
                if (!_hairBones.Contains(bone) || bone == _head || _byBone.ContainsKey(bone))
                {
                    continue;
                }

                Transform ancestor = NearestHairPart(bone);
                if (ancestor != null && !_byBone.ContainsKey(bone))
                {
                    _byBone.Add(bone, _byBone[ancestor]);
                }
            }
        }

        static void CollectHairDescendants(Transform node, HashSet<Transform> into)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy || into.Contains(child))
                {
                    continue;
                }

                into.Add(child);
                CollectHairDescendants(child, into);
            }
        }

        /// <summary>往上找最近的那根"已经是某个部位"的头发骨骼</summary>
        Transform NearestHairPart(Transform bone)
        {
            Transform current = bone.parent;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                if (_byBone.ContainsKey(current))
                {
                    return current;
                }

                if (!_hairBones.Contains(current))
                {
                    return null;
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>头顶：刷头顶那面只是轻轻点头（模组 scalp）</summary>
        void BuildScalp()
        {
            if (_head == null)
            {
                return;
            }

            MaidGroomPart part = new MaidGroomPart();
            part.Id = "scalp/head";
            part.Kind = MaidGroomKind.Scalp;
            part.Driver = _head;
            part.Tip = _head;
            part.Group = part.Id;
            part.Movable = true;
            part.FlowLength = 0.05f;
            AddPart(part);
        }

        void AddPart(MaidGroomPart part)
        {
            // 同一个部位 id 只留一份（头发的"整块"会命中很多根骨骼）
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Id == part.Id)
                {
                    if (part.Tip != null && !_byBone.ContainsKey(part.Tip))
                    {
                        _byBone.Add(part.Tip, _parts[i]);
                    }

                    return;
                }
            }

            _parts.Add(part);
            if (part.Tip != null && !_byBone.ContainsKey(part.Tip))
            {
                _byBone.Add(part.Tip, part);
            }
        }

        /// <summary>每根挂了网格的骨骼都收进来：属于部位的能刷，其余的只当遮挡面</summary>
        void BuildSurfaces(List<Transform> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                Transform bone = all[i];
                MeshFilter filter = bone.GetComponent<MeshFilter>();
                Renderer renderer = bone.GetComponent<Renderer>();
                if (filter == null || renderer == null || !renderer.enabled || filter.sharedMesh == null)
                {
                    continue;
                }

                Mesh mesh = filter.sharedMesh;
                if (!mesh.isReadable)
                {
                    continue;
                }

                MaidGroomPart part;
                if (!_byBone.TryGetValue(bone, out part))
                {
                    // 兜底：挂在尾巴/头发/耳朵底下、自己没登记的网格（分叉、夹层、装饰骨骼），
                    // 归给最近的那根祖先部位（模组也是这么顺着父链找 driver 的）
                    part = AncestorPart(bone);
                }

                MaidGroomSurface surface = new MaidGroomSurface();
                surface.Bone = bone;
                surface.Renderer = renderer;
                surface.Part = part;
                surface.Vertices = mesh.vertices;
                surface.Triangles = mesh.triangles;
                surface.ToWorld = bone.localToWorldMatrix;
                if (part != null && part.Kind == MaidGroomKind.Tail)
                {
                    // 尾巴：整条链共用一根中轴，进度从链根算
                    surface.StrandRoot = RootOf(part);
                    surface.GrainRange = Mathf.Max(0.02f, part.FlowLength);
                    surface.GrainEligible = true;
                }
                else if (part != null && part.Kind == MaidGroomKind.Hair)
                {
                    surface.StrandRoot = HairStrandRoot(bone);
                    surface.GrainEligible = true;
                }

                _surfaces.Add(surface);
            }
        }

        /// <summary>顺着父链往上找最近的一个"已经是部位"的祖先，只认尾巴/头发/耳朵（脸和身体不算）</summary>
        MaidGroomPart AncestorPart(Transform bone)
        {
            Transform current = bone != null ? bone.parent : null;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                MaidGroomPart found;
                if (_byBone.TryGetValue(current, out found))
                {
                    return found.Kind == MaidGroomKind.Tail || found.Kind == MaidGroomKind.Hair
                        || found.Kind == MaidGroomKind.Ear
                        ? found
                        : null;
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>这束头发挂在哪根骨骼上（顺着"算头发"的祖先一路往上找最上面那根）</summary>
        Transform HairStrandRoot(Transform bone)
        {
            Transform root = bone;
            Transform current = bone.parent;
            int guard = 0;
            while (current != null && guard++ < 64 && _hairBones.Contains(current))
            {
                root = current;
                current = current.parent;
            }

            return root;
        }

        // ---------- 拾取 ----------

        /// <summary>刷新每根骨骼的世界矩阵和毛流方向（拾取和渲染都按当帧姿势算）</summary>
        public void RefreshSurfaces()
        {
            _axisStamp++;
            for (int i = 0; i < _surfaces.Count; i++)
            {
                MaidGroomSurface surface = _surfaces[i];
                if (surface.Bone == null)
                {
                    continue;
                }

                surface.ToWorld = surface.Bone.localToWorldMatrix;
                RefreshFlow(surface);
            }
        }

        /// <summary>
        /// 算这块网格的毛流。**不能靠骨骼 pivot 推**：方块模型（SimpleBedrockModel）里
        /// 真正那几撮头发的骨骼 pivot 和父骨骼是重合的，照骨骼推会得到零向量或反向。
        /// 所以按几何算：从"离发根最近的那一角"指向"最远的那一角"（尾巴用整条链最远的那一角）。
        /// </summary>
        void RefreshFlow(MaidGroomSurface surface)
        {
            MaidGroomPart part = surface.Part;
            if (part != null && part.Kind == MaidGroomKind.Tail)
            {
                Transform root = surface.StrandRoot != null ? surface.StrandRoot : RootOf(part);
                surface.Flow = TailAxis(part, root);
                surface.GrainOrigin = root != null ? root.position : surface.Renderer.bounds.center;
                surface.GrainRange = Mathf.Max(0.02f, part.FlowLength);
                return;
            }

            if (part != null && part.Kind == MaidGroomKind.Hair && surface.GrainEligible && surface.Renderer != null)
            {
                // 头发挂在哪就往哪梳：锚点用头（头的世界位置永远靠得住），没有头才退回发根
                Vector3 anchor = _head != null
                    ? _head.position
                    : (surface.StrandRoot != null ? surface.StrandRoot.position : surface.Renderer.bounds.center);
                Bounds bounds = surface.Renderer.bounds;
                Vector3 near = bounds.center;
                Vector3 far = bounds.center;
                float bestNear = float.MaxValue;
                float bestFar = -1f;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, Corner(k));
                    float distance = (corner - anchor).sqrMagnitude;
                    if (distance < bestNear)
                    {
                        bestNear = distance;
                        near = corner;
                    }

                    if (distance > bestFar)
                    {
                        bestFar = distance;
                        far = corner;
                    }
                }

                Vector3 flow = far - near;
                surface.Flow = flow.sqrMagnitude > 4e-4f ? flow.normalized : LongestAxis(surface);
                surface.GrainOrigin = near;
                surface.GrainRange = Mathf.Max(0.02f, Vector3.Dot(bounds.size,
                    new Vector3(Mathf.Abs(surface.Flow.x), Mathf.Abs(surface.Flow.y), Mathf.Abs(surface.Flow.z))));
                return;
            }

            surface.Flow = ParentFlow(surface);
        }

        /// <summary>整条尾巴的中轴：从链根指向"离链根最远的那一块网格的角"（比按骨骼 pivot 稳）</summary>
        Vector3 TailAxis(MaidGroomPart part, Transform root)
        {
            if (part.AxisStamp == _axisStamp)
            {
                return part.Axis;
            }

            part.AxisStamp = _axisStamp;
            part.Axis = ParentFlowOfBone(part.Driver);
            if (root != null)
            {
                Vector3 anchor = root.position;
                float best = 1e-6f;
                Vector3 far = anchor;
                bool has = false;
                for (int i = 0; i < _surfaces.Count; i++)
                {
                    MaidGroomSurface other = _surfaces[i];
                    if (other.Renderer == null || other.Part == null || other.Part.Group != part.Group)
                    {
                        continue;
                    }

                    Bounds bounds = other.Renderer.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, Corner(k));
                        float distance = (corner - anchor).sqrMagnitude;
                        if (distance > best)
                        {
                            best = distance;
                            far = corner;
                            has = true;
                        }
                    }
                }

                if (has)
                {
                    part.Axis = (far - anchor).normalized;
                }
            }

            return part.Axis;
        }

        /// <summary>网格最长的那根轴（世界），退化时才用</summary>
        static Vector3 LongestAxis(MaidGroomSurface surface)
        {
            MeshFilter filter = surface.Bone.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3 size = filter.sharedMesh.bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
                Vector3 local = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
                Vector3 world = surface.Bone.TransformDirection(local);
                if (world.sqrMagnitude > 1e-8f)
                {
                    return world.normalized;
                }
            }

            return surface.Bone.forward;
        }

        static Vector3 ParentFlow(MaidGroomSurface surface)
        {
            return ParentFlowOfBone(surface.Bone);
        }

        static Vector3 ParentFlowOfBone(Transform bone)
        {
            if (bone == null)
            {
                return Vector3.forward;
            }

            Transform parent = bone.parent;
            Vector3 flow = parent != null ? bone.position - parent.position : Vector3.zero;
            return flow.sqrMagnitude > 1e-10f ? flow.normalized : bone.forward;
        }

        /// <summary>尾巴链的最根那一节（顺逆毛的进度从这里起算）</summary>
        static Transform RootOf(MaidGroomPart part)
        {
            if (part == null || part.Tail == null || part.Tail.Bones == null || part.Tail.Bones.Length == 0)
            {
                return part != null ? part.Driver : null;
            }

            return part.Tail.Bones[0] != null ? part.Tail.Bones[0] : part.Driver;
        }

        /// <summary>
        /// 梳得到的那几块（头 / 头发 / 耳朵 / 尾巴）的世界包围盒，取景用：挡脸的身体不算进来。
        /// includeTail = false 就只框头上那几块（尾巴又会摊到画面外）
        /// </summary>
        public bool TryGetBounds(out Bounds bounds, bool includeTail)
        {
            bounds = new Bounds();
            bool has = false;
            for (int i = 0; i < _surfaces.Count; i++)
            {
                MaidGroomSurface surface = _surfaces[i];
                if (surface.Part == null || surface.Renderer == null || !surface.Renderer.enabled)
                {
                    continue;
                }

                if (!includeTail && surface.Part.Kind == MaidGroomKind.Tail)
                {
                    continue;
                }

                if (!has)
                {
                    bounds = surface.Renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(surface.Renderer.bounds);
                }
            }

            return has;
        }

        /// <summary>
        /// 手指位置 → 最近的可梳表面。耳朵额外做一次"轮廓放大"的补拾取（照抄模组 EAR_HIT_RADIUS_SCALE），
        /// 因为耳朵细，手机上几像素就点不中了；放大后的假三角形仍然要和真实表面比深度。
        /// </summary>
        public MaidGroomHit Pick(Vector2 screenPoint, Camera camera, float earHitScale)
        {
            if (camera == null || _surfaces.Count == 0)
            {
                return null;
            }

            Ray ray = camera.ScreenPointToRay(screenPoint);
            MaidGroomHit best = Nearest(ray, null, 0f);
            if (_ears.Count > 0 && earHitScale > 1.001f)
            {
                for (int i = 0; i < _ears.Count; i++)
                {
                    if (!InEarScreenRect(_ears[i], camera, screenPoint, earHitScale))
                    {
                        continue;
                    }

                    MaidGroomHit ear = Nearest(ray, _ears[i], earHitScale);
                    if (ear != null && (best == null || ear.Distance < best.Distance))
                    {
                        best = ear;
                    }
                }
            }

            if (best == null || best.Part == null)
            {
                return null;
            }

            // 头顶只认朝上的那面：摸到脸/下巴不该点头
            if (best.Part.Kind == MaidGroomKind.Scalp && Vector3.Dot(best.Normal, Vector3.up) < 0.5f)
            {
                return null;
            }

            return best;
        }

        MaidGroomHit Nearest(Ray ray, MaidGroomPart onlyPart, float inflate)
        {
            MaidGroomHit best = null;
            Vector3 view = ray.direction.normalized;
            for (int i = 0; i < _surfaces.Count; i++)
            {
                MaidGroomSurface surface = _surfaces[i];
                if (onlyPart != null && surface.Part != onlyPart)
                {
                    continue;
                }

                if (surface.Renderer == null || !surface.Renderer.enabled)
                {
                    continue;
                }

                float reach;
                if (!surface.Renderer.bounds.IntersectRay(ray, out reach))
                {
                    continue;
                }

                Vector3 center = Vector3.zero;
                if (inflate > 1.001f)
                {
                    Bounds bounds = surface.Renderer.bounds;
                    center = bounds.center;
                }

                Vector3[] vertices = surface.Vertices;
                int[] triangles = surface.Triangles;
                Matrix4x4 toWorld = surface.ToWorld;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector3 a = toWorld.MultiplyPoint3x4(vertices[triangles[t]]);
                    Vector3 b = toWorld.MultiplyPoint3x4(vertices[triangles[t + 1]]);
                    Vector3 c = toWorld.MultiplyPoint3x4(vertices[triangles[t + 2]]);
                    if (inflate > 1.001f)
                    {
                        a = Inflate(a, center, view, inflate);
                        b = Inflate(b, center, view, inflate);
                        c = Inflate(c, center, view, inflate);
                    }

                    float distance;
                    if (!RayTriangle(ray, a, b, c, out distance))
                    {
                        continue;
                    }

                    if (best != null && distance >= best.Distance)
                    {
                        continue;
                    }

                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-12f)
                    {
                        continue;
                    }

                    normal.Normalize();
                    if (Vector3.Dot(normal, view) > 0f)
                    {
                        normal = -normal;
                    }

                    MaidGroomHit hit = new MaidGroomHit();
                    hit.Surface = surface;
                    hit.Point = ray.origin + ray.direction * distance;
                    hit.Normal = normal;
                    hit.Distance = distance;
                    best = hit;
                }
            }

            return best;
        }

        /// <summary>把补拾取用的角点沿屏幕方向撑开（沿视线的分量不变，所以深度还是真的）</summary>
        static Vector3 Inflate(Vector3 point, Vector3 center, Vector3 view, float scale)
        {
            Vector3 offset = point - center;
            Vector3 onScreen = offset - Vector3.Dot(offset, view) * view;
            return point + onScreen * (scale - 1f);
        }

        /// <summary>耳朵在屏幕上的包围盒（放大过），用来快速排除"根本没点耳朵"的情况</summary>
        bool InEarScreenRect(MaidGroomPart part, Camera camera, Vector2 screenPoint, float scale)
        {
            Bounds bounds = new Bounds();
            bool has = false;
            for (int s = 0; s < _surfaces.Count; s++)
            {
                MaidGroomSurface surface = _surfaces[s];
                if (surface.Part != part || surface.Renderer == null || !surface.Renderer.enabled)
                {
                    continue;
                }

                Bounds world = surface.Renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = world.center + Vector3.Scale(world.extents, Corner(i));
                    Vector3 screen = camera.WorldToScreenPoint(corner);
                    if (screen.z <= 0f)
                    {
                        continue;
                    }

                    Bounds point = new Bounds(screen, Vector3.zero);
                    if (!has)
                    {
                        bounds = point;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            if (!has)
            {
                return false;
            }

            Vector3 center = bounds.center;
            Vector2 half = new Vector2(bounds.extents.x, bounds.extents.y) * scale;
            return Mathf.Abs(screenPoint.x - center.x) <= half.x + 4f
                && Mathf.Abs(screenPoint.y - center.y) <= half.y + 4f;
        }

        static Vector3 Corner(int index)
        {
            return new Vector3((index & 1) == 0 ? -1f : 1f,
                (index & 2) == 0 ? -1f : 1f,
                (index & 4) == 0 ? -1f : 1f);
        }

        static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, edge2);
            float det = Vector3.Dot(edge1, p);
            if (Mathf.Abs(det) < 1e-9f)
            {
                return false;
            }

            float inv = 1f / det;
            Vector3 t = ray.origin - a;
            float u = Vector3.Dot(t, p) * inv;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(ray.direction, q) * inv;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            distance = Vector3.Dot(edge2, q) * inv;
            return distance > 1e-5f;
        }

        // ---------- 反应 ----------

        /// <summary>
        /// 刷了一下：把"世界空间的拖动量"换算成驱动骨骼父空间里的一个旋转，
        /// 公式照抄模组（flow × (切线 - 0.25×长度×法线)，幅度 min(0.48, 0.08+长度×4)）。
        /// </summary>
        public void Brush(MaidGroomPart part, MaidGroomHit hit, Vector3 worldDelta)
        {
            if (part == null || hit == null || part.Driver == null)
            {
                return;
            }

            float length = worldDelta.magnitude;
            if (length < 0.0008f)
            {
                return;
            }

            if (part.Kind == MaidGroomKind.Ear)
            {
                // 耳朵不是"弯"，是"压下去"：x 分量当权重，模组 min(1, 0.65+长度×6)
                Vector3 press = new Vector3(Mathf.Min(1f, 0.65f + length * 6f), 0f, 0f);
                SetReaction(part, press);
                return;
            }

            Transform parent = part.Driver.parent;
            if (parent == null)
            {
                return;
            }

            Vector3 flow = parent.InverseTransformDirection(hit.Surface.Flow);
            Vector3 tangent = parent.InverseTransformDirection(worldDelta);
            Vector3 normal = parent.InverseTransformDirection(hit.Normal);
            Vector3 bend = flow.normalized;
            Vector3 side = tangent + normal * (-length * 0.25f);
            if (side.sqrMagnitude < 1e-10f)
            {
                return;
            }

            // 左手里是左手坐标系，叉积方向可能和模组（右手系）相反，反了就把这里取负
            Vector3 local = Vector3.Cross(bend, side).normalized
                * Mathf.Min(0.48f, 0.08f + length * 4f);
            if (local.sqrMagnitude < 1e-10f)
            {
                return;
            }

            SetReaction(part, local);
        }

        /// <summary>尾巴被刷到时邻居也带一点，看着像整条尾巴在让（模组是 0.22 倍）</summary>
        public void BrushTailNeighbours(MaidGroomPart part, MaidGroomHit hit, Vector3 worldDelta)
        {
            Brush(part, hit, worldDelta);
            if (part == null || part.Kind != MaidGroomKind.Tail || part.Tail == null)
            {
                return;
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                MaidGroomPart other = _parts[i];
                if (other.Kind != MaidGroomKind.Tail || other.Group != part.Group || other == part)
                {
                    continue;
                }

                if (Mathf.Abs(other.Segment - part.Segment) != 1)
                {
                    continue;
                }

                MaidGroomHit neighbour = new MaidGroomHit();
                neighbour.Surface = hit.Surface;
                neighbour.Point = hit.Point;
                neighbour.Normal = hit.Normal;
                neighbour.Distance = hit.Distance;
                Brush(other, neighbour, worldDelta * 0.22f);
            }
        }

        void SetReaction(MaidGroomPart part, Vector3 value)
        {
            Reaction reaction;
            if (!_reactions.TryGetValue(part.Id, out reaction))
            {
                reaction = new Reaction();
                reaction.Part = part;
                reaction.Bone = part.Driver;
                _reactions.Add(part.Id, reaction);
                _reactionOrder.Add(reaction);
            }

            reaction.Target = value;
            reaction.QuietSteps = 0;
        }

        /// <summary>手指刚按到耳朵上：先压一点点，不等拖动就有反应</summary>
        public void PressEar(MaidGroomPart part, float weight)
        {
            if (part == null || part.Kind != MaidGroomKind.Ear)
            {
                return;
            }

            SetReaction(part, new Vector3(Mathf.Clamp01(weight), 0f, 0f));
        }

        /// <summary>20Hz 推弹簧（模组在客户端 tick 里跑的节奏）</summary>
        public void Step(float seconds)
        {
            _stepAccumulator += seconds;
            int guard = 0;
            while (_stepAccumulator >= MaidTailChain.StepSeconds && guard++ < 8)
            {
                _stepAccumulator -= MaidTailChain.StepSeconds;
                for (int i = 0; i < _reactionOrder.Count; i++)
                {
                    Reaction reaction = _reactionOrder[i];
                    reaction.Previous = reaction.Current;
                    reaction.Current = Vector3.Lerp(reaction.Current, reaction.Target, ResponseLerp);
                    reaction.QuietSteps++;
                    if (reaction.QuietSteps > QuietStepsBeforeDecay)
                    {
                        reaction.Target *= ResponseDecay;
                    }
                }
            }
        }

        /// <summary>把反应叠到骨骼上。alpha 是这一步的插值，免得只有 20fps</summary>
        public void Apply(float alpha)
        {
            for (int i = 0; i < _reactionOrder.Count; i++)
            {
                Reaction reaction = _reactionOrder[i];
                MaidGroomPart part = reaction.Part;
                Transform bone = reaction.Bone;
                if (bone == null || part == null)
                {
                    continue;
                }

                // 这条链正被拖动 / 固定 / 还没弹回原位（弹簧链在写），反应主动让位
                if (part.Tail != null && (BusyGroups.Contains(part.Group) || part.Tail.IsBusy))
                {
                    continue;
                }

                Vector3 value = Vector3.Lerp(reaction.Previous, reaction.Current, alpha);
                if (part.Kind == MaidGroomKind.Ear)
                {
                    float weight = Mathf.Clamp01(value.x);
                    if (weight > 0.001f)
                    {
                        ApplyEarPress(reaction, weight);
                    }

                    continue;
                }

                float angle = value.magnitude;
                if (angle < 1e-4f)
                {
                    continue;
                }

                float max = part.IsHeadDriven ? HeadReactionMax : BendReactionMax;
                if (angle > max)
                {
                    value = value * (max / angle);
                }

                Quaternion delta = Quaternion.AngleAxis(value.magnitude * Mathf.Rad2Deg, value.normalized);
                ApplyWorldDelta(bone, delta, reaction);
            }
        }

        /// <summary>
        /// 压耳：算"耳尖 → 往头里压"要用的那根轴和角度（模组 earTarget 的做法），
        /// 角度按权重打折扣，最多 55°。
        /// </summary>
        void ApplyEarPress(Reaction reaction, float weight)
        {
            MaidGroomPart part = reaction.Part;
            Transform bone = reaction.Bone;
            Transform parent = bone.parent;
            if (parent == null || part.Tip == null || _head == null)
            {
                return;
            }

            Vector3 tipWorld;
            if (!TryEarTip(part, out tipWorld))
            {
                return;
            }

            Vector3 from = parent.InverseTransformDirection(tipWorld - bone.position);
            Vector3 toward = parent.InverseTransformDirection(_head.position - bone.position);
            if (from.sqrMagnitude < 1e-10f || toward.sqrMagnitude < 1e-10f)
            {
                return;
            }

            from.Normalize();
            toward.Normalize();
            Vector3 axis = Vector3.Cross(from, toward);
            if (axis.sqrMagnitude < 1e-10f)
            {
                return;
            }

            // 左手坐标系下叉积方向可能和模组（右手系）相反：压耳方向反了就把 axis 取负
            float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(from, toward), -1f, 1f)) * Mathf.Rad2Deg;
            angle = Mathf.Min(angle, EarPressMaxDegrees) * Mathf.Clamp01(weight);
            if (angle < 0.05f)
            {
                return;
            }

            Quaternion delta = Quaternion.AngleAxis(angle, axis.normalized);
            ApplyWorldDelta(bone, delta, reaction);
        }

        /// <summary>离耳根最远的那个耳朵顶点（模组也是这么找耳尖的；耳朵只有一个骨骼时也能用）</summary>
        bool TryEarTip(MaidGroomPart part, out Vector3 tip)
        {
            tip = Vector3.zero;
            Transform bone = part.Driver;
            if (bone == null)
            {
                return false;
            }

            float best = 1e-6f;
            bool found = false;
            for (int i = 0; i < _surfaces.Count; i++)
            {
                MaidGroomSurface surface = _surfaces[i];
                if (surface.Part != part || surface.Vertices == null || surface.Bone == null)
                {
                    continue;
                }

                Matrix4x4 toWorld = surface.Bone.localToWorldMatrix;
                for (int v = 0; v < surface.Vertices.Length; v++)
                {
                    Vector3 world = toWorld.MultiplyPoint3x4(surface.Vertices[v]);
                    float distance = (world - bone.position).sqrMagnitude;
                    if (distance > best)
                    {
                        best = distance;
                        tip = world;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 叠旋转到骨骼上。动画每帧都会重写 localRotation，所以要用"上次自己写的值"判断
        /// 这一帧动画有没有写过，免得把动画姿势吃掉（和摸脸、摸尾巴一个套路）。
        /// </summary>
        static void ApplyWorldDelta(Transform bone, Quaternion delta, Reaction reaction)
        {
            Quaternion current = bone.localRotation;
            Quaternion basis = reaction.HasAnimated && Quaternion.Dot(current, reaction.Written) > 0.999999f
                ? reaction.Animated
                : current;
            reaction.Animated = basis;
            reaction.HasAnimated = true;
            reaction.Written = delta * basis;
            bone.localRotation = reaction.Written;
        }

        /// <summary>退出：把写过的骨骼还回动画姿势（方块模型不会每帧覆盖，不还原会留着）</summary>
        public void Clear()
        {
            for (int i = 0; i < _reactionOrder.Count; i++)
            {
                Reaction reaction = _reactionOrder[i];
                // 只在"这帧还是我们写的那份"时才还原：不这样的话，刚被尾巴弹簧链写过的骨骼
                // 会被记下来的旧姿势盖回去（退出梳毛时尾巴会跳一下）
                if (reaction.HasAnimated && reaction.Bone != null
                    && Quaternion.Dot(reaction.Bone.localRotation, reaction.Written) > 0.999999f)
                {
                    reaction.Bone.localRotation = reaction.Animated;
                }
            }

            _reactionOrder.Clear();
            _reactions.Clear();
            _stepAccumulator = 0f;
        }

        // ---------- 毛流进度（顺逆毛判定） ----------

        /// <summary>命中点在毛流上的进度（0 = 根，1 = 尖）；算不出来返回 -1</summary>
        public float Progress(MaidGroomHit hit)
        {
            if (hit == null || hit.Surface == null || hit.Part == null)
            {
                return -1f;
            }

            MaidGroomSurface surface = hit.Surface;
            if (!surface.GrainEligible)
            {
                return -1f;
            }

            // 起点和长度都是 RefreshFlow 按几何算好的（尾巴从链根、头发从这块网格离头最近的那一角），
            // 方向一律"根 -> 尖"，所以顺着毛刷 = 数值变大 = WithGrain
            float length = Mathf.Max(0.02f, surface.GrainRange);
            float value = Vector3.Dot(hit.Point - surface.GrainOrigin, surface.Flow) / length;
            return Mathf.Clamp01(value);
        }

        // ---------- 工具 ----------

        static float ChainLength(Transform[] bones)
        {
            float length = 0f;
            for (int i = 1; i < bones.Length; i++)
            {
                if (bones[i] != null && bones[i - 1] != null)
                {
                    length += Vector3.Distance(bones[i].position, bones[i - 1].position);
                }
            }

            return Mathf.Max(0.02f, length);
        }

        static float StrandLength(Transform bone)
        {
            float length = 0f;
            Transform node = bone;
            int guard = 0;
            while (node != null && guard++ < 16)
            {
                Transform next = null;
                for (int i = 0; i < node.childCount; i++)
                {
                    Transform child = node.GetChild(i);
                    if (IsHair(child.name))
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                {
                    break;
                }

                length += Vector3.Distance(next.position, node.position);
                node = next;
            }

            return Mathf.Max(0.02f, length);
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

        static bool IsHair(string name)
        {
            string n = Normalize(name);
            if (n.Length == 0)
            {
                return false;
            }

            return n.Contains("hair") || n.Contains("bang") || n.Contains("fringe")
                || n.Contains("ponytail") || n.Contains("ahoge");
        }

        static bool IsUnder(Transform node, Transform ancestor)
        {
            if (ancestor == null)
            {
                return true;
            }

            Transform current = node;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
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
