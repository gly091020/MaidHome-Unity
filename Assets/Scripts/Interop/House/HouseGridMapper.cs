using UnityEngine;

namespace MaidHome.Interop.House
{
    /// <summary>格数据相对模型的方向。默认 GltfMirrorX 是 glTFast 导入时的坐标系转换所需的。</summary>
    public enum GridAxis
    {
        /// <summary>不旋转 + 镜像 X（glTFast 的默认行为，正常情况下用这个）。</summary>
        MirrorX,
        /// <summary>不旋转 + 不镜像。</summary>
        NoMirror,
        Rot90MirrorX,
        Rot90NoMirror,
        Rot180MirrorX,
        Rot180NoMirror,
        Rot270MirrorX,
        Rot270NoMirror,
    }

    /// <summary>
    /// 格坐标 ↔ 模型局部坐标。**这里必须和 glTFast 的坐标系转换保持一致**。
    ///
    /// glTF 是右手系、Unity 是左手系，glTFast 导入时把顶点的 x 取了负
    /// （见包里的 Jobs.cs：ConvertPositions*Job 里 `new float3(-off[0], off[1], off[2])`）。
    /// 所以 Unity 里看到的模型相对 glTF 文件是沿 X 镜像的：文件里的格 x 落在 Unity 的 -x 一侧。
    /// 格数据是 MC/文件坐标系（不镜像），映射时必须跟着取反，否则格子会整体偏 7 格、形状左右翻。
    /// </summary>
    public static class HouseGridMapper
    {
        public static bool HasMirror(GridAxis axis)
        {
            switch (axis)
            {
                case GridAxis.NoMirror:
                case GridAxis.Rot90NoMirror:
                case GridAxis.Rot180NoMirror:
                case GridAxis.Rot270NoMirror:
                    return false;
                default:
                    return true;
            }
        }

        public static int Rotation(GridAxis axis)
        {
            switch (axis)
            {
                case GridAxis.Rot90MirrorX:
                case GridAxis.Rot90NoMirror:
                    return 1;
                case GridAxis.Rot180MirrorX:
                case GridAxis.Rot180NoMirror:
                    return 2;
                case GridAxis.Rot270MirrorX:
                case GridAxis.Rot270NoMirror:
                    return 3;
                default:
                    return 0;
            }
        }

        /// <summary>格中心 → 模型局部坐标：先按 rotation 旋转格坐标，再按 glTFast 的规则镜像 X。</summary>
        public static Vector3 ToLocal(GridAxis axis, int sizeX, int sizeZ, Vector3Int cell)
        {
            float fx = cell.x + 0.5f;
            float fz = cell.z + 0.5f;
            float rx;
            float rz;
            Rotate(Rotation(axis), sizeX, sizeZ, fx, fz, out rx, out rz);
            return new Vector3(HasMirror(axis) ? -rx : rx, cell.y, rz);
        }

        /// <summary>模型局部坐标 → 格。x/z 向下取整（点在格内），y 四舍五入（脚底正好在格底面上，浮点会让 floor 掉一格）。</summary>
        public static Vector3Int ToCell(GridAxis axis, int sizeX, int sizeZ, Vector3 local)
        {
            float dx = HasMirror(axis) ? -local.x : local.x;
            float fx;
            float fz;
            Unrotate(Rotation(axis), sizeX, sizeZ, dx, local.z, out fx, out fz);
            return new Vector3Int(Mathf.FloorToInt(fx), Mathf.RoundToInt(local.y), Mathf.FloorToInt(fz));
        }

        static void Rotate(int rotation, int sizeX, int sizeZ, float x, float z, out float rx, out float rz)
        {
            switch (rotation)
            {
                case 1:
                    rx = z;
                    rz = sizeX - x;
                    break;
                case 2:
                    rx = sizeX - x;
                    rz = sizeZ - z;
                    break;
                case 3:
                    rx = sizeZ - z;
                    rz = x;
                    break;
                default:
                    rx = x;
                    rz = z;
                    break;
            }
        }

        static void Unrotate(int rotation, int sizeX, int sizeZ, float x, float z, out float fx, out float fz)
        {
            switch (rotation)
            {
                case 1:
                    fx = sizeZ - z;
                    fz = x;
                    break;
                case 2:
                    fx = sizeX - x;
                    fz = sizeZ - z;
                    break;
                case 3:
                    fx = z;
                    fz = sizeX - x;
                    break;
                default:
                    fx = x;
                    fz = z;
                    break;
            }
        }

        // 注意：这里不提供"默认方向"的便捷重载。方向只有一个来源（HouseGridView.Axis），
        // 多一个默认写死 MirrorX 的重载，迟早会有调用点忘了传方向、画出来的格子是错的。
    }
}
