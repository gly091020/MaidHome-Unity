using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>挂牌交互的可调参数。场景里的 Interaction 会优先读这份配置，没接配置时继续用脚本上的原字段。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/挂牌交互", fileName = "MaidSignSettings")]
    public sealed class MaidSignSettings : ScriptableObject
    {
        [SerializeField] private float _signOrthographicSize = 0.75f;
        [SerializeField] private float _signFocusPitchDegrees = 25f;
        [SerializeField] private MaidAttachmentStyle _style = new MaidAttachmentStyle { SizeRatio = 0.3f };
    }
}
