using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>梳毛交互的可调参数。场景里的 Interaction 会优先读这份配置，没接配置时继续用脚本上的原字段。</summary>
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
    }
}
