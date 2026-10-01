#!/usr/bin/env python3
"""用 Blockbench 自己导出的 OBJ 当标准答案，校验基岩转换约定。

Blockbench 处理基岩模型的方式（源码 js/formats/bedrock/bedrock.js + outliner/types/cube.js）：
  1. 内部坐标是基岩坐标沿 X 镜像：bb_x = -bedrock_x，pivot 同样镜像
  2. 网格 rotation 取 (-rx, -ry, rz) 度，Euler 顺序固定 ZYX（three.js）
  3. 骨骼走 bone_rig + use_absolute_position：子物体位置 = 自己 pivot - 父 pivot
  4. 导出 OBJ 时顶点 = mesh.matrixWorld 变换后的世界坐标 / model_export_scale

所以 OBJ 里的顶点位置就是"正确答案"，任何旋转约定只要跟它一致就是对的。

用法:
  python Tools/obj_oracle.py <model.json> <blockbench.obj> [--scale 16] [--survey]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from bedrock_preview import (  # noqa: E402
    IDENTITY,
    Builder,
    Convert,
    apply,
    cross,
    dot,
    load_json,
    mat3,
    mat_mul,
    norm,
    parse_geometry,
    sub,
)

ORDERS = ("xzy", "zxy", "zyx", "xyz", "yxz", "yzx")
SIGNS = ((-1, -1, -1), (-1, -1, 1), (-1, 1, -1), (-1, 1, 1),
         (1, -1, -1), (1, -1, 1), (1, 1, -1), (1, 1, 1))

# OBJ 里每个面的 4 个顶点（Blockbench getGlobalVertexPositions 的 1-based 下标）
FACE_VERTS = {
    "north": (2, 5, 7, 4), "east": (1, 2, 4, 3), "south": (6, 1, 3, 8),
    "west": (5, 6, 8, 7), "up": (5, 2, 1, 6), "down": (8, 3, 4, 7),
}
VERTS_OF_FACE = {frozenset(v): k for k, v in FACE_VERTS.items()}

# Blockbench 内部沿 X 镜像，它的 east/west 和工程里的东西向是反的
BB_FACE_TO_OURS = {"north": "north", "south": "south", "up": "up", "down": "down",
                   "east": "west", "west": "east"}


def mirror(p):
    """基岩坐标 -> Blockbench 内部坐标。"""
    return (-p[0], p[1], p[2])


def bb_rotation(rot):
    """Blockbench 里这个方块/骨骼实际用的旋转矩阵（three.js Euler ZYX）。"""
    rx, ry, rz = rot
    return mat_mul(mat3(0, 0, rz), mat_mul(mat3(0, -ry, 0), mat3(-rx, 0, 0)))


def cube_corners(from_, to):
    """Blockbench getGlobalVertexPositions 的 8 个角点顺序。"""
    x0, y0, z0 = from_
    x1, y1, z1 = to
    return [
        (x1, y1, z1), (x1, y1, z0), (x1, y0, z1), (x1, y0, z0),
        (x0, y1, z0), (x0, y1, z1), (x0, y0, z0), (x0, y0, z1),
    ]


def blockbench_corners(geo):
    """算出每个方块 8 个角点在 Blockbench 世界坐标里的位置（单位：像素）。"""
    bones = geo["bones"]
    by_name = {}
    for b in bones:
        by_name.setdefault(b.get("name", "?"), b)

    result = {}

    def walk(bone, parent_pos, parent_rot, parent_pivot_bb):
        pivot_bb = mirror(tuple(bone.get("pivot", (0, 0, 0))))
        local = sub(pivot_bb, parent_pivot_bb)
        shifted = apply(parent_rot, local)
        pos = tuple(parent_pos[i] + shifted[i] for i in range(3))
        rot = mat_mul(parent_rot, bb_rotation(bone.get("rotation", (0, 0, 0))))

        cubes = []
        for cube in bone.get("cubes", []) or []:
            origin = tuple(cube.get("origin", (0, 0, 0)))
            size = tuple(cube.get("size", (0, 0, 0)))
            inflate = float(cube.get("inflate", 0) or 0)
            if inflate == 0:
                inflate = float(bone.get("inflate", 0) or 0)
            # 绕中心对称扩张，pivot 不动
            center = tuple(origin[i] + size[i] / 2 for i in range(3))
            from_ = tuple(center[i] - (size[i] / 2 + inflate) for i in range(3))
            to = tuple(center[i] + (size[i] / 2 + inflate) for i in range(3))
            corner_a = mirror(from_)
            corner_b = mirror(to)
            from_bb = tuple(min(corner_a[i], corner_b[i]) for i in range(3))
            to_bb = tuple(max(corner_a[i], corner_b[i]) for i in range(3))
            # Blockbench 里方块的网格原点就是 pivot，没写 pivot 时按 [0,0,0] 处理
            pivot = cube.get("pivot")
            origin_bb = mirror(tuple(pivot)) if pivot is not None else (0.0, 0.0, 0.0)
            cube_rot = bb_rotation(cube.get("rotation", (0, 0, 0)))
            has_rot = any(abs(v) > 1e-9 for v in cube.get("rotation", (0, 0, 0)))
            offset = sub(origin_bb, pivot_bb)
            points = []
            for corner in cube_corners(from_bb, to_bb):
                local_p = sub(corner, origin_bb)
                if has_rot:
                    local_p = apply(cube_rot, local_p)
                local_p = tuple(local_p[i] + offset[i] for i in range(3))
                world = apply(rot, local_p)
                points.append(tuple(pos[i] + world[i] for i in range(3)))
            cubes.append(points)
        result[bone.get("name", "?")] = cubes

        for child in bone.get("children", []):
            walk(child, pos, rot, pivot_bb)

    for b in bones:
        b["children"] = []
    for b in bones:
        parent = b.get("parent")
        if parent and parent in by_name:
            by_name[parent]["children"].append(b)
    for b in bones:
        if not b.get("parent") or b.get("parent") not in by_name:
            walk(b, (0.0, 0.0, 0.0), IDENTITY, (0.0, 0.0, 0.0))
    return result


def load_obj(path, scale):
    """返回 {object 名: [每个方块的 8 个角点]}。"""
    objects = []
    current = None
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("o "):
                current = (line[2:].strip(), [])
                objects.append(current)
            elif line.startswith("v "):
                _, x, y, z = line.split()
                current[1].append((float(x) * scale, float(y) * scale, float(z) * scale))

    grouped = {}
    for name, verts in objects:
        grouped.setdefault(name, [])
        for i in range(0, len(verts), 8):
            grouped[name].append(verts[i:i + 8])
    return grouped


def load_obj_uv(path, scale, tex_w, tex_h):
    """返回 {(object 名, 面): [(基岩坐标角点, (u, v) 像素), ...]}，已排序。"""
    vertices, texcoords, objects = [], [], []
    current = None
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("o "):
                current = {"name": line[2:].strip(), "v0": len(vertices), "faces": []}
                objects.append(current)
            elif line.startswith("v "):
                vertices.append(tuple(float(x) * scale for x in line.split()[1:]))
            elif line.startswith("vt "):
                texcoords.append((float(line.split()[1]), float(line.split()[2])))
            elif line.startswith("f "):
                current["faces"].append(
                    [tuple(int(x) for x in p.split("/")) for p in line.split()[1:]])

    answer = {}
    for obj in objects:
        v0 = obj["v0"]
        corners = vertices[v0:v0 + 8]
        for triples in obj["faces"]:
            local = [p[0] - v0 for p in triples]
            face = VERTS_OF_FACE.get(frozenset(local))
            if face is None:
                continue
            order = FACE_VERTS[face]
            # OBJ 的面是倒着写的：第 j 个顶点对应 order[3-j]，UV 也是倒着给的
            table = answer.setdefault((obj["name"], BB_FACE_TO_OURS[face]), [])
            for j in range(4):
                p = corners[order[3 - j] - 1]
                uv = texcoords[triples[j][1] - 1]
                table.append((quantize((-p[0], p[1], p[2])), (uv[0] * tex_w, (1.0 - uv[1]) * tex_h)))
    for table in answer.values():
        table.sort()
    return answer


def quantize(p):
    return tuple(round(v, 3) for v in p)


def check_uv(builder, answer, tex_w, tex_h):
    """逐 (骨骼, 面, 角点) 比较 UV，返回最大偏差（像素）。

    顶点位置相同的角点（复制方块、薄片正反面）按集合比较，只要有一个对上就算对。
    """
    def group(pairs):
        out = {}
        for pos, uv in pairs:
            out.setdefault(pos, set()).add(tuple(round(v, 3) for v in uv))
        return out

    ours = {}
    for i, name in enumerate(builder.owner):
        p = builder.vertices[i]
        uv = builder.uvs[i]
        ours.setdefault((name, builder.face_of[i]), []).append(
            (quantize((-p[0] * 16.0, p[1] * 16.0, -p[2] * 16.0)),
             (uv[0] * tex_w, (1.0 - uv[1]) * tex_h)))
    ours = {k: group(v) for k, v in ours.items()}

    worst = 0.0
    worst_at = None
    compared = 0
    skipped = 0
    for key, pairs in answer.items():
        mine = ours.get(key)
        if not mine:
            skipped += 1
            continue
        for pos, uvs in group(pairs).items():
            got = mine.get(pos)
            if not got:
                continue
            compared += 1
            d = max(min(math.dist(a, b) for b in got) for a in uvs)
            if d > worst:
                worst = d
                worst_at = (key[0], key[1], pos, sorted(uvs)[0], sorted(got)[0])
    return worst, worst_at, compared, skipped


def unity_points(points):
    """Blockbench 世界坐标 -> Unity 坐标（和 C# 的 ToUnity 一致）。

    bb 坐标本身就是基岩坐标沿 X 镜像来的，所以这里只翻 Z，再翻一次 X 就翻多了。
    """
    return [(p[0] / 16.0, p[1] / 16.0, -p[2] / 16.0) for p in points]


def compare(expected, builder):
    """expected: {骨骼名: [点...]}，返回每个点到自己骨骼最近顶点的最大/平均距离。"""
    produced = {}
    for i, name in enumerate(builder.owner):
        produced.setdefault(name, []).append(builder.vertices[i])

    worst = 0.0
    total = 0.0
    count = 0
    worst_bone = None
    missing = []
    for name, points in expected.items():
        ours = produced.get(name)
        if not ours:
            missing.append(name)
            continue
        for p in points:
            best = min(math.dist(p, q) for q in ours)
            total += best
            count += 1
            if best > worst:
                worst = best
                worst_bone = name
    return worst, worst_bone, (total / count if count else 0.0), missing


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 1
    model_path, obj_path = argv[1], argv[2]
    scale = 16.0
    survey = False
    i = 3
    while i < len(argv):
        if argv[i] == "--scale":
            scale = float(argv[i + 1])
            i += 2
        elif argv[i] == "--survey":
            survey = True
            i += 1
        else:
            print("未知参数: " + argv[i])
            return 1

    geo = parse_geometry(load_json(model_path))
    expected = blockbench_corners(geo)
    obj = load_obj(obj_path, scale)

    print("Blockbench OBJ: %d 个方块  |  模型: %d 个方块" % (
        sum(len(v) for v in obj.values()), sum(len(v) for v in expected.values())))
    names = set(expected) | set(obj)
    only_expected = sorted(n for n in names if expected.get(n) and n not in obj)
    only_obj = sorted(n for n in names if n in obj and n not in expected)
    if only_expected:
        print("只在模型里: " + ", ".join(only_expected))
    if only_obj:
        print("只在 OBJ 里: " + ", ".join(only_obj))

    # OBJ 坐标 -> Unity 坐标，和 C# 的 ToUnity 一致
    answer = {}
    for name, cubes in expected.items():
        if not cubes or name not in obj:
            continue
        flat = [p for cube in cubes for p in cube]
        answer[name] = unity_points(flat)

    # UV 对拍（用 C# 的默认旋转约定）
    conv = Convert()
    builder = Builder(geo, conv).build()
    answer_uv = load_obj_uv(obj_path, scale, geo["tex_w"], geo["tex_h"])
    uv_worst, uv_at, compared, skipped = check_uv(builder, answer_uv, geo["tex_w"], geo["tex_h"])
    print("")
    print("=== UV 对拍（当前默认约定）===")
    print("比较 %d 个角点，跳过 %d 个面组，最大 UV 偏差 %.3f 像素" % (compared, skipped, uv_worst))
    if uv_at and uv_worst > 0.05:
        print("  最大处: 骨骼=%s 面=%s 角点=%s OBJ=%s 我们=%s" % (
            uv_at[0], uv_at[1], uv_at[2],
            tuple(round(x, 2) for x in uv_at[3]), tuple(round(x, 2) for x in uv_at[4])))

    if not survey:
        return 0

    print("")
    print("=== 和 Blockbench 的输出对拍（单位：格，1 格 = 16 像素）===")
    results = []
    combos = [(s, o) for s in SIGNS for o in ORDERS] if survey else [((1, 1, 1), "xzy")]
    for signs in {c[0] for c in combos}:
        for order in {c[1] for c in combos}:
            conv = Convert()
            conv.rotation_signs = signs
            conv.rotation_order = order
            builder = Builder(geo, conv).build()
            worst, bone, mean, _ = compare(answer, builder)
            results.append((worst, mean, signs, order, bone))
    results.sort(key=lambda r: r[0])
    for worst, mean, signs, order, bone in results[:12]:
        print("  %+d%+d%+d %-3s  最大偏差=%.4f 格 平均=%.4f (%s)" % (
            signs[0], signs[1], signs[2], order.upper(), worst, mean, bone))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
