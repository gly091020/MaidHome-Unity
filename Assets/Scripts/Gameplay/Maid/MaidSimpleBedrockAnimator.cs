using System;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// TLM 的 SimpleBedrockModel 不使用 GeckoLib 的 animation.json，手臂和腿是按
    /// 走路里程/速度实时算出来的。这里照搬 MaidBaseAnimation 的 getArmDefault/getLegDefault。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidSimpleBedrockAnimator : MonoBehaviour
    {
        [Header("骨骼名")]
        [SerializeField] private string _armLeftBone = DefaultArmLeftBone;
        [SerializeField] private string _armRightBone = DefaultArmRightBone;
        [SerializeField] private string _legLeftBone = DefaultLegLeftBone;
        [SerializeField] private string _legRightBone = DefaultLegRightBone;

        public const string DefaultArmLeftBone = "armLeft";
        public const string DefaultArmRightBone = "armRight";
        public const string DefaultLegLeftBone = "legLeft";
        public const string DefaultLegRightBone = "legRight";

        [Header("走路")]
        [Tooltip("速度达到这个值时摆手幅度最大")]
        [SerializeField] private float _fullSwingSpeed = 1.8f;
        [Tooltip("每走 1 米推进多少摆动相位")]
        [SerializeField] private float _phasePerMeter = 8f;
        [SerializeField] private float _blendSpeed = 8f;
        [SerializeField] private float _armSwing = 0.7f;
        [SerializeField] private float _legSwing = 0.3f;
        [SerializeField] private float _armSway = 0.05f;

        [Header("吃东西")]
        [Tooltip("被喂东西时右臂往前抬多少度（简单模型没有吃东西动画，用这个示意）。正值 = 往前，反了才改负号")]
        [SerializeField] private float _eatArmDegrees = 85f;

        [Header("坐姿：照 TLM 的 MaidBaseAnimation.sittingPosture / getSitSkirtRotation")]
        [Tooltip("大腿往前抬（弧度 -1.134）= -65°。坐下去脚正好落地，改之前先想清楚")]
        [SerializeField] private float _sitLegX = -65f;
        [Tooltip("腿往两边撇（弧度 ∓0.262）= ∓15°")]
        [SerializeField] private float _sitLegZ = 15f;
        [Tooltip("手臂往前（弧度 -0.798）= -45.7°")]
        [SerializeField] private float _sitArmX = -45.7f;
        [Tooltip("手臂外撇（弧度 ∓0.274）= ∓15.7°")]
        [SerializeField] private float _sitArmZ = 15.7f;
        [Tooltip("裙摆往前（弧度 -0.567）= -32.5°，站起来会复位")]
        [SerializeField] private float _sitSkirtX = -32.5f;
        [Tooltip("裙摆骨骼名，模型里没有就留空")]
        [SerializeField] private string _sitSkirtBone = "sittingRotationSkirt";
        [Tooltip("睡觉时头抬多少度（TLM 的 getHeadDefault 睡觉时就是 15°）")]
        [SerializeField] private float _sleepHeadDegrees = 15f;

        Transform _armLeft;
        Transform _armRight;
        Transform _legLeft;
        Transform _legRight;
        Transform _body;
        Transform _skirt;
        Transform _head;

        Quaternion _armLeftRest = Quaternion.identity;
        Quaternion _armRightRest = Quaternion.identity;
        Quaternion _legLeftRest = Quaternion.identity;
        Quaternion _legRightRest = Quaternion.identity;
        Quaternion _bodyRest = Quaternion.identity;
        Quaternion _skirtRest = Quaternion.identity;
        Quaternion _headRest = Quaternion.identity;

        Vector3 _lastPosition;
        float _limbSwing;
        float _limbSwingAmount;
        float _age;
        bool _moving;
        bool _sitting;
        bool _sleeping;
        bool _headNeedsRestore;
        float _eatRemaining;
        float _eatTotal;

        void Awake()
        {
            _lastPosition = transform.position;
            _armLeft = FindBone(_armLeftBone);
            _armRight = FindBone(_armRightBone);
            _legLeft = FindBone(_legLeftBone);
            _legRight = FindBone(_legRightBone);
            _body = FindByName(transform, "body");
            _head = FindByName(transform, "head");
            _skirt = string.IsNullOrEmpty(_sitSkirtBone) ? null : FindByName(transform, _sitSkirtBone);

            _armLeftRest = _armLeft != null ? _armLeft.localRotation : Quaternion.identity;
            _armRightRest = _armRight != null ? _armRight.localRotation : Quaternion.identity;
            _legLeftRest = _legLeft != null ? _legLeft.localRotation : Quaternion.identity;
            _legRightRest = _legRight != null ? _legRight.localRotation : Quaternion.identity;
            _bodyRest = _body != null ? _body.localRotation : Quaternion.identity;
            _headRest = _head != null ? _head.localRotation : Quaternion.identity;
            _skirtRest = _skirt != null ? _skirt.localRotation : Quaternion.identity;
        }

        void OnEnable()
        {
            _lastPosition = transform.position;
            _limbSwingAmount = 0f;
        }

        void OnDisable()
        {
            Restore();
        }

        public void SetMoving(bool moving)
        {
            _moving = moving;
        }

        /// <summary>坐 / 站。方块模型没有 sit 动画，坐下来靠这套姿势。</summary>
        public void SetSitting(bool sitting)
        {
            _sitting = sitting;
            if (sitting)
            {
                _sleeping = false;
            }
        }

        /// <summary>
        /// 睡觉。躺下本身是 MaidArPose 把根节点放倒，这里只管骨架：四肢回静止、头照 TLM 抬 15°。
        /// （TLM 的 getSleepDefault 只是切 sleepHide/sleepShow 节点，方块模型里没有这俩节点，
        ///   真正让她躺下的是原版渲染器对睡觉实体的整体旋转。）
        /// </summary>
        public void SetSleeping(bool sleeping)
        {
            _sleeping = sleeping;
            if (sleeping)
            {
                _sitting = false;
                _headNeedsRestore = true;
            }
        }

        /// <summary>抬手吃东西的动作演 seconds 秒（只有一个抬起再放下的弧线）</summary>
        public void PlayEat(float seconds)
        {
            _eatTotal = Mathf.Max(0.2f, seconds);
            _eatRemaining = _eatTotal;
        }

        public void CancelEat()
        {
            _eatRemaining = 0f;
            _eatTotal = 0f;
        }

        void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0.000001f)
            {
                return;
            }

            Vector3 position = transform.position;
            Vector3 delta = position - _lastPosition;
            delta.y = 0f;
            _lastPosition = position;

            // 传送/吸附会产生很大的单帧位移，夹一下，别把摆动相位直接推飞
            float speed = Mathf.Min(delta.magnitude / deltaTime, _fullSwingSpeed * 4f);
            float targetAmount = _moving ? Mathf.Clamp01(speed / Mathf.Max(0.01f, _fullSwingSpeed)) : 0f;
            _limbSwingAmount = Mathf.Lerp(_limbSwingAmount, targetAmount,
                Mathf.Clamp01(deltaTime * _blendSpeed));

            if (_moving)
            {
                _limbSwing += speed * deltaTime * _phasePerMeter;
            }

            _age += deltaTime;
            if (_eatRemaining > 0f)
            {
                _eatRemaining = Mathf.Max(0f, _eatRemaining - deltaTime);
            }

            Apply();
        }

        void Apply()
        {
            float swing = Mathf.Cos(_limbSwing * 0.67f) * _limbSwingAmount;
            // TLM: cos(ageInTicks * 0.05)，ageInTicks = 秒 * 20
            float sway = Mathf.Cos(_age) * _armSway;

            if (_sleeping)
            {
                // 躺着：四肢全回静止（手臂自然放身侧），只有头照 TLM 抬 15°
                ApplyDelta(_legLeft, _legLeftRest, Vector3.zero);
                ApplyDelta(_legRight, _legRightRest, Vector3.zero);
                ApplyDelta(_armLeft, _armLeftRest, Vector3.zero);
                ApplyDelta(_armRight, _armRightRest, Vector3.zero);
                ApplyDelta(_skirt, _skirtRest, Vector3.zero);
                ApplyDelta(_body, _bodyRest, Vector3.zero);
                ApplyDelta(_head, _headRest, new Vector3(_sleepHeadDegrees * Mathf.Deg2Rad, 0f, 0f));
                return;
            }

            if (_sitting)
            {
                // 照抄 TLM 的 sittingPosture：腿 -65° 往两边撇 15°，手臂 -45.7° 外撇 15.7°，
                // 裙摆 -32.5°（不转的话会从腿里穿出来）
                ApplyDelta(_legLeft, _legLeftRest,
                    new Vector3(_sitLegX * Mathf.Deg2Rad, 0f, -_sitLegZ * Mathf.Deg2Rad));
                ApplyDelta(_legRight, _legRightRest,
                    new Vector3(_sitLegX * Mathf.Deg2Rad, 0f, _sitLegZ * Mathf.Deg2Rad));
                ApplyDelta(_armLeft, _armLeftRest,
                    new Vector3(_sitArmX * Mathf.Deg2Rad, 0f, _sitArmZ * Mathf.Deg2Rad));
                ApplyDelta(_armRight, _armRightRest,
                    new Vector3(_sitArmX * Mathf.Deg2Rad, 0f, -_sitArmZ * Mathf.Deg2Rad));
                ApplyDelta(_skirt, _skirtRest, new Vector3(_sitSkirtX * Mathf.Deg2Rad, 0f, 0f));
                return;
            }

            // 站起来：腿和手臂走走路那套，裙摆和身体必须显式复位，
            // 不然会停在上面的坐姿角度（踩过：裙子站起来以后一直翘着）
            ApplyDelta(_skirt, _skirtRest, Vector3.zero);
            ApplyDelta(_body, _bodyRest, Vector3.zero);

            // 头只在睡醒那一帧复位，之后不能再碰：摸脸/扇脸/戳脸是每帧往头上叠旋转的，
            // 这里每帧复位会把她们的晃动直接抹掉（踩过：方块模型扇脸时脑袋不晃了）
            if (_headNeedsRestore)
            {
                _headNeedsRestore = false;
                ApplyDelta(_head, _headRest, Vector3.zero);
            }

            // 吃东西：右臂抬起来再放下，抬到最高在中间
            float eat = 0f;
            if (_eatTotal > 0.0001f && _eatRemaining > 0f)
            {
                float t = 1f - Mathf.Clamp01(_eatRemaining / _eatTotal);
                eat = Mathf.Sin(t * Mathf.PI) * _eatArmDegrees * Mathf.Deg2Rad;
            }

            ApplyDelta(_armLeft, _armLeftRest, new Vector3(-swing * _armSwing, 0f, sway));
            // 吃东西是"往前"抬：绕局部 X 取负才是往 +Z（模型正面）那边摆，取正会往后甩
            ApplyDelta(_armRight, _armRightRest, new Vector3(swing * _armSwing - eat, 0f, -sway));
            ApplyDelta(_legLeft, _legLeftRest, new Vector3(swing * _legSwing, 0f, 0f));
            ApplyDelta(_legRight, _legRightRest, new Vector3(-swing * _legSwing, 0f, 0f));
        }

        static void ApplyDelta(Transform bone, Quaternion rest, Vector3 deltaRadians)
        {
            if (bone == null)
            {
                return;
            }

            Vector3 deltaDegrees = deltaRadians * Mathf.Rad2Deg;
            Quaternion delta = BedrockModelBuilder.ComposeRotation(deltaDegrees, Vector3.one,
                BedrockRotationOrder.ZYX);
            bone.localRotation = rest * delta;
        }

        void Restore()
        {
            if (_armLeft != null)
            {
                _armLeft.localRotation = _armLeftRest;
            }

            if (_armRight != null)
            {
                _armRight.localRotation = _armRightRest;
            }

            if (_legLeft != null)
            {
                _legLeft.localRotation = _legLeftRest;
            }

            if (_legRight != null)
            {
                _legRight.localRotation = _legRightRest;
            }

            if (_body != null)
            {
                _body.localRotation = _bodyRest;
            }

            if (_skirt != null)
            {
                _skirt.localRotation = _skirtRest;
            }

            if (_head != null)
            {
                _head.localRotation = _headRest;
            }
        }

        /// <summary>
        /// 默认那套四肢骨骼名是不是都在。给"要不要给这个模型挂程序化动画"用：
        /// 模组侧 Java 统一驱动的方块模型没有 animation.json，Unity 这边靠这个判定统一走程序化动画。
        /// 挂上以后仍然以 Inspector 上的名字为准。
        /// </summary>
        public static bool HasDefaultRig(Transform root)
        {
            return root != null
                && FindByName(root, DefaultArmLeftBone) != null
                && FindByName(root, DefaultArmRightBone) != null
                && FindByName(root, DefaultLegLeftBone) != null
                && FindByName(root, DefaultLegRightBone) != null;
        }

        static Transform FindByName(Transform root, string boneName)
        {
            if (root == null || string.IsNullOrEmpty(boneName))
            {
                return null;
            }

            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (string.Equals(bones[i].name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return bones[i];
                }
            }

            return null;
        }

        Transform FindBone(string boneName)
        {
            Transform bone = FindByName(transform, boneName);
            if (bone == null)
            {
                Debug.LogWarning("SimpleBedrockModel 找不到骨骼: " + boneName, this);
            }

            return bone;
        }
    }
}
