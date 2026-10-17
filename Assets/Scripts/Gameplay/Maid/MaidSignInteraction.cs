using System;
using System.Collections;
using MaidHome.Core.Input;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 挂牌（游街示众）模式。点女仆面板的「挂牌」进来：相机取景到她胸口，上方弹出编辑面板，
    /// 改一个字牌面上就变一个字（实时预览）。
    /// 「完成」写进她自己的文件夹（saves/maid/&lt;uuid&gt;/attachments.json），「取下」摘掉，「返回」不保存。
    /// 原模组（Love-And-Loathe 的 MaidSignManager / MaidSignEditScreen）是手持告示牌右键挂上去、
    /// 在女仆状态界面里编辑；本工程统一走"互动模式"这条路，和摸脸 / 梳毛一个套路。
    /// 牌子本体在 MaidSignBoard，旁观女仆的反应在 MaidSignDeterrence。
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class MaidSignInteraction : MonoBehaviour
    {
        public event Action Ended;

        /// <summary>挂上 / 改完之后她自己说的几句（原模组这条走 LLM，这里给几句现成的）</summary>
        static readonly string[] ParadeLines =
        {
            "……主人，这块牌子真的要挂出去吗？",
            "上面写的东西，路过的人都看得见啊……",
            "呜呜，我又没做错什么……",
        };

        static readonly string[] RemoveLines =
        {
            "牌子终于取下来了……",
            "呼，不用再挂着那个了。",
        };

        [Header("配置（接了就覆盖下面的默认参数）")]
        [SerializeField] private MaidSignSettings _settings;

        [Tooltip("挂牌模式把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        private float _signOrthographicSize = 0.75f;
        [Tooltip("挂牌模式的俯角（度）：牌子挂在胸口、是竖着的板面，俯得太狠看不清字；-1 = 沿用 HouseCameraFitter 的设置")]
        private float _signFocusPitchDegrees = 25f;
        [Tooltip("牌子挂件配置：预制体留空就按 Resources/MaidAttachment/sign 找；挂点/大小/朝向都在这里调")]
        private MaidAttachmentStyle _style = new MaidAttachmentStyle { SizeRatio = 0.3f };
        [Tooltip("编辑面板，留空就用同一物体上的 MaidSignPanel，都没有就自动生成一块。"
            + "要接自己的 UI 就把 MaidSignPanel 在场景里挂好（填它的 Panel / 输入框 / 按钮 / 色板），再拖到这里")]
        [SerializeField] private MaidSignPanel _panel;

        MaidAgent _agent;
        MaidAttachmentStore _store;
        MaidSignData _working;
        MaidSignBoard _board;
        string _hint = "";
        Coroutine _refocusRoutine;
        bool _refocus;

        public bool IsActive { get; private set; }

        void Awake()
        {
            MaidInteractionConfigApplier.ApplyRequired(_settings, this, this, "MaidSignInteraction");
            // 配置登记给全局：从存档恢复时也用同一套参数挂牌子
            MaidAttachmentStyles.UseSign(_style);
        }

        /// <summary>编辑中的那份（面板每次改动都直接写它，牌面照它画）</summary>
        public MaidSignData Data
        {
            get { return _working; }
        }

        /// <summary>挂牌对模型没要求（挂点认不出就按身高比例挂），有存档就能挂</summary>
        public bool Supports(MaidAgent agent)
        {
            return _settings != null && agent != null;
        }

        public bool Begin(MaidAgent agent)
        {
            if (_settings == null || agent == null)
            {
                return false;
            }

            Abort();

            _agent = agent;
            // 面板里可能已经被别的逻辑放过暂停（例如附近挂着牌子的女仆吓到她），进模式再钉一次
            if (agent.Wanderer != null)
            {
                agent.Wanderer.SetPaused(true);
            }

            _store = MaidAttachmentStore.Load(agent.Save);
            _working = _store.Sign.Clone();
            _board = MaidSignBoard.Attach(agent, _working, true);
            EnsurePanel();
            if (_panel != null)
            {
                _panel.Show(_working);
                _panel.SetHint(_hint);
            }

            IsActive = true;
            _refocus = false;
            if (_refocusRoutine != null)
            {
                StopCoroutine(_refocusRoutine);
            }

            _refocusRoutine = StartCoroutine(RefocusRoutine());
            return true;
        }

        /// <summary>玩家自己退出：退回去重新打开女仆面板</summary>
        public void End()
        {
            if (!IsActive)
            {
                return;
            }

            Release();
            if (Ended != null)
            {
                Ended();
            }
        }

        /// <summary>被外层关掉：不回调</summary>
        public void Abort()
        {
            Release();
        }

        void OnDestroy()
        {
            Release();
            Ended = null;
        }

        void Release()
        {
            if (_refocusRoutine != null)
            {
                StopCoroutine(_refocusRoutine);
                _refocusRoutine = null;
            }

            _refocus = false;

            if (_board != null && _store != null)
            {
                // 预览告一段落：牌面回到存档里那份（挂着的还是挂着，没挂的就收起来）
                _board.Apply(_store.Sign, false);
            }

            if (_panel != null)
            {
                _panel.Hide();
            }

            _board = null;
            _store = null;
            _working = null;
            _agent = null;
            IsActive = false;
        }

        void Update()
        {
            if (!IsActive)
            {
                return;
            }

            if (_agent == null || !_agent.gameObject.activeInHierarchy)
            {
                Abort();
                return;
            }

            // Android 返回键 / 电脑 Esc = 不保存退出
            if (PointerInput.BackPressed)
            {
                OnBack();
            }
        }

        /// <summary>取景用哪块包围盒：牌子本身（连头一起框进来）</summary>
        public bool TryGetFocusBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_agent == null)
            {
                return false;
            }

            Bounds board;
            if (_board != null && _board.TryGetBounds(out board))
            {
                bounds = board;
                bounds.Expand(new Vector3(0.55f, 0.95f, 0.55f));
                return true;
            }

            // 牌子还没建出来（挂不上）就框她上半身
            Bounds all = _agent.GetBounds();
            Vector3 center = new Vector3(all.center.x, all.min.y + all.size.y * 0.72f, all.center.z);
            bounds = new Bounds(center, new Vector3(all.size.x * 0.8f, all.size.y * 0.5f, all.size.z * 0.8f));
            return true;
        }

        public float FocusOrthographicSize
        {
            get { return _signOrthographicSize; }
        }

        public float FocusPitchDegrees
        {
            get { return _signFocusPitchDegrees; }
        }

        /// <summary>她转稳了要不要重新取一次景（外层 Update 里轮询）</summary>
        public bool ConsumeRefocusRequest()
        {
            if (!_refocus)
            {
                return false;
            }

            _refocus = false;
            return true;
        }

        /// <summary>
        /// 面板里她转向镜头是平滑的；玩家刚打开面板就点「挂牌」的话，取景会抓在转到一半的朝向上。
        /// 等她朝向两帧基本不动（最多 0.8 秒）再让外层重新取一次景。
        /// </summary>
        IEnumerator RefocusRoutine()
        {
            float deadline = Time.unscaledTime + 0.8f;
            Quaternion last = _agent != null ? _agent.transform.rotation : Quaternion.identity;
            int stable = 0;
            while (Time.unscaledTime < deadline)
            {
                yield return null;
                if (_agent == null)
                {
                    yield break;
                }

                Quaternion now = _agent.transform.rotation;
                if (Quaternion.Angle(last, now) < 0.2f)
                {
                    stable++;
                    if (stable >= 2)
                    {
                        break;
                    }
                }
                else
                {
                    stable = 0;
                }

                last = now;
            }

            _refocus = true;
            _refocusRoutine = null;
        }

        // ------------------------------------------------------------ 面板

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidSignPanel>();
            }

            if (_panel == null)
            {
                // 场景里恰好摆了一份（挂在别的物体上）就用它；有多份就不猜，自己生成一份
                _panel = FindSingleScenePanel();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidSignPanel>();
            }

            _panel.DoneRequested -= OnDone;
            _panel.DoneRequested += OnDone;
            _panel.RemoveRequested -= OnRemove;
            _panel.RemoveRequested += OnRemove;
            _panel.BackRequested -= OnBack;
            _panel.BackRequested += OnBack;
            _panel.PreviewChanged -= OnPreviewChanged;
            _panel.PreviewChanged += OnPreviewChanged;
            _hint = Hint();
        }

        static MaidSignPanel FindSingleScenePanel()
        {
            MaidSignPanel[] all = FindObjectsOfType<MaidSignPanel>(true);
            MaidSignPanel found = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = all[i];
            }

            return found;
        }

        string Hint()
        {
            return "四行文字每行最多 " + MaidSignData.MaxLineLength + " 字；一个字都不写等于没挂";
        }

        void OnPreviewChanged(string[] lines, string color, bool scare)
        {
            if (_working == null)
            {
                return;
            }

            for (int i = 0; i < MaidSignData.MaxLines; i++)
            {
                string line = lines != null && i < lines.Length ? lines[i] : "";
                if (line == null)
                {
                    line = "";
                }

                if (line.Length > MaidSignData.MaxLineLength)
                {
                    line = line.Substring(0, MaidSignData.MaxLineLength);
                }

                _working.Lines[i] = line;
            }

            _working.Color = MaidSignPalette.Normalize(color);
            _working.Scare = scare;
            if (_board != null)
            {
                _board.Apply(_working, true);
            }
        }

        /// <summary>「完成」：挂上并写进她的文件夹</summary>
        void OnDone()
        {
            if (_working == null || _store == null)
            {
                return;
            }

            if (_working.IsBlank)
            {
                // 空牌子没东西可示众，按"取下"处理
                RemoveFromStore();
                return;
            }

            _working.Enabled = true;
            Copy(_working, _store.Sign);
            if (!Save())
            {
                return;
            }

            _board = MaidSignBoard.Attach(_agent, _store.Sign, false);
            MaidChatBubble.Show(_agent, Pick(ParadeLines), 5f);
            End();
        }

        /// <summary>「取下」：摘掉牌子</summary>
        void OnRemove()
        {
            RemoveFromStore();
        }

        void RemoveFromStore()
        {
            if (_store == null)
            {
                return;
            }

            _store.Sign.Clear();
            if (!Save())
            {
                return;
            }

            if (_board != null)
            {
                _board.Apply(_store.Sign, false);
            }

            if (_agent != null)
            {
                MaidChatBubble.Show(_agent, Pick(RemoveLines), 4f);
            }

            End();
        }

        /// <summary>「返回」：这次改的都不要</summary>
        void OnBack()
        {
            if (_board != null && _store != null)
            {
                _board.Apply(_store.Sign, false);
            }

            End();
        }

        bool Save()
        {
            string error;
            if (_store.Save(out error))
            {
                return true;
            }

            if (_panel != null)
            {
                _panel.SetHint("存不进她的文件夹：" + error + "（改完再点一次试试）");
            }

            Debug.LogWarning("[挂牌] " + error, this);
            return false;
        }

        static void Copy(MaidSignData from, MaidSignData to)
        {
            to.Enabled = from.Enabled;
            to.Color = from.Color;
            to.Scare = from.Scare;
            for (int i = 0; i < MaidSignData.MaxLines; i++)
            {
                to.Lines[i] = from.Lines[i];
            }
        }

        static string Pick(string[] lines)
        {
            return lines[UnityEngine.Random.Range(0, lines.Length)];
        }
    }
}
