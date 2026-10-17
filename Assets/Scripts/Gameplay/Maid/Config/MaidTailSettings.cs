using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>尾巴交互的可调参数。场景里的 Interaction 会优先读这份配置，没接配置时继续用脚本上的原字段。</summary>
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
    }
}
