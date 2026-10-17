using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>摸脸交互的可调参数。场景里的 Interaction 会优先读这份配置，没接配置时继续用脚本上的原字段。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/摸脸交互", fileName = "MaidFaceSettings")]
    public sealed class MaidFaceSettings : ScriptableObject
    {
        [SerializeField] private float _earRollDegrees = -55f;
        [SerializeField] private float _earPitchDegrees = -30f;
        [SerializeField] private float _earTwistDegrees = 0f;
        [SerializeField] private float _earDragResponse = 0.12f;
        [SerializeField] private float _earStretchStart = 0.05f;
        [SerializeField] private float _earStretchFull = 0.26f;
        [SerializeField] private float _earStretchMax = 0.35f;
        [SerializeField] private float _earDamageStretch = 0.2f;
        [SerializeField] private float _faceDragStartRatio = 0.012f;
        [SerializeField] private float _faceDragMaxYaw = 105f;
        [SerializeField] private float _faceDragMaxPitch = 100f;
        [SerializeField] private float _faceDragDamageAngle = 65f;
        [SerializeField] private float _faceDragReturnSpeed = 300f;
        [SerializeField] private float _slapMinDistance = 0.07f;
        [SerializeField] private float _slapMaxSeconds = 0.4f;
        [SerializeField] private float _slapMinSpeed = 0.2f;
        [SerializeField] private float _slapHorizontalRatio = 2f;
        [SerializeField] private float _slapRepeatDelay = 0.1f;
        [SerializeField] private float _comboTimeoutSeconds = 2.5f;
        [SerializeField] private int _comboMilestoneStep = 100;
        [SerializeField] private float _slapYawDegrees = 38f;
        [SerializeField] private float _slapRollDegrees = 10f;
        [SerializeField] private float _slapVolume = 6f;
        [SerializeField] private AudioClip _milestoneClip;
        [SerializeField] private float _eyeYawDegrees = 24f;
        [SerializeField] private float _eyePitchDegrees = 12f;
        [SerializeField] private float _cheekYawDegrees = 12f;
        [SerializeField] private float _cheekPitchDegrees = 5f;
        [SerializeField] private float _tapMaxSeconds = 0.3f;
        [SerializeField] private float _tapSlopPixels = 24f;
        [SerializeField] private float _pokeCooldownSeconds = 0.2f;
        [SerializeField] private float _pokeReactionCooldownSeconds = 0f;
        [SerializeField] private float _hitPaddingPixels = 6f;
        [SerializeField] private float _earZoneScale = 0.75f;
        [SerializeField] private Vector2 _faceEyeUvMin = new Vector2(1f, 5f);
        [SerializeField] private Vector2 _faceEyeUvSize = new Vector2(2f, 2f);
        [SerializeField] private float _facePixels = 8f;
        [SerializeField] private float _turnSeconds = 0.35f;
        [SerializeField] private float _voiceVolume = 6f;
        [SerializeField] private Color _flashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _flashSeconds = 0.5f;
        [SerializeField] private float _lineSeconds = 1f;
        [SerializeField] private float _complainCooldownSeconds = 1f;
        [SerializeField] private float _slapLineCooldownSeconds = 0.6f;
        [SerializeField] private bool _showZones;
        [SerializeField] private bool _slapOnly;
        [SerializeField] private bool _maskBlocksEyes = true;
        [SerializeField] private float _comboSizeRatio = 0.085f;
        [SerializeField] private float _comboTopRatio = 0.035f;
        [SerializeField] private float _milestoneSizeRatio = 0.11f;
        [SerializeField] private float _milestoneTopRatio = 0.12f;
        [SerializeField] private float _comboParticleScale = 2f;
        [SerializeField] private float _comboScale = 1.45f;
    }
}
