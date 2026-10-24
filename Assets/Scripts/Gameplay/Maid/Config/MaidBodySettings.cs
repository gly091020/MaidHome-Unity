using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>玩全身交互的可调参数。Interaction 直接从这份配置读取；没接配置会报错。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/玩全身交互", fileName = "MaidBodySettings")]
    public sealed class MaidBodySettings : ScriptableObject
    {
        [SerializeField] private float _spring = 1500f;
        [SerializeField] private float _damper = 100f;
        [SerializeField] private float _tolerance = 0.025f;
        [SerializeField] private int _springDelayFrames = 3;
        [SerializeField] private float _pickFallbackPixels = 24f;
        [SerializeField] private float _pickFallbackScreenRatio = 0.04f;
        [SerializeField] private float _rayDistance = 500f;
        [SerializeField] private LayerMask _dragMask = ~0;
        [SerializeField] private bool _ignoreAllSelfCollision = true;
        [SerializeField] private bool _disableAngleLimits;
        [SerializeField] private float _bodyPerspectiveFov = 35f;
        [SerializeField] private float _viewRotateDegreesPerScreenHeight = 180f;
        [SerializeField] private float _viewPitchMinDegrees = -75f;
        [SerializeField] private float _viewPitchMaxDegrees = 75f;
        [SerializeField] private float _viewFitMargin = 1.15f;
        [SerializeField] private float _sensorGravity = 9.81f;
        [SerializeField] private float _sensorAccelerationScale = 4f;
        [SerializeField] private float _sensorJerkScale = 0.12f;
        [SerializeField] private float _sensorSmoothing = 6f;
        [SerializeField] private float _sensorDeadzone = 0.01f;
        [SerializeField] private float _motionDepthRatio = 0.7f;
        [SerializeField] private float _motionWallThickness = 0.08f;
        [SerializeField] private float _impactFlashSpeed = 4f;
        [SerializeField] private float _impactFlashCooldown = 0.6f;
        [SerializeField] private float _hurtRestartCooldown = 0.15f;
        [SerializeField] private float _impactWarmupSeconds = 0.4f;
        [SerializeField] private Color _impactFlashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _impactFlashSeconds = 0.5f;
        [SerializeField] private float _impactVoiceVolume = 6f;
        public float Spring
        {
            get { return _spring; }
        }

        public float Damper
        {
            get { return _damper; }
        }

        public float Tolerance
        {
            get { return _tolerance; }
        }

        public int SpringDelayFrames
        {
            get { return _springDelayFrames; }
        }

        public float PickFallbackPixels
        {
            get { return _pickFallbackPixels; }
        }

        public float PickFallbackScreenRatio
        {
            get { return _pickFallbackScreenRatio; }
        }

        public float RayDistance
        {
            get { return _rayDistance; }
        }

        public LayerMask DragMask
        {
            get { return _dragMask; }
        }

        public bool IgnoreAllSelfCollision
        {
            get { return _ignoreAllSelfCollision; }
        }

        public bool DisableAngleLimits
        {
            get { return _disableAngleLimits; }
        }

        public float BodyPerspectiveFov
        {
            get { return _bodyPerspectiveFov; }
        }

        public float ViewRotateDegreesPerScreenHeight
        {
            get { return _viewRotateDegreesPerScreenHeight; }
        }

        public float ViewPitchMinDegrees
        {
            get { return _viewPitchMinDegrees; }
        }

        public float ViewPitchMaxDegrees
        {
            get { return _viewPitchMaxDegrees; }
        }

        public float ViewFitMargin
        {
            get { return _viewFitMargin; }
        }

        public float SensorGravity
        {
            get { return _sensorGravity; }
        }

        public float SensorAccelerationScale
        {
            get { return _sensorAccelerationScale; }
        }

        public float SensorJerkScale
        {
            get { return _sensorJerkScale; }
        }

        public float SensorSmoothing
        {
            get { return _sensorSmoothing; }
        }

        public float SensorDeadzone
        {
            get { return _sensorDeadzone; }
        }

        public float MotionDepthRatio
        {
            get { return _motionDepthRatio; }
        }

        public float MotionWallThickness
        {
            get { return _motionWallThickness; }
        }

        public float ImpactFlashSpeed
        {
            get { return _impactFlashSpeed; }
        }

        public float ImpactFlashCooldown
        {
            get { return _impactFlashCooldown; }
        }

        public float HurtRestartCooldown
        {
            get { return _hurtRestartCooldown; }
        }

        public float ImpactWarmupSeconds
        {
            get { return _impactWarmupSeconds; }
        }

        public Color ImpactFlashColor
        {
            get { return _impactFlashColor; }
        }

        public float ImpactFlashSeconds
        {
            get { return _impactFlashSeconds; }
        }

        public float ImpactVoiceVolume
        {
            get { return _impactVoiceVolume; }
        }

    }
}
