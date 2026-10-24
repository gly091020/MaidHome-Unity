using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>喂食交互的可调参数。Interaction 直接从这份配置读取；没接配置会报错。</summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/喂食交互", fileName = "MaidFeedSettings")]
    public sealed class MaidFeedSettings : ScriptableObject
    {
        [SerializeField] private string _eatClip = "use_mainhand:eat";
        [SerializeField] private string _begClip = "beg";
        [SerializeField] private float _clipFadeSeconds = 0.12f;
        [SerializeField] private float _eatMaxSeconds = 2.5f;
        [SerializeField] private float _eatSoundDelay = 0.2f;
        [SerializeField] private float _soundVolume = 6f;
        [SerializeField] private AudioClip[] _eatSounds;
        [SerializeField] private GameObject _cakePrefab;
        [SerializeField] private AudioClip _cakeHitSound;
        [SerializeField] private Texture2D _particleTexture;
        [SerializeField] private Material _particleMaterial;
        [SerializeField] private float _feedOrthographicSize = 0.8f;
        [SerializeField] private int _particleCount = 12;
        [SerializeField] private float _particleLifetime = 0.6f;
        [SerializeField] private float _particleSpeed = 0.35f;
        [SerializeField] private Vector2 _particleSize = new Vector2(0.05f, 0.09f);
        [SerializeField] private Color _particleColor = Color.white;
        [SerializeField] private Vector3 _particleOffset = new Vector3(0f, -0.35f, 0.9f);
        [SerializeField] private float _particleDelay = 1f;
        [SerializeField] private float _particleGravity = 0.5f;
        [SerializeField] private string _voiceEvent = MaidSoundId.Feed;
        [SerializeField] private float _voiceDelay = 1.7f;
        [SerializeField] private float _lineSeconds = 4f;
        [SerializeField] private string[] _lines =
        {
            "唔……好吃，谢谢主人！",
            "这个蛋糕是给我的吗？",
            "奶油沾到嘴上了……",
            "嘿嘿，主人喂的最好吃。",
            "再来一口也可以哦。"
        };
        [SerializeField] private int _defaultLevel = MaidFeedInteraction.LevelLight;
        [SerializeField] private float _feedCooldownSeconds = 0.3f;
        [SerializeField] private float _cakeStartViewportY = -0.6f;
        [SerializeField] private float _cakeStartDepth = 1f;
        [SerializeField] private float _cakeFlySeconds = 0.35f;
        [SerializeField] private float _cakeSpinDegrees = 720f;
        [SerializeField] private int _maxThrownCakes = 10;
        [SerializeField] private float _heavyFlySeconds = 0.18f;
        [SerializeField] private AudioClip _heavyHitSound;
        [SerializeField] private float _heavyTwistDegrees = 34f;
        [SerializeField] private float _heavyTwistPitchDegrees = 10f;
        [SerializeField] private float _heavyTwistSeconds = 0.5f;
        [SerializeField] private string _heavyVoiceEvent = MaidSoundId.Hurt;
        [SerializeField] private string[] _heavyLines =
        {
            "呜……！主人你干嘛呀！",
            "好疼好疼好疼！",
            "呜哇——脸要被砸扁了……",
            "过、过分！这次真的生气了！",
            "哼，再这样我可要还手了哦。"
        };
        [SerializeField] private float _comboTimeoutSeconds = 2.5f;
        [SerializeField] private int _comboMilestoneStep = 100;
        [SerializeField] private AudioClip _milestoneClip;
        [SerializeField] private float _comboParticleScale = 2f;
        [SerializeField] private float _comboScale = 1.45f;
        [SerializeField] private Color _cakeFlashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _cakeFlashSeconds = 0.5f;
        [SerializeField] private string _cakeVoiceEvent = MaidSoundId.Hurt;
        [SerializeField] private string[] _cakeLines =
        {
            "呜哇！谁扔的蛋糕呀！",
            "唔！我的脸……",
            "蛋、蛋糕砸到脸上了啦！",
            "哼，主人好过分……",
            "不过……还挺好吃的。"
        };
        [SerializeField] private float _simpleEatSeconds = 1.5f;
        public string EatClip
        {
            get { return _eatClip; }
        }

        public string BegClip
        {
            get { return _begClip; }
        }

        public float ClipFadeSeconds
        {
            get { return _clipFadeSeconds; }
        }

        public float EatMaxSeconds
        {
            get { return _eatMaxSeconds; }
        }

        public float EatSoundDelay
        {
            get { return _eatSoundDelay; }
        }

        public float SoundVolume
        {
            get { return _soundVolume; }
        }

        public AudioClip[] EatSounds
        {
            get { return _eatSounds; }
        }

        public GameObject CakePrefab
        {
            get { return _cakePrefab; }
        }

        public AudioClip CakeHitSound
        {
            get { return _cakeHitSound; }
        }

        public Texture2D ParticleTexture
        {
            get { return _particleTexture; }
        }

        public Material ParticleMaterial
        {
            get { return _particleMaterial; }
        }

        public float FeedOrthographicSize
        {
            get { return _feedOrthographicSize; }
        }

        public int ParticleCount
        {
            get { return _particleCount; }
        }

        public float ParticleLifetime
        {
            get { return _particleLifetime; }
        }

        public float ParticleSpeed
        {
            get { return _particleSpeed; }
        }

        public Vector2 ParticleSize
        {
            get { return _particleSize; }
        }

        public Color ParticleColor
        {
            get { return _particleColor; }
        }

        public Vector3 ParticleOffset
        {
            get { return _particleOffset; }
        }

        public float ParticleDelay
        {
            get { return _particleDelay; }
        }

        public float ParticleGravity
        {
            get { return _particleGravity; }
        }

        public string VoiceEvent
        {
            get { return _voiceEvent; }
        }

        public float VoiceDelay
        {
            get { return _voiceDelay; }
        }

        public float LineSeconds
        {
            get { return _lineSeconds; }
        }

        public string[] Lines
        {
            get { return _lines; }
        }

        public int DefaultLevel
        {
            get { return _defaultLevel; }
        }

        public float FeedCooldownSeconds
        {
            get { return _feedCooldownSeconds; }
        }

        public float CakeStartViewportY
        {
            get { return _cakeStartViewportY; }
        }

        public float CakeStartDepth
        {
            get { return _cakeStartDepth; }
        }

        public float CakeFlySeconds
        {
            get { return _cakeFlySeconds; }
        }

        public float CakeSpinDegrees
        {
            get { return _cakeSpinDegrees; }
        }

        public int MaxThrownCakes
        {
            get { return _maxThrownCakes; }
        }

        public float HeavyFlySeconds
        {
            get { return _heavyFlySeconds; }
        }

        public AudioClip HeavyHitSound
        {
            get { return _heavyHitSound; }
        }

        public float HeavyTwistDegrees
        {
            get { return _heavyTwistDegrees; }
        }

        public float HeavyTwistPitchDegrees
        {
            get { return _heavyTwistPitchDegrees; }
        }

        public float HeavyTwistSeconds
        {
            get { return _heavyTwistSeconds; }
        }

        public string HeavyVoiceEvent
        {
            get { return _heavyVoiceEvent; }
        }

        public string[] HeavyLines
        {
            get { return _heavyLines; }
        }

        public float ComboTimeoutSeconds
        {
            get { return _comboTimeoutSeconds; }
        }

        public int ComboMilestoneStep
        {
            get { return _comboMilestoneStep; }
        }

        public AudioClip MilestoneClip
        {
            get { return _milestoneClip; }
        }

        public float ComboParticleScale
        {
            get { return _comboParticleScale; }
        }

        public float ComboScale
        {
            get { return _comboScale; }
        }

        public Color CakeFlashColor
        {
            get { return _cakeFlashColor; }
        }

        public float CakeFlashSeconds
        {
            get { return _cakeFlashSeconds; }
        }

        public string CakeVoiceEvent
        {
            get { return _cakeVoiceEvent; }
        }

        public string[] CakeLines
        {
            get { return _cakeLines; }
        }

        public float SimpleEatSeconds
        {
            get { return _simpleEatSeconds; }
        }

    }
}
