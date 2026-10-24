using System;
using System.Collections;
using UnityEngine;

namespace MaidHome.Gameplay.Bag
{
    /// <summary>可选：放置需要加载资源时，让背包面板等待整个加载协程结束，而不是同步卡住。</summary>
    public interface IBagPacedPlacement
    {
        IEnumerator PlaceAtRoutine(string id, Vector3 feet, float yaw, Action<bool> completed);
    }
}
