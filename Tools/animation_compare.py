#!/usr/bin/env python3
r"""把一份「动画采样 dump」和 Python 参考实现逐点对比。

用法:
  python Tools/animation_compare.py <dump.txt> <animation.json> <model.json>

dump 是制表符分隔的文本（utf-8），格式见 load_dump：
  clip <名字>
  length <秒>
  time <秒>            后面跟若干行 bone
  bone <骨骼路径> <位置 x y z> <旋转 x y z w> <缩放 x y z>
原来产生这份 dump 的 C# 工具（Tools/cscheck）已删除；要重新对拍 C# 就按这个格式再导一份。
"""
import sys

import animation_check as ac
import bedrock_preview as bp


def load_dump(path):
    times = []
    bones = {}
    clip = ""
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if parts[0] == "clip":
                clip = parts[1]
            elif parts[0] == "time":
                times.append(float(parts[1]))
                bones[times[-1]] = []
            elif parts[0] == "bone":
                values = [float(v) for v in parts[2:] if v != ""]
                bones[times[-1]].append((parts[1], values[:3], values[3:7], values[7:10]))
    return clip, times, bones


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        return 1

    dump_path, animation_path, model_path = sys.argv[1], sys.argv[2], sys.argv[3]
    clip, times, dump = load_dump(dump_path)
    data = ac.parse_animation_file(animation_path)
    animation = data["animations"][clip]
    geometry = bp.parse_geometry(bp.load_json(model_path))
    by_path = {}
    by_name, paths = ac.bone_hierarchy(geometry)
    for name, path in paths.items():
        by_path.setdefault(path, name)

    worst_position = (0.0, "")
    worst_rotation = (0.0, "")
    worst_scale = (0.0, "")
    missing = set()
    checked = 0
    for time in times:
        expected = ac.sample_pose(animation, geometry, time)
        actual = dump[time]
        if len(actual) != len(expected):
            print("时间 %.4f 的骨骼数量不一致: C# %d, Python %d" % (time, len(actual), len(expected)))
            return 2
        for path, position, rotation, scale in actual:
            name = by_path.get(path)
            if name is None:
                missing.add(path)
                continue
            entry = expected[name]
            want_position = entry["position"]
            want_rotation = ac.matrix_to_quaternion(entry["rotation"])
            want_scale = entry["scale"]
            dp = max(abs(position[i] - want_position[i]) for i in range(3))
            dr = max(abs(rotation[i] - want_rotation[i]) for i in range(4))
            dr_sign = max(abs(rotation[i] + want_rotation[i]) for i in range(4))
            dr = min(dr, dr_sign)
            ds = max(abs(scale[i] - want_scale[i]) for i in range(3))
            if dp > worst_position[0]:
                worst_position = (dp, "%s @ %.4fs" % (name, time))
            if dr > worst_rotation[0]:
                worst_rotation = (dr, "%s @ %.4fs" % (name, time))
            if ds > worst_scale[0]:
                worst_scale = (ds, "%s @ %.4fs" % (name, time))
            checked += 1

    print("动画 %s: 采样点 %d，比较骨骼 %d 个" % (clip, len(times), checked // max(1, len(times))))
    print("  位置最大偏差 %.6f  (%s)" % worst_position)
    print("  旋转最大偏差 %.6f  (%s)" % worst_rotation)
    print("  缩放最大偏差 %.6f  (%s)" % worst_scale)
    if missing:
        print("  C# 导出的路径对不上 Python 的骨骼: " + ", ".join(sorted(missing)))
        return 3

    ok = worst_position[0] < 1e-4 and worst_rotation[0] < 1e-4 and worst_scale[0] < 1e-4
    print("  结论: " + ("一致" if ok else "不一致"))
    return 0 if ok else 4


if __name__ == "__main__":
    sys.exit(main())
