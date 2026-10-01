using System;
using System.Collections;
using System.Collections.Generic;
using MaidHome.Core.Save;
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
    public sealed class MaidManager : MonoBehaviour, IBagItemProvider
    {
        public static MaidManager Instance { get; private set; }

        [SerializeField] private bool _restoreOnStart = true;
        [Tooltip("等房子加载/烘焙 NavMesh 的最长秒数，超时后女仆继续留在背包")]
        [SerializeField] private float _restoreWaitSeconds = 20f;
        [SerializeField] private float _autoSaveSeconds = 10f;

        public event Action Changed;

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

        public bool TryPlaceAt(string id, Vector3 feet, float yaw)
        {
            EnsureLoaded();
            MaidInstanceState state = FindState(id);
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

            // 位置对齐到格中心，避免看起来卡在格子边缘
            feet = view.CellFeet(cell);

            if (state.Assets == null)
            {
                state.Assets = MaidLoader.Load(state.Save);
                if (state.Assets == null)
                {
                    return false;
                }
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
                return;
            }

            _restored = true;
            EnsureLoaded();

            if (!HouseContext.HasHouse)
            {
                Debug.LogWarning("没有加载房子，上次放出来的女仆先留在背包");
                return;
            }

            string houseId = CurrentHouseId();
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

                TryPlaceAt(maid.Id, feet, record.RotationY);
            }

            _dirty = true;
            SaveNow();
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
