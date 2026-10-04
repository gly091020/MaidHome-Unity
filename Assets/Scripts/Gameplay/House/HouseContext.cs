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

        /// <summary>
        /// 场上有没有人正在加载房子（HouseSpawner 这种进游戏就加载的）。HouseSwitcher 在加载期间
        /// 要接着等——只按秒数等的话，真机上 glTF + 烘 NavMesh 一超时它就会自己再加载一栋，
        /// 结果场上并排摆出两栋房子。
        /// </summary>
        public static bool IsLoading { get; private set; }

        public static void BeginLoad()
        {
            IsLoading = true;
        }

        public static void EndLoad()
        {
            IsLoading = false;
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
