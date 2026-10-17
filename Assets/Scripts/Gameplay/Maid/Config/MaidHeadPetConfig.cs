using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 摸摸交互的手感、搓热进度和着火参数。
    /// Assets/Resources/Settings/MaidHeadPetConfig.asset。
    /// </summary>
    [CreateAssetMenu(menuName = "MaidHome/设置/摸摸交互", fileName = "MaidHeadPetConfig")]
    public sealed class MaidHeadPetConfig : ScriptableObject
    {
        [Header("女仆反应动画")]
        [Tooltip("模型里有 moreanimation 的摸头反应动画就播它；没有就只用程序化的头部侧倒 / 压扁")]
        [SerializeField] private bool _playMaidPetAnimation = true;
        [SerializeField] private string _petReactionClip = "pet_reaction";
        [SerializeField] private string _petHoldClip = "pet_reaction_hold";

        [Header("头顶起手")]
        [Tooltip("横向判定宽度：1 = 整个头宽，越小越要求按在头顶正中")]
        [SerializeField] private float _headStartWidthRatio = 0.95f;
        [Tooltip("从头顶往下算多深：0.35 = 头高的上 35% 都算头顶")]
        [SerializeField] private float _headStartDepthRatio = 0.35f;
        [Tooltip("允许按在头顶上方多少（按头高比例）")]
        [SerializeField] private float _headStartAboveRatio = 0.2f;

        [Header("左右摸")]
        [Tooltip("左右拖到头两侧时头往侧面倒多少度")]
        [SerializeField] private float _rubRollDegrees = 8f;
        [Tooltip("左右拖到头两侧时头往左右转多少度")]
        [SerializeField] private float _rubYawDegrees = 5f;
        [SerializeField] private float _rubSmooth = 12f;
        [Tooltip("每摸过这么多屏幕高度算一次抚摸，播放一次声音")]
        [SerializeField] private float _rubStep = 0.06f;
        [SerializeField] private float _rubVolume = 1f;
        [SerializeField] private AudioClip _rubClip;
        [Tooltip("横向速度超过这个值（屏幕高度/秒）才开始搓热")]
        [SerializeField] private float _rubIgniteSpeed = 2.5f;
        [Tooltip("搓得够快时每秒增加多少热量")]
        [SerializeField] private float _rubHeatRate = 1.2f;
        [Tooltip("没在搓或速度不够时每秒衰减多少热量")]
        [SerializeField] private float _rubHeatDecay = 0.8f;

        [Header("着火")]
        [SerializeField] private GameObject _firePrefab;
        [SerializeField] private float _fireScale = 0.45f;
        [Tooltip("火焰相对头顶的相机空间偏移（右 / 上 / 前，米）")]
        [SerializeField] private Vector3 _fireCameraOffset;
        [Tooltip("头发燃烧时叠在贴图上的颜色；alpha = 叠加不透明度（0 = 不变，1 = 完全覆盖）")]
        [SerializeField] private Color _burnColor = new Color(0.08f, 0.05f, 0.04f, 1f);
        [Tooltip("着火多久后熄灭")]
        [SerializeField] private float _fireSeconds = 5f;
        [SerializeField] private float _fireLineSeconds = 4f;
        [Tooltip("燃烧结束的 ding 音效（留空就没声音）")]
        [SerializeField] private AudioClip _fireEndClip;
        [SerializeField] private float _fireEndVolume = 1f;
        [Tooltip("着火时播放的反应动画；模型没有就跳过")]
        [SerializeField] private string _burnReactionClip = "!??!";
        [Tooltip("烧完后和正常动画混用的表情动画；模型没有就跳过")]
        [SerializeField] private string _wuyuClip = "wuyu";
        [Range(0f, 1f)]
        [Tooltip("wuyu 表情的混合权重；1 = 完全覆盖它写到的眼睛/眉毛骨骼")]
        [SerializeField] private float _wuyuWeight = 1f;
        [SerializeField] private float _wuyuFadeSeconds = 0.4f;
        [SerializeField] private string[] _fireLines =
        {
            "……",
            "主人，你认真的吗。",
            "头发，着火了。",
            "……我现在该说什么。",
            "你搓得挺开心的。",
            "算了，烧吧。",
        };

        [Header("竖直按")]
        [Tooltip("从按下点往下拖这么多屏幕高度就到最大挤压")]
        [SerializeField] private float _pressRange = 0.16f;
        [Tooltip("按压起始偏移（屏幕高度比例）：正数更容易压下去，负数要拖更长")]
        [SerializeField] private float _pressStartOffset;
        [Tooltip("头沿屏幕竖直方向最多压扁多少（0.35 = 压掉 35%）")]
        [SerializeField] private float _pressSquash = 0.35f;
        [Tooltip("另外两个轴鼓起来多少，保持体积感")]
        [SerializeField] private float _pressBulge = 0.18f;
        [SerializeField] private float _pressSmooth = 14f;
        [Tooltip("按到这个程度播放声音")]
        [SerializeField] private float _pressSoundAt = 0.18f;
        [SerializeField] private float _pressVolume = 1f;
        [SerializeField] private AudioClip _pressClip;

        [Header("按压过久")]
        [Tooltip("保持重压多久后触发受伤")]
        [SerializeField] private float _pressHurtSeconds = 5f;
        [Tooltip("按压程度达到多少才算“按住”（0~1）")]
        [SerializeField] private float _pressHurtThreshold = 0.75f;
        [Tooltip("有这条动画就保持眩晕姿势")]
        [SerializeField] private string _dizzinessClip = "dizziness";
        [SerializeField] private string[] _pressTooLongLines =
        {
            "……被按傻了。",
            "主人，我好像听见星星在唱歌。",
            "晕……我是谁，我在哪。",
            "再按下去脑子要扁了。",
            "……刚才发生什么了。",
        };
        [SerializeField] private float _pressVoiceVolume = 6f;
        [SerializeField] private float _pressLineSeconds = 3f;
        [SerializeField] private Color _pressFlashColor = new Color(1f, 0f, 0f, 0.5f);
        [SerializeField] private float _pressFlashSeconds = 0.5f;

        [Header("按傻星星")]
        [SerializeField] private int _starCount = 10;
        [SerializeField] private float _starRadius = 0.5f;
        [SerializeField] private float _starSize = 0.1f;
        [SerializeField] private float _starHeight = 0.3f;
        [Tooltip("星星绕行速度（弧度/秒）；参考模组在 20 tick/秒下约等于 3")]
        [SerializeField] private float _starSpeed = 3f;

        [Header("普通台词")]
        [SerializeField] private string[] _rubLines =
        {
            "唔……",
            "好舒服……",
            "再摸摸……",
            "嗯……",
            "……还行。",
        };
        [SerializeField] private string[] _pressLines =
        {
            "唔！",
            "轻一点……",
            "别按那么用力……",
            "头要扁了……",
            "……别按了。",
        };
        [SerializeField] private float _normalLineCooldownSeconds = 2.5f;
        [SerializeField] private float _normalLineSeconds = 2f;

        public GameObject FirePrefab
        {
            get { return _firePrefab; }
        }

        public bool PlayMaidPetAnimation
        {
            get { return _playMaidPetAnimation; }
        }

        public string PetReactionClip
        {
            get { return _petReactionClip; }
        }

        public string PetHoldClip
        {
            get { return _petHoldClip; }
        }

        public float HeadStartWidthRatio
        {
            get { return _headStartWidthRatio; }
        }

        public float HeadStartDepthRatio
        {
            get { return _headStartDepthRatio; }
        }

        public float HeadStartAboveRatio
        {
            get { return _headStartAboveRatio; }
        }

        public float RubIgniteSpeed
        {
            get { return _rubIgniteSpeed; }
        }

        public float RubHeatRate
        {
            get { return _rubHeatRate; }
        }

        public float RubHeatDecay
        {
            get { return _rubHeatDecay; }
        }

        public float FireScale
        {
            get { return _fireScale; }
        }

        public Vector3 FireCameraOffset
        {
            get { return _fireCameraOffset; }
        }

        public Color BurnColor
        {
            get { return _burnColor; }
        }

        public float FireSeconds
        {
            get { return _fireSeconds; }
        }

        public float FireLineSeconds
        {
            get { return _fireLineSeconds; }
        }

        public AudioClip FireEndClip
        {
            get { return _fireEndClip; }
        }

        public float FireEndVolume
        {
            get { return _fireEndVolume; }
        }

        public string BurnReactionClip
        {
            get { return _burnReactionClip; }
        }

        public string WuyuClip
        {
            get { return _wuyuClip; }
        }

        public float WuyuWeight
        {
            get { return _wuyuWeight; }
        }

        public float WuyuFadeSeconds
        {
            get { return _wuyuFadeSeconds; }
        }

        public string[] FireLines
        {
            get { return _fireLines; }
        }

        public float RubRollDegrees
        {
            get { return _rubRollDegrees; }
        }

        public float RubYawDegrees
        {
            get { return _rubYawDegrees; }
        }

        public float RubSmooth
        {
            get { return _rubSmooth; }
        }

        public float RubStep
        {
            get { return _rubStep; }
        }

        public float RubVolume
        {
            get { return _rubVolume; }
        }

        public AudioClip RubClip
        {
            get { return _rubClip; }
        }

        public float PressRange
        {
            get { return _pressRange; }
        }

        public float PressStartOffset
        {
            get { return _pressStartOffset; }
        }

        public float PressSquash
        {
            get { return _pressSquash; }
        }

        public float PressBulge
        {
            get { return _pressBulge; }
        }

        public float PressSmooth
        {
            get { return _pressSmooth; }
        }

        public float PressSoundAt
        {
            get { return _pressSoundAt; }
        }

        public float PressVolume
        {
            get { return _pressVolume; }
        }

        public AudioClip PressClip
        {
            get { return _pressClip; }
        }

        public float PressHurtSeconds
        {
            get { return _pressHurtSeconds; }
        }

        public float PressHurtThreshold
        {
            get { return _pressHurtThreshold; }
        }

        public string DizzinessClip
        {
            get { return _dizzinessClip; }
        }

        public string[] PressTooLongLines
        {
            get { return _pressTooLongLines; }
        }

        public float PressVoiceVolume
        {
            get { return _pressVoiceVolume; }
        }

        public float PressLineSeconds
        {
            get { return _pressLineSeconds; }
        }

        public Color PressFlashColor
        {
            get { return _pressFlashColor; }
        }

        public float PressFlashSeconds
        {
            get { return _pressFlashSeconds; }
        }

        public int StarCount
        {
            get { return _starCount; }
        }

        public float StarRadius
        {
            get { return _starRadius; }
        }

        public float StarSize
        {
            get { return _starSize; }
        }

        public float StarHeight
        {
            get { return _starHeight; }
        }

        public float StarSpeed
        {
            get { return _starSpeed; }
        }

        public string[] RubLines
        {
            get { return _rubLines; }
        }

        public string[] PressLines
        {
            get { return _pressLines; }
        }

        public float NormalLineCooldownSeconds
        {
            get { return _normalLineCooldownSeconds; }
        }

        public float NormalLineSeconds
        {
            get { return _normalLineSeconds; }
        }

    }
}
