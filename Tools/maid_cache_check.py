"""校验女仆缓存 maid.bin 的结构，不需要 Unity。

重点查一件事：**动画曲线路径能不能在骨骼层级里找到**。找不到就说明曲线挂不上去，
表现出来就是"模型在动、但姿势不变"这类静默失效。缓存格式见 MaidAssetCache.WriteBin。

用法: python Tools/maid_cache_check.py <maid.bin>
默认路径: %USERPROFILE%\\AppData\\LocalLow\\DefaultCompany\\MaidHome\\cache\\maid\\<uuid>\\maid.bin
"""

import os
import struct
import sys


def read_string(fh):
    length = 0
    shift = 0
    while True:
        b = fh.read(1)[0]
        length |= (b & 0x7F) << shift
        if not b & 0x80:
            break
        shift += 7
    return fh.read(length).decode("utf-8")


def read_int(fh):
    return struct.unpack("<i", fh.read(4))[0]


def read_float(fh):
    return struct.unpack("<f", fh.read(4))[0]


def skip_array(fh, element_size):
    count = read_int(fh)
    fh.seek(count * element_size, os.SEEK_CUR)
    return count


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    path = sys.argv[1]
    with open(path, "rb") as fh:
        version = read_int(fh)
        root_name = read_string(fh)
        print("格式版本 = %d, 根节点名 = %r" % (version, root_name))

        transform_count = read_int(fh)
        names = []
        parents = []
        meshes = 0
        vertices = 0
        for i in range(transform_count):
            name = read_string(fh)
            read_string(fh)  # 相对路径，暂不用
            parent = read_int(fh)
            read_float(fh); read_float(fh); read_float(fh)
            read_float(fh); read_float(fh); read_float(fh); read_float(fh)
            read_float(fh); read_float(fh); read_float(fh)
            names.append(name)
            parents.append(parent)
            if fh.read(1)[0]:
                meshes += 1
                vertices += skip_array(fh, 12)
                skip_array(fh, 12)
                skip_array(fh, 8)
                index_count = read_int(fh)
                fh.seek(index_count * 4, os.SEEK_CUR)

        print("节点 %d 个，其中 %d 个带网格，共 %d 个顶点" % (transform_count, meshes, vertices))

        clip_count = read_int(fh)
        clips = []
        for i in range(clip_count):
            name = read_string(fh)
            length = read_float(fh)
            wrap = read_int(fh)
            track_count = read_int(fh)
            tracks = []
            for t in range(track_count):
                track_path = read_string(fh)
                count = read_int(fh)
                fh.seek(count * 4, os.SEEK_CUR)          # times
                fh.seek(count * 12, os.SEEK_CUR)         # positions
                fh.seek(count * 16, os.SEEK_CUR)         # rotations
                fh.seek(count * 12, os.SEEK_CUR)         # scales
                tracks.append((track_path, count))
            clips.append((name, length, wrap, tracks))

    # 模型根（0 号）自己在曲线路径里不出现，其它节点的路径就是 BuildPath 的写法
    def path_of(index):
        parts = []
        while index > 0:
            parts.append(names[index])
            index = parents[index]
        return "/".join(reversed(parts))

    known = set(path_of(i) for i in range(1, transform_count))

    print("动画 %d 条" % clip_count)
    for name, length, wrap, tracks in clips:
        missing = [track[0] for track in tracks if track[0] not in known]
        print("  %-22s 长度=%.4f 通道=%d 关键帧=%d %s"
              % (name, length, len(tracks), sum(t[1] for t in tracks),
                 "循环" if wrap == 2 else "停最后一帧"))
        for track_path in missing[:3]:
            print("      路径对不上: %r" % track_path)

    errors = []
    if parents[0] != -1:
        errors.append("0 号节点的 parentIndex 是 %d，应该是 -1（它就是模型根）" % parents[0])
    for i in range(1, transform_count):
        if parents[i] < 0 or parents[i] >= i:
            errors.append("%d 号节点 %r 的 parentIndex=%d 不合法（父节点必须排在前面）"
                          % (i, names[i], parents[i]))

    all_missing = sum(1 for _, _, _, tracks in clips
                      for track in tracks if track[0] not in known)
    if all_missing:
        errors.append("有 %d 个动画通道的路径在骨骼层级里找不到，这些曲线不会生效" % all_missing)

    if errors:
        print("\n发现问题:")
        for item in errors[:10]:
            print("  " + item)
        return 1

    print("\n检查通过：层级合法，所有动画通道的路径都能对上")
    return 0


if __name__ == "__main__":
    sys.exit(main())
