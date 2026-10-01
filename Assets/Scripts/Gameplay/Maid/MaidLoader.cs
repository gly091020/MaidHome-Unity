using System.Collections.Generic;
using MaidHome.Core.Storage;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆存档的运行时入口：列存档、按 id 找、加载成 MaidAssets。
    /// 加载出来的模型是收起来的（SetActive(false)），也就是"还在背包里"的状态；
    /// 玩家决定放置时再交给 MaidPlacement。
    /// </summary>
    public static class MaidLoader
    {
        public static List<MaidSaveData> List()
        {
            return MaidSaveData.ScanRoot(AppPaths.MaidSaveRoot);
        }

        /// <summary>id 传空就取第一只；找不到返回 null。</summary>
        public static MaidSaveData Find(List<MaidSaveData> maids, string id)
        {
            if (maids == null || maids.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(id))
            {
                return maids[0];
            }

            for (int i = 0; i < maids.Count; i++)
            {
                if (maids[i].Id == id || maids[i].Name == id)
                {
                    return maids[i];
                }
            }

            return null;
        }

        public static MaidAssets Load(MaidSaveData maid)
        {
            if (maid == null)
            {
                Debug.LogError("没有可加载的女仆存档");
                return null;
            }

            MaidAssets assets = MaidAssetLoader.Load(maid);
            if (assets == null)
            {
                Debug.LogError("女仆资源加载失败: " + maid.Id);
                return null;
            }

            if (assets.Root != null)
            {
                assets.Root.SetActive(false);
            }

            return assets;
        }
    }
}
