using UnityEngine;

namespace MaidHome.Core.Save
{
    /// <summary>一只女仆在世界里的状态。Position 是房子根节点下的本地坐标。</summary>
    public sealed class MaidWorldRecord
    {
        public string Id = "";
        public string HouseId = "";
        public bool InBag = true;
        public Vector3 Position = Vector3.zero;
        public float RotationY;
    }
}
