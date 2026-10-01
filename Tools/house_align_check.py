"""核对"导出的模型"和"house.json 里标的可走格"方向对不对得上。

做法：从 glTF 里挑出**朝上、位于某一层的水平面**（也就是地板顶面），得到一份几何轮廓，
再把 house.json 的 walkable 按 8 种摆放（4 个旋转 × 是否镜像）变换过去做比较。
地板被墙压住的地方导出时会被剔除，所以几何轮廓是 walkable 的子集，正确的方向应该
「几何里有底、但 walkable 没标」的格子最少。

用法: python Tools/house_align_check.py <house.gltf> <house.json>
"""

import json
import os
import struct
import sys

COMPONENT_FORMATS = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
COMPONENT_SIZES = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}
COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def load_gltf(path):
    with open(path, "rb") as fh:
        head = fh.read(12)
        if head[:4] == b"glTF":
            while True:
                chunk_head = fh.read(8)
                if len(chunk_head) < 8:
                    break
                chunk_len, chunk_type = struct.unpack("<II", chunk_head)
                data = fh.read(chunk_len)
                if chunk_type == 0x4E4F534A:
                    gltf = json.loads(data.decode("utf-8"))
                elif chunk_type == 0x004E4942:
                    binary = data
            return gltf, binary

        fh.seek(0)
        gltf = json.load(fh)
        binary = b""
        for buffer in gltf.get("buffers", []):
            uri = buffer.get("uri", "")
            if uri and not uri.startswith("data:"):
                with open(os.path.join(os.path.dirname(path), uri), "rb") as bf:
                    binary += bf.read()
        return gltf, binary


def read_accessor(gltf, binary, index):
    accessor = gltf["accessors"][index]
    components = COMPONENTS[accessor["type"]]
    component_type = accessor["componentType"]
    size = COMPONENT_SIZES[component_type]
    fmt = "<" + COMPONENT_FORMATS[component_type] * components
    view = gltf["bufferViews"][accessor["bufferView"]]
    base = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = view.get("byteStride") or components * size
    values = []
    for i in range(accessor["count"]):
        values.append(struct.unpack_from(fmt, binary, base + i * stride))
    return values


def floor_cells(gltf, binary, mesh_index, level, tolerance=0.02):
    """朝上、且三个顶点都在 y=level 附近的三角形 -> 它盖住的 (x,z) 格。"""
    cells = set()
    mesh = gltf["meshes"][mesh_index]
    for primitive in mesh["primitives"]:
        attributes = primitive.get("attributes", {})
        if "POSITION" not in attributes or "indices" not in primitive:
            continue
        positions = read_accessor(gltf, binary, attributes["POSITION"])
        indices = read_accessor(gltf, binary, primitive["indices"])
        for i in range(0, len(indices) - 2, 3):
            a = positions[indices[i][0]]
            b = positions[indices[i + 1][0]]
            c = positions[indices[i + 2][0]]
            if abs(a[1] - level) > tolerance or abs(b[1] - level) > tolerance or abs(c[1] - level) > tolerance:
                continue

            # 法线朝上才算地板顶面（朝下的是底面）
            ux, uy, uz = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
            vx, vy, vz = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
            ny = uz * vx - ux * vz
            if ny <= 0:
                continue

            cx = (a[0] + b[0] + c[0]) / 3.0
            cz = (a[2] + b[2] + c[2]) / 3.0
            cells.add((int(cx), int(cz)))
    return cells


def walkable_cells(doc, level):
    layers = doc.get("walkable", [])
    if level >= len(layers):
        return set()
    rows = layers[level].split("\n")
    cells = set()
    for z, row in enumerate(rows):
        for x, c in enumerate(row.rstrip("\r")):
            if c == "1":
                cells.add((x, z))
    return cells


def transforms(sx, sz):
    def rot0(x, z):
        return (x, z)

    def rot90(x, z):
        return (z, sx - 1 - x)

    def rot180(x, z):
        return (sx - 1 - x, sz - 1 - z)

    def rot270(x, z):
        return (sz - 1 - z, x)

    def mirror(x, z):
        return (sx - 1 - x, z)

    variants = [("原样", rot0), ("转90", rot90), ("转180", rot180), ("转270", rot270)]
    for name, fn in list(variants):
        variants.append((name + "+镜像x", lambda x, z, fn=fn: mirror(*fn(x, z))))
    return variants


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    gltf_path, json_path = sys.argv[1], sys.argv[2]
    gltf, binary = load_gltf(gltf_path)
    with open(json_path, "r", encoding="utf-8") as fh:
        doc = json.load(fh)

    size = doc.get("size", [0, 0, 0])
    sx, sy, sz = size[0], size[1], size[2]
    print("模型: %s" % os.path.basename(gltf_path))
    print("size=%s" % size)

    for level in range(sy):
        expected = walkable_cells(doc, level)
        if not expected:
            continue

        geometry = floor_cells(gltf, binary, 0, level)
        print("\ny=%d：walkable %d 格，几何上朝上的地板面 %d 格" % (level, len(expected), len(geometry)))
        if not geometry:
            continue

        scored = []
        for name, fn in transforms(sx, sz):
            moved = {fn(x, z) for (x, z) in expected}
            # 几何有地板但没标能走 = 方向不对的直接证据
            missing = len(geometry - moved)
            extra = len(moved - geometry)
            scored.append((missing, extra, name))
        scored.sort()
        for missing, extra, name in scored:
            mark = "  <== 最匹配" if (missing, extra, name) == scored[0] else ""
            print("    %-10s 几何没标能走 %3d 格，标了却没地板 %3d 格%s" % (name, missing, extra, mark))

    return 0


if __name__ == "__main__":
    sys.exit(main())
