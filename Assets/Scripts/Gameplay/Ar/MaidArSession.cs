using System.Collections.Generic;
using MaidHome.Core.Save;
using MaidHome.Gameplay.Maid;
using MaidHome.Interop.Maid;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

namespace MaidHome.Gameplay.Ar
{
    /// <summary>
    /// AR 模式的总控：列出「在背包里」的女仆、按需加载模型、管放置和坐站、退出时收拾干净。
    ///
    /// 三条硬规矩：
    /// ① **不碰存档**：全程不走 MaidManager（那个只在它自己那份状态上自动保存），AR 里摆出来的
    ///    纯粹是装饰，退出就没了 —— 下一节再说"位置不保存"。
    /// ② 只有 `in_bag: true`（或者存档里还没有记录）的女仆能被放出来。
    /// ③ 场景里不许有 MaidManager：一边恢复一边被 AR 摆弄，存档和背包状态会打架。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidArSession : MonoBehaviour
    {
        [Header("场景引用")]
        [SerializeField] private MaidArPanel _panel;
        [SerializeField] private MaidArPlacement _placement;
        [SerializeField] private MaidArPose _pose;
        [Tooltip("AR 相机（带 ARCameraManager 的那台），留空就用 Camera.main")]
        [SerializeField] private Camera _camera;
        [Tooltip("跟着环境亮度调的太阳（平行光），留空就不调")]
        [SerializeField] private Light _sun;
        [SerializeField] private float _sunMinIntensity = 0.35f;
        [SerializeField] private float _sunMaxIntensity = 1.3f;

        [Header("行为")]
        [Tooltip("点退出回哪个场景")]
        [SerializeField] private string _exitScene = "Main";

        readonly List<MaidSaveData> _candidates = new List<MaidSaveData>();

        MaidAssets _assets;
        string _selectedId = "";
        bool _placed;
        ARCameraManager _cameraManager;

        void Awake()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidArPanel>();
            }

            if (_placement == null)
            {
                _placement = GetComponent<MaidArPlacement>();
            }

            if (_pose == null)
            {
                _pose = GetComponent<MaidArPose>();
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_cameraManager == null && _camera != null)
            {
                _cameraManager = _camera.GetComponent<ARCameraManager>();
            }

            if (_panel == null || _placement == null || _pose == null)
            {
                Debug.LogError("MaidArSession 缺引用：MaidArPanel / MaidArPlacement / MaidArPose 都要接上", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            if (_panel != null)
            {
                _panel.MaidChosen += OnMaidChosen;
                _panel.SitRequested += OnSitRequested;
                _panel.StandRequested += OnStandRequested;
                _panel.SleepRequested += OnSleepRequested;
                _panel.RotateRequested += OnRotateRequested;
                _panel.ScaleRequested += OnScaleRequested;
                _panel.ClearRequested += ClearMaid;
                _panel.ExitRequested += Exit;
            }

            if (_placement != null)
            {
                _placement.PlaneTapped += OnPlaneTapped;
                _placement.PlaneMissed += OnPlaneMissed;
            }

            if (_cameraManager != null)
            {
                _cameraManager.frameReceived += OnCameraFrame;
            }
        }

        void OnDisable()
        {
            if (_panel != null)
            {
                _panel.MaidChosen -= OnMaidChosen;
                _panel.SitRequested -= OnSitRequested;
                _panel.StandRequested -= OnStandRequested;
                _panel.SleepRequested -= OnSleepRequested;
                _panel.RotateRequested -= OnRotateRequested;
                _panel.ScaleRequested -= OnScaleRequested;
                _panel.ClearRequested -= ClearMaid;
                _panel.ExitRequested -= Exit;
            }

            if (_placement != null)
            {
                _placement.PlaneTapped -= OnPlaneTapped;
                _placement.PlaneMissed -= OnPlaneMissed;
            }

            if (_cameraManager != null)
            {
                _cameraManager.frameReceived -= OnCameraFrame;
            }
        }

        void Start()
        {
            // AR 画面靠相机背景，雾一起上来整片会糊成一块：AR 场景不该有 McStyleRig，但防一手
            RenderSettings.fog = false;

            if (_cameraManager != null)
            {
                // 只要亮度：拿它调太阳强度。方向估计各家设备差异大，先不用
                _cameraManager.requestedLightEstimation = LightEstimation.AmbientIntensity;
            }

            if (MaidManager.Instance != null)
            {
                Debug.LogWarning("AR 场景里不该有 MaidManager：AR 的女仆不进存档，两套状态一起跑会打架", this);
            }

            RefreshCandidates();
        }

        void OnDestroy()
        {
            ClearMaid();
        }

        /// <summary>重新扫一遍「在背包里」的女仆（回到 AR 场景时可以再调一次）。</summary>
        public void RefreshCandidates()
        {
            _candidates.Clear();

            List<MaidSaveData> maids = MaidLoader.List();
            MaidWorldSave world = MaidWorldSaveStore.Load();
            for (int i = 0; i < maids.Count; i++)
            {
                MaidWorldRecord record = world != null ? world.Find(maids[i].Id) : null;
                // 没有记录 = 从没放出去过 = 在背包里（和 MaidManager 的缺省一致）
                if (record == null || record.InBag)
                {
                    _candidates.Add(maids[i]);
                }
            }

            if (_panel != null)
            {
                _panel.SetCandidates(_candidates);
                _panel.SetSelected(_selectedId);
            }

            if (_candidates.Count == 0 && _panel != null)
            {
                _panel.SetStatus("背包里没有女仆：先在主界面把她们收回背包再进 AR");
            }
        }

        void OnMaidChosen(string maidId)
        {
            if (string.IsNullOrEmpty(maidId) || maidId == _selectedId)
            {
                return;
            }

            MaidSaveData maid = FindCandidate(maidId);
            if (maid == null)
            {
                Debug.LogWarning("这只女仆不在背包里，不能在 AR 里放出来：" + maidId, this);
                return;
            }

            ClearMaid();

            _assets = MaidLoader.Load(maid);
            if (_assets == null)
            {
                if (_panel != null)
                {
                    _panel.SetStatus("女仆资源加载失败：" + maidId);
                }

                return;
            }

            GameObject root = MaidPlacement.Place(_assets, Vector3.zero, Quaternion.identity);
            if (root == null)
            {
                ClearMaid();
                return;
            }

            // 她在 AR 里只是摆设：不游走、没有地面碰撞体（CharacterController 留着会一直往下掉）
            MaidWanderer wanderer = root.GetComponent<MaidWanderer>();
            if (wanderer != null)
            {
                wanderer.enabled = false;
            }

            CharacterController controller = root.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            _selectedId = maidId;
            _placed = false;
            // 先量坐姿高度再藏起来：Transform 的 lossyScale 在物体关掉以后不好保证
            _pose.Bind(root);
            root.SetActive(false);
            _placement.Detach();
            _placement.SetHeightOffset(0f);

            if (_panel != null)
            {
                _panel.SetSelected(_selectedId);
                _panel.SetPlaced(false, _pose.CanSit);
            }
        }

        void OnPlaneTapped(Pose pose)
        {
            if (_assets == null || _assets.Root == null)
            {
                if (_panel != null)
                {
                    _panel.SetStatus("先在下面选一只女仆，再点地面放下她");
                }

                return;
            }

            _assets.Root.SetActive(true);
            _placement.Attach(_assets.Root.transform);
            ApplyPoseToPlacement();
            _placement.PlaceAt(pose, true);
            _placed = true;

            if (_panel != null)
            {
                _panel.SetPlaced(true, _pose.CanSit);
                _panel.SetScale(_placement.Scale, _placement.MinScale, _placement.MaxScale);
            }
        }

        void OnSitRequested()
        {
            if (!_placed || !_pose.SetSit())
            {
                return;
            }

            ApplyPoseToPlacement();
        }

        void OnSleepRequested()
        {
            if (!_placed || !_pose.SetSleep())
            {
                return;
            }

            ApplyPoseToPlacement();
        }

        /// <summary>点了屏幕却没打到平面：直接说清卡在哪一步，别让人以为是坏了。</summary>
        void OnPlaneMissed()
        {
            if (_panel == null)
            {
                return;
            }

            if (ARSession.state != ARSessionState.SessionTracking)
            {
                _panel.SetStatus("AR 还没开始跟踪（" + ARSession.state + "）：拿着手机对着地面慢慢转一圈");
                return;
            }

            _panel.SetStatus("没打到平面：手机慢慢左右移动一下，等地面识别出来再点（只认水平面，墙面不算）");
        }

        void OnStandRequested()
        {
            if (!_placed || !_pose.SetStand())
            {
                return;
            }

            ApplyPoseToPlacement();
        }

        /// <summary>把当前姿势要的竖直位移和倾斜推给 MaidArPlacement。</summary>
        void ApplyPoseToPlacement()
        {
            float height = 0f;
            if (_pose.Current == MaidArPose.Pose.Sit)
            {
                height = _pose.SitHeightOffset;
            }
            else if (_pose.Current == MaidArPose.Pose.Sleep)
            {
                height = _pose.SleepHeightOffset;
            }

            _placement.SetHeightOffset(height);
            _placement.SetTilt(_pose.Tilt);
        }

        void OnRotateRequested(float degrees)
        {
            if (_placed)
            {
                _placement.Rotate(degrees);
            }
        }

        void OnScaleRequested(float factor)
        {
            if (!_placed)
            {
                return;
            }

            _placement.ApplyScale(_placement.Scale * factor);
            if (_panel != null)
            {
                _panel.SetScale(_placement.Scale, _placement.MinScale, _placement.MaxScale);
            }
        }

        void ClearMaid()
        {
            _placement.Detach();
            _placement.SetHeightOffset(0f);
            _pose.Clear();
            _placed = false;
            _selectedId = "";

            if (_assets != null)
            {
                _assets.Dispose();
                _assets = null;
            }

            if (_panel != null)
            {
                _panel.SetSelected("");
                _panel.SetPlaced(false, false);
            }
        }

        void Exit()
        {
            ClearMaid();
            SceneManager.LoadScene(_exitScene);
        }

        MaidSaveData FindCandidate(string maidId)
        {
            for (int i = 0; i < _candidates.Count; i++)
            {
                MaidSaveData maid = _candidates[i];
                if (maid.Id == maidId || maid.Name == maidId)
                {
                    return maid;
                }
            }

            return null;
        }

        void OnCameraFrame(ARCameraFrameEventArgs args)
        {
            if (_sun == null)
            {
                return;
            }

            float? brightness = args.lightEstimation.averageBrightness;
            if (brightness.HasValue)
            {
                _sun.intensity = Mathf.Lerp(_sunMinIntensity, _sunMaxIntensity,
                    Mathf.Clamp01(brightness.Value));
            }
        }
    }
}
