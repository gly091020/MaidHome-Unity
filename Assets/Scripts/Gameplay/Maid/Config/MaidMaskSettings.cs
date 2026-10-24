using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>面具交互的可调参数。Interaction 直接从这份配置读取；没接配置会报错。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/面具交互", fileName = "MaidMaskSettings")]
    public sealed class MaidMaskSettings : ScriptableObject
    {
        [SerializeField] private MaidAttachmentStyle _style = new MaidAttachmentStyle { SizeRatio = 1.15f };
        [SerializeField] private float _maskOrthographicSize = 0.35f;
        [SerializeField] private float _maskFocusPitchDegrees = -1f;
        public MaidAttachmentStyle Style
        {
            get { return _style; }
        }

        public float MaskOrthographicSize
        {
            get { return _maskOrthographicSize; }
        }

        public float MaskFocusPitchDegrees
        {
            get { return _maskFocusPitchDegrees; }
        }

    }
}
