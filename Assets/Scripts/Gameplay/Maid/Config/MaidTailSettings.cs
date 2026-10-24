using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>尾巴交互的可调参数。Interaction 直接从这份配置读取；没接配置会报错。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/尾巴交互", fileName = "MaidTailSettings")]
    public sealed class MaidTailSettings : ScriptableObject
    {
        [SerializeField] private float _dragSensitivity = 1.85f;
        [SerializeField] private float _dragReferencePixels = 400f;
        [SerializeField] private float _yawSign = 1f;
        [SerializeField] private float _pitchSign = -1f;
        [SerializeField] private float _zFollow = 0.08f;
        [SerializeField] private float _safeZoneWidth = 0.70f;
        [SerializeField] private float _safeZoneHeight = 0.65f;
        [SerializeField] private float _hitPaddingPixels = 8f;
        [SerializeField] private float _overstretchDragFraction = 0.3f;
        [SerializeField] private float _turnSeconds = 0.35f;
        [SerializeField] private float _sniffTravelSeconds = 0.3f;
        [SerializeField] private float _sniffMaxSeconds = 2f;
        [SerializeField] private float _sniffZoomScale = 0.6f;
        [SerializeField] private float _sniffMinSize = 0.35f;
        [SerializeField] private float _soundVolume = 6f;
        [SerializeField] private Color _flashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _flashSeconds = 0.5f;
        [SerializeField] private float _lineSeconds = 1f;
        [SerializeField] private float _complainCooldownSeconds = 1f;
        public float DragSensitivity
        {
            get { return _dragSensitivity; }
        }

        public float DragReferencePixels
        {
            get { return _dragReferencePixels; }
        }

        public float YawSign
        {
            get { return _yawSign; }
        }

        public float PitchSign
        {
            get { return _pitchSign; }
        }

        public float ZFollow
        {
            get { return _zFollow; }
        }

        public float SafeZoneWidth
        {
            get { return _safeZoneWidth; }
        }

        public float SafeZoneHeight
        {
            get { return _safeZoneHeight; }
        }

        public float HitPaddingPixels
        {
            get { return _hitPaddingPixels; }
        }

        public float OverstretchDragFraction
        {
            get { return _overstretchDragFraction; }
        }

        public float TurnSeconds
        {
            get { return _turnSeconds; }
        }

        public float SniffTravelSeconds
        {
            get { return _sniffTravelSeconds; }
        }

        public float SniffMaxSeconds
        {
            get { return _sniffMaxSeconds; }
        }

        public float SniffZoomScale
        {
            get { return _sniffZoomScale; }
        }

        public float SniffMinSize
        {
            get { return _sniffMinSize; }
        }

        public float SoundVolume
        {
            get { return _soundVolume; }
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

    }
}
