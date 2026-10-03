"""校验女仆缓存的「按需加载动画」，不需要 Unity。

做的两件事：
  1. 把 MaidAssetCache.BinStreamReader 的缓冲/跳过逻辑**一行一行照搬**到 Python，
     用真实 maid.bin 跑一遍，和"朴实无华的游标读法"逐项比对；
  2. 模拟 LoadBin 的两段式读法：顺序读到网格 + 建索引（采样跳过去），再按索引里的偏移
     单独把某几条动画读回来，和顺序读下来的结果比对。偏移或跳过长度算错都会在这里暴露。

用法: python Tools/maid_lazy_check.py <maid.bin>
"""

import os
import struct
import sys

BUFFER_SIZE = 4 * 1024
TRACK_FLOATS = 11  # 时间 1 + 位置 3 + 旋转 4 + 缩放 3


def read_string_at(data, pos):
    length = 0
    shift = 0
    while True:
        b = data[pos]
        pos += 1
        length |= (b & 0x7F) << shift
        if not b & 0x80:
            break
        shift += 7
    return data[pos:pos + length].decode("utf-8"), pos + length


class PlainReader:
    """朴实无华的游标读法，只用来当参照物。"""

    def __init__(self, data):
        self.data = data
        self.pos = 0

    def i32(self):
        value = struct.unpack_from("<i", self.data, self.pos)[0]
        self.pos += 4
        return value

    def f32(self):
        value = struct.unpack_from("<f", self.data, self.pos)[0]
        self.pos += 4
        return value

    def boolean(self):
        value = self.data[self.pos] != 0
        self.pos += 1
        return value

    def string(self):
        value, self.pos = read_string_at(self.data, self.pos)
        return value

    def floats(self, count):
        values = struct.unpack_from("<%df" % count, self.data, self.pos)
        self.pos += count * 4
        return values


class FakeFileStream:
    """模拟 FileStream(bufferSize: 1)：读多少就是多少，顺便统计真实读盘量。"""

    def __init__(self, data):
        self.data = data
        self.pos = 0
        self.bytes_read = 0
        self.read_calls = 0

    def seek(self, pos):
        self.pos = pos

    def read(self, count):
        self.read_calls += 1
        chunk = self.data[self.pos:self.pos + count]
        self.pos += len(chunk)
        self.bytes_read += len(chunk)
        return chunk


class BufferedReader:
    """C# 里 BinStreamReader 的等价实现（Fill / ReadBytes / ReadString / Skip 一一对应）。"""

    def __init__(self, stream, offset):
        self.stream = stream
        self.buffer = bytearray(BUFFER_SIZE)
        self.start = 0
        self.end = 0
        self.position = offset
        self.stream.seek(offset)

    def fill(self, count):
        if self.end - self.start >= count:
            return
        if count > BUFFER_SIZE:
            count = BUFFER_SIZE
        left = self.end - self.start
        if left > 0 and self.start > 0:
            self.buffer[0:left] = self.buffer[self.start:self.end]
        self.start = 0
        self.end = left
        while self.end < count:
            chunk = self.stream.read(BUFFER_SIZE - self.end)
            if not chunk:
                raise EOFError("缓存文件不完整")
            self.buffer[self.end:self.end + len(chunk)] = chunk
            self.end += len(chunk)

    def read_bytes(self, count):
        out = bytearray(count)
        copied = 0
        while copied < count:
            self.fill(1)
            take = min(count - copied, self.end - self.start)
            out[copied:copied + take] = self.buffer[self.start:self.start + take]
            self.start += take
            copied += take
            self.position += take
        return bytes(out)

    def i32(self):
        self.fill(4)
        value = struct.unpack_from("<i", self.buffer, self.start)[0]
        self.start += 4
        self.position += 4
        return value

    def f32(self):
        self.fill(4)
        value = struct.unpack_from("<f", self.buffer, self.start)[0]
        self.start += 4
        self.position += 4
        return value

    def boolean(self):
        self.fill(1)
        value = self.buffer[self.start] != 0
        self.start += 1
        self.position += 1
        return value

    def string(self):
        length = 0
        shift = 0
        while True:
            self.fill(1)
            value = self.buffer[self.start]
            self.start += 1
            self.position += 1
            length |= (value & 0x7F) << shift
            if not value & 0x80:
                break
            shift += 7
        if length <= 0:
            return ""
        return self.read_bytes(length).decode("utf-8")

    def skip(self, count):
        if count <= 0:
            return
        self.position += count
        if count <= self.end - self.start:
            self.start += count
            return
        self.start = 0
        self.end = 0
        self.stream.seek(self.position)

    def floats(self, count):
        return struct.unpack("<%df" % count, self.read_bytes(count * 4))


def skip_transforms(reader):
    transform_count = reader.i32()
    for _ in range(transform_count):
        reader.string()
        reader.string()
        reader.i32()
        for _ in range(10):
            reader.f32()
        if not reader.boolean():
            continue
        for _ in range(reader.i32() * 3):
            reader.f32()
        for _ in range(reader.i32() * 3):
            reader.f32()
        for _ in range(reader.i32() * 2):
            reader.f32()
        for _ in range(reader.i32()):
            reader.i32()


def index_pass(reader):
    version = reader.i32()
    reader.string()
    skip_transforms(reader)

    index = {}
    order = []
    for _ in range(reader.i32()):
        name = reader.string()
        length = reader.f32()
        wrap = reader.i32()
        offset = reader.position
        track_count = reader.i32()
        for _ in range(track_count):
            reader.string()
            count = reader.i32()
            reader.skip(count * TRACK_FLOATS * 4)
        index[name] = (offset, length, wrap)
        order.append(name)

    return version, index, order


def full_pass(data):
    """参照读法顺序读完整份缓存，返回 (版本, 骨骼, 动画)"""
    reader = PlainReader(data)
    version = reader.i32()
    reader.string()

    bones = []
    for _ in range(reader.i32()):
        name = reader.string()
        reader.string()
        parent = reader.i32()
        bones.append((name, parent) + reader.floats(10))
        if not reader.boolean():
            continue
        # 顶点 / 法线 / UV 各自带一个 count
        for element in (3, 3, 2):
            for _ in range(reader.i32() * element):
                reader.f32()
        for _ in range(reader.i32()):
            reader.i32()

    clips = {}
    order = []
    for _ in range(reader.i32()):
        name = reader.string()
        length = reader.f32()
        wrap = reader.i32()
        tracks = []
        for _ in range(reader.i32()):
            path = reader.string()
            count = reader.i32()
            values = (reader.floats(count) + reader.floats(count * 3)
                      + reader.floats(count * 4) + reader.floats(count * 3))
            tracks.append((path, values))
        clips[name] = (length, wrap, tracks)
        order.append(name)

    return version, bones, clips, order


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    path = sys.argv[1]
    with open(path, "rb") as fh:
        data = fh.read()

    version, bones, clips, order = full_pass(data)

    stream = FakeFileStream(data)
    reader = BufferedReader(stream, 0)
    buffered_version, index, buffered_order = index_pass(reader)

    print("格式版本 = %d（参照 %d），骨骼 %d，动画 %d 条，索引读完位于 %d / %d 字节"
          % (buffered_version, version, len(bones), len(index), reader.position, len(data)))
    if buffered_version != version or len(index) != len(clips) or buffered_order != order:
        print("缓冲读取和参照读法对不上（版本 / 条数 / 顺序）")
        return 1
    if reader.position != len(data):
        print("索引结束后位置和文件长度对不上：跳过采样数据算错了长度")
        return 1

    failed = 0
    probes = sorted(set([order[0], order[len(order) // 2], order[-1]]))
    for name in probes:
        offset, length, wrap = index[name]
        expect_length, expect_wrap, expect_tracks = clips[name]
        if abs(length - expect_length) > 1e-6 or wrap != expect_wrap:
            print("  %-32s 元数据对不上" % name)
            failed += 1
            continue

        lazy = BufferedReader(FakeFileStream(data), offset)
        track_count = lazy.i32()
        tracks = []
        for _ in range(track_count):
            track_path = lazy.string()
            count = lazy.i32()
            values = (lazy.floats(count) + lazy.floats(count * 3)
                      + lazy.floats(count * 4) + lazy.floats(count * 3))
            tracks.append((track_path, values))

        ok = len(tracks) == len(expect_tracks) and all(
            a[0] == b[0] and a[1] == b[1] for a, b in zip(tracks, expect_tracks))
        samples = sum(len(t[1]) // TRACK_FLOATS for t in tracks)
        print("  %-32s 偏移=%9d 长度=%8.3f 轨道=%3d 采样=%7d %s"
              % (name, offset, length, len(tracks), samples, "OK" if ok else "数据对不上"))
        if not ok:
            failed += 1

    print("索引阶段真实读盘 %.2f MB / %d 次读取（文件共 %.2f MB）"
          % (stream.bytes_read / 1e6, stream.read_calls, len(data) / 1e6))

    if failed:
        print("\n有 %d 条动画对不上" % failed)
        return 1

    print("\n检查通过：缓冲读取、跳过采样、按偏移重读都和参照读法一致")
    return 0


if __name__ == "__main__":
    sys.exit(main())
