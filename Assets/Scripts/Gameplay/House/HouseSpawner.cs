using System.Collections.Generic;
using MaidHome.Core.Save;
using MaidHome.Core.Storage;
using MaidHome.Core.UI;
using MaidHome.Gameplay.Maid;
using MaidHome.Interop.House;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>测试入口：进 Play 就把房子加载并摆进世界。女仆那边自己找 HouseGridView 拿格表。</summary>
    public sealed class HouseSpawner : MonoBehaviour
    {
        [Tooltip("留空 = 用 slot0.json 里记的那栋（没有记录才取列表第一栋）")]
        [SerializeField] private string _houseId = "";

        [SerializeField] private Vector3 _position = Vector3.zero;

        [SerializeField] private float _yaw = 0f;

        public HouseGridView View { get; private set; }

        async void Start()
        {
            LoadingScreen.Register(LoadingScreen.HouseJob, 3f, "正在加载房子…");

            // 加载期间立起 HouseContext.IsLoading：HouseSwitcher 等不到房子的话会自己再加载一栋，
            // 真机上 glTF + 烘 NavMesh 经常超过它那几秒等待，结果场上并排摆出两栋
            HouseContext.BeginLoad();
            try
            {
                await LoadHouse();
            }
            finally
            {
                HouseContext.EndLoad();
            }
        }

        /// <summary>真正的加载流程，单独一个方法是为了让 Start 的 finally 一定放得下 IsLoading。</summary>
        async System.Threading.Tasks.Task LoadHouse()
        {
            // 第一次进游戏（一栋房子都没有）会把随包默认房子装进存档，免得空着没得看；
            // Android 上要从 apk 里读，所以是 async 的（这里本来就是 async 流程）
            await HouseStarter.EnsureAsync(AppPaths.HouseSaveRoot);
            List<HouseSaveData> houses = HouseSaveData.ScanRoot(AppPaths.HouseSaveRoot);
            HouseSaveData house = Find(houses, ResolveHouseId());
            if (house == null && houses.Count > 0)
            {
                // 存档里记的那栋被删了：退回第一栋，别让她进不去游戏
                Debug.LogWarning("存档里记的房子已经不在列表里，先加载第一栋");
                house = houses[0];
            }

            if (house == null)
            {
                Debug.LogError("找不到房子存档，检查目录: " + AppPaths.HouseSaveRoot);
                LoadingScreen.Complete(LoadingScreen.HouseJob);
                if (MaidManager.Instance != null)
                {
                    MaidManager.Instance.OnHouseFailed();
                }

                return;
            }

            GameObject root = await HouseImporter.LoadAsync(house, _position, Quaternion.Euler(0f, _yaw, 0f));
            if (root == null)
            {
                LoadingScreen.Complete(LoadingScreen.HouseJob);
                if (MaidManager.Instance != null)
                {
                    MaidManager.Instance.OnHouseFailed();
                }

                return;
            }

            View = root.GetComponent<HouseGridView>();
            int walkable = View != null && View.Grid != null ? View.Grid.WalkableCount() : 0;
            Debug.Log("已放置房子 " + house.Name + "，可走格 " + walkable);

            HouseContext.Set(root);
            if (HouseCameraFitter.Instance != null)
            {
                HouseCameraFitter.Instance.FitTo(root);
            }

            if (MaidManager.Instance != null)
            {
                MaidManager.Instance.OnHouseReady();
            }

            LoadingScreen.Complete(LoadingScreen.HouseJob);
        }

        void OnDestroy()
        {
            HouseContext.Clear();
        }

        /// <summary>
        /// 没在 Inspector 里点名就用存档里记的那栋。
        /// 不这么做的话进游戏进哪栋取决于 uuid 排序，而且存档里"在别栋房子里的女仆"
        /// 会因为 house_id 对不上被 `MaidManager.RestorePlacedMaids` 全部收回背包。
        /// </summary>
        string ResolveHouseId()
        {
            if (!string.IsNullOrEmpty(_houseId))
            {
                return _houseId;
            }

            MaidWorldSave world = MaidWorldSaveStore.Load();
            return world != null ? world.HouseId : "";
        }

        static HouseSaveData Find(List<HouseSaveData> houses, string id)
        {
            if (houses == null || houses.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(id))
            {
                return houses[0];
            }

            for (int i = 0; i < houses.Count; i++)
            {
                if (houses[i].Id == id || houses[i].Name == id)
                {
                    return houses[i];
                }
            }

            return null;
        }
    }
}
