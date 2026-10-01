using System.Collections.Generic;
using System.IO;
using MaidHome.Core.Json;
using UnityEngine;

namespace MaidHome.Interop.House
{
    /// <summary>
    /// house.json：MC 侧导出的房子元数据 + 可通行格数据。
    ///
    /// <code>
    /// {
    ///   "size":     [7, 7, 7],              // x, y, z 各几格
    ///   "name":     "房屋1",
    ///   "origin":   [4, 1, 4],              // 房子的落脚点（格坐标），不是格子表的原点
    ///   "model":    "1",                    // 同目录下的 glTF 文件名，不带扩展名
    ///   "walkable": ["0000000\n...", ...]   // 每层一个字符串；层序 y 递增，行 z 递增，字符 x 递增，'1' = 能站
    /// }
    /// </code>
    ///
    /// 每格的含义：能站 = 实体站在这一格的**底面**上，脚底高度就等于这格的 y 坐标。
    /// 下面是不是实心、头顶够不够 1.8 格、这块是楼梯还是台阶桌子，全部由 MC 侧判断完，
    /// Unity 侧不重新推——两边各推一次必然对不齐，而且 Unity 侧没有方块语义。
    /// </summary>
    public sealed class HouseSaveData
    {
        public string Folder = "";
        public string Id = "";
        public string Name = "";
        public string ModelFile = "model";
        public int SizeX;
        public int SizeY;
        public int SizeZ;

        /// <summary>house.json 里的 origin，按"房子的落脚点"理解。</summary>
        public int OriginX;
        public int OriginY;
        public int OriginZ;

        /// <summary>索引 (y * SizeZ + z) * SizeX + x。</summary>
        public bool[] Walkable = new bool[0];

        public readonly List<string> Warnings = new List<string>();

        public string ModelPath
        {
            get
            {
                if (string.IsNullOrEmpty(ModelFile))
                {
                    return "";
                }

                string direct = Path.Combine(Folder, ModelFile);
                if (File.Exists(direct))
                {
                    return direct;
                }

                string gltf = Path.Combine(Folder, ModelFile + ".gltf");
                if (File.Exists(gltf))
                {
                    return gltf;
                }

                string glb = Path.Combine(Folder, ModelFile + ".glb");
                return File.Exists(glb) ? glb : gltf;
            }
        }

        public static HouseSaveData ParseFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            HouseSaveData house = Parse(File.ReadAllText(path), Path.GetDirectoryName(path));
            house.Warnings.Insert(0, "读取: " + path);
            return house;
        }

        /// <summary>扫房子存档根目录，一个 uuid 一个文件夹，里面放 house.json。</summary>
        public static List<HouseSaveData> ScanRoot(string houseRoot)
        {
            List<HouseSaveData> result = new List<HouseSaveData>();
            if (string.IsNullOrEmpty(houseRoot) || !Directory.Exists(houseRoot))
            {
                return result;
            }

            string[] folders = Directory.GetDirectories(houseRoot);
            for (int i = 0; i < folders.Length; i++)
            {
                string file = Path.Combine(folders[i], "house.json");
                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    HouseSaveData house = ParseFile(file);
                    house.Id = Path.GetFileName(folders[i]);
                    result.Add(house);
                }
                catch (System.Exception error)
                {
                    HouseSaveData broken = new HouseSaveData();
                    broken.Folder = folders[i];
                    broken.Id = Path.GetFileName(folders[i]);
                    broken.Name = "(解析失败)";
                    broken.Warnings.Add("house.json 解析失败: " + error.Message);
                    result.Add(broken);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        public static HouseSaveData Parse(string json, string folder)
        {
            HouseSaveData house = new HouseSaveData();
            house.Folder = folder ?? "";

            JsonValue root = MiniJson.Parse(json);
            house.Name = root["name"].AsString("");
            house.ModelFile = root["model"].AsString("model");

            ReadVector(root["size"], out house.SizeX, out house.SizeY, out house.SizeZ, "size", house.Warnings);
            ReadVector(root["origin"], out house.OriginX, out house.OriginY, out house.OriginZ, "origin", house.Warnings);

            if (house.SizeX <= 0 || house.SizeY <= 0 || house.SizeZ <= 0)
            {
                house.Warnings.Add("size 不合法: " + house.SizeX + "x" + house.SizeY + "x" + house.SizeZ);
                return house;
            }

            house.Walkable = new bool[house.SizeX * house.SizeY * house.SizeZ];
            ReadWalkable(root["walkable"], house);
            return house;
        }

        static void ReadVector(JsonValue node, out int x, out int y, out int z, string label, List<string> warnings)
        {
            x = y = z = 0;
            if (!node.IsArray || node.Count < 3)
            {
                warnings.Add(label + " 缺失或不是长度为 3 的数组");
                return;
            }

            x = node[0].AsInt(0);
            y = node[1].AsInt(0);
            z = node[2].AsInt(0);
        }

        static void ReadWalkable(JsonValue layers, HouseSaveData house)
        {
            if (!layers.IsArray || layers.Count == 0)
            {
                house.Warnings.Add("walkable 缺失或不是数组");
                return;
            }

            if (layers.Count != house.SizeY)
            {
                house.Warnings.Add("walkable 层数是 " + layers.Count + "，size[1] 是 " + house.SizeY + "，缺的层按全不可走处理");
            }

            int unknownChars = 0;
            int badRows = 0;
            int layerCount = Mathf.Min(layers.Count, house.SizeY);
            for (int y = 0; y < layerCount; y++)
            {
                string text = layers[y].AsString("");
                string[] rows = text.Split('\n');
                if (rows.Length != house.SizeZ)
                {
                    badRows++;
                }

                int rowCount = Mathf.Min(rows.Length, house.SizeZ);
                for (int z = 0; z < rowCount; z++)
                {
                    string row = rows[z].TrimEnd('\r');
                    int columnCount = Mathf.Min(row.Length, house.SizeX);
                    for (int x = 0; x < columnCount; x++)
                    {
                        char c = row[x];
                        if (c == '1')
                        {
                            house.Walkable[Index(house, x, y, z)] = true;
                        }
                        else if (c != '0')
                        {
                            unknownChars++;
                        }
                    }
                }
            }

            if (badRows > 0)
            {
                house.Warnings.Add("有 " + badRows + " 层的行数和 size[2](" + house.SizeZ + ") 不一致");
            }

            if (unknownChars > 0)
            {
                house.Warnings.Add("walkable 里有 " + unknownChars + " 个非 0/1 字符，按不可走处理");
            }
        }

        static int Index(HouseSaveData house, int x, int y, int z)
        {
            return (y * house.SizeZ + z) * house.SizeX + x;
        }
    }
}
