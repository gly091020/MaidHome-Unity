using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>梳毛交互的可调参数。Interaction 直接从这份配置读取；没接配置会报错。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/梳毛交互", fileName = "MaidGroomingSettings")]
    public sealed class MaidGroomingSettings : ScriptableObject
    {
        [SerializeField] private float _earHitScale = 1.4f;
        [SerializeField] private float _maxDragWorld = 0.35f;
        [SerializeField] private bool _grainOnHair = true;
        [SerializeField] private float _comfortCooldown = 3f;
        [SerializeField] private float _againstCooldown = 0.6f;
        [SerializeField] private float _lineSeconds = 1.6f;
        [SerializeField] private float _earPressOnTouch = 0.35f;
        [SerializeField] private bool _lieDown = true;
        [SerializeField] private float _lieTiltDegrees = 90f;
        [SerializeField] private float _lieLift = 0f;
        [SerializeField] private float _lieGroundClearance = 0.02f;
        [SerializeField] private string _sleepClip = "sleep";
        [SerializeField] private float _lieDelaySeconds = 0.45f;
        [SerializeField] private float _turnBackSeconds = 0.35f;
        [SerializeField] private bool _focusTailToo;
        [SerializeField] private float _tailSensitivity = 1.85f;
        [SerializeField] private float _tailDragPixels = 400f;
        [SerializeField] private Color _flashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _flashSeconds = 0.5f;
        [SerializeField] private float _voiceVolume = 6f;
        [SerializeField] private float _cheerSeconds = 20f;
        [SerializeField] private string _cheerSoundId = MaidSoundId.Tamed;
        [SerializeField] private bool _showDebug;
        public float EarHitScale
        {
            get { return _earHitScale; }
        }

        public float MaxDragWorld
        {
            get { return _maxDragWorld; }
        }

        public bool GrainOnHair
        {
            get { return _grainOnHair; }
        }

        public float ComfortCooldown
        {
            get { return _comfortCooldown; }
        }

        public float AgainstCooldown
        {
            get { return _againstCooldown; }
        }

        public float LineSeconds
        {
            get { return _lineSeconds; }
        }

        public float EarPressOnTouch
        {
            get { return _earPressOnTouch; }
        }

        public bool LieDown
        {
            get { return _lieDown; }
        }

        public float LieTiltDegrees
        {
            get { return _lieTiltDegrees; }
        }

        public float LieLift
        {
            get { return _lieLift; }
        }

        public float LieGroundClearance
        {
            get { return _lieGroundClearance; }
        }

        public string SleepClip
        {
            get { return _sleepClip; }
        }

        public float LieDelaySeconds
        {
            get { return _lieDelaySeconds; }
        }

        public float TurnBackSeconds
        {
            get { return _turnBackSeconds; }
        }

        public bool FocusTailToo
        {
            get { return _focusTailToo; }
        }

        public float TailSensitivity
        {
            get { return _tailSensitivity; }
        }

        public float TailDragPixels
        {
            get { return _tailDragPixels; }
        }

        public Color FlashColor
        {
            get { return _flashColor; }
        }

        public float FlashSeconds
        {
            get { return _flashSeconds; }
        }

        public float VoiceVolume
        {
            get { return _voiceVolume; }
        }

        public float CheerSeconds
        {
            get { return _cheerSeconds; }
        }

        public string CheerSoundId
        {
            get { return _cheerSoundId; }
        }

        public bool ShowDebug
        {
            get { return _showDebug; }
        }

    }
}
