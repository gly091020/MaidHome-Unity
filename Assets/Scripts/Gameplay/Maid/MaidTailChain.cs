using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 一条尾巴的骨骼链 + 弹簧模拟，参数照抄 moreanimation 的 TailInteractionState。
    /// 骨骼识别照抄 TailGroups 再放宽一点：名字带 tail（foxTail / Tail_1 都算，但排掉
    /// ponytail / hair / coat 这些明显不是尾巴的）、shippo、尾巴，或者 wb[数字] 且祖先里有 FOX；
    /// 分叉的尾巴从分叉点起每条分支各自成一条链（多尾模型）。
    /// </summary>
    public sealed class MaidTailChain
    {
        public const int SegmentCount = 7;
        /// <summary>一条尾巴最多几节骨骼（防止链一路走下去把尾巴后面的东西也带上）</summary>
        public const int MaxChainBones = 24;
        public const float MaxYaw = 55f;
        public const float MinPitch = -40f;
        public const float MaxPitch = 50f;
        /// 原版是在客户端 tick 里跑的（20Hz），所以这里也按 1/20 秒一步走
        public const float StepSeconds = 1f / 20f;

        const float MaxAngularVelocityPerStep = 9f;

        static readonly float[] Weights = { 0.46f, 0.31f, 0.24f, 0.19f, 0.15f, 0.11f, 0.08f };
        static readonly float[] Stiffness = { 0.40f, 0.18f, 0.12f, 0.085f, 0.060f, 0.045f, 0.034f };
        static readonly float[] Damping = { 0.50f, 0.69f, 0.78f, 0.84f, 0.87f, 0.90f, 0.92f };
        static readonly float[] Transfer = { 0f, 0.46f, 0.56f, 0.64f, 0.71f, 0.77f, 0.82f };

        readonly Transform[] _bones;
        readonly int[] _segments;
        readonly float[] _yaw = new float[SegmentCount];
        readonly float[] _pitch = new float[SegmentCount];
        // 上一小步的姿势：弹簧是 20Hz 算的，渲染时在两步之间插值，不然尾巴只有 20fps
        readonly float[] _prevYaw = new float[SegmentCount];
        readonly float[] _prevPitch = new float[SegmentCount];
        readonly float[] _yawVelocity = new float[SegmentCount];
        readonly float[] _pitchVelocity = new float[SegmentCount];
        // 这两个按骨骼存，不能按段存：一根链可能超过 7 段，那段号会重复
        readonly Quaternion[] _animated;
        readonly Quaternion[] _written;
        readonly bool[] _hasAnimated;

        float _targetYaw;
        float _targetPitch;
        bool _holding;
        bool _frozen;

        public string Id { get; private set; }
        public Transform[] Bones { get { return _bones; } }
        /// 有没有在被拉/还没回到原位
        public bool IsBusy
        {
            get
            {
                if (_frozen || _holding)
                {
                    return true;
                }

                for (int i = 0; i < SegmentCount; i++)
                {
                    if (Mathf.Abs(_yaw[i]) > 0.01f || Mathf.Abs(_pitch[i]) > 0.01f)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        MaidTailChain(string id, List<Transform> bones)
        {
            Id = id;
            _bones = bones.ToArray();
            _segments = new int[_bones.Length];
            _animated = new Quaternion[_bones.Length];
            _written = new Quaternion[_bones.Length];
            _hasAnimated = new bool[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                // 原版是 min(链上第几根, 6)，超出的段共用最后一段权重
                _segments[i] = Mathf.Min(i, SegmentCount - 1);
            }
        }

        /// 抓住的时候起手角度：用第 0 段的当前值反推（和原版一致）
        public void SnapshotStart(out float yaw, out float pitch)
        {
            yaw = _yaw[0] / Weights[0];
            pitch = _pitch[0] / Weights[0];
        }

        /// 这条链上有没有"能看见"的几何（被隐藏的骨骼不算，例如酒狐那只小狐狸的尾巴）
        public bool HasVisibleGeometry()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                Transform bone = _bones[i];
                if (bone == null || !bone.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Renderer renderer = bone.GetComponent<Renderer>();
                if (renderer != null && renderer.enabled)
                {
                    return true;
                }
            }

            return false;
        }

        public void SetTarget(float yaw, float pitch, bool holding)
        {
            _targetYaw = Mathf.Clamp(yaw, -MaxYaw, MaxYaw);
            _targetPitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
            _holding = holding;
        }

        public void Freeze(bool frozen)
        {
            _frozen = frozen;
            if (frozen)
            {
                for (int i = 0; i < SegmentCount; i++)
                {
                    _yawVelocity[i] = 0f;
                    _pitchVelocity[i] = 0f;
                }
            }
        }

        public void ResetAll()
        {
            for (int i = 0; i < SegmentCount; i++)
            {
                _yaw[i] = 0f;
                _pitch[i] = 0f;
                _prevYaw[i] = 0f;
                _prevPitch[i] = 0f;
                _yawVelocity[i] = 0f;
                _pitchVelocity[i] = 0f;
            }

            _targetYaw = 0f;
            _targetPitch = 0f;
            _holding = false;
            _frozen = false;
        }

        /// 一步弹簧积分。每段的目标角度从前一段"带惯性传过来"，所以尾巴末梢会甩
        public void Step()
        {
            if (_frozen)
            {
                return;
            }

            for (int i = 0; i < SegmentCount; i++)
            {
                _prevYaw[i] = _yaw[i];
                _prevPitch[i] = _pitch[i];

                float weightedYaw;
                float weightedPitch;
                if (i == 0)
                {
                    weightedYaw = _targetYaw * Weights[i];
                    weightedPitch = _targetPitch * Weights[i];
                }
                else
                {
                    float ratio = Weights[i] / Weights[i - 1];
                    weightedYaw = (_yaw[i - 1] + _yawVelocity[i - 1] * Transfer[i]) * ratio;
                    weightedPitch = (_pitch[i - 1] + _pitchVelocity[i - 1] * Transfer[i]) * ratio;
                }

                float stiffness = Stiffness[i] * (_holding ? 1f : 0.58f);
                _yawVelocity[i] = (_yawVelocity[i] + (weightedYaw - _yaw[i]) * stiffness) * Damping[i];
                _pitchVelocity[i] = (_pitchVelocity[i] + (weightedPitch - _pitch[i]) * stiffness) * Damping[i];

                float velocityLimit = MaxAngularVelocityPerStep * Mathf.Max(0.35f, Weights[i] / Weights[0]);
                _yawVelocity[i] = Mathf.Clamp(_yawVelocity[i], -velocityLimit, velocityLimit);
                _pitchVelocity[i] = Mathf.Clamp(_pitchVelocity[i], -velocityLimit, velocityLimit);
                _yaw[i] += _yawVelocity[i];
                _pitch[i] += _pitchVelocity[i];

                if (!_holding && Mathf.Abs(_yaw[i]) < 0.0005f && Mathf.Abs(_pitch[i]) < 0.0005f
                    && Mathf.Abs(_yawVelocity[i]) < 0.0005f && Mathf.Abs(_pitchVelocity[i]) < 0.0005f)
                {
                    _yaw[i] = 0f;
                    _pitch[i] = 0f;
                    _yawVelocity[i] = 0f;
                    _pitchVelocity[i] = 0f;
                }
            }
        }

        /// 在动画姿势之上叠一层偏移。alpha 是"离上一小步过去多久"（0~1），用来把 20Hz 的弹簧插成平滑的。
        public void Apply(float pitchSign, float yawSign, float zFollow, float alpha)
        {
            // 冻住的时候 Step 不再走积分，_prev 会停在"冻住前那一刻"，而 _yaw 是当时的值；
            // 这时候还按 alpha 插值，尾巴就会在两步之间来回跳（看起来是两帧抽搐一下）。
            // 固定/吸一口期间必须直接用当前值。
            alpha = _frozen ? 1f : Mathf.Clamp01(alpha);
            for (int i = 0; i < _bones.Length; i++)
            {
                Transform bone = _bones[i];
                if (bone == null)
                {
                    continue;
                }

                int segment = _segments[i];
                float yaw = Mathf.Lerp(_prevYaw[segment], _yaw[segment], alpha);
                float pitch = Mathf.Lerp(_prevPitch[segment], _pitch[segment], alpha);
                Quaternion delta = Quaternion.Euler(
                    -pitch * pitchSign,
                    -yaw * yawSign,
                    -yaw * zFollow * yawSign);

                Quaternion current = bone.localRotation;
                Quaternion animated;
                if (_hasAnimated[i] && Mathf.Abs(Quaternion.Dot(current, _written[i])) > 0.999999f)
                {
                    // 动画这一帧没写（比如停在某个姿势），沿用上次记下来的动画姿势
                    animated = _animated[i];
                }
                else
                {
                    animated = current;
                }

                _animated[i] = animated;
                _hasAnimated[i] = true;
                _written[i] = animated * delta;
                bone.localRotation = _written[i];
            }
        }

        public static List<MaidTailChain> Build(Transform root)
        {
            List<MaidTailChain> result = new List<MaidTailChain>();
            if (root == null)
            {
                return result;
            }

            List<Transform> all = new List<Transform>();
            Collect(root, all);

            // 全部子骨骼（不分名字）：尾巴中段常常挂在 bone56 这种匿名骨骼上，
            // 只跟着"名字像尾巴"的子骨骼走的话，链会在第一节就断掉，真正那几块尾巴网格就不受控了
            Dictionary<Transform, List<Transform>> allChildren = new Dictionary<Transform, List<Transform>>();
            for (int i = 0; i < all.Count; i++)
            {
                // 根节点的 parent 是 null（女仆模型根可能直接摆在场景里），
                // Dictionary 不接受 null 键，跳过它——它本来也不会是谁的子节点
                if (all[i].parent == null)
                {
                    continue;
                }

                List<Transform> list;
                if (!allChildren.TryGetValue(all[i].parent, out list))
                {
                    list = new List<Transform>();
                    allChildren.Add(all[i].parent, list);
                }

                list.Add(all[i]);
            }

            // 父节点 -> 属于尾巴的子节点
            Dictionary<Transform, List<Transform>> children = new Dictionary<Transform, List<Transform>>();
            for (int i = 0; i < all.Count; i++)
            {
                Transform node = all[i];
                if (!IsTail(node))
                {
                    continue;
                }

                if (node.parent == null)
                {
                    continue;
                }

                List<Transform> list;
                if (!children.TryGetValue(node.parent, out list))
                {
                    list = new List<Transform>();
                    children.Add(node.parent, list);
                }

                list.Add(node);
            }

            for (int i = 0; i < all.Count; i++)
            {
                Transform node = all[i];
                if (!IsTail(node) || Count(children, node.parent) == 1 && IsTail(node.parent))
                {
                    continue;
                }

                // 分叉的尾巴（多尾/毛束分成几股）不要整条丢掉：从分叉点往下每条分支各自成一条链，
                // 分叉点自己会因为"NoGeometry"被筛掉
                List<Transform> chain = new List<Transform>();
                Transform next = node;
                while (next != null && !chain.Contains(next) && chain.Count < MaxChainBones)
                {
                    chain.Add(next);
                    next = Single(allChildren, next);
                }

                if (!HasGeometry(chain))
                {
                    continue;
                }

                result.Add(new MaidTailChain(node.name, chain));
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        static void Collect(Transform node, List<Transform> into)
        {
            into.Add(node);
            for (int i = 0; i < node.childCount; i++)
            {
                Collect(node.GetChild(i), into);
            }
        }

        static bool IsTail(Transform node)
        {
            if (node == null)
            {
                return false;
            }

            string name = node.name.ToLowerInvariant();
            if (IsNumbered(name, "tail") || IsNumbered(name, "body_tail"))
            {
                return true;
            }

            string flat = name.Replace("_", "").Replace("-", "").Replace(".", "").Replace(" ", "");
            // 简单模型（SimpleBedrockModel）的骨骼名是模型作者随手起的：foxTail / Tail_1 / shippo 都可能，
            // 只要名字里带 tail 就算，但排掉"头发/衣服"这些明显不是尾巴的
            if (flat.Contains("tail") && !ContainsAny(flat, NotTailWords))
            {
                return true;
            }

            if (flat.Contains("shippo") || name.Contains("尾巴"))
            {
                return true;
            }

            if (!IsNumbered(flat, "wb"))
            {
                return false;
            }

            // wb 只有挂在 FOX 底下才算尾巴（那是酒狐本体的尾巴）
            Transform parent = node.parent;
            int guard = 0;
            while (parent != null && guard++ < 64)
            {
                if (string.Equals(parent.name, "FOX", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                parent = parent.parent;
            }

            return false;
        }

        /// <summary>名字里带 tail 但不是尾巴的词（马尾、外套、各种"细节"）</summary>
        static readonly string[] NotTailWords = { "ponytail", "hair", "detail", "retail", "tailor", "coat", "skirt", "cloth", "dress", "shirt", "tailwrap" };

        static bool ContainsAny(string text, string[] words)
        {
            for (int i = 0; i < words.Length; i++)
            {
                if (text.Contains(words[i]))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsNumbered(string name, string prefix)
        {
            if (!name.StartsWith(prefix))
            {
                return false;
            }

            for (int i = prefix.Length; i < name.Length; i++)
            {
                if (name[i] < '0' || name[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        static bool HasGeometry(List<Transform> chain)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i].GetComponent<MeshRenderer>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        static int Count(Dictionary<Transform, List<Transform>> map, Transform key)
        {
            List<Transform> list;
            return key != null && map.TryGetValue(key, out list) ? list.Count : 0;
        }

        static Transform Single(Dictionary<Transform, List<Transform>> map, Transform key)
        {
            List<Transform> list;
            if (key == null || !map.TryGetValue(key, out list) || list.Count != 1)
            {
                return null;
            }

            return list[0];
        }
    }
}
