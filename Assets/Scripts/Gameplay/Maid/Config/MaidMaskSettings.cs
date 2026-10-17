using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>面具交互的可调参数。场景里的 Interaction 会优先读这份配置，没接配置时继续用脚本上的原字段。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/面具交互", fileName = "MaidMaskSettings")]
    public sealed class MaidMaskSettings : ScriptableObject
    {
        [SerializeField] private MaidAttachmentStyle _style = new MaidAttachmentStyle { SizeRatio = 1.15f };
        [SerializeField] private float _maskOrthographicSize = 0.35f;
        [SerializeField] private float _maskFocusPitchDegrees = -1f;
    }
}
