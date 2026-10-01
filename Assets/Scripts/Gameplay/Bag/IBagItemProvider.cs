using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Bag
{
    /// <summary>
    /// 背包面板的数据来源。女仆、以后可能的家具/道具各实现一份，
    /// 面板本身不关心这些东西具体是什么。
    /// </summary>
    public interface IBagItemProvider
    {
        event Action Changed;

        string Kind { get; }

        void GetItems(List<BagItemInfo> results);

        bool TryBeginPlacement(string id);

        bool TryPlaceAt(string id, Vector3 feet, float yaw);

        bool TryPutAway(string id);
    }
}
