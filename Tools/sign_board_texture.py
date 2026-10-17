"""生成女仆挂牌的板面贴图（纯 zlib 写 PNG，不依赖 PIL）。

输出：
  Assets/Resources/MaidSignBoard/sign.png      96×48（= 24×12 个"牌面像素"的 4 倍），Point 过滤
  Tools/out/sign_board_preview.png             放大的预览，人眼看一下用

改美术就改下面的常量/图案再跑一次：
  python Tools/sign_board_texture.py
牌面坐标系和代码里一致：24 宽 × 12 高，y 从上往下数（和 PNG 的文件行序一致）。
"""

import os
import struct
import zlib

SCALE = 4                     # 每个"牌面像素"放大成 SCALE×SCALE 个纹素
WIDTH, HEIGHT = 24, 12        # 牌面像素尺寸，改这里的话 MaidSignBoard.BoardTexelsX/Y 也要跟着改
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)  # Tools/ 的上一级 = 工程根（从哪儿调用都不受影响）
OUT_PNG = os.path.join(ROOT, "Assets", "Resources", "MaidSignBoard", "sign.png")
OUT_PREVIEW = os.path.join(ROOT, "Tools", "out", "sign_board_preview.png")
PREVIEW_SCALE = 6

# 颜色（照橡木告示牌那套木色）
FRAME = (0x5E, 0x41, 0x27, 0xFF)
BEVEL_LIGHT = (0x9A, 0x73, 0x43, 0xFF)
BEVEL_DARK = (0x7E, 0x5C, 0x35, 0xFF)
PLANKS = [
    (0xBB, 0x8F, 0x59, 0xFF),
    (0xB2, 0x86, 0x4F, 0xFF),
    (0xB8, 0x8C, 0x55, 0xFF),
]
SEAM = (0x8E, 0x6A, 0x3E, 0xFF)
GRAIN = (0x9C, 0x75, 0x44, 0xFF)
NAIL = (0x6B, 0x4B, 0x2C, 0xFF)
NAIL_HILIGHT = (0xD8, 0xB7, 0x7F, 0xFF)

SEAM_ROWS = {4, 8}            # 板缝所在的行（板面像素）
GRAIN_PIXELS = [              # 木纹：写死的几点，保证每次生成一模一样
    (5, 2), (6, 2), (13, 3), (18, 6), (7, 6), (4, 9),
    (12, 5), (19, 9), (16, 10), (9, 7), (3, 6), (20, 2),
]
NAIL_PIXELS = [(2, 2), (21, 2), (2, 9), (21, 9)]


def plank_color(y):
    """每一层板子给一点点色差，免得整块死板"""
    if y < 4:
        return PLANKS[0]
    if y < 8:
        return PLANKS[1]
    return PLANKS[2]


def board_pixel(x, y):
    """牌面像素 (x, y) 的颜色"""
    if x == 0 or y == 0 or x == WIDTH - 1 or y == HEIGHT - 1:
        return FRAME

    if (x, y) in NAIL_PIXELS:
        return NAIL

    # 上下各留一行当立体边（上面亮、下面暗），左右不加圈 —— 加了圈整块就变成"相框"，
    # 字能用的地方也变小了
    if y == 1:
        return BEVEL_LIGHT
    if y == HEIGHT - 2:
        return BEVEL_DARK

    if y in SEAM_ROWS:
        return SEAM

    if (x, y) in GRAIN_PIXELS:
        return GRAIN

    return plank_color(y)


def build_texels():
    """返回 SCALE 倍后的纹素矩阵（行序和 PNG 一致：第一行在最上面）"""
    rows = []
    for y in range(HEIGHT):
        row = []
        for x in range(WIDTH):
            color = board_pixel(x, y)
            row.extend([color] * SCALE)
        rows.append(row)

    texels = []
    for y in range(HEIGHT):
        for _ in range(SCALE):
            texels.append(list(rows[y]))

    # 钉子高光：每个钉子左上角一个纹素
    for (x, y) in NAIL_PIXELS:
        texels[y * SCALE][x * SCALE] = NAIL_HILIGHT
        texels[y * SCALE][x * SCALE + 1] = NAIL_HILIGHT
        texels[y * SCALE + 1][x * SCALE] = NAIL_HILIGHT

    return texels


def upscale(texels, factor):
    out = []
    for row in texels:
        expanded = []
        for pixel in row:
            expanded.extend([pixel] * factor)
        for _ in range(factor):
            out.append(list(expanded))
    return out


def write_png(path, texels):
    height = len(texels)
    width = len(texels[0])
    raw = b"".join(b"\x00" + bytes(v for pixel in row for v in pixel) for row in texels)

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")

    folder = os.path.dirname(path)
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)
    with open(path, "wb") as handle:
        handle.write(png)
    print("写出 %s（%d×%d，%d 字节）" % (path, width, height, len(png)))


def main():
    texels = build_texels()
    write_png(OUT_PNG, texels)
    write_png(OUT_PREVIEW, upscale(texels, PREVIEW_SCALE))


if __name__ == "__main__":
    main()
