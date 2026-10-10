#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""女仆布娃娃（网格方案）校验（纯标准库，不依赖 Unity）。

这是 Assets/Scripts/Gameplay/Maid/MaidRagdollPlanner.cs 的规则镜像：

* **识别到的部位**（头 / 身体 / 左右手 / 左右脚 / 裙子 / 尾巴，名字按优先级匹配、
  精确优先于包含、层级浅优先）会把子树里的网格**合成一块** ——
  "头 + 脸 + 头发 + 耳朵"是一块，"裙子 + 各片裙摆"是一块；
* 没识别到的网格各自一块；
* 关节按骨架层级连（头发的上一节就是上一段头发），没有祖先块的用相交兜底、再不行挂主干；
* 真的互相插进去的块之间不互相碰撞（Unity 侧用真实凸包 + 采样算体积，
  这里用外接盒粗估）。

用法::

    python Tools/ragdoll_gen_check.py <geo.json> [<geo.json> ...]
    python Tools/ragdoll_gen_check.py <geo.json> --preview
    python Tools/ragdoll_gen_check.py --selftest
    python Tools/ragdoll_gen_check.py <geo.json> --json Tools/out/ragdoll_plan.json

坐标/尺寸都按原始模型像素（1 格 = 16 像素）打印；Unity 侧是在建好的骨架层级上算，
旋转过的骨骼（TLM 模型左上臂那种）包围盒会有零点几像素的出入，属于正常。
"""

from __future__ import annotations

import argparse
import json
import math
import sys

# 网格模式（对应 Unity 的 MaidRagdollMode.Mesh）的常数，和 MaidRagdollMeshSplitter 一致
MESH_MAX_BODIES = 48
MESH_MAX_CONVEX_TRIS = 240
MESH_CONTACT_TOL = 0.012    # 判断接触的容差（格）
MESH_MAX_CONTACTS = 4       # 每块最多连几处

# 重叠判定（对应 MaidRagdollOverlap.Ratio）：相交体积占较小那块这么多就算"插进去了"，
# 这样的部位对之间取消碰撞（角度默认跟普通关节一样，Unity 侧可以用 _overrideOverlapAngles 覆盖）
OVERLAP_RATIO = 0.05

# 角色 -> (别名按优先级, 典型尺寸像素)。别名去分隔符小写，先精确匹配、再包含匹配。
ROLE_RULES = [
    # 能"合成一块"的部位（对应 MaidRagdollPlanner.PartNames）：识别到的部位把子树里的网格并成一块
    ("head", ["head", "mhead", "allhead", "headall"], (8, 8, 8)),
    ("body", ["body", "upbody", "upperbody", "allbody", "downbody", "torso", "chest", "spine", "waist"],
     (8, 10, 6)),
    ("skirt", ["skirt", "clothe", "clothes", "dress", "robe"], (8, 8, 8)),
    ("tail", ["tail"], (6, 12, 8)),
    ("armLeft", ["armleft", "leftarm", "larm", "arml"], (3, 9, 3)),
    ("armRight", ["armright", "rightarm", "rarm", "armr"], (3, 9, 3)),
    ("legLeft", ["legleft", "leftleg", "lleg", "legl"], (3, 9, 3)),
    ("legRight", ["legright", "rightleg", "rleg", "legr"], (3, 9, 3)),
]

# 随模型一起藏起来的骨骼（照抄 MaidAssetLoader.HiddenNodes），整棵子树都不参与识别
HIDDEN_NODES = ["fox", "ahoge", "begshow", "blink", "blink2", "hurtblink", "danmakuattackshow"]

# 几何推测（不认名字，纯按体积分支挑部件根）用的门槛
GUESS_MIN_SHARE = 0.12       # 分支体积占父级这么多才算一个部件（其余算装饰，留在父部件里）
GUESS_DOMINANT = 0.90        # 只有一个分支独占这么多体积时算"同一段身体"，往下降根
GUESS_MAX_PARTS = 16
GUESS_BOX_RATIO = 2.5        # 比主干盒子大这么多倍的盒子（头发/尾巴）不进碰撞盒

# ---------------------------------------------------------------- 矩阵工具


def mat_identity():
    return [[1.0, 0, 0, 0], [0, 1.0, 0, 0], [0, 0, 1.0, 0], [0, 0, 0, 1.0]]


def mat_mul(a, b):
    out = [[0.0] * 4 for _ in range(4)]
    for i in range(4):
        for j in range(4):
            out[i][j] = sum(a[i][k] * b[k][j] for k in range(4))
    return out


def mat_translate(x, y, z):
    m = mat_identity()
    m[0][3], m[1][3], m[2][3] = x, y, z
    return m


def mat_rot_x(deg):
    r = math.radians(deg)
    c, s = math.cos(r), math.sin(r)
    return [[1, 0, 0, 0], [0, c, -s, 0], [0, s, c, 0], [0, 0, 0, 1]]


def mat_rot_y(deg):
    r = math.radians(deg)
    c, s = math.cos(r), math.sin(r)
    return [[c, 0, s, 0], [0, 1, 0, 0], [-s, 0, c, 0], [0, 0, 0, 1]]


def mat_rot_z(deg):
    r = math.radians(deg)
    c, s = math.cos(r), math.sin(r)
    return [[c, -s, 0, 0], [s, c, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]


def mat_apply(m, x, y, z):
    return (
        m[0][0] * x + m[0][1] * y + m[0][2] * z + m[0][3],
        m[1][0] * x + m[1][1] * y + m[1][2] * z + m[1][3],
        m[2][0] * x + m[2][1] * y + m[2][2] * z + m[2][3],
    )


# ---------------------------------------------------------------- 模型读取


def normalize(name):
    return "".join(ch for ch in (name or "").lower() if ch.isalnum())


def parse_bones(model_json):
    """兼容 1.12 的 minecraft:geometry 和 1.10 的 geometry.model 两种写法。"""
    bones = None
    geometry = model_json.get("geometry.model")
    if isinstance(geometry, dict):
        bones = geometry.get("bones")
    if bones is None:
        geometries = model_json.get("minecraft:geometry")
        if isinstance(geometries, list) and geometries:
            bones = geometries[0].get("bones")
    return bones or []


def pivot_internal(bone):
    """骨骼轴在模型空间的位置（像素，y 向下，脚底在 y=24）。"""
    px, py, pz = bone.get("pivot", [0, 0, 0])
    return (px, 24.0 - py, pz)


def bone_matrix(bones, name, cache):
    """骨骼相对模型根的变换矩阵（像素），旋转顺序照抄 TLM/Blockbench 的 ZYX。"""
    if name in cache:
        return cache[name]
    bone = bones[name]
    pivot = pivot_internal(bone)
    parent = bone.get("parent")
    if parent and parent in bones:
        parent_matrix, parent_pivot = bone_matrix(bones, parent, cache)[:2]
        rel = (pivot[0] - parent_pivot[0], pivot[1] - parent_pivot[1], pivot[2] - parent_pivot[2])
        base = mat_mul(parent_matrix, mat_translate(*rel))
    else:
        base = mat_translate(*pivot)
    rotation = bone.get("rotation")
    matrix = base
    if rotation and any(rotation):
        rotation_matrix = mat_mul(mat_rot_z(rotation[2]), mat_mul(mat_rot_y(rotation[1]), mat_rot_x(rotation[0])))
        matrix = mat_mul(base, rotation_matrix)
    cache[name] = (matrix, pivot)
    return cache[name]


def cube_local_bounds(bone, cube):
    px, py, pz = bone.get("pivot", [0, 0, 0])
    ox, oy, oz = cube.get("origin", [0, 0, 0])
    sx, sy, sz = cube.get("size", [0, 0, 0])
    inflate = cube.get("inflate", 0) or 0
    return (
        ox - inflate - px,
        py - (oy + sy + inflate),
        oz - inflate - pz,
        ox + sx + inflate - px,
        py - (oy - inflate),
        oz + sz + inflate - pz,
    )


def box_in_root(matrix, local):
    xs, ys, zs = [], [], []
    for x in (local[0], local[3]):
        for y in (local[1], local[4]):
            for z in (local[2], local[5]):
                rx, ry, rz = mat_apply(matrix, x, y, z)
                xs.append(rx)
                ys.append(ry)
                zs.append(rz)
    return (min(xs), min(ys), min(zs), max(xs), max(ys), max(zs))


def union(boxes):
    return (
        min(b[0] for b in boxes),
        min(b[1] for b in boxes),
        min(b[2] for b in boxes),
        max(b[3] for b in boxes),
        max(b[4] for b in boxes),
        max(b[5] for b in boxes),
    )


def box_center(box):
    return ((box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2)


def box_size(box):
    return (box[3] - box[0], box[4] - box[1], box[5] - box[2])


# ---------------------------------------------------------------- 识别


def box_volume(box):
    return max(0.0, box[3] - box[0]) * max(0.0, box[4] - box[1]) * max(0.0, box[5] - box[2])


def subtree_volumes(model):
    """每根骨骼子树里所有盒子的体积和（挑"枢纽"用的）。"""
    volumes = {}

    def walk(name):
        value = box_volume(model.own_box[name]) if name in model.own_box else 0.0
        for child in model.children.get(name, []):
            value += walk(child)
        volumes[name] = value
        return value

    tops = [name for name in model.children if model.parent.get(name) is None]
    if tops:
        walk(tops[0])
    for name in model.visible():
        if name not in volumes:
            walk(name)
    return volumes


def subtree_box(model, name):
    boxes = [model.own_box[item] for item in model.subtree(name) if item in model.own_box]
    return union(boxes) if boxes else None


def part_chain(model, volumes, root, dominant=0.9):
    """主干链：从这根骨骼往下走独生子（或者空骨架里独占绝大部分体积的那个孩子）。

    一段连续的身体（一只胳膊、一条尾巴、一个头）在层级上就是一条链。
    """
    chain = [root]
    cursor = root
    for _ in range(24):
        kids = model.children.get(cursor, [])
        if not kids:
            break
        if len(kids) == 1:
            nxt = kids[0]
        elif cursor not in model.own_box and volumes:
            parent_volume = max(volumes.get(cursor, 0.0), 1e-9)
            best = max(kids, key=lambda kid: volumes.get(kid, 0.0))
            if volumes.get(best, 0.0) < parent_volume * dominant:
                break
            nxt = best
        else:
            break
        if nxt in chain:
            break
        chain.append(nxt)
        cursor = nxt
    return chain


def representative(model, name):
    """穿过"没有自己几何、只有一个孩子"的骨架节点，落到真正有内容的骨骼上。"""
    cursor = name
    for _ in range(64):
        if cursor in model.own_box or len(model.children.get(cursor, [])) != 1:
            break
        cursor = model.children[cursor][0]
    return cursor


# ---------------------------------------------------------------- 网格模式


def mesh_boxes(model):
    """每根带网格的骨骼一块物理块：返回 [(骨骼名, 根空间包围盒, 估计三角面数)]。

    基岩模型每根骨骼的网格就是一个或几个方块，方块默认 6 个面 = 12 个三角面，
    用来估"凸网格碰撞体吃不吃得下"（Unity 的凸包上限 255 面）。
    """
    boxes = []
    for name in model.own_box:
        cubes = len(model.bones[name].get("cubes") or [])
        boxes.append((name, model.own_box[name], cubes * 12))
    boxes.sort(key=lambda item: -box_volume(item[1]))
    return boxes[:MESH_MAX_BODIES]


def mesh_overlap(a, b):
    lo = [max(a[i], b[i]) for i in range(3)]
    hi = [min(a[i + 3], b[i + 3]) for i in range(3)]
    sizes = [hi[i] - lo[i] + MESH_CONTACT_TOL for i in range(3)]
    if any(size <= 0 for size in sizes):
        return None
    center = [(lo[i] + hi[i]) / 2 for i in range(3)]
    return center, sizes[0] * sizes[1] * sizes[2]


def orient(model, boxes, parent, a, b):
    """决定这条接触线谁当父级：优先让"还没有父级"的那个当子级，方向尽量顺着层级。

    每个块只能有一个父级（Unity 那边一个部位一根关节），两边都挂过了就只能丢掉这条接触，
    不然会连出好几棵树 / 有向环。
    """
    if parent[a] < 0 and parent[b] >= 0:
        return a, b
    if parent[b] < 0 and parent[a] >= 0:
        return b, a
    if parent[a] < 0 and parent[b] < 0:
        if model.is_descendant(boxes[a][0], boxes[b][0]):
            return a, b
        if model.is_descendant(boxes[b][0], boxes[a][0]):
            return b, a
        if box_volume(boxes[a][1]) >= box_volume(boxes[b][1]):
            return b, a
        return a, b
    return -1, -1


def reroot(parent, root, centers):
    """把树重新挂到 root 上：沿着它的父链翻边，锚点跟着边不动。"""
    count = len(parent)
    neighbours = [[] for _ in range(count)]
    for index in range(count):
        if parent[index] >= 0:
            neighbours[index].append(parent[index])
            neighbours[parent[index]].append(index)

    new_parent = [-1] * count
    new_centers = [None] * count
    seen = [False] * count
    queue = [root]
    seen[root] = True
    while queue:
        node = queue.pop(0)
        for other in neighbours[node]:
            if seen[other]:
                continue
            seen[other] = True
            new_parent[other] = node
            # 锚点挂在"边"上：换方向以后仍旧用那条边的接触中心
            new_centers[other] = centers[other] if parent[other] == node else centers[node]
            queue.append(other)
    return new_parent, new_centers


def plan_model_mesh(model_json):
    """唯一的方案（对应 MaidRagdollPlanner）：识别到的部位把子树网格合成一块，其余逐网格一块。

    * 部位（头 / 身体 / 裙子 / 尾巴 / 左右手 / 左右脚）按名字认出来，各自一块；
      例如"头 + 脸 + 头发 + 耳朵"是一块，"裙子 + 各片裙摆"是一块；
    * 归属：每块网格往上找最近的部位根，找不到就自己一块；
    * 关节：按骨架层级连（头发的上一节就是上一段头发），接触面只用来兜底和当锚点。
    """
    model = Model(model_json)
    parts = detect_parts(model)[0]
    # 每块网格归到最近的部位根（含自己）
    groups = []
    owner_of = {}
    for name in sorted(model.own_box):
        owner = name
        cursor = name
        while cursor is not None:
            if cursor in parts.values():
                owner = cursor
                break
            cursor = model.parent.get(cursor)
        owner_of[name] = owner
        if owner not in [g["root"] for g in groups]:
            groups.append({"root": owner, "bones": []})
        for group in groups:
            if group["root"] == owner:
                group["bones"].append(name)

    # 部位在前，单块网格在后
    order = list(parts.values())
    groups.sort(key=lambda group: (order.index(group["root"]) if group["root"] in order else 99,
                                   group["root"]))

    result = {"mode": "mesh", "parts": [], "warnings": [], "links": []}
    if not groups:
        result["warnings"].append("模型里没有可用网格，建不出网格布娃娃")
        return result

    count = len(groups)
    boxes = []
    for group in groups:
        union_box = union([model.own_box[name] for name in group["bones"] if name in model.own_box])
        mesh_count = sum(1 for name in group["bones"] if name in model.own_box)
        boxes.append((group["root"], union_box, mesh_count))

    names = {boxes[n][0]: n for n in range(count)}
    parent = [-1] * count
    centers = [None] * count
    anchored = [False] * count

    # 1. 层级优先：沿骨骼父链往上找第一个也是物理块的祖先
    for index in range(count):
        cursor = model.parent.get(boxes[index][0])
        while cursor is not None:
            owner = names.get(cursor, -1)
            if owner >= 0 and owner != index:
                parent[index] = owner
                break
            cursor = model.parent.get(cursor)

    # 2. 接触面：算所有相交的对，顺便当兜底父级和关节锚点
    pairs = []
    for i in range(count):
        for j in range(i + 1, count):
            hit = mesh_overlap(boxes[i][1], boxes[j][1])
            if hit is not None:
                pairs.append((hit[1], i, j, hit[0]))
    pairs.sort(key=lambda item: -item[0])

    for _overlap, a, b, center in pairs:
        if parent[a] >= 0 and parent[b] >= 0:
            continue
        if parent[a] < 0 and parent[b] < 0:
            if box_volume(boxes[a][1]) >= box_volume(boxes[b][1]):
                child, owner = b, a
            else:
                child, owner = a, b
        else:
            child, owner = (a, b) if parent[a] < 0 else (b, a)
        if creates_cycle(parent, child, owner):
            continue
        parent[child] = owner
        centers[child] = center
        anchored[child] = True

    # 3. 还是没有父级的（既没有祖先块、也没挨着任何块）挂到主干：层级最浅的那块（一般是躯干）
    main = min(range(count),
               key=lambda index: (model.depth(boxes[index][0]), -box_volume(boxes[index][1])))
    for index in range(count):
        if parent[index] < 0 and index != main and not creates_cycle(parent, index, main):
            parent[index] = main
            anchored[index] = False

    # 4. 父子正好相交的话，关节锚点用相交区域的中心
    for _overlap, a, b, center in pairs:
        if parent[a] == b:
            centers[a] = center
            anchored[a] = True
        elif parent[b] == a:
            centers[b] = center
            anchored[b] = True

    links = []
    for index, (name, box, mesh_count) in enumerate(boxes):
        result["parts"].append({
            "index": index,
            "bone": name,
            "parent": parent[index],
            "label": parts and next((role for role, bone in parts.items() if bone == name), name) or name,
            "collider": "%d 网格" % mesh_count,
            "size": [round(v, 3) for v in box_size(box)],
            "center": [round(v, 3) for v in box_center(box)],
        })
        if parent[index] >= 0:
            links.append((index, parent[index], centers[index] if anchored[index] else None))

    result["links"] = links
    roots = [index for index in range(count) if parent[index] < 0]
    merged = sum(1 for _name, _box, mesh_count in boxes if mesh_count > 1)
    result["note"] = ("%d 块物理块（其中 %d 块是识别到的部位、子树网格合成一体），%d 处连接，根 = %s"
                      % (len(result["parts"]), merged, len(links),
                         boxes[roots[0]][0] if roots else "?"))
    return result


def creates_cycle(parent, child, owner):
    """把 child 挂到 owner 上会不会绕出环（owner 顺着父链走回 child 就是环）。"""
    cursor = owner
    for _ in range(64):
        if cursor < 0:
            return False
        if cursor == child:
            return True
        cursor = parent[cursor]
    return False


class Model:
    """一份模型里识别要用的全部特征：可见骨骼、层级、根空间包围盒。"""

    def __init__(self, model_json):
        raw = parse_bones(model_json)
        self.bones = {}
        for bone in raw:
            name = bone.get("name")
            if name and name not in self.bones:
                self.bones[name] = bone
        self.hidden = self._collect_hidden()
        self.children = {name: [] for name in self.bones}
        self.parent = {}
        for name, bone in self.bones.items():
            parent = bone.get("parent")
            if parent in self.children and name not in self.hidden:
                self.children[parent].append(name)
                self.parent[name] = parent
        self.matrices = {}
        for name in self.bones:
            bone_matrix(self.bones, name, self.matrices)
        self.own_box = {}
        for name, bone in self.bones.items():
            if name in self.hidden:
                continue
            boxes = [
                box_in_root(self.matrices[name][0], cube_local_bounds(bone, cube))
                for cube in bone.get("cubes") or []
            ]
            if boxes:
                self.own_box[name] = union(boxes)

    def _collect_hidden(self):
        """隐藏节点 + 它们的整棵子树。"""
        hidden = set()
        for name in self.bones:
            if normalize(name) in HIDDEN_NODES:
                hidden.add(name)
        changed = True
        while changed:
            changed = False
            for name, bone in self.bones.items():
                if name in hidden:
                    continue
                if bone.get("parent") in hidden:
                    hidden.add(name)
                    changed = True
        return hidden

    def visible(self):
        return [name for name in self.bones if name not in self.hidden]

    def depth(self, name):
        count = 0
        seen = set()
        bone = self.bones.get(name)
        while bone is not None and bone.get("parent") in self.bones and bone.get("parent") not in seen:
            seen.add(bone.get("parent"))
            count += 1
            bone = self.bones[bone.get("parent")]
        return count

    def is_descendant(self, name, ancestor):
        bone = self.bones.get(name)
        seen = set()
        while bone is not None and bone.get("parent") in self.bones:
            parent = bone.get("parent")
            if parent == ancestor:
                return True
            if parent in seen:
                return False
            seen.add(parent)
            bone = self.bones[parent]
        return False

    def subtree(self, name):
        result = [name]
        for child in self.children.get(name, []):
            result.extend(self.subtree(child))
        return result

    def has_geometry(self, name):
        return any(bone in self.own_box for bone in self.subtree(name))

    def pivot(self, name):
        return self.matrices[name][1]


def match_preference(normalized, aliases):
    """返回 (是否模糊匹配, 别名下标)，没命中返回 None。

    精确匹配整体优先于包含匹配 —— 不然 "MAllbody"/"UpBody" 都会被
    "body" 这个子串命中，身体反而会挑到最靠上的空骨架。
    """
    for index, alias in enumerate(aliases):
        if normalized == alias:
            return (0, index)
    for index, alias in enumerate(aliases):
        if alias in normalized:
            return (1, index)
    return None


def detect_parts(model):
    """按名字挑六个部位根骨骼，名字认不出的用几何兜底。"""
    parts = {}
    sources = {}
    taken = []
    for role, aliases, _typical in ROLE_RULES:
        best = None
        best_key = None
        for name in model.visible():
            if name in taken or "locator" in normalize(name) or not model.has_geometry(name):
                continue
            if any(model.is_descendant(name, other) for other in taken if other != parts.get("body")):
                continue
            matched = match_preference(normalize(name), aliases)
            if matched is None:
                continue
            key = (matched[0], matched[1], model.depth(name), -len(model.bones[name].get("cubes") or []))
            if best_key is None or key < best_key:
                best, best_key = name, key
        if best is not None:
            parts[role] = best
            sources[role] = "name"
            taken.append(best)
    fill_by_geometry(model, parts, sources, taken)
    return parts, sources


def fill_by_geometry(model, parts, sources, taken):
    """名字认不出来的部位按位置兜底：最高的当头，身体之上偏外侧的当手、之下当脚。"""
    def rest():
        return [
            name for name in model.visible()
            if name not in taken and model.has_geometry(name)
            # 已经认出来的部位的父级（MRoot/AllBody 这类空骨架）不参与兜底，不然
            # 它们离锚点最远、块数最多，会把四肢全抢走
            and not any(model.is_descendant(other, name) for other in taken)
            and not any(model.is_descendant(name, other) for other in taken if other != parts.get("body"))
        ]

    if "head" not in parts:
        pool = rest()
        if pool:
            # 模型空间 y 向下，头是 y 最小的那根
            head = min(pool, key=lambda name: model.pivot(name)[1])
            parts["head"] = head
            sources["head"] = "geometry"
            taken.append(head)

    if "body" not in parts:
        return

    body_y = model.pivot(parts["body"])[1]
    pool = [name for name in rest() if name != parts.get("head")]
    upper = [name for name in pool if model.pivot(name)[1] <= body_y + 3.0]
    lower = [name for name in pool if model.pivot(name)[1] > body_y + 3.0]

    def lateral(names, count):
        return sorted(names, key=lambda name: abs(model.pivot(name)[0]), reverse=True)[:count]

    if "armLeft" not in parts or "armRight" not in parts:
        fill_pair(model, parts, sources, lateral(upper, 2), "armLeft", "armRight")
    if "legLeft" not in parts or "legRight" not in parts:
        fill_pair(model, parts, sources, lateral(lower, 2), "legLeft", "legRight")


def fill_pair(model, parts, sources, picked, left, right):
    for name in picked:
        if left in parts and right in parts:
            return
        if name in parts.values():
            continue
        # x 为正当左边只是兜底推断（部分模型方向相反），跟 SableRagdollLib 的兜底一致
        role = left if model.pivot(name)[0] >= 0 else right
        if role in parts:
            role = right if role == left else left
        if role in parts:
            continue
        parts[role] = name
        sources[role] = "geometry"


def part_groups(model, parts):
    """每个部位管哪些骨骼：自己的子树，扣掉挂在里面的别的部位（头/四肢）。"""
    groups = {}
    for role, bone in parts.items():
        inside = set(model.subtree(bone))
        for other_role, other in parts.items():
            # 只扣"挂在这个部位里面"的别的部位。身体是所有部位的祖先，不能反过来把
            # 头/四肢从它们自己的分组里挖空
            if other_role != role and model.is_descendant(other, bone):
                inside -= set(model.subtree(other))
        groups[role] = sorted(inside)
    return groups


def role_anchor(model, parts, role, typical):
    pivots = {name: model.pivot(bone) for name, bone in parts.items()}
    if role == "head":
        return pivots.get("head")
    if role == "body":
        head = pivots.get("head")
        hips = [pivots[name] for name in ("legLeft", "legRight") if name in pivots]
        if head and hips:
            hip = tuple(sum(p[i] for p in hips) / len(hips) for i in range(3))
            return tuple((head[i] + hip[i]) / 2 for i in range(3))
        return pivots.get("body")
    pivot = pivots.get(role)
    if pivot is None:
        return None
    # 模型空间 y 向下，+ 就是往下半个典型长度（四肢长在关节下面）
    return (pivot[0], pivot[1] + typical[1] / 2, pivot[2])


def pick_collider(model, parts, groups, role, typical):
    """从部位组里挑碰撞盒：典型尺寸 + 离锚点距离打分，取最优的那一块。

    不做"把挨着的盒子并进来"——头发/尾巴会跟头/身体的盒子挨着，一并进来碰撞盒
    就整个炸开（头会连长发一起包住）。宁可小一点、可预测一点。
    """
    candidates = [model.own_box[name] for name in groups[role] if name in model.own_box]
    if not candidates:
        return None
    anchor = role_anchor(model, parts, role, typical)
    if anchor is None:
        anchor = box_center(union(candidates))

    def score(box):
        size = box_size(box)
        distance = sum(abs(size[i] - typical[i]) for i in range(3)) * 0.4
        center = box_center(box)
        distance += math.sqrt(sum((center[i] - anchor[i]) ** 2 for i in range(3)))
        return distance

    best = min(candidates, key=score)
    return best


def build_plan(model_json, mode):
    model = Model(model_json)
    parts, sources = detect_parts(model)
    groups = part_groups(model, parts)
    result = {"mode": mode, "parts": {}, "warnings": []}
    for role, aliases, typical in ROLE_RULES:
        if role not in parts:
            result["warnings"].append("没认出部位（名字和几何都对不上）: " + role)
            continue
        bone = parts[role]
        box = pick_collider(model, parts, groups, role, typical)
        if box is None:
            result["warnings"].append("部位没有几何，做不出碰撞盒: " + role + " -> " + bone)
            continue
        size = box_size(box)
        center = box_center(box)
        result["parts"][role] = {
            "bone": bone,
            "rule": normalize(bone),
            "matched": next((a for a in aliases if a in normalize(bone)), ""),
            "source": sources.get(role, "name"),
            "group": len(groups[role]),
            "size": [round(v, 3) for v in size],
            "center": [round(v, 3) for v in center],
        }
    return result


def plan_model(model_json):
    return build_plan(model_json, "names")


# ---------------------------------------------------------------- 自动分段
#
# 比"六个部位"大胆得多的一套：完全不管部位叫什么，也不管一只手一条腿，
# 只按"分支体积"把骨架切成若干块（头发、尾巴、耳朵、裙摆够大就各自成块），
# 块多了自然就软。每块可以带好几个碰撞盒，关节按层级自动连。

AUTO_MIN_SHARE = 0.025      # 分支体积占模型总量这么多，才值得单独立一块
AUTO_DOMINANT = 0.80        # 一个分支独占这么多体积算同一段身体，不另起一块
AUTO_MAX_PARTS = 24
AUTO_MAX_BOXES = 4          # 每块最多几个碰撞盒（原来的骨骼盒子）
AUTO_BOX_MIN_RATIO = 0.08   # 小于最大盒子这个比例的盒子不进碰撞盒（耳钉、丝带之类）
AUTO_SPLIT_MIN_GAP = 1.5    # 左右切分的最小间隙（像素）
AUTO_SPLIT_MIN_SIDE = 0.25  # 切开以后两边各占这么多体积


def auto_part_roots(model):
    """按分支体积把骨架切成若干块，返回每块的根骨骼。

    从最大的分支开始吃（用优先队列），先保证躯干 / 头 / 两条腿 / 两只手这些大块拿到名额，
    头发丝带这种小分支排在后面，名额不够就不切了 —— 否则头发会把预算吃光。
    """
    volumes = subtree_volumes(model)
    total = sum(box_volume(box) for box in model.own_box.values())
    if total <= 0:
        return []
    tops = [name for name in model.children if model.parent.get(name) is None]
    if not tops:
        return []

    # 顶层的骨骼可能不止一根（TLM 模型经常是 head / armRight / body / legLeft 平铺），
    # 这时把最粗的那根当根块，其余顶层骨骼都当成它的兄弟分支
    ranked = sorted(tops, key=lambda name: -volumes.get(name, 0.0))
    start = representative(model, ranked[0])
    roots = [start]
    spines = [part_chain(model, volumes, start, AUTO_DOMINANT)]
    queue = []

    def push(index):
        for node in spines[index]:
            for kid in model.children.get(node, []):
                child = representative(model, kid)
                queue.append((volumes.get(child, 0.0), child, index))

    for name in ranked[1:]:
        child = representative(model, name)
        queue.append((volumes.get(child, 0.0), child, 0))
    push(0)
    while queue and len(roots) < AUTO_MAX_PARTS:
        queue.sort(key=lambda item: -item[0])
        volume, node, _owner = queue.pop(0)
        if volume < total * AUTO_MIN_SHARE:
            break
        if any(node in spine for spine in spines):
            continue
        roots.append(node)
        spines.append(part_chain(model, volumes, node, AUTO_DOMINANT))
        push(len(roots) - 1)
    return roots


def auto_part_bones(model, root, roots):
    """这一块管哪些骨骼：自己的子树扣掉更深的块。"""
    inside = set(model.subtree(root))
    for other in roots:
        if other != root and model.is_descendant(other, root):
            inside -= set(model.subtree(other))
    return sorted(inside)


def auto_part_boxes(model, bones):
    """这一块的碰撞盒：成员骨骼各自的盒子，太大的不要、太小的丢掉，最多留几个大的。"""
    boxes = [model.own_box[name] for name in bones if name in model.own_box]
    if not boxes:
        return []
    biggest = max(box_volume(box) for box in boxes)
    boxes = [box for box in boxes if box_volume(box) >= biggest * AUTO_BOX_MIN_RATIO]
    boxes.sort(key=box_volume, reverse=True)
    return boxes[:AUTO_MAX_BOXES]


def auto_parent(model, root, roots):
    index_of = {name: index for index, name in enumerate(roots)}
    cursor = model.parent.get(root)
    while cursor is not None:
        if cursor in index_of:
            return index_of[cursor]
        cursor = model.parent.get(cursor)
    return -1


def plan_model_auto(model_json):
    """自动分段：不认部位，按几何把模型切成若干块（每块可能带好几个碰撞盒）。"""
    model = Model(model_json)
    roots = auto_part_roots(model)
    result = {"mode": "auto", "parts": [], "warnings": []}
    if not roots:
        result["warnings"].append("模型没有可用几何，切不出部件")
        return result

    # 一点几何都没有的块（"Arm""DownBody"这种骨架节点）丢掉，丢完再连父子
    kept = []
    for root in roots:
        bones = auto_part_bones(model, root, roots)
        boxes = auto_part_boxes(model, bones)
        if boxes:
            kept.append(root)
    if not kept:
        result["warnings"].append("模型没有可用几何，切不出部件")
        return result

    root_index = min(range(len(kept)), key=lambda index: model.depth(kept[index]))
    for index, root in enumerate(kept):
        bones = auto_part_bones(model, root, kept)
        boxes = auto_part_boxes(model, bones)
        parent = -1 if index == root_index else auto_parent(model, root, kept)
        if parent < 0 and index != root_index:
            # 层级上找不到祖先块（两条腿挂在没有几何的 DownBody 上）就挂到根块
            parent = root_index
        union_box = union(boxes)
        result["parts"].append({
            "index": index,
            "bone": root,
            "parent": parent,
            "group": len(bones),
            "boxes": len(boxes),
            "size": [round(v, 3) for v in box_size(union_box)],
            "center": [round(v, 3) for v in box_center(union_box)],
        })

    result["note"] = "%d 个部件（头发 / 尾巴 / 裙摆够大就各自成块，块多了自然软）" % len(result["parts"])
    return result



# ---------------------------------------------------------------- 自检


def selftest_model():
    """搭一个 TLM 风格的最小模型：MRoot/Root/AllBody/UpBody + 头（带头发）+ 四肢。"""
    def cube(origin, size):
        return {"origin": origin, "size": size}

    return {
        "format_version": "1.12.0",
        "minecraft:geometry": [{
            "description": {"identifier": "geometry.selftest", "texture_width": 64, "texture_height": 64},
            "bones": [
                {"name": "MRoot", "pivot": [0, 0, 0]},
                {"name": "Root", "parent": "MRoot", "pivot": [0, 0, 0]},
                {"name": "AllBody", "parent": "Root", "pivot": [0, 13, 0]},
                {"name": "UpBody", "parent": "AllBody", "pivot": [0, 19, 0],
                 "cubes": [cube([-4, 17, -2], [8, 6, 4])]},
                {"name": "Head", "parent": "UpBody", "pivot": [0, 23, 0],
                 "cubes": [cube([-4, 23, -4], [8, 8, 8])]},
                {"name": "Hair", "parent": "Head", "pivot": [0, 23, 0],
                 "cubes": [cube([-6, 21, -6], [12, 14, 12])]},
                {"name": "LeftArm", "parent": "UpBody", "pivot": [4, 22, 0],
                 "cubes": [cube([2.5, 13, -1.5], [3, 9, 3])]},
                {"name": "RightArm", "parent": "UpBody", "pivot": [-4, 22, 0],
                 "cubes": [cube([-5.5, 13, -1.5], [3, 9, 3])]},
                {"name": "LeftLeg", "parent": "AllBody", "pivot": [2, 12, 0],
                 "cubes": [cube([0.5, 3, -1.5], [3, 9, 3])]},
                {"name": "RightLeg", "parent": "AllBody", "pivot": [-2, 12, 0],
                 "cubes": [cube([-3.5, 3, -1.5], [3, 9, 3])]},
                {"name": "FOX", "parent": "Root", "pivot": [0, 0, 0]},
                {"name": "Head2", "parent": "FOX", "pivot": [0, 10, -7],
                 "cubes": [cube([-3, 6, -10], [6, 6, 6])]},
                {"name": "RightArm2", "parent": "FOX", "pivot": [-3, 9, -4],
                 "cubes": [cube([-4, 6, -5], [2, 6, 2])]},
            ],
        }],
    }


def run_selftest():
    """自检：唯一的方案（识别到的部位把子树网格合成一块）。"""
    failures = []
    plan = plan_model_mesh(selftest_model())
    parts = {part["label"]: part for part in plan["parts"]}
    names = {part["label"]: part["bone"] for part in plan["parts"]}

    for role, bone in (("head", "Head"), ("body", "UpBody"), ("armLeft", "LeftArm"),
                       ("armRight", "RightArm"), ("legLeft", "LeftLeg"), ("legRight", "RightLeg")):
        if names.get(role) != bone:
            failures.append(role + ": 期望骨骼 " + bone + "，实际 " + str(names.get(role)))

    # "头 + 头发合成一块"就是作者要的效果
    head = parts.get("head")
    if head is None or head["collider"] != "2 网格":
        failures.append("head: 应该把头发并进来（2 网格），实际 " + str(head and head["collider"]))
    # 藏起来的 FOX 子树不能进来
    if any("fox" in part["bone"].lower() for part in plan["parts"]):
        failures.append("FOX 子树不该参与")

    # 单根 + N-1 条连接 + 父级合法
    roots = [part["bone"] for part in plan["parts"] if part["parent"] < 0]
    if len(roots) != 1:
        failures.append("应该只有一棵树，实际根有 " + str(roots))
    if len(plan["links"]) != len(plan["parts"]) - 1:
        failures.append("连接数不对：%d 块 / %d 条" % (len(plan["parts"]), len(plan["links"])))
    for part in plan["parts"]:
        if part["parent"] >= len(plan["parts"]):
            failures.append("父块越界: " + part["bone"])

    # 两条腿挂在身体上（上一条 bug：挂到头上去了）
    for role in ("legLeft", "legRight"):
        part = parts.get(role)
        if part is not None and part["parent"] >= 0:
            parent_label = plan["parts"][part["parent"]]["label"]
            if parent_label != "body":
                failures.append(role + ": 应该挂在 body 上，实际 " + parent_label)

    if failures:
        print("自检失败:")
        for line in failures:
            print("  - " + line)
        return 1
    print("自检通过：六个部位 + 头发并进头、FOX 跳过、单棵树、两条腿挂身体")
    return 0
# ---------------------------------------------------------------- 主流程


def plan_part_list(plan):
    """把三种模式统一成 [(下标, 骨骼名, size, center)]。

    六个部位模式是按部位名存的字典，自动分段/网格模式是列表。
    """
    parts = plan.get("parts") or {}
    items = []
    if isinstance(parts, dict):
        for key, info in parts.items():
            items.append((len(items), info.get("bone", key), info.get("size"), info.get("center")))
    else:
        for info in parts:
            items.append((info.get("index", len(items)), info.get("bone", ""),
                          info.get("size"), info.get("center")))
    return items


def overlap_pairs(plan):
    """找出"现在就互相插进去"的部位对：这些对之间取消碰撞、关节收紧角度。

    和 Unity 的 MaidRagdollOverlap 同一套规则：世界包围盒相交体积达到较小那块
    OVERLAP_RATIO 才算，只是碰到边不算。
    """
    boxes = {}
    for index, _bone, size, center in plan_part_list(plan):
        if not center or not size:
            continue
        lo = [center[i] - size[i] / 2 for i in range(3)]
        hi = [center[i] + size[i] / 2 for i in range(3)]
        boxes[index] = (lo, hi, max(size[0] * size[1] * size[2], 1e-9))

    pairs = []
    ids = sorted(boxes)
    for ai in range(len(ids)):
        for bi in range(ai + 1, len(ids)):
            a = boxes[ids[ai]]
            b = boxes[ids[bi]]
            overlap = [min(a[1][i], b[1][i]) - max(a[0][i], b[0][i]) for i in range(3)]
            if any(value <= 0 for value in overlap):
                continue
            volume = overlap[0] * overlap[1] * overlap[2]
            if volume >= min(a[2], b[2]) * OVERLAP_RATIO:
                pairs.append((ids[ai], ids[bi]))
    return pairs


def load_model(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def report(path, plan):
    print("=" * 72)
    print(path)
    print("=" * 72)
    mode = plan.get("mode")
    title = "网格方案（识别到的部位把子树网格合成一块，其余逐网格一块）"
    print("  识别方式: " + title)
    names = {}
    for index, bone, _size, _center in plan_part_list(plan):
        names[index] = bone
    pairs = overlap_pairs(plan)
    if pairs:
        print("  重叠 %d 对（这些对之间不互相碰撞；角度跟普通关节一样。这里是外接盒粗估，Unity 侧用真实凸包 + 采样算体积）: " % len(pairs)
              + ", ".join("%s↔%s" % (names.get(a, a), names.get(b, b)) for a, b in pairs[:12])
              + (" ..." if len(pairs) > 12 else ""))
    if mode == "mesh":
        names = {part["index"]: part["bone"] for part in plan["parts"]}
        for part in plan["parts"]:
            parent = "无（根）" if part["parent"] < 0 else str(part["parent"])
            print("  #%-2d %-10s -> %-18s 父块=%-6s %-8s %s px"
                  % (part["index"], part.get("label", ""), part["bone"], parent,
                     part["collider"], part["size"]))
        print("  · 连接: " + ", ".join(
            "%s→%s" % (names[child], names[owner]) for child, owner, _center in plan["links"]))
        if plan.get("note"):
            print("  · " + plan["note"])
        for warning in plan["warnings"]:
            print("  ! " + warning)
        return

    if mode == "auto":
        for part in plan["parts"]:
            parent = "无（根）" if part["parent"] < 0 else str(part["parent"])
            blocks = [round(v / 16.0, 3) for v in part["size"]]
            print("  #%-2d -> %-18s 父块=%-6s 碰撞盒 %d 个 合并 %s px = %s 格  组内 %d 根"
                  % (part["index"], part["bone"], parent, part["boxes"], part["size"], blocks, part["group"]))
        if plan.get("note"):
            print("  · " + plan["note"])
        for warning in plan["warnings"]:
            print("  ! " + warning)
        return

    for role in [rule[0] for rule in ROLE_RULES]:
        info = plan["parts"].get(role)
        if info is None:
            print("  %-9s 未识别" % role)
            continue
        size = info["size"]
        blocks = [round(v / 16.0, 3) for v in size]
        source = "几何兜底" if info["source"] == "geometry" else info["matched"] or "-"
        print("  %-9s -> %-16s 识别=%-10s 碰撞盒 %s px = %s 格  组内 %d 根"
              % (role, info["bone"], source, size, blocks, info["group"]))
    for warning in plan["warnings"]:
        print("  ! " + warning)


def ascii_preview(model, parts, width=46):
    """正视图（x 横、y 竖）把每个部位管的骨骼盒子画成字符图，提前看"推测效果"。"""
    groups = part_groups(model, parts)
    marks = {}
    for index, (role, _aliases, _typical) in enumerate(ROLE_RULES):
        marks[role] = "0123456789abcdefghijklmnopqrstuvwxyz"[index]
    boxes = []
    legend = []
    for role, _aliases, _typical in ROLE_RULES:
        if role not in parts:
            continue
        legend.append(marks[role] + "=" + role)
        for name in groups[role]:
            box = model.own_box.get(name)
            if box is not None:
                boxes.append((marks[role], box))
    if not boxes:
        return []

    lo = [min(box[i] for _mark, box in boxes) for i in range(3)]
    hi = [max(box[i + 3] for _mark, box in boxes) for i in range(3)]
    span_x = max(hi[0] - lo[0], 1e-3)
    span_y = max(hi[1] - lo[1], 1e-3)
    cell = max(span_x / width, span_y / (width * 0.6))
    columns = max(1, int(span_x / cell) + 1)
    rows = max(1, int(span_y / cell) + 1)
    grid = [[" "] * columns for _ in range(rows)]
    for mark, box in boxes:
        x0 = int((box[0] - lo[0]) / cell)
        x1 = int((box[3] - lo[0]) / cell)
        y0 = int((box[1] - lo[1]) / cell)
        y1 = int((box[4] - lo[1]) / cell)
        for row in range(max(0, y0), min(rows - 1, y1) + 1):
            for column in range(max(0, x0), min(columns - 1, x1) + 1):
                grid[row][column] = mark

    lines = ["  ---- 推测效果（正视图，" + "  ".join(legend) + "）----"]
    lines.extend("  |" + "".join(row) for row in grid)
    return lines


def report_preview(path, model_json, mode):
    model = Model(model_json)
    plan = plan_model_mesh(model_json)
    for line in ascii_preview_parts(plan):
        print(line)


def ascii_preview_parts(plan, width=46):
    """正视图：按每块物理体的合并包围盒画字符图。"""
    boxes = []
    for part in plan["parts"]:
        center = part.get("center")
        size = part.get("size")
        if center and size:
            boxes.append((part["index"], center, size))
    if not boxes:
        return []
    lo = [min(c[i] - s[i] / 2 for _k, c, s in boxes) for i in range(3)]
    hi = [max(c[i] + s[i] / 2 for _k, c, s in boxes) for i in range(3)]
    cell = max(max(hi[0] - lo[0], 1e-3) / width, max(hi[1] - lo[1], 1e-3) / (width * 0.6))
    columns = max(1, int((hi[0] - lo[0]) / cell) + 1)
    rows = max(1, int((hi[1] - lo[1]) / cell) + 1)
    grid = [[" "] * columns for _ in range(rows)]
    marks = "0123456789abcdefghijklmnopqrstuvwxyz"
    legend = []
    for index, center, size in boxes:
        mark = marks[index % len(marks)]
        legend.append(mark + "=" + plan["parts"][index].get("label", "?"))
        for row in range(max(0, int((center[1] - size[1] / 2 - lo[1]) / cell)),
                         min(rows - 1, int((center[1] + size[1] / 2 - lo[1]) / cell)) + 1):
            for column in range(max(0, int((center[0] - size[0] / 2 - lo[0]) / cell)),
                                min(columns - 1, int((center[0] + size[0] / 2 - lo[0]) / cell)) + 1):
                grid[row][column] = mark
    lines = ["  ---- 物理块正视效果（" + "  ".join(legend) + "）----"]
    lines.extend("  |" + "".join(row) for row in grid)
    return lines


def ascii_preview_auto(model, roots, width=46):
    """自动分段的正视图：每个部件一个字符。"""
    boxes = []
    legend = []
    marks = "0123456789abcdefghijklmnopqrstuvwxyz"
    for index, root in enumerate(roots):
        legend.append(marks[index % len(marks)] + "=" + root)
        for name in auto_part_bones(model, root, roots):
            box = model.own_box.get(name)
            if box is not None:
                boxes.append((marks[index % len(marks)], box))
    if not boxes:
        return []
    lo = [min(box[i] for _mark, box in boxes) for i in range(3)]
    hi = [max(box[i + 3] for _mark, box in boxes) for i in range(3)]
    cell = max(max(hi[0] - lo[0], 1e-3) / width, max(hi[1] - lo[1], 1e-3) / (width * 0.6))
    columns = max(1, int((hi[0] - lo[0]) / cell) + 1)
    rows = max(1, int((hi[1] - lo[1]) / cell) + 1)
    grid = [[" "] * columns for _ in range(rows)]
    for mark, box in boxes:
        for row in range(max(0, int((box[1] - lo[1]) / cell)), min(rows - 1, int((box[4] - lo[1]) / cell)) + 1):
            for column in range(max(0, int((box[0] - lo[0]) / cell)),
                                min(columns - 1, int((box[3] - lo[0]) / cell)) + 1):
                grid[row][column] = mark
    lines = ["  ---- 自动分段效果（正视图，" + "  ".join(legend) + "）----"]
    lines.extend("  |" + "".join(row) for row in grid)
    return lines


def main():
    parser = argparse.ArgumentParser(description="女仆布娃娃（网格方案）校验")
    parser.add_argument("models", nargs="*", help="基岩模型 json")
    parser.add_argument("--json", help="把识别结果写成 json")
    parser.add_argument("--selftest", action="store_true", help="跑内置自检")
    parser.add_argument("--preview", action="store_true", help="额外画一张正视图字符画")
    args = parser.parse_args()

    if args.selftest:
        return run_selftest()

    if not args.models:
        parser.print_help()
        return 0

    results = {}
    failed = 0
    for path in args.models:
        try:
            model_json = load_model(path)
            plan = plan_model_mesh(model_json)
        except (OSError, ValueError, KeyError) as error:
            print("读取失败 %s: %s" % (path, error))
            failed += 1
            continue
        report(path, plan)
        if args.preview:
            report_preview(path, model_json, "mesh")
        results[path] = plan
        if plan["warnings"]:
            failed += 1

    if args.json and results:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(results, handle, ensure_ascii=False, indent=2)
        print("识别结果已写入 " + args.json)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
