"""校验 house.json 的可通行格数据，不需要 Unity。

会打印每层的平面图、连通块、以及"每一列哪几层能站"，用来核对数据是不是和房子对得上。
连通性按"MC 走法"算：四方向可同层/升降 1 格，对角不许切墙角（原来 C# 侧那份 A* 已经删了，这里是独立实现）。

用法: python Tools/house_grid_check.py <house.json>
"""

import json
import os
import sys
from collections import deque

# 同层 / 上一格 / 下一格，对角另外要求两个正交邻居同高
OFFSETS = ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1))


def parse(path):
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def build_walkable(doc):
    size = doc.get("size", [])
    sx, sy, sz = (size + [0, 0, 0])[:3]
    layers = doc.get("walkable", [])
    walkable = set()
    warnings = []

    if len(layers) != sy:
        warnings.append("walkable 层数 %d != size[1] %d，缺的层按不可走" % (len(layers), sy))

    unknown = 0
    for y in range(min(len(layers), sy)):
        rows = layers[y].split("\n")
        if len(rows) != sz:
            warnings.append("第 %d 层行数 %d != size[2] %d" % (y, len(rows), sz))
        for z in range(min(len(rows), sz)):
            row = rows[z].rstrip("\r")
            if len(row) != sx:
                warnings.append("第 %d 层第 %d 行长度 %d != size[0] %d" % (y, z, len(row), sx))
            for x in range(min(len(row), sx)):
                c = row[x]
                if c == "1":
                    walkable.add((x, y, z))
                elif c != "0":
                    unknown += 1

    if unknown:
        warnings.append("有 %d 个非 0/1 字符，按不可走处理" % unknown)
    return sx, sy, sz, walkable, warnings


def neighbors(x, y, z, walkable):
    for dx, dz in OFFSETS:
        diagonal = dx != 0 and dz != 0
        for dy in (0, 1, -1):
            ny = y + dy
            if (x + dx, ny, z + dz) not in walkable:
                continue
            if diagonal and ((x + dx, ny, z) not in walkable or (x, ny, z + dz) not in walkable):
                continue
            yield (x + dx, ny, z + dz)


def components(walkable):
    remaining = set(walkable)
    groups = []
    while remaining:
        seed = next(iter(remaining))
        queue = deque([seed])
        remaining.discard(seed)
        group = [seed]
        while queue:
            x, y, z = queue.popleft()
            for nxt in neighbors(x, y, z, walkable):
                if nxt in remaining:
                    remaining.discard(nxt)
                    queue.append(nxt)
                    group.append(nxt)
        groups.append(group)
    groups.sort(key=len, reverse=True)
    return groups


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    path = sys.argv[1]
    doc = parse(path)
    sx, sy, sz, walkable, warnings = build_walkable(doc)

    print("文件: %s" % os.path.basename(path))
    print("name=%r  model=%r" % (doc.get("name", ""), doc.get("model", "")))
    print("size=%s  origin=%s" % (doc.get("size"), doc.get("origin")))

    model = doc.get("model", "")
    if model:
        for ext in (".gltf", ".glb", ""):
            candidate = os.path.join(os.path.dirname(path), model + ext)
            if ext and os.path.isfile(candidate):
                print("模型: %s (%d 字节)" % (os.path.basename(candidate), os.path.getsize(candidate)))
                break

    print("可走格 %d / 总格 %d (%.0f%%)" % (len(walkable), sx * sy * sz,
                                          100.0 * len(walkable) / max(sx * sy * sz, 1)))

    print("\n逐层平面图（行 = z 递增，列 = x 递增，'#' = 能站）:")
    for y in range(sy):
        count = sum(1 for cell in walkable if cell[1] == y)
        print("  y=%d  (%d 格)" % (y, count))
        if count == 0:
            print("    （空）")
            continue
        for z in range(sz):
            line = "".join("#" if (x, y, z) in walkable else "." for x in range(sx))
            print("    " + line)

    columns = {}
    for x, y, z in walkable:
        columns.setdefault((x, z), []).append(y)
    multi = {key: value for key, value in columns.items() if len(value) > 1}
    print("\n有 %d 个 (x,z) 列存在多层能站" % len(multi))
    for key in sorted(multi)[:8]:
        print("    x=%d z=%d -> y %s" % (key[0], key[1], sorted(multi[key])))

    groups = components(walkable)
    print("\n连通块 %d 个" % len(groups))
    for group in groups[:6]:
        sample = sorted(group)[0]
        print("    %d 格，例如 %s" % (len(group), sample))
    if len(groups) > 6:
        print("    …还有 %d 个" % (len(groups) - 6))

    origin = doc.get("origin", [])
    if len(origin) >= 3:
        cell = (origin[0], origin[1], origin[2])
        if 0 <= cell[0] < sx and 0 <= cell[1] < sy and 0 <= cell[2] < sz:
            state = "能站" if cell in walkable else "不能站"
            print("\norigin %s 落在格子内，该格%s" % (list(cell), state))
        else:
            print("\norigin %s 超出 size %s 的范围" % (origin, doc.get("size")))

    if warnings:
        print("\n格式警告:")
        for item in warnings[:10]:
            print("  " + item)
        return 1

    print("\n格式检查通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
