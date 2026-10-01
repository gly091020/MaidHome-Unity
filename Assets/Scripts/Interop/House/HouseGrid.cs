using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Interop.House
{
    /// <summary>
    /// 可通行格的只读视图。只认格坐标，不管世界坐标、也不管模型。
    ///
    /// 约定：格 (x,y,z) 能站 = 实体站在这一格的**底面**上，脚底高度就等于 y。
    /// 索引 (y * SizeZ + z) * SizeX + x，和 house.json 里 walkable 的书写顺序一致。
    /// </summary>
    public sealed class HouseGrid
    {
        public readonly int SizeX;
        public readonly int SizeY;
        public readonly int SizeZ;

        readonly bool[] _walkable;

        public HouseGrid(int sizeX, int sizeY, int sizeZ, bool[] walkable)
        {
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            _walkable = walkable != null && walkable.Length == sizeX * sizeY * sizeZ
                ? walkable
                : new bool[sizeX * sizeY * sizeZ];
        }

        public static HouseGrid From(HouseSaveData house)
        {
            return house == null ? null : new HouseGrid(house.SizeX, house.SizeY, house.SizeZ, house.Walkable);
        }

        public int Count
        {
            get { return SizeX * SizeY * SizeZ; }
        }

        public int Index(int x, int y, int z)
        {
            return (y * SizeZ + z) * SizeX + x;
        }

        public bool InBounds(int x, int y, int z)
        {
            return x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ;
        }

        public bool IsWalkable(int x, int y, int z)
        {
            return InBounds(x, y, z) && _walkable[Index(x, y, z)];
        }

        public int WalkableCount()
        {
            int total = 0;
            for (int i = 0; i < _walkable.Length; i++)
            {
                if (_walkable[i])
                {
                    total++;
                }
            }

            return total;
        }

        /// <summary>
        /// 标出"被可行走区域围住的不可走格"——也就是内部真正要挖掉的东西（台阶拼的桌子、家具之类）。
        ///
        /// 做法：只在有可走格的层里，从该层 xz 的四条边界往里 flood fill 不可走格；
        /// 能连到边界的是外墙或屋外，靠几何本来就挡住了，不用挖——挖了反而会把贴墙的走道吃掉一圈。
        /// 连不到边界的，就是屋子内部的障碍。
        /// </summary>
        public int MarkEnclosedNonWalkable(bool[] marks)
        {
            if (marks == null || marks.Length < Count)
            {
                return 0;
            }

            for (int i = 0; i < marks.Length; i++)
            {
                marks[i] = false;
            }

            bool[] reachable = new bool[Count];
            List<int> queue = new List<int>();

            for (int y = 0; y < SizeY; y++)
            {
                bool hasWalkable = false;
                for (int i = 0; i < SizeX * SizeZ && !hasWalkable; i++)
                {
                    hasWalkable = IsWalkable(i % SizeX, y, i / SizeX);
                }

                if (!hasWalkable)
                {
                    continue;
                }

                for (int x = 0; x < SizeX; x++)
                {
                    EnqueueBlocked(x, y, 0, reachable, queue);
                    EnqueueBlocked(x, y, SizeZ - 1, reachable, queue);
                }

                for (int z = 0; z < SizeZ; z++)
                {
                    EnqueueBlocked(0, y, z, reachable, queue);
                    EnqueueBlocked(SizeX - 1, y, z, reachable, queue);
                }
            }

            // 只在本层内四邻扩散
            for (int head = 0; head < queue.Count; head++)
            {
                int index = queue[head];
                int x = index % SizeX;
                int rest = index / SizeX;
                int z = rest % SizeZ;
                int y = rest / SizeZ;

                EnqueueBlocked(x + 1, y, z, reachable, queue);
                EnqueueBlocked(x - 1, y, z, reachable, queue);
                EnqueueBlocked(x, y, z + 1, reachable, queue);
                EnqueueBlocked(x, y, z - 1, reachable, queue);
            }

            int total = 0;
            for (int y = 0; y < SizeY; y++)
            {
                bool hasWalkable = false;
                for (int x = 0; x < SizeX && !hasWalkable; x++)
                {
                    for (int z = 0; z < SizeZ && !hasWalkable; z++)
                    {
                        hasWalkable = IsWalkable(x, y, z);
                    }
                }

                if (!hasWalkable)
                {
                    continue;
                }

                for (int z = 0; z < SizeZ; z++)
                {
                    for (int x = 0; x < SizeX; x++)
                    {
                        int index = Index(x, y, z);
                        if (!_walkable[index] && !reachable[index])
                        {
                            marks[index] = true;
                            total++;
                        }
                    }
                }
            }

            return total;
        }

        void EnqueueBlocked(int x, int y, int z, bool[] reachable, List<int> queue)
        {
            if (!InBounds(x, y, z))
            {
                return;
            }

            int index = Index(x, y, z);
            if (_walkable[index] || reachable[index])
            {
                return;
            }

            reachable[index] = true;
            queue.Add(index);
        }
    }
}
