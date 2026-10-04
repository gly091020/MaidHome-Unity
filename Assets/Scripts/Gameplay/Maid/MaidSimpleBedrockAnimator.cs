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
        [SerializeField] private string _armLeftBone = "armLeft";
        [SerializeField] private string _armRightBone = "armRight";
        [SerializeField] private string _legLeftBone = "legLeft";
        [SerializeField] private string _legRightBone = "legRight";

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
        [Tooltip("被喂东西时右臂往前抬多少度（简单模型没有吃东西动画，用这个示意）。方向反了就改成负数")]
        [SerializeField] private float _eatArmDegrees = 85f;

        Transform _armLeft;
        Transform _armRight;
        Transform _legLeft;
        Transform _legRight;

        Quaternion _armLeftRest = Quaternion.identity;
        Quaternion _armRightRest = Quaternion.identity;
        Quaternion _legLeftRest = Quaternion.identity;
        Quaternion _legRightRest = Quaternion.identity;

        Vector3 _lastPosition;
        float _limbSwing;
        float _limbSwingAmount;
        float _age;
        bool _moving;
        float _eatRemaining;
        float _eatTotal;

        void Awake()
        {
            _lastPosition = transform.position;
            _armLeft = FindBone(_armLeftBone);
            _armRight = FindBone(_armRightBone);
            _legLeft = FindBone(_legLeftBone);
            _legRight = FindBone(_legRightBone);

            _armLeftRest = _armLeft != null ? _armLeft.localRotation : Quaternion.identity;
            _armRightRest = _armRight != null ? _armRight.localRotation : Quaternion.identity;
            _legLeftRest = _legLeft != null ? _legLeft.localRotation : Quaternion.identity;
            _legRightRest = _legRight != null ? _legRight.localRotation : Quaternion.identity;
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

            // 吃东西：右臂抬起来再放下，抬到最高在中间
            float eat = 0f;
            if (_eatTotal > 0.0001f && _eatRemaining > 0f)
            {
                float t = 1f - Mathf.Clamp01(_eatRemaining / _eatTotal);
                eat = Mathf.Sin(t * Mathf.PI) * _eatArmDegrees * Mathf.Deg2Rad;
            }

            ApplyDelta(_armLeft, _armLeftRest, new Vector3(-swing * _armSwing, 0f, sway));
            ApplyDelta(_armRight, _armRightRest, new Vector3(swing * _armSwing + eat, 0f, -sway));
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
        }

        Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return null;
            }

            Transform[] bones = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (string.Equals(bones[i].name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return bones[i];
                }
            }

            Debug.LogWarning("SimpleBedrockModel 找不到骨骼: " + boneName, this);
            return null;
        }
    }
}
