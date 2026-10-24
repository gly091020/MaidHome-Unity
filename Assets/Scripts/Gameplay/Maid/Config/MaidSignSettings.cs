using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>挂牌交互的可调参数。Interaction 直接从这份配置读取；没接配置会报错。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/挂牌交互", fileName = "MaidSignSettings")]
    public sealed class MaidSignSettings : ScriptableObject
    {
        [SerializeField] private float _signOrthographicSize = 0.75f;
        [SerializeField] private float _signFocusPitchDegrees = 25f;
        [SerializeField] private MaidAttachmentStyle _style = new MaidAttachmentStyle { SizeRatio = 0.3f };
        public float SignOrthographicSize
        {
            get { return _signOrthographicSize; }
        }

        public float SignFocusPitchDegrees
        {
            get { return _signFocusPitchDegrees; }
        }

        public MaidAttachmentStyle Style
        {
            get { return _style; }
        }

    }
}
