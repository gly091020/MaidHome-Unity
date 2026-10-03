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
            GameObject root;
            if (string.IsNullOrEmpty(modelPath))
            {
                // house.json 里 model 就是空的 = 内置示例房间，按格表程序化搭一个
                root = BuildPlaceholder(house);
            }
            else if (!File.Exists(modelPath))
            {
                // 真房子的模型不见了：也搭个占位，别让玩家对着空场景还以为游戏坏了
                Debug.LogWarning("房子模型文件不见了，按格表搭一个占位房间: " + modelPath);
                root = BuildPlaceholder(house);
            }
            else
            {
                root = await GltfModelLoader.LoadAsync(modelPath, null, 1f);
            }

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
                // 已经有碰撞体（占位房间用的 Cube 自带 BoxCollider）就别再加一个 MeshCollider
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
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

        /// <summary>
        /// 没有模型时按格表搭一个"地板 + 四面墙"。用 Unity 自带的 Cube（自带网格和 BoxCollider），
        /// 位置全部走 HouseGridMapper，保证和 HouseGridView 认的格子是同一套坐标（镜像/旋转都对得上）。
        /// </summary>
        static GameObject BuildPlaceholder(HouseSaveData house)
        {
            GameObject root = new GameObject(string.IsNullOrEmpty(house.Name) ? "House" : house.Name);
            float sizeX = Mathf.Max(1, house.SizeX);
            float sizeZ = Mathf.Max(1, house.SizeZ);
            float height = Mathf.Max(1f, house.SizeY);

            float x0 = float.MaxValue;
            float x1 = float.MinValue;
            float z0 = float.MaxValue;
            float z1 = float.MinValue;
            for (int x = 0; x < (int)sizeX; x++)
            {
                for (int z = 0; z < (int)sizeZ; z++)
                {
                    Vector3 center = HouseGridMapper.ToLocal(GridAxis.MirrorX, (int)sizeX, (int)sizeZ,
                        new Vector3Int(x, 0, z));
                    x0 = Mathf.Min(x0, center.x - 0.5f);
                    x1 = Mathf.Max(x1, center.x + 0.5f);
                    z0 = Mathf.Min(z0, center.z - 0.5f);
                    z1 = Mathf.Max(z1, center.z + 0.5f);
                }
            }

            Material material = CreatePlaceholderMaterial();
            float centerX = (x0 + x1) * 0.5f;
            float centerZ = (z0 + z1) * 0.5f;
            float width = x1 - x0;
            float depth = z1 - z0;

            // 地板：顶面正好在脚底高度 y = 0
            MakeBox(root, "Floor", material, new Vector3(centerX, -0.1f, centerZ),
                new Vector3(width, 0.2f, depth));
            // 四面墙：厚 1，正好压在边上那一圈格上（那圈在格表里是不可走的）
            MakeBox(root, "Wall -X", material, new Vector3(x0 + 0.5f, height * 0.5f, centerZ),
                new Vector3(1f, height, depth));
            MakeBox(root, "Wall +X", material, new Vector3(x1 - 0.5f, height * 0.5f, centerZ),
                new Vector3(1f, height, depth));
            MakeBox(root, "Wall -Z", material, new Vector3(centerX, height * 0.5f, z0 + 0.5f),
                new Vector3(Mathf.Max(1f, width - 2f), height, 1f));
            MakeBox(root, "Wall +Z", material, new Vector3(centerX, height * 0.5f, z1 - 0.5f),
                new Vector3(Mathf.Max(1f, width - 2f), height, 1f));
            return root;
        }

        static void MakeBox(GameObject parent, string name, Material material, Vector3 position, Vector3 scale)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            if (material != null)
            {
                box.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        /// <summary>占位房间的材质：MC 平光那套（没导进来就退回 Standard），白 1×1 贴图 + 一点米色。</summary>
        static Material CreatePlaceholderMaterial()
        {
            Shader shader = Shader.Find("MaidHome/MinecraftBlock");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            material.name = "HousePlaceholder";
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            if (material.HasProperty("_MainTex"))
            {
                material.mainTexture = texture;
            }

            if (material.HasProperty("_Color"))
            {
                material.color = new Color(0.78f, 0.74f, 0.66f, 1f);
            }

            return material;
        }
    }
}
