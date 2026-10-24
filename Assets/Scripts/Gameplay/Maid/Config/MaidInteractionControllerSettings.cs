using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>女仆交互控制器的取景与公共音效参数。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/交互控制器", fileName = "MaidInteractionControllerSettings")]
    public sealed class MaidInteractionControllerSettings : ScriptableObject
    {
        [SerializeField] private float _faceCameraSeconds = 0.35f;
        [SerializeField] private AudioClip _tailSniffClip;
        [SerializeField] private AudioClip _slapClip;
        [SerializeField] private float _faceOrthographicSize = 0.8f;
        [SerializeField] private float _faceFocusPitchDegrees = -1f;
        [SerializeField] private float _faceFocusLookHeightRatio = -1f;
        [SerializeField] private float _petOrthographicSize = 0.65f;
        [SerializeField] private float _petFocusPitchDegrees = -1f;
        [SerializeField] private float _petFocusLookHeightRatio = -1f;
        [SerializeField] private float _feedPerspectiveFov = 15f;
        [SerializeField] private float _groomOrthographicSize = 0.9f;
        [SerializeField] private float _groomFocusPitchDegrees = 65f;
        [SerializeField] private float _groomZoom = 1f;
        [SerializeField] private float _groomPerspectiveFov;
        [SerializeField] private float _signOrthographicSize = -1f;
        [SerializeField] private float _maskOrthographicSize = -1f;
        [SerializeField] private float _clickDistance = 300f;
        [SerializeField] private LayerMask _clickMask = ~0;
        public float FaceCameraSeconds
        {
            get { return _faceCameraSeconds; }
        }

        public AudioClip TailSniffClip
        {
            get { return _tailSniffClip; }
        }

        public AudioClip SlapClip
        {
            get { return _slapClip; }
        }

        public float FaceOrthographicSize
        {
            get { return _faceOrthographicSize; }
        }

        public float FaceFocusPitchDegrees
        {
            get { return _faceFocusPitchDegrees; }
        }

        public float FaceFocusLookHeightRatio
        {
            get { return _faceFocusLookHeightRatio; }
        }

        public float PetOrthographicSize
        {
            get { return _petOrthographicSize; }
        }

        public float PetFocusPitchDegrees
        {
            get { return _petFocusPitchDegrees; }
        }

        public float PetFocusLookHeightRatio
        {
            get { return _petFocusLookHeightRatio; }
        }

        public float FeedPerspectiveFov
        {
            get { return _feedPerspectiveFov; }
        }

        public float GroomOrthographicSize
        {
            get { return _groomOrthographicSize; }
        }

        public float GroomFocusPitchDegrees
        {
            get { return _groomFocusPitchDegrees; }
        }

        public float GroomZoom
        {
            get { return _groomZoom; }
        }

        public float GroomPerspectiveFov
        {
            get { return _groomPerspectiveFov; }
        }

        public float SignOrthographicSize
        {
            get { return _signOrthographicSize; }
        }

        public float MaskOrthographicSize
        {
            get { return _maskOrthographicSize; }
        }

        public float ClickDistance
        {
            get { return _clickDistance; }
        }

        public LayerMask ClickMask
        {
            get { return _clickMask; }
        }

    }
}
