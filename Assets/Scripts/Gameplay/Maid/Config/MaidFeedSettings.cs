using MaidHome.Gameplay.Audio;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>喂食交互的可调参数。场景里的 Interaction 会优先读这份配置，没接配置时继续用脚本上的原字段。</summary>
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
    }
}
