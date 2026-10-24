using System;
using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.Save;
using MaidHome.Core.UI;
using MaidHome.Gameplay.Bag;
using MaidHome.Gameplay.House;
using MaidHome.Interop.House;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆的运行时总管：背包状态、取出/收回、位置存档和启动恢复。
    /// 模型资源取出后一直保留，收回只是 SetActive(false)，所以再次取出不用重新转换。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidManager : MonoBehaviour, IBagItemProvider, IBagSlowPlacement, IBagPacedPlacement
    {
        public static MaidManager Instance { get; private set; }

        [SerializeField] private bool _restoreOnStart = true;
        [Tooltip("等房子加载/烘焙 NavMesh 的最长秒数，超时后女仆继续留在背包")]
        [SerializeField] private float _restoreWaitSeconds = 20f;
        [SerializeField] private float _autoSaveSeconds = 10f;

        public event Action Changed;

        /// <summary>本次场景里的女仆恢复流程是否已经结束（成功、失败或无需恢复都算）。</summary>
        public bool RestoreFinished { get; private set; }

        public string Kind
        {
            get { return "maid"; }
        }

        readonly List<MaidSaveData> _maids = new List<MaidSaveData>();
        readonly Dictionary<string, MaidInstanceState> _states = new Dictionary<string, MaidInstanceState>();

        MaidWorldSave _world;
        bool _loaded;
        bool _dirty;
        bool _restored;
        float _nextAutoSave;
        Coroutine _restoreRoutine;

        sealed class MaidInstanceState
        {
            public MaidSaveData Save;
            public MaidAssets Assets;
            public bool InBag = true;
            public bool CacheFreshKnown;
            public bool CacheFresh;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("场景里有多个 MaidManager，后一个已禁用", this);
                enabled = false;
                return;
            }

            Instance = this;
            _world = MaidWorldSaveStore.Load();
        }

        void Start()
        {
            EnsureLoaded();
            RestoreFinished = !_restoreOnStart;
            _nextAutoSave = Time.unscaledTime + _autoSaveSeconds;
            if (_restoreOnStart)
            {
                _restoreRoutine = StartCoroutine(RestoreWhenHouseReady());
            }
        }

        void Update()
        {
            if (Time.unscaledTime < _nextAutoSave)
            {
                return;
            }

            _nextAutoSave = Time.unscaledTime + _autoSaveSeconds;
            CapturePlacedPositions();
            if (_dirty)
            {
                SaveNow();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Flush();
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                Flush();
            }
        }

        void OnApplicationQuit()
        {
            Flush();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            DisposeAll();
        }

        /// <summary>房子加载完并烘好 NavMesh 后调一次，立刻恢复上次放出来的女仆。</summary>
        public void OnHouseReady()
        {
            if (_restoreRoutine != null)
            {
                StopCoroutine(_restoreRoutine);
                _restoreRoutine = null;
            }

            RestorePlacedMaids();
        }

        /// <summary>房子根本加载不出来时调一次：别再干等了，女仆留在背包。</summary>
        public void OnHouseFailed()
        {
            if (_restoreRoutine != null)
            {
                StopCoroutine(_restoreRoutine);
                _restoreRoutine = null;
            }

            FinishRestoreJob();
        }

        /// <summary>
        /// 那栋房子的存档被删了（HouseSwitcher.Delete）：把住在里面的女仆收回背包、清掉 house_id。
        /// 不清的话存档里会留着指向"已经删掉的房子"的记录，下次进游戏她们会被判成"在别栋房子里"再收一次。
        /// </summary>
        public void OnHouseDeleted(string houseId)
        {
            if (string.IsNullOrEmpty(houseId))
            {
                return;
            }

            EnsureLoaded();
            bool dirty = false;
            for (int i = 0; i < _maids.Count; i++)
            {
                MaidSaveData maid = _maids[i];
                MaidWorldRecord record = _world.Find(maid.Id);
                if (record == null || record.HouseId != houseId)
                {
                    continue;
                }

                MaidInstanceState state = FindState(maid.Id);
                if (state != null && !state.InBag)
                {
                    TryPutAway(maid.Id);
                }

                record.InBag = true;
                record.HouseId = "";
                dirty = true;
            }

            if (dirty)
            {
                _dirty = true;
                SaveNow();
            }
        }

        public void GetItems(List<BagItemInfo> results)
        {
            EnsureLoaded();
            for (int i = 0; i < _maids.Count; i++)
            {
                MaidSaveData maid = _maids[i];
                MaidInstanceState state = _states[maid.Id];
                BagItemInfo item = new BagItemInfo();
                item.Kind = Kind;
                item.Id = maid.Id;
                item.DisplayName = string.IsNullOrEmpty(maid.Name) ? maid.Id : maid.Name;
                item.Subtitle = "Lv." + maid.Level;
                item.InBag = state.InBag;
                item.CanPlace = true;
                results.Add(item);
            }
        }

        public bool TryBeginPlacement(string id)
        {
            EnsureLoaded();
            MaidInstanceState state = FindState(id);
            if (state == null || !state.InBag)
            {
                return false;
            }

            if (!HouseContext.HasHouse)
            {
                Debug.LogWarning("没有加载房子，暂时不能放置女仆");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 放置这一只是不是要现烘（缓存没有或者过期）：资产已经加载过、或者缓存是新鲜的都算快，
        /// 放置面板只在真的要转换时才亮进度条。第一次转换能有好几秒，不提示的话玩家以为卡死了。
        /// </summary>
        public bool NeedsLoading(string id)
        {
            EnsureLoaded();
            MaidInstanceState state = FindState(id);
            if (state == null)
            {
                return false;
            }

            state.CacheFreshKnown = false;
            state.CacheFresh = false;
            if (state.Assets != null)
            {
                return false;
            }

            state.CacheFresh = MaidAssetCache.IsFresh(state.Save);
            state.CacheFreshKnown = true;
            return !state.CacheFresh;
        }

        public bool TryPlaceAt(string id, Vector3 feet, float yaw)
        {
            EnsureLoaded();
            MaidInstanceState state;
            if (!TryResolvePlacement(id, feet, out state, out feet))
            {
                return false;
            }

            if (state.Assets == null)
            {
                state.Assets = MaidLoader.Load(state.Save, TakeCacheFresh(state));
            }

            return PlaceLoaded(state, feet, yaw);
        }

        public IEnumerator PlaceAtRoutine(string id, Vector3 feet, float yaw, Action<bool> completed)
        {
            bool reported = false;
            IEnumerator routine = PlaceAtRoutineCore(id, feet, yaw, result =>
            {
                reported = true;
                if (completed != null)
                {
                    completed(result);
                }
            }, LoadingScreen.PlaceJob);
            yield return RunPlacementSafely(routine, () =>
            {
                if (!reported && completed != null)
                {
                    completed(false);
                }
            });
        }

        static IEnumerator RunPlacementSafely(IEnumerator routine, Action failed)
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = routine.MoveNext();
                }
                catch (Exception error)
                {
                    Debug.LogError("放置女仆失败: " + error);
                    if (failed != null)
                    {
                        failed();
                    }

                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                yield return routine.Current;
            }
        }

        IEnumerator PlaceAtRoutineCore(string id, Vector3 feet, float yaw, Action<bool> completed, string loadingJob,
            float loadingFrom = 0f, float loadingTo = 1f)
        {
            EnsureLoaded();
            MaidInstanceState state;
            if (!TryResolvePlacement(id, feet, out state, out feet))
            {
                if (completed != null)
                {
                    completed(false);
                }

                yield break;
            }

            if (state.Assets == null)
            {
                bool cacheFresh = TakeCacheFresh(state);
                MaidAssets loaded = null;
                yield return MaidAssetLoader.LoadRoutine(state.Save, cacheFresh, value => loaded = value,
                    (value, label) =>
                    {
                        if (!string.IsNullOrEmpty(loadingJob))
                        {
                            LoadingScreen.Report(loadingJob,
                                Mathf.LerpUnclamped(loadingFrom, loadingTo, value), label);
                        }
                    });

                if (loaded == null || loaded.Root == null)
                {
                    if (completed != null)
                    {
                        completed(false);
                    }

                    yield break;
                }

                loaded.Root.SetActive(false);
                state.Assets = loaded;
            }

            yield return null;
            bool placed = PlaceLoaded(state, feet, yaw);
            if (completed != null)
            {
                completed(placed);
            }
        }

        bool TryResolvePlacement(string id, Vector3 feet, out MaidInstanceState state, out Vector3 snappedFeet)
        {
            state = FindState(id);
            snappedFeet = feet;
            if (state == null)
            {
                return false;
            }

            HouseGridView view = HouseContext.View;
            if (view == null)
            {
                Debug.LogWarning("房子还没准备好，不能放置女仆");
                return false;
            }

            Vector3Int cell;
            bool walkable;
            if (!view.TryWorldToCell(feet, out cell, out walkable) || !walkable)
            {
                Debug.LogWarning("只能把女仆放在可走的位置");
                return false;
            }

            snappedFeet = view.CellFeet(cell);
            return true;
        }

        static bool TakeCacheFresh(MaidInstanceState state)
        {
            bool fresh = state.CacheFreshKnown && state.CacheFresh;
            state.CacheFreshKnown = false;
            state.CacheFresh = false;
            return fresh;
        }

        bool PlaceLoaded(MaidInstanceState state, Vector3 feet, float yaw)
        {
            if (state.Assets == null || state.Assets.Root == null)
            {
                return false;
            }

            GameObject root = MaidPlacement.Place(state.Assets, feet, Quaternion.Euler(0f, yaw, 0f));
            if (root == null)
            {
                return false;
            }

            MaidWanderer wanderer = root.GetComponent<MaidWanderer>();
            if (wanderer != null && HouseContext.Nav != null && HouseContext.Nav.IsBuilt)
            {
                wanderer.SetHouse(HouseContext.Nav);
            }

            state.InBag = false;
            WriteRecord(state, feet, yaw);
            _dirty = true;
            SaveNow();
            RaiseChanged();
            return true;
        }

        public bool TryPutAway(string id)
        {
            EnsureLoaded();
            MaidInstanceState state = FindState(id);
            if (state == null || state.InBag)
            {
                return false;
            }

            if (state.Assets != null && state.Assets.Root != null)
            {
                CaptureOne(state, state.Assets.Root.transform);
                state.Assets.Root.SetActive(false);
            }

            state.InBag = true;
            MaidWorldRecord record = _world.GetOrCreate(id);
            record.InBag = true;
            _dirty = true;
            SaveNow();
            RaiseChanged();
            return true;
        }

        public void SaveNow()
        {
            EnsureLoaded();
            if (HouseContext.HasHouse)
            {
                _world.HouseId = CurrentHouseId();
            }

            MaidWorldSaveStore.Save(_world);
            _dirty = false;
        }

        /// <summary>
        /// 把场上所有放出来的女仆收回背包（切房子用）。位置会先记进存档，
        /// 收回后她们在背包面板里就能重新放置到新房子里。
        /// </summary>
        public int PutAllAway()
        {
            EnsureLoaded();

            // 场上没有房子就没「放出来」这回事：这时候收回只是把存档里的 in_bag 全改成 true，
            // 把玩家放出来的女仆永久塞回背包（切房子时旧房子还挂着，不受这个判断影响）
            if (!HouseContext.HasHouse)
            {
                return 0;
            }

            List<string> placed = new List<string>();
            foreach (KeyValuePair<string, MaidInstanceState> pair in _states)
            {
                if (!pair.Value.InBag)
                {
                    placed.Add(pair.Key);
                }
            }

            for (int i = 0; i < placed.Count; i++)
            {
                TryPutAway(placed[i]);
            }

            return placed.Count;
        }

        void Flush()
        {
            CapturePlacedPositions();
            SaveNow();
        }

        void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            if (_world == null)
            {
                _world = MaidWorldSaveStore.Load();
            }

            _maids.Clear();
            _maids.AddRange(MaidLoader.List());

            for (int i = 0; i < _maids.Count; i++)
            {
                MaidSaveData maid = _maids[i];
                MaidInstanceState existing;
                if (_states.TryGetValue(maid.Id, out existing))
                {
                    existing.Save = maid;
                    continue;
                }

                MaidWorldRecord record = _world.GetOrCreate(maid.Id);
                MaidInstanceState state = new MaidInstanceState();
                state.Save = maid;
                state.InBag = record.InBag;
                _states.Add(maid.Id, state);
            }
        }

        MaidInstanceState FindState(string id)
        {
            MaidInstanceState state;
            return !string.IsNullOrEmpty(id) && _states.TryGetValue(id, out state) ? state : null;
        }

        IEnumerator RestoreWhenHouseReady()
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, _restoreWaitSeconds);
            while (!HouseContext.HasHouse && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _restoreRoutine = null;
            RestorePlacedMaids();
        }

        void RestorePlacedMaids()
        {
            if (_restored)
            {
                if (RestoreFinished)
                {
                    FinishRestoreJob();
                }

                return;
            }

            _restored = true;
            RestoreFinished = false;
            StartCoroutine(RestoreMaidsRoutine());
        }

        /// <summary>分帧放人：一帧放完的话进度条根本来不及画，多只女仆时也会顿一下。</summary>
        IEnumerator RestoreMaidsRoutine()
        {
            EnsureLoaded();
            if (!HouseContext.HasHouse)
            {
                Debug.LogWarning("没有加载房子，上次放出来的女仆先留在背包");
                FinishRestoreJob();
                yield break;
            }

            LoadingScreen.Register(LoadingScreen.MaidJob, 1f, "正在恢复女仆…");

            string houseId = CurrentHouseId();
            List<MaidSaveData> pending = new List<MaidSaveData>();
            for (int i = 0; i < _maids.Count; i++)
            {
                MaidSaveData maid = _maids[i];
                MaidWorldRecord record = _world.Find(maid.Id);
                if (record == null || record.InBag)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(record.HouseId) && record.HouseId != houseId)
                {
                    record.InBag = true;
                    continue;
                }

                pending.Add(maid);
            }

            for (int i = 0; i < pending.Count; i++)
            {
                MaidSaveData maid = pending[i];
                MaidWorldRecord record = _world.Find(maid.Id);
                Vector3 world = HouseContext.View.transform.TransformPoint(record.Position);
                Vector3 feet;
                if (!HouseContext.View.TryGetWalkableFeet(world, out feet))
                {
                    Vector3Int cell;
                    if (!HouseContext.View.TryFindNearestWalkable(world, out cell, out feet))
                    {
                        record.InBag = true;
                        continue;
                    }

                    Debug.LogWarning("女仆存档位置不可走，已移到最近的格子: " + maid.Name);
                }

                LoadingScreen.Report(LoadingScreen.MaidJob, i / Mathf.Max(1f, pending.Count),
                    "正在恢复女仆 " + (i + 1) + "/" + pending.Count + "…");
                yield return RunPlacementSafely(
                    PlaceAtRoutineCore(maid.Id, feet, record.RotationY, null, LoadingScreen.MaidJob,
                        i / Mathf.Max(1f, pending.Count), (i + 1f) / Mathf.Max(1f, pending.Count)), null);
                LoadingScreen.Report(LoadingScreen.MaidJob, (i + 1f) / Mathf.Max(1f, pending.Count));
                if (i + 1 < pending.Count)
                {
                    yield return null;
                }
            }

            _dirty = true;
            SaveNow();
            FinishRestoreJob();
        }

        void FinishRestoreJob()
        {
            LoadingScreen.Complete(LoadingScreen.MaidJob);
            RestoreFinished = true;
        }

        void CapturePlacedPositions()
        {
            EnsureLoaded();
            if (!HouseContext.HasHouse)
            {
                return;
            }

            foreach (KeyValuePair<string, MaidInstanceState> pair in _states)
            {
                MaidInstanceState state = pair.Value;
                if (state.InBag || state.Assets == null || state.Assets.Root == null)
                {
                    continue;
                }

                CaptureOne(state, state.Assets.Root.transform);
            }
        }

        void CaptureOne(MaidInstanceState state, Transform root)
        {
            if (!HouseContext.HasHouse)
            {
                return;
            }

            MaidWorldRecord record = _world.GetOrCreate(state.Save.Id);
            Vector3 local = HouseContext.View.transform.InverseTransformPoint(root.position);
            float yaw = root.eulerAngles.y;
            if ((record.Position - local).sqrMagnitude > 0.000001f
                || Mathf.Abs(Mathf.DeltaAngle(record.RotationY, yaw)) > 0.01f)
            {
                _dirty = true;
            }

            record.HouseId = CurrentHouseId();
            record.InBag = false;
            record.Position = local;
            record.RotationY = yaw;
        }

        void WriteRecord(MaidInstanceState state, Vector3 feet, float yaw)
        {
            MaidWorldRecord record = _world.GetOrCreate(state.Save.Id);
            record.HouseId = CurrentHouseId();
            record.InBag = false;
            record.Position = HouseContext.View.transform.InverseTransformPoint(feet);
            record.RotationY = yaw;
        }

        string CurrentHouseId()
        {
            HouseGridView view = HouseContext.View;
            HouseSaveData data = view != null ? view.Data : null;
            return data != null ? data.Id : "";
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        void DisposeAll()
        {
            foreach (KeyValuePair<string, MaidInstanceState> pair in _states)
            {
                if (pair.Value.Assets != null)
                {
                    pair.Value.Assets.Dispose();
                }
            }

            _states.Clear();
            _maids.Clear();
        }
    }
}
