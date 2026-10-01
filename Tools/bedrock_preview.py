#!/usr/bin/env python3
"""基岩模型转换验证工具（纯标准库，不需要 Unity）。

用法:
  python Tools/bedrock_preview.py <model.json> [--tex W H] [--report] [--ascii] [--png out.png]
"""
import json
import math
import os
import struct
import sys
import zlib

FACES = ("north", "south", "east", "west", "up", "down")
FACE_NORMAL = {
    "north": (0, 0, -1), "south": (0, 0, 1),
    "east": (1, 0, 0), "west": (-1, 0, 0),
    "up": (0, 1, 0), "down": (0, -1, 0),
}
# 每个面的贴图方向（基岩坐标系）：u 增大方向，以及指向矩形上沿（v 变小）的方向
FACE_AXES = {
    "north": ((1, 0, 0), (0, 1, 0)),
    "south": ((-1, 0, 0), (0, 1, 0)),
    "east": ((0, 0, 1), (0, 1, 0)),
    "west": ((0, 0, -1), (0, 1, 0)),
    "up": ((1, 0, 0), (0, 0, 1)),
    "down": ((1, 0, 0), (0, 0, 1)),
}


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(v):
    l = math.sqrt(dot(v, v))
    return (0.0, 0.0, 0.0) if l < 1e-12 else (v[0] / l, v[1] / l, v[2] / l)


def mat3(rx, ry, rz):
    """基岩旋转顺序 Rz * Ry * Rx（列向量）。"""
    cx, sx = math.cos(math.radians(rx)), math.sin(math.radians(rx))
    cy, sy = math.cos(math.radians(ry)), math.sin(math.radians(ry))
    cz, sz = math.cos(math.radians(rz)), math.sin(math.radians(rz))
    z = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]]
    y = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    x = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]]
    return mat_mul(z, mat_mul(y, x))


def apply(m, v):
    return (m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
            m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
            m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2])


IDENTITY = [[1.0, 0, 0], [0, 1.0, 0], [0, 0, 1.0]]


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def rotate_about(p, pivot, rot):
    return [pivot[i] + apply(rot, sub(p, pivot))[i] for i in range(3)]


class Convert:
    """基岩坐标 -> Unity 坐标。基岩模型面朝 -Z，Unity 里让其面朝 +Z。"""

    def __init__(self, pixels_per_unit=16.0):
        self.s = pixels_per_unit
        # 角度符号：默认直接用不取反（和 C# 的 BedrockRotationMode.Standard 一致）
        self.rotation_signs = (1.0, 1.0, 1.0)
        # Blockbench 内部 Euler 顺序固定 ZYX，和 C# 的默认值一致
        self.rotation_order = "zyx"

    def point(self, v):
        return (-v[0] / self.s, v[1] / self.s, -v[2] / self.s)

    def direction(self, v):
        return (-v[0], v[1], -v[2])

    def rotation(self, rx, ry, rz):
        # 和 C# 的 BedrockModelBuilder.ToUnityRotation 保持一致
        sx, sy, sz = self.rotation_signs
        x, y, z = sx * rx, sy * ry, sz * rz
        axis = {"x": mat3(x, 0, 0), "y": mat3(0, y, 0), "z": mat3(0, 0, z)}
        res = IDENTITY
        for a in self.rotation_order:
            res = mat_mul(res, axis[a])
        return res


def load_json(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def parse_geometry(doc):
    if "minecraft:geometry" in doc:
        geo = doc["minecraft:geometry"]
        if isinstance(geo, dict):
            geo = [geo]
        geo = geo[0]
        desc = geo.get("description", {})
        bones = geo.get("bones", [])
        return {
            "identifier": desc.get("identifier", "geometry.unknown"),
            "tex_w": float(desc.get("texture_width", 64)),
            "tex_h": float(desc.get("texture_height", 64)),
            "bones": bones,
            "format": str(doc.get("format_version", "?")),
        }
    for key, value in doc.items():
        if key.startswith("geometry.") and isinstance(value, dict) and "bones" in value:
            return {
                "identifier": key,
                "tex_w": float(value.get("texturewidth", 64)),
                "tex_h": float(value.get("textureheight", 64)),
                "bones": value.get("bones", []),
                "format": str(doc.get("format_version", "?")),
            }
    raise ValueError("没找到 geometry 数据")


def box_face_rect(uv, size, face, mirror=False):
    u, v = uv
    # 和 Minecraft / Blockbench 一样，盒式 UV 的尺寸向下取整
    w, h, d = math.floor(size[0]), math.floor(size[1]), math.floor(size[2])
    if face == "north":
        return u + d, v + d, w, h
    if face == "south":
        return u + 2 * d + w, v + d, w, h
    if face == "east":
        # mirror 的方块在 Blockbench 里会把 east / west 两块贴图区域对调
        return (u, v + d, d, h) if mirror else (u + d + w, v + d, d, h)
    if face == "west":
        return (u + d + w, v + d, d, h) if mirror else (u, v + d, d, h)
    if face == "up":
        return u + d, v, w, d
    return u + d + w, v, w, d


def face_rect(cube, face):
    uv = cube["uv"]
    size = cube.get("size", [0, 0, 0])
    if isinstance(uv, dict):
        entry = uv.get(face)
        if not entry:
            return None
        pu, pv = entry.get("uv", [0, 0])
        sw, sh = entry.get("uv_size", [size[0], size[1]])
        # 单独指定 UV 时，基岩的 down 面是从反向角起算的，Blockbench 会把它交换一次
        if face == "down":
            return pu, pv + sh, sw, -sh
        return pu, pv, sw, sh
    return box_face_rect(uv, size, face, bool(cube.get("mirror", False)))


def face_corners(x0, x1, y0, y1, z0, z1, face):
    if face == "north":
        return [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]
    if face == "south":
        return [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    if face == "east":
        return [(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)]
    if face == "west":
        return [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)]
    if face == "up":
        return [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)]
    return [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]


class Builder:
    def __init__(self, geometry, conv, mirror_uv=True, flip_face_u=False, double_sided_flat=True):
        self.geo = geometry
        self.conv = conv
        self.mirror_uv = mirror_uv
        self.flip_face_u = flip_face_u
        self.double_sided_flat = double_sided_flat
        self.vertices = []
        self.normals = []
        self.uvs = []
        self.triangles = []
        self.owner = []
        self.face_of = []
        self.world = {}
        self.warnings = []
        self.uv_over = []

    def build(self):
        bones = self.geo["bones"]
        by_name = {}
        for b in bones:
            by_name.setdefault(b.get("name", "?"), b)
        order = []
        visiting = set()

        def walk(bone, parent_world, parent_pivot):
            name = bone.get("name", "?")
            if name in visiting:
                self.warnings.append("循环父子关系: " + name)
                return
            visiting.add(name)
            pivot = bone.get("pivot", [0, 0, 0])
            rot = bone.get("rotation", [0, 0, 0])
            local_pos = sub(self.conv.point(pivot), self.conv.point(parent_pivot))
            local_rot = self.conv.rotation(rot[0], rot[1], rot[2])
            pos = tuple(parent_world[0][i] + apply(parent_world[1], local_pos)[i] for i in range(3))
            rotw = mat_mul(parent_world[1], local_rot)
            self.world[name] = (pos, rotw, pivot)
            self.emit_cubes(bone)
            order.append(name)
            for child in bone.get("children", []):
                walk(child, (pos, rotw), pivot)
            visiting.discard(name)

        roots = []
        for b in bones:
            parent = b.get("parent")
            if not parent:
                roots.append(b)
            elif parent not in by_name:
                self.warnings.append("找不到父骨骼 %s，按根骨骼处理: %s" % (parent, b.get("name")))
                roots.append(b)
        for b in bones:
            b["children"] = []
        for b in bones:
            parent = b.get("parent")
            if parent and parent in by_name:
                by_name[parent]["children"].append(b)
        for b in roots:
            walk(b, ((0.0, 0.0, 0.0), IDENTITY), (0.0, 0.0, 0.0))
        return self

    def emit_cubes(self, bone):
        name = bone.get("name", "?")
        pivot = bone.get("pivot", [0, 0, 0])
        bone_world = self.world[name]
        bone_inflate = float(bone.get("inflate", 0) or 0)
        for cube in bone.get("cubes", []) or []:
            origin = list(cube.get("origin", [0, 0, 0]))
            size = list(cube.get("size", [0, 0, 0]))
            inflate = float(cube.get("inflate", 0) or 0) + bone_inflate
            rot = cube.get("rotation", [0, 0, 0])
            cube_pivot = cube.get("pivot", [0, 0, 0])
            has_rot = any(abs(x) > 1e-9 for x in rot)
            x0, y0, z0 = (origin[0] - inflate, origin[1] - inflate, origin[2] - inflate)
            x1, y1, z1 = (x0 + size[0] + 2 * inflate, y0 + size[1] + 2 * inflate, z0 + size[2] + 2 * inflate)
            mirror = bool(cube.get("mirror", False))
            cube_rot = self.conv.rotation(rot[0], rot[1], rot[2])
            cube_pivot_u = self.conv.point(cube_pivot)
            # 厚度为 0 的方块要双面渲染
            flat_x = (x1 - x0) < 1e-5
            flat_y = (y1 - y0) < 1e-5
            flat_z = (z1 - z0) < 1e-5

            def to_local(p):
                q = self.conv.point(p)
                if has_rot:
                    q = rotate_about(q, cube_pivot_u, cube_rot)
                return sub(q, self.conv.point(pivot))

            for face in FACES:
                rect = face_rect(cube, face)
                if rect is None:
                    continue
                corners = face_corners(x0, x1, y0, y1, z0, z1, face)
                n_b = FACE_NORMAL[face]
                n_out = apply(cube_rot, self.conv.direction(n_b)) if has_rot else self.conv.direction(n_b)
                right, up_tex = FACE_AXES[face]
                if self.flip_face_u:
                    right = (-right[0], -right[1], -right[2])
                s_all = [dot(c, right) for c in corners]
                t_all = [dot(c, up_tex) for c in corners]
                s_min, s_max = min(s_all), max(s_all)
                t_min, t_max = min(t_all), max(t_all)
                span_s = s_max - s_min
                span_t = t_max - t_min
                if span_s < 1e-9 or span_t < 1e-9:
                    continue
                ru, rv, rw, rh = rect
                uvs = []
                for c in corners:
                    s = (dot(c, right) - s_min) / span_s
                    t = (t_max - dot(c, up_tex)) / span_t
                    if mirror and isinstance(cube["uv"], list):
                        s = 1.0 - s
                    px = ru + s * rw
                    py = rv + t * rh
                    if px < -0.001 or py < -0.001 or px > self.geo["tex_w"] + 0.001 or py > self.geo["tex_h"] + 0.001:
                        self.uv_over.append((bone.get("name"), face, px, py, self.geo["tex_w"], self.geo["tex_h"]))
                    uvs.append((px / self.geo["tex_w"], 1.0 - py / self.geo["tex_h"]))
                world_pts = []
                for c in corners:
                    local = to_local(c)
                    pos, rotw, _ = bone_world
                    p = apply(rotw, local)
                    world_pts.append((pos[0] + p[0], pos[1] + p[1], pos[2] + p[2]))
                base = len(self.vertices)
                geo_n = norm(cross(sub(world_pts[1], world_pts[0]), sub(world_pts[2], world_pts[0])))
                if dot(geo_n, norm(n_out)) < 0:
                    world_pts = [world_pts[0], world_pts[3], world_pts[2], world_pts[1]]
                    uvs = [uvs[0], uvs[3], uvs[2], uvs[1]]
                    geo_n = (-geo_n[0], -geo_n[1], -geo_n[2])
                for i in range(4):
                    self.vertices.append(world_pts[i])
                    self.normals.append(geo_n)
                    self.uvs.append(uvs[i])
                    self.owner.append(name)
                    self.face_of.append(face)
                self.triangles += [(base, base + 1, base + 2), (base, base + 2, base + 3)]

                two_sided = flat_z if face in ("north", "south") else (flat_x if face in ("east", "west") else flat_y)
                if not (two_sided and self.double_sided_flat):
                    continue
                back = len(self.vertices)
                for i in range(4):
                    self.vertices.append(world_pts[i])
                    self.normals.append((-geo_n[0], -geo_n[1], -geo_n[2]))
                    self.uvs.append(uvs[i])
                    self.owner.append(name)
                    self.face_of.append(face)
                self.triangles += [(back, back + 2, back + 1), (back, back + 3, back + 2)]
        return self

    def report(self):
        xs = [v[0] for v in self.vertices]
        ys = [v[1] for v in self.vertices]
        zs = [v[2] for v in self.vertices]
        print("模型: %s   format=%s   声明贴图=%gx%g" % (self.geo["identifier"], self.geo["format"], self.geo["tex_w"], self.geo["tex_h"]))
        print("骨骼=%d  顶点=%d  三角面=%d" % (len(self.geo["bones"]), len(self.vertices), len(self.triangles)))
        if self.vertices:
            print("包围盒: x %.3f..%.3f  y %.3f..%.3f  z %.3f..%.3f" % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
            print("高度=%.3f 米   脚底 y=%.3f" % (max(ys) - min(ys), min(ys)))
            print("X 对称偏差=%.4f (0 表示完全左右对称)" % abs(abs(min(xs)) - abs(max(xs))))
        bad = [u for u in self.uvs if u[0] < -0.001 or u[0] > 1.001 or u[1] < -0.001 or u[1] > 1.001]
        print("UV 越界顶点=%d" % len(bad))
        for w in self.warnings:
            print("警告: " + w)
        seen = set()
        for name, face, px, py, tw, th in self.uv_over:
            key = (name, face)
            if key in seen:
                continue
            seen.add(key)
            print("UV 超出贴图: 骨骼=%s 面=%s 像素=(%.2f, %.2f) 贴图=%gx%g" % (name, face, px, py, tw, th))


VIEW_AXIS = {
    # (screen_x 轴, screen_y 轴, 深度轴, screen_x 翻转, 深度方向)
    "front": (0, 1, 2, -1, 1),
    "back": (0, 1, 2, 1, -1),
    "side": (2, 1, 0, 1, 1),
}


def rasterize(builder, size, view, alpha_fn=None):
    """正交投影 + z 缓冲，返回 (depth, out)，out 每格是 (亮度, uv)。

    传入 alpha_fn 时按 alpha 剔除（跟 Unity 的 Cutout 材质一致）：透明像素不写深度。
    """
    w, h = size
    sx, sy, key, flip, sign = VIEW_AXIS[view]
    pts = [(p[sx] * flip, p[sy], p[key] * sign) for p in builder.vertices]
    minx, maxx = min(p[0] for p in pts), max(p[0] for p in pts)
    miny, maxy = min(p[1] for p in pts), max(p[1] for p in pts)
    scale = min((w - 2) / max(maxx - minx, 1e-6), (h - 2) / max(maxy - miny, 1e-6))
    screen = [((p[0] - (minx + maxx) * 0.5) * scale + w * 0.5,
               (p[1] - (miny + maxy) * 0.5) * scale + h * 0.5,
               p[2]) for p in pts]
    depth = [[-1e9] * w for _ in range(h)]
    out = [[(-1.0, (0.0, 0.0)) for _ in range(w)] for _ in range(h)]
    for tri in builder.triangles:
        a, b, c = screen[tri[0]], screen[tri[1]], screen[tri[2]]
        lum = max(0.0, dot(builder.normals[tri[0]], norm((0.35, 0.7, 0.6))))
        minix = max(0, int(math.floor(min(a[0], b[0], c[0]))))
        maxix = min(w - 1, int(math.ceil(max(a[0], b[0], c[0]))))
        miniy = max(0, int(math.floor(min(a[1], b[1], c[1]))))
        maxiy = min(h - 1, int(math.ceil(max(a[1], b[1], c[1]))))
        area = (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])
        if abs(area) < 1e-9:
            continue
        for py in range(miniy, maxiy + 1):
            for px in range(minix, maxix + 1):
                x, y = px + 0.5, py + 0.5
                w0 = ((b[0] - x) * (c[1] - y) - (c[0] - x) * (b[1] - y)) / area
                w1 = ((c[0] - x) * (a[1] - y) - (a[0] - x) * (c[1] - y)) / area
                w2 = 1.0 - w0 - w1
                if w0 < -1e-6 or w1 < -1e-6 or w2 < -1e-6:
                    continue
                z = w0 * a[2] + w1 * b[2] + w2 * c[2]
                row = h - 1 - py
                if z > depth[row][px]:
                    uv0, uv1, uv2 = (builder.uvs[tri[0]], builder.uvs[tri[1]], builder.uvs[tri[2]])
                    uv = (w0 * uv0[0] + w1 * uv1[0] + w2 * uv2[0],
                          w0 * uv0[1] + w1 * uv1[1] + w2 * uv2[1])
                    if alpha_fn is not None and alpha_fn(uv[0], uv[1]) < 128:
                        continue
                    depth[row][px] = z
                    out[row][px] = (lum, uv)
    return depth, out


def ascii_view(out, chars=" .:-=+*#%@"):
    lines = []
    for row in out:
        line = []
        for lum, _uv in row:
            if lum < 0:
                line.append(" ")
            else:
                line.append(chars[min(len(chars) - 1, int(lum * (len(chars) - 1) + 0.5))])
        lines.append("".join(line))
    return lines


def read_png(path):
    data = open(path, "rb").read()
    pos, idat = 8, b""
    w = h = ct = None
    while pos < len(data):
        ln = struct.unpack(">I", data[pos:pos + 4])[0]
        typ = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + ln]
        pos += 12 + ln
        if typ == b"IHDR":
            w, h, _bd, ct = struct.unpack(">IIBB", chunk[:10])
        elif typ == b"IDAT":
            idat += chunk
        elif typ == b"IEND":
            break
    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ct]
    stride = w * ch
    out = bytearray(w * h * ch)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        for i in range(stride):
            a = line[i - ch] if i >= ch else 0
            b = prev[i]
            c = prev[i - ch] if i >= ch else 0
            if f == 1:
                line[i] = (line[i] + a) & 255
            elif f == 2:
                line[i] = (line[i] + b) & 255
            elif f == 3:
                line[i] = (line[i] + ((a + b) >> 1)) & 255
            elif f == 4:
                pp = a + b - c
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 255
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, ch, bytes(out)


def write_png(path, w, h, rgb):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgb[y * w * 3:(y + 1) * w * 3]
    comp = zlib.compress(bytes(raw), 6)

    def chunk(typ, data):
        return struct.pack(">I", len(data)) + typ + data + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF)

    out = b"\x89PNG\r\n\x1a\n"
    out += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    out += chunk(b"IDAT", comp)
    out += chunk(b"IEND", b"")
    open(path, "wb").write(out)


def render_png(builder, tex_path, out_path, cell=256):
    tex = read_png(tex_path) if tex_path and os.path.exists(tex_path) else None
    cut = alpha_sampler(tex)
    views = ("front", "side", "back")
    W, H = cell * len(views), cell
    buf = bytearray(W * H * 3)
    for vi, view in enumerate(views):
        depth, out = rasterize(builder, (cell, cell), view, cut)
        for y in range(cell):
            for x in range(cell):
                lum, uv = out[y][x]
                if lum < 0:
                    continue
                if tex:
                    c = sample(tex, uv)
                else:
                    c = (200, 200, 200, 255)
                k = 0.35 + 0.65 * lum
                o = (y * W + (vi * cell + x)) * 3
                buf[o] = int(min(255, c[0] * k))
                buf[o + 1] = int(min(255, c[1] * k))
                buf[o + 2] = int(min(255, c[2] * k))
    write_png(out_path, W, H, buf)
    return out_path


def sample(tex, uv):
    w, h, ch, px = tex
    u = min(max(uv[0], 0.0), 0.999999)
    v = min(max(uv[1], 0.0), 0.999999)
    x = int(u * w)
    y = int((1.0 - v) * h)
    y = min(max(y, 0), h - 1)
    o = (y * w + x) * ch
    r, g, b = px[o], px[o + 1], px[o + 2]
    a = px[o + 3] if ch == 4 else 255
    if a < 128:
        return (255, 255, 255, 0)
    return (r, g, b, a)


def alpha_sampler(tex):
    if tex is None:
        return None
    w, h, ch, px = tex

    def f(u, v):
        if ch != 4:
            return 255
        x = int(min(max(u, 0.0), 0.999999) * w)
        y = int(min(max(1.0 - v, 0.0), 0.999999) * h)
        return px[(y * w + x) * ch + 3]

    return f


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    path = argv[1]
    tex = None
    png = None
    do_ascii = False
    do_report = False
    i = 2
    while i < len(argv):
        if argv[i] == "--tex":
            tex = (float(argv[i + 1]), float(argv[i + 2]))
            i += 3
        elif argv[i] == "--png":
            png = argv[i + 1]
            i += 2
        elif argv[i] == "--ascii":
            do_ascii = True
            i += 1
        elif argv[i] == "--report":
            do_report = True
            i += 1
        else:
            print("未知参数: " + argv[i])
            return 1
    doc = load_json(path)
    geo = parse_geometry(doc)
    if tex:
        geo["tex_w"], geo["tex_h"] = tex
    builder = Builder(geo, Convert()).build()
    if do_report or not (do_ascii or png):
        builder.report()
    if do_ascii:
        tex_path = os.path.splitext(path)[0] + ".png"
        cut = alpha_sampler(read_png(tex_path)) if os.path.exists(tex_path) else None
        for view in ("front", "side", "back"):
            depth, out = rasterize(builder, (64, 40), view, cut)
            print("")
            print("=== %s 视图 ===" % view)
            for line in ascii_view(out):
                print(line)
    if png:
        tex_path = os.path.splitext(path)[0] + ".png"
        print("输出预览: " + render_png(builder, tex_path, png))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
