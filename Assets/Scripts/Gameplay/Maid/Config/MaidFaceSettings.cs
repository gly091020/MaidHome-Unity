using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>摸脸交互的可调参数。Interaction 直接从这份配置读取；没接配置会报错。</summary>
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
        public float EarRollDegrees
        {
            get { return _earRollDegrees; }
        }

        public float EarPitchDegrees
        {
            get { return _earPitchDegrees; }
        }

        public float EarTwistDegrees
        {
            get { return _earTwistDegrees; }
        }

        public float EarDragResponse
        {
            get { return _earDragResponse; }
        }

        public float EarStretchStart
        {
            get { return _earStretchStart; }
        }

        public float EarStretchFull
        {
            get { return _earStretchFull; }
        }

        public float EarStretchMax
        {
            get { return _earStretchMax; }
        }

        public float EarDamageStretch
        {
            get { return _earDamageStretch; }
        }

        public float FaceDragStartRatio
        {
            get { return _faceDragStartRatio; }
        }

        public float FaceDragMaxYaw
        {
            get { return _faceDragMaxYaw; }
        }

        public float FaceDragMaxPitch
        {
            get { return _faceDragMaxPitch; }
        }

        public float FaceDragDamageAngle
        {
            get { return _faceDragDamageAngle; }
        }

        public float FaceDragReturnSpeed
        {
            get { return _faceDragReturnSpeed; }
        }

        public float SlapMinDistance
        {
            get { return _slapMinDistance; }
        }

        public float SlapMaxSeconds
        {
            get { return _slapMaxSeconds; }
        }

        public float SlapMinSpeed
        {
            get { return _slapMinSpeed; }
        }

        public float SlapHorizontalRatio
        {
            get { return _slapHorizontalRatio; }
        }

        public float SlapRepeatDelay
        {
            get { return _slapRepeatDelay; }
        }

        public float ComboTimeoutSeconds
        {
            get { return _comboTimeoutSeconds; }
        }

        public int ComboMilestoneStep
        {
            get { return _comboMilestoneStep; }
        }

        public float SlapYawDegrees
        {
            get { return _slapYawDegrees; }
        }

        public float SlapRollDegrees
        {
            get { return _slapRollDegrees; }
        }

        public float SlapVolume
        {
            get { return _slapVolume; }
        }

        public AudioClip MilestoneClip
        {
            get { return _milestoneClip; }
        }

        public float EyeYawDegrees
        {
            get { return _eyeYawDegrees; }
        }

        public float EyePitchDegrees
        {
            get { return _eyePitchDegrees; }
        }

        public float CheekYawDegrees
        {
            get { return _cheekYawDegrees; }
        }

        public float CheekPitchDegrees
        {
            get { return _cheekPitchDegrees; }
        }

        public float TapMaxSeconds
        {
            get { return _tapMaxSeconds; }
        }

        public float TapSlopPixels
        {
            get { return _tapSlopPixels; }
        }

        public float PokeCooldownSeconds
        {
            get { return _pokeCooldownSeconds; }
        }

        public float PokeReactionCooldownSeconds
        {
            get { return _pokeReactionCooldownSeconds; }
        }

        public float HitPaddingPixels
        {
            get { return _hitPaddingPixels; }
        }

        public float EarZoneScale
        {
            get { return _earZoneScale; }
        }

        public Vector2 FaceEyeUvMin
        {
            get { return _faceEyeUvMin; }
        }

        public Vector2 FaceEyeUvSize
        {
            get { return _faceEyeUvSize; }
        }

        public float FacePixels
        {
            get { return _facePixels; }
        }

        public float TurnSeconds
        {
            get { return _turnSeconds; }
        }

        public float VoiceVolume
        {
            get { return _voiceVolume; }
        }

        public Color FlashColor
        {
            get { return _flashColor; }
        }

        public float FlashSeconds
        {
            get { return _flashSeconds; }
        }

        public float LineSeconds
        {
            get { return _lineSeconds; }
        }

        public float ComplainCooldownSeconds
        {
            get { return _complainCooldownSeconds; }
        }

        public float SlapLineCooldownSeconds
        {
            get { return _slapLineCooldownSeconds; }
        }

        public bool ShowZones
        {
            get { return _showZones; }
        }

        public bool SlapOnly
        {
            get { return _slapOnly; }
        }

        public bool MaskBlocksEyes
        {
            get { return _maskBlocksEyes; }
        }

        public float ComboSizeRatio
        {
            get { return _comboSizeRatio; }
        }

        public float ComboTopRatio
        {
            get { return _comboTopRatio; }
        }

        public float MilestoneSizeRatio
        {
            get { return _milestoneSizeRatio; }
        }

        public float MilestoneTopRatio
        {
            get { return _milestoneTopRatio; }
        }

        public float ComboParticleScale
        {
            get { return _comboParticleScale; }
        }

        public float ComboScale
        {
            get { return _comboScale; }
        }

    }
}
