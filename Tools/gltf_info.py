"""打印 .glb / .gltf 的结构摘要，用来判断导入 Unity 后为什么会看起来不对。

会打印：材质 / sampler / alphaMode / doubleSided、贴图到图集的对应、
网格顶点属性（有没有 COLOR_0）、以及每张贴图的灰度程度。
草方块这类原版靠生物群系染色的贴图是灰度的，灰度值接近 0 就说明需要染色。

用法: python Tools/gltf_info.py <file.glb> [--stats]
"""

import base64
import json
import os
import struct
import sys
import zlib

FILTERS = {
    9728: "NEAREST",
    9729: "LINEAR",
    9984: "NEAREST_MIPMAP_NEAREST",
    9985: "LINEAR_MIPMAP_NEAREST",
    9986: "NEAREST_MIPMAP_LINEAR",
    9987: "LINEAR_MIPMAP_LINEAR",
}

WRAPS = {33071: "CLAMP_TO_EDGE", 33648: "MIRRORED_REPEAT", 10497: "REPEAT"}

COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

COMPONENT_SIZES = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}
COMPONENT_FORMATS = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
COMPONENT_DIVISORS = {5120: 127.0, 5121: 255.0, 5122: 32767.0, 5123: 65535.0}


def read_accessor(gltf, binary, index, limit=None):
    accessor = gltf["accessors"][index]
    components = COMPONENTS[accessor["type"]]
    component_type = accessor["componentType"]
    element_size = COMPONENT_SIZES[component_type]
    count = accessor["count"] if limit is None else min(accessor["count"], limit)
    view = gltf["bufferViews"][accessor["bufferView"]]
    base = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = view.get("byteStride") or components * element_size
    fmt = "<" + COMPONENT_FORMATS[component_type] * components
    divisor = COMPONENT_DIVISORS.get(component_type)
    values = []
    for i in range(count):
        raw = struct.unpack_from(fmt, binary, base + i * stride)
        if divisor:
            raw = tuple(max(v / divisor, -1.0) for v in raw)
        values.append(raw)
    return values


def load_gltf(path):
    with open(path, "rb") as fh:
        head = fh.read(12)
        if head[:4] == b"glTF":
            version, length = struct.unpack("<II", head[4:12])
            if version != 2:
                raise SystemExit("只认识 glTF 2.0，这个文件是 %d" % version)
            gltf = None
            binary = b""
            while fh.tell() < length:
                chunk_head = fh.read(8)
                if len(chunk_head) < 8:
                    break
                chunk_len, chunk_type = struct.unpack("<II", chunk_head)
                data = fh.read(chunk_len)
                if chunk_type == 0x4E4F534A:  # JSON
                    gltf = json.loads(data.decode("utf-8"))
                elif chunk_type == 0x004E4942:  # BIN
                    binary = data
            if gltf is None:
                raise SystemExit("glb 里没有 JSON chunk")
            return gltf, binary

        fh.seek(0)
        return json.load(fh), b""


def image_bytes(gltf, index, binary):
    image = gltf.get("images", [])[index]
    if "bufferView" in image:
        view = gltf["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        return binary[start:start + view["byteLength"]]
    uri = image.get("uri", "")
    if uri.startswith("data:"):
        return base64.b64decode(uri.split(",", 1)[1])
    return None


def decode_png(data):
    """最小 PNG 解码：8bit、非隔行，够读 MC 贴图。返回 (w, h, rgb 像素列表)。"""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        return None

    pos = 8
    idat = b""
    palette = None
    width = height = depth = color_type = 0
    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        kind = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            width, height, depth, color_type, _, _, interlace = struct.unpack(">IIBBBBB", chunk)
            if interlace:
                return None
        elif kind == b"PLTE":
            palette = chunk
        elif kind == b"IDAT":
            idat += chunk
        elif kind == b"IEND":
            break

    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(color_type)
    if channels is None or depth != 8:
        return None

    raw = zlib.decompress(idat)
    stride = width * channels
    rows = []
    prev = bytearray(stride)
    p = 0
    for _ in range(height):
        filter_type = raw[p]
        line = bytearray(raw[p + 1:p + 1 + stride])
        p += 1 + stride
        for i in range(stride):
            left = line[i - channels] if i >= channels else 0
            up = prev[i]
            up_left = prev[i - channels] if i >= channels else 0
            if filter_type == 1:
                line[i] = (line[i] + left) & 0xFF
            elif filter_type == 2:
                line[i] = (line[i] + up) & 0xFF
            elif filter_type == 3:
                line[i] = (line[i] + ((left + up) >> 1)) & 0xFF
            elif filter_type == 4:
                pa = abs(up - up_left)
                pb = abs(left - up_left)
                pc = abs(left + up - 2 * up_left)
                predictor = left if (pa <= pb and pa <= pc) else (up if pb <= pc else up_left)
                line[i] = (line[i] + predictor) & 0xFF
        rows.append(bytes(line))
        prev = line

    pixels = []
    for line in rows:
        for x in range(width):
            chunk = line[x * channels:(x + 1) * channels]
            if color_type == 0:
                pixels.append((chunk[0], chunk[0], chunk[0], 255))
            elif color_type == 4:
                pixels.append((chunk[0], chunk[0], chunk[0], chunk[1]))
            elif color_type == 2:
                pixels.append((chunk[0], chunk[1], chunk[2], 255))
            elif color_type == 6:
                pixels.append((chunk[0], chunk[1], chunk[2], chunk[3]))
            else:  # 调色板
                idx = chunk[0] * 3
                pixels.append((palette[idx], palette[idx + 1], palette[idx + 2], 255))
    return width, height, pixels


def texture_stats(pixels):
    total = len(pixels)
    saturation = 0.0
    max_saturation = 0
    alpha_min = 255
    transparent = 0
    for r, g, b, a in pixels:
        if a > 0:
            spread = max(r, g, b) - min(r, g, b)
            saturation += spread
            max_saturation = max(max_saturation, spread)
        alpha_min = min(alpha_min, a)
        if a == 0:
            transparent += 1
    return {
        "mean_saturation": saturation / total,
        "max_saturation": max_saturation,
        "alpha_min": alpha_min,
        "transparent_ratio": transparent / total,
    }


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    want_stats = "--stats" in sys.argv
    if not args:
        print(__doc__)
        return 1

    path = args[0]
    gltf, binary = load_gltf(path)
    asset = gltf.get("asset", {})
    print("文件: %s" % os.path.basename(path))
    print("生成器: %s (glTF %s)" % (asset.get("generator", "?"), asset.get("version", "?")))

    counts = {k: len(gltf.get(k, [])) for k in
              ("scenes", "nodes", "meshes", "materials", "textures", "images", "samplers",
               "animations", "skins", "accessors")}
    print("节点 %(nodes)d / 网格 %(meshes)d / 材质 %(materials)d / 贴图 %(textures)d / "
          "图集 %(images)d / 采样器 %(samplers)d / 动画 %(animations)d / 骨骼 %(skins)d"
          % counts)

    samplers = gltf.get("samplers", [])
    for i, sampler in enumerate(samplers):
        mag = sampler.get("magFilter", 9729)
        minf = sampler.get("minFilter", 9987)
        wrap = sampler.get("wrapS", 10497)
        print("  sampler[%d] mag=%s min=%s wrap=%s" %
              (i, FILTERS.get(mag, mag), FILTERS.get(minf, minf), WRAPS.get(wrap, wrap)))

    images = gltf.get("images", [])
    textures = gltf.get("textures", [])
    for i, texture in enumerate(textures):
        print("  texture[%d] -> image[%s] sampler=%s" %
              (i, texture.get("source", "?"), texture.get("sampler", "?")))

    print("")
    materials = gltf.get("materials", [])
    for i, material in enumerate(materials):
        pbr = material.get("pbrMetallicRoughness", {})
        tex_index = (pbr.get("baseColorTexture") or {}).get("index", -1)
        source = textures[tex_index].get("source", -1) if 0 <= tex_index < len(textures) else -1
        image_name = images[source].get("name", "") if 0 <= source < len(images) else ""
        print("  material[%d] %r" % (i, material.get("name", "")))
        print("      alphaMode=%s cutoff=%s doubleSided=%s" %
              (material.get("alphaMode", "OPAQUE"), material.get("alphaCutoff", 0.5),
               bool(material.get("doubleSided", False))))
        print("      baseColorFactor=%s baseColorTexture=%s (image %s %r)" %
              ([round(v, 3) for v in pbr.get("baseColorFactor", [1, 1, 1, 1])],
               tex_index, source, image_name))
        extras = [k for k in ("metallicFactor", "roughnessFactor", "metallicRoughnessTexture",
                              "normalTexture", "occlusionTexture", "emissiveTexture")
                  if k in pbr or k in material]
        if extras:
            print("      PBR 额外项（会被丢弃）: %s" % ", ".join(extras))
        ext = list(material.get("extensions", {}).keys())
        if ext:
            print("      扩展: %s" % ", ".join(ext))

    print("")
    for i, mesh in enumerate(gltf.get("meshes", [])):
        print("  mesh[%d] %r  primitive %d 个" % (i, mesh.get("name", ""), len(mesh.get("primitives", []))))
        for j, primitive in enumerate(mesh.get("primitives", [])):
            attributes = primitive.get("attributes", {})
            indices = primitive.get("indices")
            count = gltf["accessors"][indices]["count"] if indices is not None else 0
            position = gltf["accessors"][attributes["POSITION"]] if "POSITION" in attributes else None
            print("      [%d] material=%s 三角形=%d 属性=%s" %
                  (j, primitive.get("material", "-"), count // 3,
                   ",".join(sorted(attributes.keys()))))
            if position and "min" in position:
                print("          bounds min=%s max=%s" %
                      ([round(v, 4) for v in position["min"]], [round(v, 4) for v in position["max"]]))

    has_color = any("COLOR_0" in p.get("attributes", {})
                    for mesh in gltf.get("meshes", []) for p in mesh.get("primitives", []))
    print("")
    print("网格带顶点色 COLOR_0: %s" % ("是" if has_color else "否"))

    if has_color:
        print("COLOR_0 取值（glTF 规范里是线性值；括号里是按 sRGB 解释的 0-255）:")
        for i, mesh in enumerate(gltf.get("meshes", [])):
            for j, primitive in enumerate(mesh.get("primitives", [])):
                accessor = primitive.get("attributes", {}).get("COLOR_0")
                if accessor is None:
                    continue
                values = read_accessor(gltf, binary, accessor, limit=4096)
                unique = []
                for value in values:
                    key = tuple(round(v, 3) for v in value)
                    if key not in unique:
                        unique.append(key)
                for key in unique[:4]:
                    rgb = key[:3]
                    srgb = " ".join(str(int(round(255 * (max(c, 0.0) ** (1.0 / 2.2))))) for c in rgb)
                    print("  mesh[%d] primitive[%d] material=%s 线性 %s -> sRGB(%s)" %
                          (i, j, primitive.get("material", "-"),
                           " ".join("%.3f" % v for v in key), srgb))
                if len(unique) > 4:
                    print("      （采样里共 %d 种颜色，只列前 4 个）" % len(unique))

    if not want_stats:
        print("（加 --stats 看每张贴图的灰度统计）")
        return 0

    print("")
    print("贴图统计（mean_saturation 接近 0 = 灰度贴图，原版靠染色上色）:")
    for i, image in enumerate(images):
        data = image_bytes(gltf, i, binary)
        if not data:
            print("  image[%d] %s: 取不到数据（外部文件）" % (i, image.get("name", "")))
            continue
        decoded = decode_png(data)
        if not decoded:
            print("  image[%d] %s: PNG 解不了（可能是 jpeg 或非 8bit）" % (i, image.get("name", "")))
            continue
        width, height, pixels = decoded
        stats = texture_stats(pixels)
        print("  image[%d] %-26s %dx%d  mean_sat=%.1f max_sat=%d  alpha_min=%d 全透明占比=%.2f" %
              (i, image.get("name", ""), width, height, stats["mean_saturation"],
               stats["max_saturation"], stats["alpha_min"], stats["transparent_ratio"]))

    return 0


if __name__ == "__main__":
    sys.exit(main())
