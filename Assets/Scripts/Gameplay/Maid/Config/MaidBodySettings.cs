using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>玩全身交互的可调参数。场景里的 Interaction 会优先读这份配置，没接配置时继续用脚本上的原字段。</summary>
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
    }
}
