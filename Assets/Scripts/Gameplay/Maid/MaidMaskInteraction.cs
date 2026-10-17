using System;
using System.Collections;
using MaidHome.Core.Input;
using MaidHome.Core.Storage;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 面具模式：点女仆面板的「面具」进来，相机推近到脸上，「选照片」从系统选择器里挑一张
    /// （电脑 = 打开文件对话框，手机 = 系统照片选择器，见 PhotoPicker），选完裁成正方形贴到面具正面。
    /// 照片按女仆存：saves/maid/&lt;uuid&gt;/mask.png，记录写在 attachments.json 的 mask 段里。
    /// 和示众牌是同一套「挂件」框架（MaidAttachmentStyles + MaidAttachmentInstance），
    /// 区别只是示众牌写字、面具贴图。
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class MaidMaskInteraction : MonoBehaviour
    {
        public event Action Ended;

        static readonly string[] WearLines =
        {
            "这个面具……主人是从哪找出来的？",
            "戴着还挺合适？",
            "唔，照片贴得正不正啊……",
        };

        static readonly string[] RemoveLines =
        {
            "呼，摘下来了。",
            "不戴了不戴了。",
        };

        [Header("配置（接了就覆盖下面的默认参数）")]
        [SerializeField] private MaidMaskSettings _settings;

        [Header("挂件配置（预制体留空就按 Resources/MaidAttachment/mask 找）")]
        private MaidAttachmentStyle _style = new MaidAttachmentStyle { SizeRatio = 1.15f };
        [Tooltip("面具模式把相机推近到这个正交尺寸（0 = 不推近，保持点开女仆的取景）")]
        private float _maskOrthographicSize = 0.35f;
        [Tooltip("面具模式的俯角（度）：-1 = 沿用 HouseCameraFitter 的设置")]
        private float _maskFocusPitchDegrees = -1f;

        [Header("自定义 UI（不接就用同一物体上的 / 自动生成一块）")]
        [SerializeField] private MaidMaskPanel _panel;

        MaidAgent _agent;
        MaidAttachmentStore _store;
        MaidMaskData _working;
        MaidMaskView _view;
        bool _picking;
        Coroutine _refocusRoutine;
        bool _refocus;

        public bool IsActive { get; private set; }

        public float FocusOrthographicSize
        {
            get { return _maskOrthographicSize; }
        }

        public float FocusPitchDegrees
        {
            get { return _maskFocusPitchDegrees; }
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

        /// <summary>和挂牌一样：等她转稳再取景，别抓在转到一半的朝向上</summary>
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

        void Awake()
        {
            MaidInteractionConfigApplier.ApplyRequired(_settings, this, this, "MaidMaskInteraction");
            // 把配置登记给全局：从存档恢复时也按同一套参数挂，两处调不一样就怪了
            MaidAttachmentStyles.UseMask(_style);
        }

        /// <summary>有面具预制体就能戴</summary>
        public bool Supports(MaidAgent agent)
        {
            return _settings != null && agent != null && MaidMaskView.Supports();
        }

        public bool Begin(MaidAgent agent)
        {
            if (_settings == null || agent == null)
            {
                return false;
            }

            Abort();

            _agent = agent;
            // 和挂牌一样：进来先把她的游走钉住，别让别的逻辑（看客威慑之类）把她放走
            if (agent.Wanderer != null)
            {
                agent.Wanderer.SetPaused(true);
            }

            _store = MaidAttachmentStore.Load(agent.Save);
            _working = _store.Mask.Clone();
            _view = MaidMaskView.Attach(agent, _working, true);
            if (_view == null)
            {
                Debug.LogWarning("面具挂不上去（没有预制体？）：" + agent.name, this);
                Release();
                return false;
            }

            EnsurePanel();
            _picking = false;
            if (_panel != null)
            {
                _panel.Show(_working);
                _panel.SetHint(Hint());
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

            if (_view != null && _store != null)
            {
                // 预览告一段落：回到存档里那份（戴着的还是戴着）
                _view.Apply(_store.Mask, false);
            }

            if (_panel != null)
            {
                _panel.Hide();
            }

            _view = null;
            _store = null;
            _working = null;
            _agent = null;
            _picking = false;
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

        /// <summary>取景框：面具本身（连脸一起框进来），没有面具就框头上那一段</summary>
        public bool TryGetFocusBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_agent == null)
            {
                return false;
            }

            Bounds mask;
            if (_view != null && _view.TryGetBounds(out mask))
            {
                bounds = mask;
                bounds.Expand(mask.size * 0.6f);
                return true;
            }

            Bounds all = _agent.GetBounds();
            Vector3 center = new Vector3(all.center.x, all.max.y - all.size.y * 0.22f, all.center.z);
            bounds = new Bounds(center, new Vector3(all.size.x * 0.6f, all.size.y * 0.36f, all.size.z * 0.6f));
            return true;
        }

        // ------------------------------------------------------------ 面板

        void EnsurePanel()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidMaskPanel>();
            }

            if (_panel == null)
            {
                _panel = FindSingleScenePanel();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidMaskPanel>();
            }

            _panel.PickRequested -= OnPick;
            _panel.PickRequested += OnPick;
            _panel.ClearPhotoRequested -= OnClearPhoto;
            _panel.ClearPhotoRequested += OnClearPhoto;
            _panel.DoneRequested -= OnDone;
            _panel.DoneRequested += OnDone;
            _panel.RemoveRequested -= OnRemove;
            _panel.RemoveRequested += OnRemove;
            _panel.BackRequested -= OnBack;
            _panel.BackRequested += OnBack;
        }

        static MaidMaskPanel FindSingleScenePanel()
        {
            MaidMaskPanel[] all = FindObjectsOfType<MaidMaskPanel>(true);
            MaidMaskPanel found = null;
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
            if (!PhotoPicker.IsSupported)
            {
                return "这个平台还没有系统照片选择器，先只能戴个空面具";
            }

            return "点「选照片」挑一张图，选完会自动裁成正方形贴到面具正面；照片存在她自己的文件夹里";
        }

        void OnPick()
        {
            if (_picking || _agent == null)
            {
                return;
            }

            if (!PhotoPicker.IsSupported)
            {
                if (_panel != null)
                {
                    _panel.SetHint(Hint());
                }

                return;
            }

            _picking = true;
            if (_panel != null)
            {
                _panel.SetHint("已打开系统的照片选择器……");
            }

            PhotoPicker.Pick(OnPicked);
        }

        void OnPicked(string path)
        {
            _picking = false;
            if (!IsActive || _agent == null || _working == null)
            {
                // 等待期间玩家已经退出了，这一次选择就不要了
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                if (_panel != null)
                {
                    _panel.SetHint(Hint());
                }

                return;
            }

            string error;
            string file = MaidMaskPhoto.Import(_agent.Save, path, out error);
            if (string.IsNullOrEmpty(file))
            {
                if (_panel != null)
                {
                    _panel.SetHint("这张照片没能贴上去：" + error);
                }

                Debug.LogWarning("[面具] 导入照片失败：" + error, this);
                return;
            }

            _working.Photo = file;
            _working.Enabled = true;
            if (_view != null)
            {
                _view.ReloadPhoto();
                _view.Apply(_working, true);
            }

            if (_panel != null)
            {
                _panel.SetPhoto(_working);
                _panel.SetHint("照片贴好了，点「完成」就给她戴上");
            }
        }

        void OnClearPhoto()
        {
            if (_working == null)
            {
                return;
            }

            _working.Photo = "";
            if (_view != null)
            {
                _view.Apply(_working, true);
            }

            if (_panel != null)
            {
                _panel.SetPhoto(_working);
            }
        }

        void OnDone()
        {
            if (_working == null || _store == null)
            {
                return;
            }

            _working.Enabled = true;
            Copy(_working, _store.Mask);
            if (!Save())
            {
                return;
            }

            if (_view != null)
            {
                _view.Apply(_store.Mask, false);
            }

            if (_agent != null)
            {
                MaidChatBubble.Show(_agent, Pick(WearLines), 4f);
            }

            End();
        }

        void OnRemove()
        {
            if (_store == null)
            {
                return;
            }

            _store.Mask.Clear();
            if (!Save())
            {
                return;
            }

            if (_view != null)
            {
                _view.Apply(_store.Mask, false);
            }

            if (_agent != null)
            {
                MaidChatBubble.Show(_agent, Pick(RemoveLines), 4f);
            }

            End();
        }

        void OnBack()
        {
            if (_view != null && _store != null)
            {
                _view.Apply(_store.Mask, false);
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

            Debug.LogWarning("[面具] " + error, this);
            return false;
        }

        static void Copy(MaidMaskData from, MaidMaskData to)
        {
            to.Enabled = from.Enabled;
            to.Photo = from.Photo;
        }

        static string Pick(string[] lines)
        {
            return lines[UnityEngine.Random.Range(0, lines.Length)];
        }
    }
}
