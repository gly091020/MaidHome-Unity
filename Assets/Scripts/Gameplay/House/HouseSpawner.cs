using System.Collections.Generic;
using MaidHome.Core.Storage;
using MaidHome.Gameplay.Maid;
using MaidHome.Interop.House;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>测试入口：进 Play 就把房子加载并摆进世界。女仆那边自己找 HouseGridView 拿格表。</summary>
    public sealed class HouseSpawner : MonoBehaviour
    {
        [Tooltip("留空就取第一份房子")]
        [SerializeField] private string _houseId = "";

        [SerializeField] private Vector3 _position = Vector3.zero;

        [SerializeField] private float _yaw = 0f;

        public HouseGridView View { get; private set; }

        async void Start()
        {
            List<HouseSaveData> houses = HouseSaveData.ScanRoot(AppPaths.HouseSaveRoot);
            HouseSaveData house = Find(houses, _houseId);
            if (house == null)
            {
                Debug.LogError("找不到房子存档，检查目录: " + AppPaths.HouseSaveRoot);
                return;
            }

            GameObject root = await HouseImporter.LoadAsync(house, _position, Quaternion.Euler(0f, _yaw, 0f));
            if (root == null)
            {
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
        }

        void OnDestroy()
        {
            HouseContext.Clear();
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
