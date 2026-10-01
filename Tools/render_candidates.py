#!/usr/bin/env python3
"""把若干种转换组合各渲染一张预览，拼成对比图，用来人工挑选。

用法: python Tools/render_candidates.py <model.json> [--out out.png] [--only 名字片段]
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bedrock_preview as bp


def crop_to(geo, roots):
    """只保留 roots 子树的骨骼，渲染时相机会自动贴近这部分。"""
    keep = set()
    by_name = {b["name"]: b for b in geo["bones"]}
    for root in roots:
        stack = [root]
        while stack:
            name = stack.pop()
            if name in keep:
                continue
            bone = by_name.get(name)
            if bone is None:
                continue
            keep.add(name)
            for b in geo["bones"]:
                if b.get("parent") == name:
                    stack.append(b["name"])
    geo = copy.deepcopy(geo)
    geo["bones"] = [b for b in geo["bones"] if b["name"] in keep]
    for b in geo["bones"]:
        b["parent"] = b.get("parent") if b.get("parent") in keep else None
    return geo


def render_case(full_geo, tex, signs, order, flip_u, double_sided, cell=220, views=("front", "side")):
    geo = copy.deepcopy(full_geo)
    conv = bp.Convert()
    conv.rotation_signs = signs
    conv.rotation_order = order
    builder = bp.Builder(geo, conv, flip_face_u=flip_u, double_sided_flat=double_sided)
    builder.build()
    cut = bp.alpha_sampler(tex)
    tiles = []
    for view in views:
        depth, out = bp.rasterize(builder, (cell, cell), view, cut)
        tiles.append(out)
    return tiles


CASES = [
    ("A +X+Y+Z_XZY  u翻 双面", (1, 1, 1), "xzy", True, True),
    ("B +X+Y+Z_XZY  u翻 单面", (1, 1, 1), "xzy", True, False),
    ("C +X+Y+Z_XZY  u原 双面", (1, 1, 1), "xzy", False, True),
    ("D +X+Y+Z_XZY  u原 单面", (1, 1, 1), "xzy", False, False),
    ("E +X-Y+Z_ZYX  u翻 双面", (1, -1, 1), "zyx", True, True),
    ("F +X-Y+Z_ZYX  u原 单面", (1, -1, 1), "zyx", False, False),
    ("G -X+Y-Z_ZYX  u原 双面", (-1, 1, -1), "zyx", False, True),
    ("H +X+Y+Z_ZYX  u原 单面", (1, 1, 1), "zyx", False, False),
]


def main(argv):
    model = argv[1]
    out_path = "Tools/out/hair_candidates.png"
    only = None
    i = 2
    while i < len(argv):
        if argv[i] == "--out":
            out_path = argv[i + 1]
            i += 2
        elif argv[i] == "--only":
            only = argv[i + 1]
            i += 2
        else:
            i += 1

    doc = bp.load_json(model)
    full_geo = bp.parse_geometry(doc)
    tex = bp.read_png(os.path.splitext(model)[0] + ".png")
    roots = [b["name"] for b in full_geo["bones"] if b["name"] in ("head", "bone40", "hairLeft", "hairRight")]
    geo = crop_to(full_geo, roots)
    print("裁剪后保留的骨骼:", [b["name"] for b in geo["bones"]])

    cases = [c for c in CASES if not only or only in c[0]]
    views = ("front", "side")
    cell = 220
    columns = 4
    rows = (len(cases) + columns - 1) // columns
    tile_w = cell * len(views)
    tile_h = cell
    W, H = tile_w * columns, tile_h * rows
    buf = bytearray(W * H * 3)
    for index, (name, signs, order, flip_u, dbl) in enumerate(cases):
        tiles = render_case(geo, tex, signs, order, flip_u, dbl, cell, views)
        col = index % columns
        row = index // columns
        for vi, out in enumerate(tiles):
            ox = col * tile_w + vi * cell
            oy = row * tile_h
            for y in range(cell):
                for x in range(cell):
                    lum, uv = out[y][x]
                    o = ((oy + y) * W + (ox + x)) * 3
                    if lum < 0:
                        continue
                    c = bp.sample(tex, uv)
                    k = 0.35 + 0.65 * lum
                    buf[o] = int(min(255, c[0] * k))
                    buf[o + 1] = int(min(255, c[1] * k))
                    buf[o + 2] = int(min(255, c[2] * k))

    folder = os.path.dirname(out_path)
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)
    bp.write_png(out_path, W, H, buf)
    print("已输出:", out_path, f"({W}x{H})")
    print("布局（每格左=正面, 右=侧面）：")
    for index, (name, _, _, _, _) in enumerate(cases):
        print(f"  第 {index // columns + 1} 行 第 {index % columns + 1} 个: {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
