using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MaidHome.Core.Save;
using MaidHome.Core.Storage;
using MaidHome.Core.UI;
using MaidHome.Gameplay.Maid;
using MaidHome.Interop.House;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 房子切换。扫 saves/house 下的房子；切的时候先把场上女仆**全部收回背包**、
    /// 卸掉旧房子（NavMesh 一起清）、加载新房子重新取景，并记住"下次还进这栋"。
    /// UI 只要调 Switch(id)，再听 Changed / Succeeded / Failed 就行。
    ///
    /// 进游戏时：如果场景里已经有房子（HouseSpawner 加载的）就直接接管，不重复加载；
    /// 等不到就按存档里记的那栋加载。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HouseSwitcher : MonoBehaviour
    {
        public static HouseSwitcher Instance { get; private set; }

        [Tooltip("房子在世界里的落点，一般留 0")]
        [SerializeField] private Vector3 _position = Vector3.zero;
        [SerializeField] private float _yaw;

        [Tooltip("进游戏后等这么久，如果场景里已经有房子就直接接管，不重复加载")]
        [SerializeField] private float _adoptWaitSeconds = 3f;

        [Tooltip("等不到现成的房子就按存档里记的那栋加载")]
        [SerializeField] private bool _loadOnStart = true;

        [Tooltip("切走的房子先留着不销毁，切回来直接拿出来用（省掉解析 glTF 和烘 NavMesh）；留几栋，0 = 不留")]
        [SerializeField] private int _parkedLimit = 1;

        public event Action Changed;
        public event Action<string> Succeeded;
        public event Action<string> Failed;

        readonly List<HouseSaveData> _houses = new List<HouseSaveData>();
        readonly Dictionary<string, GameObject> _parked = new Dictionary<string, GameObject>();
        readonly List<string> _parkedOrder = new List<string>();
        GameObject _root;

        public IList<HouseSaveData> Houses { get { return _houses; } }
        public string CurrentId { get; private set; }
        public bool IsSwitching { get; private set; }
        /// <summary>切换中的提示 / 上一次失败原因，空字符串表示一切正常</summary>
        public string Message { get; private set; } = "";

        public static string DisplayName(HouseSaveData house)
        {
            if (house == null)
            {
                return "";
            }

            return string.IsNullOrEmpty(house.Name) ? house.Id : house.Name;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        IEnumerator Start()
        {
            Refresh();

            // 等现成的房子。正在加载（HouseSpawner 那种）就一直等下去，只按秒数等的话
            // 真机上加载一超时这里就会跟着再加载一栋，场上并排摆出两栋房子
            float deadline = Time.realtimeSinceStartup + Mathf.Max(0f, _adoptWaitSeconds);
            while (!HouseContext.HasHouse
                && (Time.realtimeSinceStartup < deadline || HouseContext.IsLoading))
            {
                yield return null;
            }

            if (HouseContext.HasHouse)
            {
                AdoptCurrent();
                yield break;
            }

            if (_loadOnStart)
            {
                SwitchToSavedOrDefault();
            }
        }

        /// <summary>重新扫一遍房子目录</summary>
        public void Refresh()
        {
            // 一栋都没有时补一个内置示例房间（只补一次，玩家删了不会再冒出来）
            HouseStarter.Ensure(AppPaths.HouseSaveRoot);
            _houses.Clear();
            _houses.AddRange(HouseSaveData.ScanRoot(AppPaths.HouseSaveRoot));
            RaiseChanged();
        }

        /// <summary>
        /// 删掉一栋房子的存档目录（整个文件夹，含模型/贴图/格数据）。
        /// **正在住的这栋不能删**——先切到别的房子再删，不然场上那栋的对象和 NavMesh 都得现场收拾。
        /// </summary>
        public bool Delete(string id, out string error)
        {
            error = "";
            HouseSaveData house = Find(id);
            if (house == null)
            {
                error = "找不到这栋房子";
                return false;
            }

            if (IsCurrent(house.Id))
            {
                error = "正在住的房子不能删，先切到别的";
                return false;
            }

            if (IsSwitching)
            {
                error = "正在切房子，等切完再删";
                return false;
            }

            string name = DisplayName(house);
            try
            {
                // 递归删除前先确认它确实在房子存档目录里，别因为 house.json 写错路径删到别处
                string root = Path.GetFullPath(AppPaths.HouseSaveRoot + Path.DirectorySeparatorChar);
                string folder = Path.GetFullPath(house.Folder);
                if (!folder.StartsWith(root, StringComparison.Ordinal) || folder.Length <= root.Length)
                {
                    error = "这栋房子的目录不在存档目录里，拒绝删除: " + folder;
                    return false;
                }

                // 挂起来（没销毁）的那份也清掉，别留着以后被拿出来
                GameObject parked;
                if (_parked.TryGetValue(house.Id, out parked))
                {
                    _parked.Remove(house.Id);
                    _parkedOrder.Remove(house.Id);
                    if (parked != null)
                    {
                        Destroy(parked);
                    }
                }

                if (Directory.Exists(house.Folder))
                {
                    Directory.Delete(house.Folder, true);
                }
            }
            catch (Exception exception)
            {
                error = "删除失败: " + exception.Message;
                return false;
            }

            _houses.Remove(house);

            // 存档里那栋房子里的女仆收回背包，免得她们的 house_id 指着一个已经没了的房子
            MaidManager maids = MaidManager.Instance;
            if (maids != null)
            {
                maids.OnHouseDeleted(house.Id);
            }

            MaidWorldSave world = MaidWorldSaveStore.Load();
            if (world != null && world.HouseId == house.Id)
            {
                world.HouseId = CurrentId != null ? CurrentId : "";
                MaidWorldSaveStore.Save(world);
            }

            Message = "已删除 " + name;
            RaiseChanged();
            Debug.Log("[房子] 已删除 " + name);
            return true;
        }

        public HouseSaveData Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _houses.Count; i++)
            {
                if (_houses[i].Id == id || _houses[i].Name == id)
                {
                    return _houses[i];
                }
            }

            return null;
        }

        public bool IsCurrent(string id)
        {
            return !string.IsNullOrEmpty(id) && id == CurrentId;
        }

        /// <summary>切到存档里记的那栋（没有记录就用列表里第一栋）</summary>
        public bool SwitchToSavedOrDefault()
        {
            MaidWorldSave world = MaidWorldSaveStore.Load();
            string id = world != null ? world.HouseId : "";
            if (Find(id) == null)
            {
                id = _houses.Count > 0 ? _houses[0].Id : "";
            }

            if (string.IsNullOrEmpty(id))
            {
                Fail("没有可用的房子存档，检查目录：" + AppPaths.HouseSaveRoot);
                return false;
            }

            return Switch(id);
        }

        /// <summary>开始切换（异步）。切换期间再调会直接返回 false。</summary>
        public bool Switch(string id)
        {
            if (IsSwitching)
            {
                return false;
            }

            // 场上已经有房子但还没接管（进游戏那会儿没等到）：先接管再判断，
            // 不然 IsCurrent 认不出它，同一栋会被当成"要切过去的新房子"再加载一遍
            if (HouseContext.HasHouse && string.IsNullOrEmpty(CurrentId))
            {
                AdoptCurrent();
            }

            HouseSaveData house = Find(id);
            if (house == null)
            {
                Fail("找不到房子：" + id);
                return false;
            }

            if (IsCurrent(house.Id))
            {
                Fail("已经在这栋房子里了");
                return false;
            }

            StartCoroutine(SwitchRoutine(house));
            return true;
        }

        void AdoptCurrent()
        {
            HouseGridView view = HouseContext.View;
            HouseSaveData data = view != null ? view.Data : null;
            _root = view != null ? view.gameObject : null;
            CurrentId = data != null ? data.Id : "";
            Message = "";

            // 以后切房子放置点跟着这栋走：场景里摆的落点（HouseSpawner 那个）可能和这里填的不一样，
            // 不接管的话一切房子就会整栋跳位置
            if (_root != null)
            {
                _position = _root.transform.position;
                _yaw = _root.transform.eulerAngles.y;
            }

            RaiseChanged();
            Debug.Log("[房子] 接管场景里已有的房子：" + (data != null ? DisplayName(data) : "(未知)"));
        }

        IEnumerator SwitchRoutine(HouseSaveData house)
        {
            string name = DisplayName(house);
            IsSwitching = true;
            Message = "正在切换到 " + name + "…";
            RaiseChanged();

            // 女仆全部收回背包，顺手把她们在旧房子里的位置记进存档
            MaidManager maids = MaidManager.Instance;
            if (maids != null)
            {
                int count = maids.PutAllAway();
                if (count > 0)
                {
                    Debug.Log("[房子] 切换前收回女仆 " + count + " 只");
                }
            }

            HouseContext.Clear();
            GameObject reused = TakeParked(house.Id);
            Park(_root);
            _root = null;

            // 挂起来的那栋直接拿出来：不用重新解析 glTF、重烘 NavMesh
            if (reused != null)
            {
                Show(house, reused, name);
                yield break;
            }

            // 等两帧：让旧房子真的销毁、NavMesh 数据卸掉，再烘新的
            yield return null;
            yield return null;

            // 挂起来的那栋已经直接拿出来了（上面就 return 了），走到这儿才是真要解析 glTF，值得给玩家看进度
            LoadingScreen.Register(LoadingScreen.HouseJob, 3f, "正在切换到 " + name + "…");
            Task<GameObject> load = HouseImporter.LoadAsync(house, _position, Quaternion.Euler(0f, _yaw, 0f));
            while (!load.IsCompleted)
            {
                yield return null;
            }

            IsSwitching = false;

            if (load.IsFaulted || load.Result == null)
            {
                Message = "";
                RaiseChanged();
                Fail("房子加载失败：" + name);
                yield break;
            }

            Show(house, load.Result, name);
        }

        /// <summary>切换完成的收尾：接管、取景、记存档、发事件</summary>
        void Show(HouseSaveData house, GameObject root, string name)
        {
            _root = root;
            IsSwitching = false;
            LoadingScreen.Complete(LoadingScreen.HouseJob);
            HouseContext.Set(_root);
            CurrentId = house.Id;
            Message = "";

            if (HouseCameraFitter.Instance != null)
            {
                HouseCameraFitter.Instance.FitTo(_root);
            }

            // 记住下次进这栋
            MaidWorldSave world = MaidWorldSaveStore.Load();
            world.HouseId = CurrentId;
            MaidWorldSaveStore.Save(world);

            RaiseChanged();
            Debug.Log("[房子] 已切换到 " + name);
            if (Succeeded != null)
            {
                Succeeded(name);
            }
        }

        /// <summary>把一栋建好的房子挂起来：SetActive(false) + 摘掉 NavMesh 数据（数据本身留着）</summary>
        void Park(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            HouseGridView view = root.GetComponent<HouseGridView>();
            string id = view != null && view.Data != null ? view.Data.Id : "";
            if (string.IsNullOrEmpty(id) || _parkedLimit <= 0)
            {
                Destroy(root);
                return;
            }

            HouseNavMesh nav = root.GetComponent<HouseNavMesh>();
            if (nav != null)
            {
                nav.SetNavMeshActive(false);
            }

            root.SetActive(false);
            if (!_parked.ContainsKey(id))
            {
                _parkedOrder.Add(id);
            }

            _parked[id] = root;
            while (_parkedOrder.Count > Mathf.Max(0, _parkedLimit))
            {
                string oldest = _parkedOrder[0];
                _parkedOrder.RemoveAt(0);
                GameObject doomed;
                if (_parked.TryGetValue(oldest, out doomed))
                {
                    _parked.Remove(oldest);
                    if (doomed != null)
                    {
                        Destroy(doomed);
                    }
                }
            }
        }

        GameObject TakeParked(string id)
        {
            GameObject root;
            if (string.IsNullOrEmpty(id) || !_parked.TryGetValue(id, out root))
            {
                return null;
            }

            _parked.Remove(id);
            _parkedOrder.Remove(id);
            if (root == null)
            {
                return null;
            }

            HouseNavMesh nav = root.GetComponent<HouseNavMesh>();
            if (nav != null)
            {
                nav.SetNavMeshActive(true);
            }

            root.SetActive(true);
            return root;
        }

        void Fail(string reason)
        {
            Message = reason;
            RaiseChanged();
            LoadingScreen.Complete(LoadingScreen.HouseJob);
            Debug.LogWarning("[房子] " + reason);
            if (Failed != null)
            {
                Failed(reason);
            }
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
