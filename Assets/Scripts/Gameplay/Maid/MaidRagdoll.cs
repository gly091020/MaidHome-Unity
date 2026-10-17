using System;
using System.Collections.Generic;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆布娃娃（只保留网格方案）。设计尽量简单：
    /// * **进 Play 自动生成 + 自动接管**（默认开），不用先点编辑器窗口里的按钮；
    /// * 生成出来的刚体/碰撞体/关节只在运行时存在，`Clear()` 会先把身上自制的物理组件扫干净再重建；
    /// * 识别到的部位（头 / 身体 / 手脚 / 裙子 / 尾巴）把子树里的网格合成一块，
    ///   没识别到的网格各自一块（见 <see cref="MaidRagdollPlanner"/>）；
    /// * 自碰撞默认全关；角度限制默认开着，关节用 10°/15° 的软限制。
    ///
    /// 玩法层只需要 Enter / Exit：Enter 关掉动画和角色控制器、让物理接管；
    /// Exit 把骨骼姿势写回去、组件恢复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidRagdoll : MonoBehaviour
    {
        [Header("自动跑")]
        [Tooltip("进 Play 自动生成布娃娃（不勾就得手动调 Rebuild）")]
        [SerializeField] private bool _buildOnStart = true;
        [Tooltip("生成完立刻让物理接管（不勾就是先建好、站着，等别的代码调 Enter）")]
        [SerializeField] private bool _enterOnStart = true;

        [Header("刚体")]
        [Tooltip("每立方米质量（格缩到米以后）。女仆整个人大概 15~25 kg")]
        [SerializeField] private float _massDensity = 40f;
        [SerializeField] private float _minMass = 0.3f;
        [SerializeField] private float _maxMass = 20f;
        [SerializeField] private float _drag = 0.5f;
        [Tooltip("Rigidbody 的全局角阻尼。肢体抖得厉害就往上调")]
        [SerializeField] private float _angularDrag = 5f;

        [Header("碰撞体")]
        [SerializeField] private float _friction = 0.6f;
        [SerializeField] private float _bounce = 0f;

        [Header("自碰撞 / 角度限制")]
        [Tooltip("全关互相碰撞：所有物理块两两都不撞（头发穿身体、胳膊穿裙子都不会互相顶）")]
        [SerializeField] private bool _ignoreAllSelfCollision = true;
        [Tooltip("全关角度限制：关节自由转（只剩位置约束，等于球关节）")]
        [SerializeField] private bool _noAngleLimits;
        [Tooltip("上面两个都关掉时才用这套：重叠的块之间取消碰撞、关节收紧")]
        [SerializeField] private bool _handleOverlappingParts;
        [SerializeField] private float _overlapTwist = 10f;
        [SerializeField] private float _overlapSwing = 15f;
        [Tooltip("接头/四肢的常规角度（度），只在没全关角度限制时用")]
        [SerializeField] private float _jointTwist = 10f;
        [SerializeField] private float _jointSwing = 15f;
        [Header("关节阻尼")]
        [Tooltip("关节到达角度限制时的软弹簧刚度。0 = 硬限制")]
        [SerializeField] private float _limitSpring = 50f;
        [Tooltip("关节到达角度限制时的阻尼，越大越不容易在极限处抖")]
        [SerializeField] private float _limitDamper = 5f;
        [Tooltip("关节被拉开多少米以后开始做投影修正，别设 0")]
        [SerializeField] private float _projectionDistance = 0.02f;
        [SerializeField] private float _projectionAngle = 8f;

        [Header("接管")]
        [Tooltip("进布娃娃时临时关掉动画 / 角色控制器 / 游走，退出时按原样恢复")]
        [SerializeField] private bool _pauseBehaviours = true;

        /// <summary>CharacterJoint 的角度软上限（177° 就等于放开限制）。</summary>
        const float FreeAngle = 177f;

        /// <summary>进布娃娃要临时关掉的组件类型。</summary>
        static readonly Type[] PausedTypes =
        {
            typeof(CharacterController),
            typeof(Animation),
            typeof(MaidWanderer),
            typeof(BedrockAnimationPlayer),
            typeof(MaidSimpleBedrockAnimator),
            typeof(MaidIdleBubble),
        };

        sealed class Rig
        {
            public Transform Bone;
            public Rigidbody Body;
            public readonly List<Collider> Colliders = new List<Collider>();
            public CharacterJoint Joint;
            public bool Overlapping;
            public Vector3 JointAnchor;
            public bool UseJointAnchor;
            public Vector3 RestPosition;
            public Quaternion RestRotation;
        }

        readonly List<Rig> _rigs = new List<Rig>();
        readonly List<Component> _paused = new List<Component>();
        readonly List<bool> _pausedWasEnabled = new List<bool>();
        PhysicMaterial _material;
        bool _sensorForces;
        Vector3 _sensorGravity;
        Vector3 _sensorAcceleration;

        public MaidRagdollPlan Plan { get; private set; }
        public bool IsActive { get; private set; }
        public int PartCount { get { return _rigs.Count; } }
        public int OverlapPairCount { get; private set; }
        public bool SelfCollisionDisabled { get { return _ignoreAllSelfCollision; } }
        public bool AngleLimitsDisabled { get { return _noAngleLimits; } }
        /// <summary>布娃娃刚体撞到东西时的冲击速度（米/秒）。给「玩全身」做闪红判定用。</summary>
        public event Action<float> ImpactDetected;

        /// <summary>
        /// 把 Inspector 里的参数恢复成「玩全身」这套预设。
        /// 旧组件已经序列化过旧的 No Angle Limits / Joint Twist / Swing，改脚本默认值不会自动覆盖它们；
        /// 这里给右键菜单和运行时接管各留一个统一入口。
        /// </summary>
        [ContextMenu("恢复默认配置")]
        public void ApplyDefaultPreset()
        {
            _buildOnStart = true;
            _enterOnStart = true;
            _massDensity = 40f;
            _minMass = 0.3f;
            _maxMass = 20f;
            _drag = 0.5f;
            _angularDrag = 1.5f;
            _friction = 0.6f;
            _bounce = 0f;
            _ignoreAllSelfCollision = true;
            _noAngleLimits = false;
            _handleOverlappingParts = false;
            _overlapTwist = 10f;
            _overlapSwing = 15f;
            _jointTwist = 10f;
            _jointSwing = 15f;
            _limitSpring = 50f;
            _limitDamper = 5f;
            _projectionDistance = 0.02f;
            _projectionAngle = 8f;
            _pauseBehaviours = true;
        }

        void Reset()
        {
            ApplyDefaultPreset();
        }

        /// <summary>
        /// 「玩全身」用：关掉自动跑，按交互需要的设置重建好，等调用方自己 Enter。
        /// 直接 AddComponent 的话 Start 还没来得及跑，这里先把开关写死，避免下一帧又自动接管一次。
        /// </summary>
        public MaidRagdollPlan PrepareForInteraction(bool ignoreAllSelfCollision, bool noAngleLimits)
        {
            ApplyDefaultPreset();
            _buildOnStart = false;
            _enterOnStart = false;
            _ignoreAllSelfCollision = ignoreAllSelfCollision;
            _noAngleLimits = noAngleLimits;
            return Rebuild();
        }

        void Start()
        {
            if (!Application.isPlaying || !_buildOnStart)
            {
                return;
            }

            Rebuild();
            if (_enterOnStart)
            {
                Enter();
            }
        }

        /// <summary>重新识别 + 重建刚体结构。已经进布娃娃的话先退出。</summary>
        public MaidRagdollPlan Rebuild()
        {
            Exit();
            Clear();
            Plan = MaidRagdollPlanner.Plan(transform);
            BuildRigs(Plan);
            return Plan;
        }

        /// <summary>让物理接管：关掉动画那套，刚体转成非运动学。</summary>
        public void Enter()
        {
            if (IsActive)
            {
                return;
            }

            if (_rigs.Count == 0)
            {
                Rebuild();
            }

            if (_rigs.Count == 0)
            {
                Debug.LogWarning("没认出任何部位，进不了布娃娃：" + name, this);
                return;
            }

            RememberPose();
            if (_pauseBehaviours)
            {
                PauseBehaviours();
            }

            Physics.SyncTransforms();
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig rig = _rigs[i];
                if (rig.Body == null)
                {
                    continue;
                }

                rig.Body.isKinematic = false;
                rig.Body.useGravity = true;
                rig.Body.WakeUp();
            }

            IsActive = true;
        }

        /// <summary>退出布娃娃：刚体冻结、骨骼姿势写回、组件恢复。</summary>
        public void Exit()
        {
            if (!IsActive)
            {
                return;
            }

            _sensorForces = false;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig rig = _rigs[i];
                if (rig.Body == null || rig.Bone == null)
                {
                    continue;
                }

                rig.Body.isKinematic = true;
                rig.Body.useGravity = false;
                rig.Bone.localPosition = rig.RestPosition;
                rig.Bone.localRotation = rig.RestRotation;
            }

            ResumeBehaviours();
            IsActive = false;
        }

        /// <summary>
        /// 体感模式：关掉 Rigidbody 自带重力，改由手机传感器给每个物理块施加重力和线性加速度。
        /// </summary>
        public void SetSensorForces(bool enabled, Vector3 gravity, Vector3 acceleration)
        {
            _sensorForces = enabled;
            _sensorGravity = gravity;
            _sensorAcceleration = acceleration;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rigidbody body = _rigs[i].Body;
                if (body != null)
                {
                    body.useGravity = !enabled;
                    if (enabled)
                    {
                        body.WakeUp();
                    }
                }
            }
        }

        void FixedUpdate()
        {
            if (!IsActive || !_sensorForces)
            {
                return;
            }

            Vector3 force = _sensorGravity + _sensorAcceleration;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rigidbody body = _rigs[i].Body;
                if (body == null || body.isKinematic)
                {
                    continue;
                }

                body.WakeUp();
                body.AddForce(force * body.mass, ForceMode.Force);
            }
        }

        /// <summary>把刚体/关节/碰撞体全拆掉（不会动姿势；退出前先 Exit）。</summary>
        public void Clear()
        {
            _rigs.Clear();
            _sensorForces = false;
            // 编辑模式里建过、进 Play 时运行时列表却是空的：按"扫身上所有自制的物理组件"兜底
            Transform[] all = transform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject target = all[i].gameObject;
                Joint[] joints = target.GetComponents<Joint>();
                for (int j = 0; j < joints.Length; j++)
                {
                    DestroyNow(joints[j]);
                }

                Rigidbody[] bodies = target.GetComponents<Rigidbody>();
                for (int j = 0; j < bodies.Length; j++)
                {
                    DestroyNow(bodies[j]);
                }

                Collider[] colliders = target.GetComponents<Collider>();
                for (int j = 0; j < colliders.Length; j++)
                {
                    // 根上的 CharacterController 是女仆自己的，不能拆
                    if (colliders[j] is CharacterController)
                    {
                        continue;
                    }

                    DestroyNow(colliders[j]);
                }

                MaidRagdollImpactRelay[] relays = target.GetComponents<MaidRagdollImpactRelay>();
                for (int j = 0; j < relays.Length; j++)
                {
                    DestroyNow(relays[j]);
                }
            }

            if (_material != null)
            {
                DestroyNow(_material);
                _material = null;
            }
        }

        void OnDisable()
        {
            Exit();
        }

        // ------------------------------------------------------------ 建造

        void BuildRigs(MaidRagdollPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            for (int i = 0; i < plan.Parts.Count; i++)
            {
                MaidRagdollPart part = plan.Parts[i];
                if (part.Bone == null)
                {
                    continue;
                }

                Rig rig = new Rig();
                rig.Bone = part.Bone;
                rig.RestPosition = part.Bone.localPosition;
                rig.RestRotation = part.Bone.localRotation;

                rig.Body = part.Bone.gameObject.AddComponent<Rigidbody>();
                rig.Body.mass = Mass(part);
                rig.Body.drag = _drag;
                rig.Body.angularDrag = _angularDrag;
                rig.Body.interpolation = RigidbodyInterpolation.Interpolate;
                rig.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rig.Body.isKinematic = true;
                rig.Body.useGravity = false;

                MaidRagdollImpactRelay relay = part.Bone.gameObject.AddComponent<MaidRagdollImpactRelay>();
                relay.Owner = this;

                // 碰撞体挂在各块网格自己的骨骼上：骨骼没有刚体，就自动属于上面那根刚体，
                // 所以"头 + 脸 + 头发"是一根刚体带好几个凸网格碰撞体
                for (int c = 0; c < part.Colliders.Count; c++)
                {
                    AddCollider(part.Colliders[c], rig);
                }

                _rigs.Add(rig);
            }

            OverlapPairCount = MarkOverlaps(plan);

            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig rig = _rigs[i];
                MaidRagdollPart part = FindPart(plan, rig.Bone);
                if (part == null || part.Parent < 0)
                {
                    continue;
                }

                Rig parent = FindRig(plan, part.Parent);
                if (parent == null)
                {
                    continue;
                }

                // 父子真的相交时，关节锚点放在相交区域的中心；有多组碰撞体相交时，
                // 选离子骨骼最近的那组，避免头/头发这种多碰撞体部位挑到偏的一块。
                Vector3 anchor;
                if (MaidRagdollOverlap.OverlapsNearest(rig.Colliders, parent.Colliders,
                        rig.Bone.position, out anchor))
                {
                    rig.JointAnchor = anchor;
                    rig.UseJointAnchor = true;
                }

                rig.Joint = BuildJoint(rig, parent, part);
                for (int a = 0; a < rig.Colliders.Count; a++)
                {
                    for (int b = 0; b < parent.Colliders.Count; b++)
                    {
                        Physics.IgnoreCollision(rig.Colliders[a], parent.Colliders[b], true);
                    }
                }
            }

            if (_ignoreAllSelfCollision)
            {
                IgnoreAllSelfCollision();
            }

            plan.Warnings.Add("自碰撞：" + (_ignoreAllSelfCollision ? "全关" : "只关相邻/重叠的")
                + "；角度限制：" + (_noAngleLimits ? "全关（关节自由）" : "开着")
                + "；重叠 " + OverlapPairCount + " 对");
        }

        internal void ReportImpact(Collision collision)
        {
            if (!IsActive || ImpactDetected == null)
            {
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            float mass = body != null ? Mathf.Max(0.05f, body.mass) : 1f;
            // relativeVelocity 对擦碰也会很大，冲量除以质量更接近"这一下有多重"
            float impulseSpeed = collision.impulse.magnitude / mass;
            float speed = Mathf.Max(collision.relativeVelocity.magnitude, impulseSpeed);
            if (ImpactDetected != null)
            {
                ImpactDetected(speed);
            }
        }

        void AddCollider(MaidRagdollCollider entry, Rig rig)
        {
            if (entry.Bone == null)
            {
                return;
            }

            if (entry.IsMesh)
            {
                // 动起来的刚体只能用凸网格碰撞体
                MeshCollider meshCollider = entry.Bone.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = entry.Mesh;
                meshCollider.convex = true;
                meshCollider.sharedMaterial = Material();
                rig.Colliders.Add(meshCollider);
                return;
            }

            BoxCollider box = entry.Bone.gameObject.AddComponent<BoxCollider>();
            box.center = entry.Center;
            box.size = entry.Size;
            box.sharedMaterial = Material();
            rig.Colliders.Add(box);
        }

        /// <summary>所有物理块两两不碰撞。</summary>
        void IgnoreAllSelfCollision()
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                for (int j = i + 1; j < _rigs.Count; j++)
                {
                    List<Collider> first = _rigs[i].Colliders;
                    List<Collider> second = _rigs[j].Colliders;
                    for (int a = 0; a < first.Count; a++)
                    {
                        if (first[a] == null)
                        {
                            continue;
                        }

                        for (int b = 0; b < second.Count; b++)
                        {
                            if (second[b] != null)
                            {
                                Physics.IgnoreCollision(first[a], second[b], true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 重叠的物理块对：取消碰撞（不取消的话几何互插会把它们顶飞）；没全关角度限制时还会收紧角度。
        /// 判定用真实碰撞体（MaidRagdollOverlap，不是外接长方体）。
        /// </summary>
        int MarkOverlaps(MaidRagdollPlan plan)
        {
            if (!_handleOverlappingParts)
            {
                return 0;
            }

            List<List<Collider>> colliders = new List<List<Collider>>();
            for (int i = 0; i < plan.Parts.Count; i++)
            {
                Rig rig = FindRig(plan, plan.Parts[i].Index);
                colliders.Add(rig != null ? rig.Colliders : new List<Collider>());
            }

            List<KeyValuePair<int, int>> overlaps = MaidRagdollOverlap.Find(colliders);
            for (int i = 0; i < overlaps.Count; i++)
            {
                int a = plan.Parts[overlaps[i].Key].Index;
                int b = plan.Parts[overlaps[i].Value].Index;
                Rig first = FindRig(plan, a);
                Rig second = FindRig(plan, b);
                if (first == null || second == null)
                {
                    continue;
                }

                for (int x = 0; x < first.Colliders.Count; x++)
                {
                    for (int y = 0; y < second.Colliders.Count; y++)
                    {
                        Physics.IgnoreCollision(first.Colliders[x], second.Colliders[y], true);
                    }
                }

                if (plan.At(a) != null && plan.At(a).Parent == b)
                {
                    first.Overlapping = true;
                }
                else if (plan.At(b) != null && plan.At(b).Parent == a)
                {
                    second.Overlapping = true;
                }
            }

            return overlaps.Count;
        }

        CharacterJoint BuildJoint(Rig rig, Rig parent, MaidRagdollPart part)
        {
            CharacterJoint joint = rig.Bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent.Body;
            Vector3 pivot = rig.UseJointAnchor ? rig.JointAnchor : rig.Bone.position;
            joint.anchor = rig.Bone.InverseTransformPoint(pivot);
            joint.connectedAnchor = parent.Bone.InverseTransformPoint(pivot);
            joint.axis = TwistAxis(rig);
            joint.swingAxis = SwingAxis(joint.axis);
            joint.enableProjection = true;
            joint.projectionDistance = _projectionDistance;
            joint.projectionAngle = _projectionAngle;

            float twist;
            float swing;
            if (_noAngleLimits)
            {
                twist = FreeAngle;
                swing = FreeAngle;
            }
            else if (rig.Overlapping)
            {
                twist = _overlapTwist;
                swing = _overlapSwing;
            }
            else
            {
                twist = _jointTwist;
                swing = _jointSwing;
            }

            joint.lowTwistLimit = Limit(-twist);
            joint.highTwistLimit = Limit(twist);
            joint.swing1Limit = Limit(swing);
            joint.swing2Limit = Limit(swing * 0.5f);

            SoftJointLimitSpring limitSpring = new SoftJointLimitSpring();
            limitSpring.spring = Mathf.Max(0f, _limitSpring);
            limitSpring.damper = Mathf.Max(0f, _limitDamper);
            joint.twistLimitSpring = limitSpring;
            joint.swingLimitSpring = limitSpring;
            return joint;
        }

        /// <summary>
        /// 扭转轴取"骨骼轴 → 第一块碰撞体中心"的主要方向：四肢沿长度方向扭、摆动绕着它转。
        /// 没有偏移（比如头）就退回骨骼的局部下方。
        /// </summary>
        static Vector3 TwistAxis(Rig rig)
        {
            Vector3 direction = Vector3.zero;
            for (int i = 0; i < rig.Colliders.Count; i++)
            {
                BoxCollider box = rig.Colliders[i] as BoxCollider;
                if (box != null)
                {
                    direction = box.center;
                    break;
                }

                MeshCollider mesh = rig.Colliders[i] as MeshCollider;
                if (mesh != null)
                {
                    direction = rig.Bone.InverseTransformPoint(mesh.bounds.center);
                    if (direction.sqrMagnitude > 1e-6f)
                    {
                        break;
                    }
                }
            }

            if (direction.sqrMagnitude < 1e-6f)
            {
                return Vector3.down;
            }

            Vector3 abs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (abs.y >= abs.x && abs.y >= abs.z)
            {
                return new Vector3(0f, direction.y >= 0f ? 1f : -1f, 0f);
            }

            if (abs.x >= abs.z)
            {
                return new Vector3(direction.x >= 0f ? 1f : -1f, 0f, 0f);
            }

            return new Vector3(0f, 0f, direction.z >= 0f ? 1f : -1f);
        }

        static Vector3 SwingAxis(Vector3 twist)
        {
            Vector3 axis = Mathf.Abs(Vector3.Dot(twist, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 swing = Vector3.ProjectOnPlane(axis, twist);
            return swing.sqrMagnitude < 1e-4f ? Vector3.right : swing.normalized;
        }

        static SoftJointLimit Limit(float degrees)
        {
            SoftJointLimit limit = new SoftJointLimit();
            limit.limit = degrees;
            return limit;
        }

        float Mass(MaidRagdollPart part)
        {
            Vector3 scale = part.Bone.lossyScale;
            float volume = 0f;
            for (int i = 0; i < part.Colliders.Count; i++)
            {
                Vector3 world = Vector3.Scale(part.Colliders[i].Size, scale);
                volume += Mathf.Abs(world.x * world.y * world.z);
            }

            if (volume <= 0f)
            {
                Vector3 bounds = Vector3.Scale(part.Size, scale);
                volume = Mathf.Abs(bounds.x * bounds.y * bounds.z);
            }

            return Mathf.Clamp(volume * _massDensity, _minMass, _maxMass);
        }

        PhysicMaterial Material()
        {
            if (_material == null)
            {
                _material = new PhysicMaterial("maid_ragdoll");
                _material.dynamicFriction = _friction;
                _material.staticFriction = _friction;
                _material.bounciness = _bounce;
                _material.frictionCombine = PhysicMaterialCombine.Average;
                _material.bounceCombine = PhysicMaterialCombine.Maximum;
            }

            return _material;
        }

        static MaidRagdollPart FindPart(MaidRagdollPlan plan, Transform bone)
        {
            for (int i = 0; i < plan.Parts.Count; i++)
            {
                if (plan.Parts[i].Bone == bone)
                {
                    return plan.Parts[i];
                }
            }

            return null;
        }

        Rig FindRig(MaidRagdollPlan plan, int index)
        {
            MaidRagdollPart part = plan.At(index);
            if (part == null)
            {
                return null;
            }

            for (int i = 0; i < _rigs.Count; i++)
            {
                if (_rigs[i].Bone == part.Bone)
                {
                    return _rigs[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------ 暂停 / 恢复

        void RememberPose()
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig rig = _rigs[i];
                if (rig.Bone != null)
                {
                    rig.RestPosition = rig.Bone.localPosition;
                    rig.RestRotation = rig.Bone.localRotation;
                }
            }
        }

        void PauseBehaviours()
        {
            _paused.Clear();
            _pausedWasEnabled.Clear();
            for (int t = 0; t < PausedTypes.Length; t++)
            {
                Component[] found = GetComponentsInChildren(PausedTypes[t], true);
                for (int i = 0; i < found.Length; i++)
                {
                    _paused.Add(found[i]);
                    _pausedWasEnabled.Add(IsEnabled(found[i]));
                    SetEnabled(found[i], false);
                }
            }

        }

        void ResumeBehaviours()
        {
            for (int i = 0; i < _paused.Count; i++)
            {
                if (_paused[i] != null)
                {
                    SetEnabled(_paused[i], _pausedWasEnabled[i]);
                }
            }

            _paused.Clear();
            _pausedWasEnabled.Clear();
        }

        static bool IsEnabled(Component component)
        {
            Behaviour behaviour = component as Behaviour;
            if (behaviour != null)
            {
                return behaviour.enabled;
            }

            Collider collider = component as Collider;
            return collider == null || collider.enabled;
        }

        static void SetEnabled(Component component, bool value)
        {
            Behaviour behaviour = component as Behaviour;
            if (behaviour != null)
            {
                behaviour.enabled = value;
                return;
            }

            Collider collider = component as Collider;
            if (collider != null)
            {
                collider.enabled = value;
            }
        }

        static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            // 故意用 DestroyImmediate：Rebuild 经常在同一帧里"先拆后建"，用 Destroy 的话
            // 旧组件要等帧末才消失，AddComponent<Rigidbody> 会拿回那个待销毁的旧刚体
            // （Rigidbody 是 DisallowMultipleComponent），新布娃娃就一根刚体都没有。
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
