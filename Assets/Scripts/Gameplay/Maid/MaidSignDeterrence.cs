using System.Collections.Generic;
using MaidHome.Gameplay.House;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 游街示众的看客反应（照原模组 MaidSignDeterrence）：身上挂着牌子的女仆，会让附近看见她的
    /// 别的女仆被吓住——停下脚步、扭头看过去、冒一句害怕的话。
    /// 原模组被吓住的其实是"自主 AI + 受惊表现"，这里就是暂停游走 + 扭头 + 气泡；
    /// 同一只女仆一次围观只吭一声，走开再回来看才会重新触发。
    /// 谁身上挂着牌子由 MaidSignBoard.Worn 说了算，牌子还没写字的预览不算。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidSignDeterrence : MonoBehaviour
    {
        /// <summary>扫描间隔（原模组是 40 tick 一次），2 秒</summary>
        const float ScanInterval = 2f;
        /// <summary>能"看到"的距离（格）</summary>
        const float SightRadius = 12f;
        /// <summary>单次被吓住多久；只要还在看就一直续</summary>
        const float DeterrenceSeconds = 3f;
        /// <summary>中断超过这么久再看到，才算新的一次围观</summary>
        const float EncounterGap = 5f;
        const float BubbleSeconds = 4f;
        /// <summary>记住了多久没再见到的看客就可以忘掉</summary>
        const float ForgetSeconds = 60f;

        static readonly string[] WitnessLines =
        {
            "……我不想有一天也被挂上去。",
            "连她都被挂上去了……我还是安分点吧。",
        };

        static MaidSignDeterrence _instance;

        sealed class Scare
        {
            public MaidAgent Agent;
            public float Until;
            public bool PausedByUs;
        }

        readonly List<Scare> _scares = new List<Scare>();
        readonly Dictionary<MaidAgent, float> _lastSeen = new Dictionary<MaidAgent, float>();
        readonly List<MaidAgent> _forget = new List<MaidAgent>();
        MaidInteractionController _controller;
        float _nextScan;

        /// <summary>有人挂上牌子了：保证场上有一个扫描器（原模组是常驻的 ServerTickEvent）</summary>
        public static void Ensure()
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<MaidSignDeterrence>();
            }

            if (_instance != null)
            {
                return;
            }

            GameObject host = new GameObject("MaidSignDeterrence");
            _instance = host.AddComponent<MaidSignDeterrence>();
        }

        void OnDestroy()
        {
            for (int i = 0; i < _scares.Count; i++)
            {
                Release(_scares[i]);
            }

            _scares.Clear();
            _lastSeen.Clear();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (_controller == null)
            {
                _controller = FindObjectOfType<MaidInteractionController>();
            }

            ReleaseExpired(now);
            if (now < _nextScan)
            {
                return;
            }

            _nextScan = now + ScanInterval;
            Scan(now);
        }

        void ReleaseExpired(float now)
        {
            for (int i = _scares.Count - 1; i >= 0; i--)
            {
                Scare scare = _scares[i];
                if (scare.Agent == null || !scare.Agent.gameObject.activeInHierarchy || now >= scare.Until)
                {
                    Release(scare);
                    _scares.RemoveAt(i);
                }
            }
        }

        void Scan(float now)
        {
            List<MaidSignBoard> boards = MaidSignBoard.Active;
            if (boards.Count == 0)
            {
                Forget(now);
                return;
            }

            MaidAgent[] maids = FindObjectsOfType<MaidAgent>();
            for (int b = 0; b < boards.Count; b++)
            {
                MaidSignBoard board = boards[b];
                if (board == null || !board.Worn || board.Data == null || !board.Data.Scare)
                {
                    continue;
                }

                MaidAgent source = board.Agent;
                if (source == null || !source.gameObject.activeInHierarchy)
                {
                    continue;
                }

                for (int m = 0; m < maids.Length; m++)
                {
                    MaidAgent witness = maids[m];
                    if (witness == null || witness == source || !witness.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    // 正在被摸 / 被梳 / 正开着面板的那只先不打断
                    if (_controller != null && _controller.Current == witness)
                    {
                        continue;
                    }

                    Vector3 offset = witness.transform.position - source.transform.position;
                    if (offset.sqrMagnitude > SightRadius * SightRadius)
                    {
                        continue;
                    }

                    if (!CanSee(witness.transform.position, source.transform.position))
                    {
                        continue;
                    }

                    Deter(now, source, witness);
                }
            }

            Forget(now);
        }

        void Deter(float now, MaidAgent source, MaidAgent witness)
        {
            MaidWanderer wanderer = witness.Wanderer;
            Scare scare = null;
            for (int i = 0; i < _scares.Count; i++)
            {
                if (_scares[i].Agent == witness)
                {
                    scare = _scares[i];
                    break;
                }
            }

            if (scare == null)
            {
                scare = new Scare();
                scare.Agent = witness;
                // 本来就是暂停的（玩家开着她面板之类）就别抢，回头也别替别人恢复
                scare.PausedByUs = wanderer != null && !wanderer.Paused;
                _scares.Add(scare);
            }

            if (scare.PausedByUs && wanderer != null && !wanderer.Paused)
            {
                wanderer.SetPaused(true);
            }

            scare.Until = now + DeterrenceSeconds;
            if (wanderer != null)
            {
                wanderer.FaceDirection(source.transform.position - witness.transform.position, 0.4f);
            }

            float last;
            bool seen = _lastSeen.TryGetValue(witness, out last);
            if (seen && now - last < EncounterGap)
            {
                return;
            }

            _lastSeen[witness] = now;
            MaidChatBubble.Show(witness, WitnessLines[Random.Range(0, WitnessLines.Length)], BubbleSeconds);
        }

        void Release(Scare scare)
        {
            if (scare == null || scare.Agent == null || !scare.PausedByUs)
            {
                return;
            }

            // 玩家正开着她的面板 / 互动：这一份暂停是人家要的，别替他把人放走
            if (_controller != null && _controller.Current == scare.Agent)
            {
                return;
            }

            MaidWanderer wanderer = scare.Agent.Wanderer;
            if (wanderer != null && wanderer.Paused)
            {
                wanderer.SetPaused(false);
            }
        }

        void Forget(float now)
        {
            _forget.Clear();
            foreach (KeyValuePair<MaidAgent, float> pair in _lastSeen)
            {
                if (pair.Key == null || now - pair.Value > ForgetSeconds)
                {
                    _forget.Add(pair.Key);
                }
            }

            for (int i = 0; i < _forget.Count; i++)
            {
                _lastSeen.Remove(_forget[i]);
            }

            _forget.Clear();
        }

        /// <summary>眼睛高度之间连线，被房子挡住就"看不见"（女仆自己的碰撞体不算遮挡）</summary>
        static bool CanSee(Vector3 from, Vector3 to)
        {
            Vector3 start = from + Vector3.up * 1.45f;
            Vector3 end = to + Vector3.up * 1.45f;
            RaycastHit hit;
            if (!Physics.Linecast(start, end, out hit, ~0, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.collider == null || hit.collider.GetComponentInParent<HouseGridView>() == null;
        }
    }
}
