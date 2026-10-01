using System.IO;
using System.Threading.Tasks;
using MaidHome.Interop.Gltf;
using MaidHome.Interop.House;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 把导出的房子放进世界：加载 glTF（复用 Interop 的 GltfModelLoader）、补碰撞体、挂格表。
    /// 碰撞体必须补：导入的 glTF 只有 MeshRenderer，没有碰撞体，女仆的 CharacterController
    /// 会直接穿到地板下面去。这个和寻路无关，是独立的一步。
    /// </summary>
    public static class HouseImporter
    {
        public static async Task<GameObject> LoadAsync(HouseSaveData house, Vector3 position, Quaternion rotation)
        {
            if (house == null)
            {
                Debug.LogError("房子数据为空");
                return null;
            }

            string modelPath = house.ModelPath;
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                Debug.LogError("房子模型不存在: " + modelPath);
                return null;
            }

            GameObject root = await GltfModelLoader.LoadAsync(modelPath, null, 1f);
            if (root == null)
            {
                return null;
            }

            root.name = string.IsNullOrEmpty(house.Name) ? "House" : house.Name;
            root.transform.SetPositionAndRotation(position, rotation);

            AddColliders(root);

            HouseGridView view = root.GetComponent<HouseGridView>();
            if (view == null)
            {
                view = root.AddComponent<HouseGridView>();
            }

            view.Set(HouseGrid.From(house), house);

            // 格表先挂上，烘焙时要靠它算"哪些内部障碍要挖洞"
            HouseNavMesh nav = root.GetComponent<HouseNavMesh>();
            if (nav == null)
            {
                nav = root.AddComponent<HouseNavMesh>();
            }

            // 烘焙失败也要让房子能用：最多是没有 NavMesh，不该把整个导入中断
            bool baked = false;
            try
            {
                baked = nav.Build();
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("NavMesh 烘焙异常: " + error.Message, root);
            }

            if (baked)
            {
                Debug.Log("房子 NavMesh 已烘焙：碰撞体 " + nav.SourceCount + " 个，内部障碍挖掉 " + nav.CarvedCells + " 格");
            }

            return root;
        }

        static void AddColliders(GameObject root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            int added = 0;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter.sharedMesh == null || filter.GetComponent<MeshCollider>() != null)
                {
                    continue;
                }

                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                added++;
            }

            if (added == 0)
            {
                Debug.LogWarning("房子网格里没找到可以加碰撞体的对象，女仆会掉下去");
            }
        }
    }
}
