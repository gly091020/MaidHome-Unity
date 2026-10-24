using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>真实流泪的手感、素材和触发参数。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/真实流泪", fileName = "MaidTearsConfig")]
    public sealed class MaidTearsConfig : ScriptableObject
    {
        [SerializeField] private float _streakWidthRatio = 0.12f;
        [SerializeField] private float _dropSizeRatio = 0.25f;
        [SerializeField] private float _dropInterval = 0.5f;
        [SerializeField] private float _surfaceOffset = 0.01f;
        [SerializeField] private float _faceSize = 0.46f;
        [SerializeField] private float _faceForward = 0.25f;
        [SerializeField] private Vector2 _faceOffset = Vector2.zero;
        [SerializeField] private Texture2D _dropTexture;
        [SerializeField] private Color _simpleTearColor = new Color(0.28f, 0.62f, 0.95f, 0.85f);
        [SerializeField] private float _simpleTearWidthRatio = 0.2f;
        [SerializeField] private float _damageThreshold = 20f;
        [SerializeField] private float _damageWindow = 30f;
        [SerializeField] private float _crySeconds = 20f;
        [SerializeField] private float _slapCrySeconds = 20f;
        public float StreakWidthRatio
        {
            get { return _streakWidthRatio; }
        }

        public float DropSizeRatio
        {
            get { return _dropSizeRatio; }
        }

        public float DropInterval
        {
            get { return _dropInterval; }
        }

        public float SurfaceOffset
        {
            get { return _surfaceOffset; }
        }

        public float FaceSize
        {
            get { return _faceSize; }
        }

        public float FaceForward
        {
            get { return _faceForward; }
        }

        public Vector2 FaceOffset
        {
            get { return _faceOffset; }
        }

        public Texture2D DropTexture
        {
            get { return _dropTexture; }
        }

        public Color SimpleTearColor
        {
            get { return _simpleTearColor; }
        }

        public float SimpleTearWidthRatio
        {
            get { return _simpleTearWidthRatio; }
        }

        public float DamageThreshold
        {
            get { return _damageThreshold; }
        }

        public float DamageWindow
        {
            get { return _damageWindow; }
        }

        public float CrySeconds
        {
            get { return _crySeconds; }
        }

        public float SlapCrySeconds
        {
            get { return _slapCrySeconds; }
        }

    }
}
