using System.Collections.Generic;
using MaidHome.Core.Storage;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 测试入口：进 Play 就从存档里加载女仆并直接放进世界。
    /// 以后要做"先加载，再让玩家选放置或留在背包"，把 MaidLoader.Load 和 MaidPlacement.Place
    /// 分开调就行 —— 中间那个状态就是一份没进世界的 MaidAssets（Root 是关掉的）。
    /// </summary>
    public sealed class MaidSpawner : MonoBehaviour
    {
        [Tooltip("留空就取第一只存档")]
        [SerializeField] private string _maidId = "";

        [Tooltip("脚底位置。有房子时只是个提示——女仆会被吸附到最近的落脚格上")]
        [SerializeField] private Vector3 _position = new Vector3(0f, 0.1f, 0f);

        [SerializeField] private float _yaw = 180f;

        [Tooltip("关掉就只是站着，不走动")]
        [SerializeField] private bool _wander = true;

        [Tooltip("关掉就只加载不放置，模型留在背包里（不激活）。以后做选择界面时走的就是这条路")]
        [SerializeField] private bool _placeImmediately = true;

        [Tooltip("场景里有 MaidManager 时交给背包系统恢复，避免这里又放一遍")]
        [SerializeField] private bool _managedByBag = true;

        MaidAssets _assets;

        void Start()
        {
            if (_managedByBag && MaidManager.Instance != null)
            {
                Debug.Log("MaidSpawner 已交给 MaidManager 管理，这里不再自动放置");
                return;
            }

            List<MaidSaveData> maids = MaidLoader.List();
            MaidSaveData maid = MaidLoader.Find(maids, _maidId);
            if (maid == null)
            {
                Debug.LogError("找不到女仆存档，检查目录: " + AppPaths.MaidSaveRoot);
                return;
            }

            _assets = MaidLoader.Load(maid);
            if (_assets == null)
            {
                return;
            }

            if (!_placeImmediately)
            {
                Debug.Log("已加载女仆 " + maid.Name + "（留在背包，没进世界），动画 "
                    + _assets.ClipData.Count + " 条，来自缓存: " + _assets.FromCache);
                return;
            }

            GameObject root = MaidPlacement.Place(_assets, _position, Quaternion.Euler(0f, _yaw, 0f));
            if (root == null)
            {
                return;
            }

            MaidWanderer wanderer = root.GetComponent<MaidWanderer>();
            if (wanderer != null)
            {
                wanderer.enabled = _wander;
            }

            Debug.Log("已放置女仆 " + maid.Name + "，动画 " + _assets.ClipData.Count + " 条，来自缓存: " + _assets.FromCache);
        }

        void OnDestroy()
        {
            if (_assets != null)
            {
                _assets.Dispose();
                _assets = null;
            }
        }
    }
}
