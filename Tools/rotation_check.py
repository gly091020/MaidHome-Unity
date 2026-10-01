#!/usr/bin/env python3
"""拿游戏渲染器（TLM 的 AbstractBedrockEntityModel + BedrockPart）的算法当基准，
逐骨骼和我们自己的转换器比几何形状，只查旋转约定对不对。

TLM 的做法（源码 com.github.tartaricacid.simplebedrockmodel）：
  convertPivot: 有父骨骼 -> x/z 是"自己-父"，y 是"父-自己"；没父骨骼 -> y 用 24 - 自己
  convertOrigin: x/z 是"方块原点-骨骼旋转点"，y 是"骨骼旋转点-方块原点-方块高度"
  convertRotation: 度 -> 弧度，**不取反**
  渲染: Matrix4f.rotateZYX(zRot, yRot, xRot)，即 Rz*Ry*Rx

把它的结果和我们的结果都化成"骨骼内两两距离"来比：距离是旋转/平移不变量，
只要两边不一样，就一定是旋转约定错了。

用法:
  python Tools/rotation_check.py <model.json>
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from bedrock_preview import IDENTITY, Builder, Convert, apply, load_json, mat3, mat_mul, parse_geometry, sub  # noqa: E402


def java_rotation(rx, ry, rz):
    """Java 侧 Rz * Ry * Rx，角度直接用。"""
    return mat_mul(mat3(0, 0, rz), mat_mul(mat3(0, ry, 0), mat3(rx, 0, 0)))


def box_corners(origin, size):
    x0, y0, z0 = origin
    x1, y1, z1 = (origin[i] + size[i] for i in range(3))
    return [(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]


def tlm_corners(geo):
    """按 TLM 的算法返回 {骨骼名: [角点...]}（Java 模型空间，单位像素）。"""
    bones = geo["bones"]
    by_name = {}
    for b in bones:
        by_name.setdefault(b.get("name", "?"), b)

    result = {}

    def walk(bone, parent_pos, parent_rot, parent_bone):
        name = bone.get("name", "?")
        pivot = bone.get("pivot", (0, 0, 0))
        if parent_bone is None:
            local = (pivot[0], 24.0 - pivot[1], pivot[2])
        else:
            pp = parent_bone.get("pivot", (0, 0, 0))
            local = (pivot[0] - pp[0], pp[1] - pivot[1], pivot[2] - pp[2])
        rotated = apply(parent_rot, local)
        pos = tuple(parent_pos[i] + rotated[i] for i in range(3))
        r = bone.get("rotation", (0, 0, 0))
        rot = mat_mul(parent_rot, java_rotation(r[0], r[1], r[2]))

        points = []
        for cube in bone.get("cubes", []) or []:
            origin = cube.get("origin", (0, 0, 0))
            size = cube.get("size", (0, 0, 0))
            cr = cube.get("rotation")
            if cr and any(abs(v) > 1e-9 for v in cr):
                cp = cube["pivot"]
                cube_local = (cp[0] - pivot[0], pivot[1] - cp[1], cp[2] - pivot[2])
                box_origin = (origin[0] - cp[0], cp[1] - origin[1] - size[1], origin[2] - cp[2])
                box_rot = java_rotation(cr[0], cr[1], cr[2])
            else:
                cube_local = (0.0, 0.0, 0.0)
                box_origin = (origin[0] - pivot[0], pivot[1] - origin[1] - size[1], origin[2] - pivot[2])
                box_rot = IDENTITY
            for corner in box_corners(box_origin, size):
                p = apply(box_rot, corner)
                p = tuple(p[i] + cube_local[i] for i in range(3))
                p = apply(rot, p)
                points.append(tuple(pos[i] + p[i] for i in range(3)))
        result[name] = points

        for child in bone.get("children", []) or []:
            walk(child, pos, rot, bone)

    for b in bones:
        b["children"] = []
    for b in bones:
        parent = b.get("parent")
        if parent and parent in by_name:
            by_name[parent]["children"].append(b)
    for b in bones:
        if not b.get("parent") or b.get("parent") not in by_name:
            walk(b, (0.0, 0.0, 0.0), IDENTITY, None)
    return result


def distance_signature(points, scale=1.0):
    uniq = sorted(set((round(p[0] * scale, 4), round(p[1] * scale, 4), round(p[2] * scale, 4)) for p in points))
    out = []
    for i in range(len(uniq)):
        for j in range(i + 1, len(uniq)):
            out.append(round(math.dist(uniq[i], uniq[j]), 4))
    return sorted(out)


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    geo = parse_geometry(load_json(argv[1]))
    reference = tlm_corners(geo)

    builder = Builder(geo, Convert()).build()
    ours = {}
    for i, name in enumerate(builder.owner):
        ours.setdefault(name, []).append(builder.vertices[i])

    print("逐骨骼比较（TLM 的 Java 空间是像素，我们的 Unity 空间是米，所以各算各的间距再比）")
    print("%-10s %-25s %-25s %s" % ("骨骼", "游戏(TLM)间距数", "我们间距数", "最大差"))
    worst = 0.0
    bad = []
    for name in sorted(reference):
        ref = distance_signature(reference[name], 1.0 / 16.0)
        mine = distance_signature(ours.get(name, []))
        if not ref or not mine:
            print("%-10s %-25s %-25s %s" % (name, len(ref), len(mine), "无顶点"))
            continue
        if len(ref) != len(mine):
            print("%-10s %-25s %-25s %s" % (name, len(ref), len(mine), "数量不一致"))
            continue
        d = max(abs(a - b) for a, b in zip(ref, mine))
        flag = "" if d < 1e-3 else "  <== 不一致"
        if d >= 1e-3:
            bad.append((d, name))
        worst = max(worst, d)
        print("%-10s %-25d %-25d %.5f%s" % (name, len(ref), len(mine), d, flag))
    print("")
    print("最大间距差 = %.5f 米" % worst)
    if bad:
        print("结论: 有 %d 根骨骼和游戏渲染器不一致: %s" % (
            len(bad), ", ".join("%s(%.4f)" % (n, d) for d, n in sorted(bad, reverse=True))))
    else:
        print("结论: 所有骨骼和游戏渲染器完全一致。")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
