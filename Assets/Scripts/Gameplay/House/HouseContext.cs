using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>当前房子的注册点。房子加载/卸载时更新，避免各处到处 FindObjectOfType。</summary>
    public static class HouseContext
    {
        public static HouseGridView View { get; private set; }
        public static HouseNavMesh Nav { get; private set; }

        public static bool HasHouse
        {
            get { return View != null; }
        }

        public static void Set(GameObject houseRoot)
        {
            if (houseRoot == null)
            {
                Clear();
                return;
            }

            View = houseRoot.GetComponent<HouseGridView>();
            Nav = houseRoot.GetComponent<HouseNavMesh>();
        }

        public static void Clear()
        {
            View = null;
            Nav = null;
        }
    }
}
