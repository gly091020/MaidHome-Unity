using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 闲置时偶尔冒一个气泡。机制照 TLM 的 RandomEmoji.tick：每隔 _checkSeconds 才掷一次骰子、
    /// 头上已经有气泡就不冒、空闲用 kaomoji.json 的 idle + core 两组。
    /// 差别是我们只做文字气泡，表情包（图片）那一半权重直接跳过。
    /// 彩蛋女仆（名字 5112151111121）另外一条路：每 _easterEggSeconds 一条、不等上一条消失，
    /// 台词换成 gly_lines.json 的 lines（见 MaidEasterEgg）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidIdleBubble : MonoBehaviour
    {
        [Tooltip("每隔多少秒掷一次骰子（TLM 的 EmojiCheckRate 是 60 秒）")]
        [SerializeField] private float _checkSeconds = 30f;
        [Tooltip("掷中的概率；TLM 里表情包和颜文字各占一半权重，所以默认 0.5")]
        [SerializeField] private float _chance = 0.5f;
        [Tooltip("气泡显示多久，0 = 用 MaidChatBubble 的默认值（15 秒）")]
        [SerializeField] private float _showSeconds;
        [Tooltip("彩蛋女仆（名字 5112151111121）的间隔：她说的是 gly_lines.json 的台词，5 秒一条")]
        [SerializeField] private float _easterEggSeconds = 5f;

        MaidAgent _agent;
        MaidWanderer _wanderer;
        float _nextCheckAt;

        void Awake()
        {
            _agent = GetComponent<MaidAgent>();
            _wanderer = GetComponent<MaidWanderer>();
            // 每只女仆错开相位，免得一群一起冒
            float interval = MaidEasterEgg.IsEasterEgg(_agent) ? _easterEggSeconds : _checkSeconds;
            _nextCheckAt = Time.time + Random.Range(0f, Mathf.Max(1f, interval));
        }

        void Update()
        {
            if (_agent == null)
            {
                return;
            }

            bool easterEgg = MaidEasterEgg.IsEasterEgg(_agent);
            if (Time.time < _nextCheckAt)
            {
                return;
            }

            float interval = Mathf.Max(1f, easterEgg ? _easterEggSeconds : _checkSeconds);
            _nextCheckAt = Time.time + interval;

            // 交互模式里她自己有台词，别插队
            if (_wanderer != null && _wanderer.Paused)
            {
                return;
            }

            if (easterEgg)
            {
                // 彩蛋女仆：每 5 秒一条，不等上一条消失（气泡显示时长也按这个间隔算）
                string easterLine = MaidEasterEgg.PickIdleLine(_agent);
                if (!string.IsNullOrEmpty(easterLine))
                {
                    MaidChatBubble.Show(_agent, easterLine, interval);
                }

                return;
            }

            // TLM 也是"头上已经有气泡就不冒"
            if (!MaidChatBubble.IsEmpty(_agent))
            {
                return;
            }

            if (Random.value > Mathf.Clamp01(_chance))
            {
                return;
            }

            string line = MaidKaomoji.Random("idle");
            if (!string.IsNullOrEmpty(line))
            {
                MaidChatBubble.Show(_agent, line, _showSeconds);
            }
        }
    }
}
