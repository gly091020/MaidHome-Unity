using MaidHome.Gameplay.Maid;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Ar
{
    /// <summary>
    /// AR 里的站 / 坐 / 睡。
    ///
    /// 站和坐各两条路：有动画的（gecko / animation.json 那批）直接播 idle / sit；方块模型
    /// （Java 统一驱动、没有 animation.json）走 MaidSimpleBedrockAnimator 的程序化姿势。
    ///
    /// 睡觉两条路：有 sleep 动画的（gecko 那批）播 sleep 拿骨架姿势，方块模型（没有 sleep）走
    /// MaidSimpleBedrockAnimator 的程序化姿势。**两条路都要再整体向后放倒 90°**：原版那边让睡觉
    /// 实体躺下的是渲染器对实体的整体旋转，动画自己不负责（踩过：只播 sleep，人是立着的）。
    ///
    /// 给 MaidArPlacement 两个补偿量，单位都是模型自己的米（placement 会乘当前缩放）：
    /// SitHeightOffset / SleepHeightOffset 是竖直位移，Tilt 是根节点的额外倾斜。
    ///
    /// 开 AR 的时候 MaidWanderer 是关掉的，所以这里不用担心和状态机抢动画。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidArPose : MonoBehaviour
    {
        public enum Pose
        {
            Stand,
            Sit,
            Sleep
        }

        [Header("站 / 坐")]
        [SerializeField] private string _standClip = "idle";
        [Tooltip("坐姿动画名。TLM 的模型里叫 sit")]
        [SerializeField] private string _sitClip = "sit";
        [Tooltip("睡觉动画名。gecko 模型里叫 sleep")]
        [SerializeField] private string _sleepClip = "sleep";
        [Tooltip("有 sit 动画的模型：坐的时候整体挪多少（模型空间的米，一般 0）")]
        [SerializeField] private float _clipSitHeightOffset;
        [Tooltip("方块模型：坐下去整体往下多少（模型空间的米）。TLM 的 sittingPosture 里是 translate(0, 0.3, 0)")]
        [SerializeField] private float _simpleSitDrop = 0.3f;

        [Header("睡觉")]
        [Tooltip("躺下时绕自身 X 轴转多少度，两条路都要：-90 = 向后放倒、仰面躺着、头朝身后")]
        [SerializeField] private float _sleepTiltDegrees = -90f;
        [Tooltip("有 sleep 动画的模型：整体抬高/压低多少（模型空间的米），让她躺平贴地")]
        [SerializeField] private float _clipSleepHeightOffset;
        [Tooltip("方块模型：躺下后整体抬高多少（模型空间的米），让背贴地而不是陷进地面")]
        [SerializeField] private float _simpleSleepHeightOffset = 0.15f;

        BedrockAnimationPlayer _player;
        MaidSimpleBedrockAnimator _simple;
        MaidHurtBlink _blink;
        bool _hasSitClip;
        bool _hasSleepClip;

        /// <summary>当前这只模型能不能坐（两条路有一种就行）</summary>
        public bool CanSit { get; private set; }

        public Pose Current { get; private set; }

        /// <summary>坐姿时整体要挪多少（模型空间的米，负值 = 往下坐）</summary>
        public float SitHeightOffset { get; private set; }

        /// <summary>躺下时整体要挪多少（模型空间的米，正值 = 抬高）</summary>
        public float SleepHeightOffset
        {
            get { return _hasSleepClip ? _clipSleepHeightOffset : _simpleSleepHeightOffset; }
        }

        /// <summary>当前姿势要求根节点额外带的倾斜（只有躺下时不是单位四元数）</summary>
        public Quaternion Tilt { get; private set; }

        /// <summary>接上一只刚加载出来的女仆，默认站姿。</summary>
        public void Bind(GameObject maidRoot)
        {
            _player = maidRoot != null ? maidRoot.GetComponent<BedrockAnimationPlayer>() : null;
            _simple = maidRoot != null ? maidRoot.GetComponent<MaidSimpleBedrockAnimator>() : null;
            _blink = maidRoot != null ? maidRoot.GetComponent<MaidHurtBlink>() : null;
            _hasSitClip = _player != null && _player.HasClip(_sitClip);
            _hasSleepClip = _player != null && _player.HasClip(_sleepClip);
            CanSit = _hasSitClip || _simple != null;
            Tilt = Quaternion.identity;
            Current = Pose.Stand;

            if (_player != null)
            {
                _player.Play(_standClip);
            }

            if (_simple != null)
            {
                _simple.SetSitting(false);
                _simple.SetSleeping(false);
            }

            if (_blink != null)
            {
                _blink.SetClosed(false);
            }

            // 方块模型的坐姿是"腿往前折 65°"，根节点要跟着下沉 0.3 格脚才落地（TLM 的固定值）
            SitHeightOffset = _hasSitClip ? _clipSitHeightOffset : -Mathf.Max(0f, _simpleSitDrop);
        }

        public void Clear()
        {
            _player = null;
            _simple = null;
            _blink = null;
            _hasSitClip = false;
            _hasSleepClip = false;
            CanSit = false;
            Tilt = Quaternion.identity;
            Current = Pose.Stand;
            SitHeightOffset = 0f;
        }

        public bool SetStand()
        {
            if (_player != null && !_player.Play(_standClip))
            {
                return false;
            }

            if (_simple != null)
            {
                _simple.SetSitting(false);
                _simple.SetSleeping(false);
            }

            if (_blink != null)
            {
                _blink.SetClosed(false);
            }

            Current = Pose.Stand;
            Tilt = Quaternion.identity;
            return true;
        }

        public bool SetSit()
        {
            if (!CanSit)
            {
                return false;
            }

            if (_hasSitClip)
            {
                if (!_player.Play(_sitClip))
                {
                    return false;
                }
            }
            else
            {
                _simple.SetSitting(true);
            }

            if (_blink != null)
            {
                _blink.SetClosed(false);
            }

            Current = Pose.Sit;
            Tilt = Quaternion.identity;
            return true;
        }

        /// <summary>睡觉：根节点放倒 + 抬到贴地，闭眼贴片亮起来当闭眼。</summary>
        public bool SetSleep()
        {
            if (_hasSleepClip)
            {
                // 自带那条 sleep 里已经包含躺下，别再叠我们自己的放倒
                if (!_player.Play(_sleepClip))
                {
                    return false;
                }
            }
            else
            {
                if (_simple != null)
                {
                    _simple.SetSleeping(true);
                }

                // 没有 sleep 的那批：别让 idle 的姿势和放倒打架，把主动画停掉
                if (_player != null)
                {
                    _player.Stop();
                }
            }

            if (_blink != null)
            {
                _blink.SetClosed(true);
            }

            Current = Pose.Sleep;
            Tilt = Quaternion.Euler(_sleepTiltDegrees, 0f, 0f);
            return true;
        }
    }
}
