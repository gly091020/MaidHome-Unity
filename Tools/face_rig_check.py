"""摸脸模式的骨骼挑选校验（不依赖 Unity）。

复刻 MaidFaceRig 的命名规则和 StretchAxis 的打分，用来在真机之前确认：
头 / 左右耳 / 眼睛分别落到哪根骨骼上、隐藏的 FOX 子树有没有被跳过、
耳朵拉长会沿哪个局部轴缩放（打分 = 网格尺寸 x 画面投影长度）。

用法：python Tools/face_rig_check.py <winefox.json> [<另一个.json> ...]
"""
import json
import math
import sys

HEAD_NAMES = ["head", "mhead", "allhead", "head2"]
LEFT_EYE_NAMES = ["lefteyepublic", "lefteyelidbase", "lefteyelid", "lefteye", "leye", "eyeleft"]
RIGHT_EYE_NAMES = ["righteyepublic", "righteyelidbase", "righteyelid", "righteye", "reye", "eyeright"]
AXES = [(1, 0, 0), (0, 1, 0), (0, 0, 1)]
AXIS_NAMES = ["X", "Y", "Z"]
VIEW = (0, 0, -1)
EAR_ZONE_SCALE = 0.8


def normalize(name):
    return (name or "").lower().replace("_", "").replace("-", "").replace(".", "").replace(" ", "")


def load_bones(path):
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    geometry = data.get("minecraft:geometry") or data.get("geometry")
    return geometry[0]["bones"] if isinstance(geometry, list) else geometry["bones"]


def by_name(bones):
    table = {}
    for bone in bones:
        table.setdefault(bone.get("name", ""), bone)
    return table


def under_fox(bones, bone):
    table = by_name(bones)
    current = bone
    guard = 0
    while current is not None and guard < 64:
        if (current.get("parent") or "").upper() == "FOX":
            return True
        current = table.get(current.get("parent") or "")
        guard += 1
    return False


def visible_bones(bones):
    return [b for b in bones if not under_fox(bones, b)]


def is_ear(name, left):
    n = normalize(name)
    if not n or "earring" in n or "earphone" in n:
        return False
    if any(k in n for k in ("hand", "arm", "held", "item")):
        return False
    if "ear" not in n:
        return False
    is_left = "left" in n or n.startswith("lear") or n.startswith("earleft")
    is_right = "right" in n or n.startswith("rear") or n.startswith("earright")
    return is_left if left else is_right


def pick_head(bones):
    for wanted in HEAD_NAMES:
        for bone in bones:
            if normalize(bone.get("name")) == wanted and bone.get("cubes"):
                return bone
    for wanted in HEAD_NAMES:
        for bone in bones:
            if normalize(bone.get("name")) == wanted:
                return bone
    return None


def pick_named(bones, names):
    for wanted in names:
        for bone in bones:
            if normalize(bone.get("name")) == wanted:
                return bone
    return None


def ear_bones(bones, left):
    return [b for b in bones if is_ear(b.get("name"), left)]


def local_size(bone):
    pivot = bone.get("pivot") or [0, 0, 0]
    lo = [float("inf")] * 3
    hi = [float("-inf")] * 3
    for cube in bone.get("cubes") or []:
        origin = cube.get("origin") or [0, 0, 0]
        size = cube.get("size") or [0, 0, 0]
        for axis in range(3):
            a = origin[axis] - pivot[axis]
            b = a + size[axis]
            lo[axis] = min(lo[axis], a, b)
            hi[axis] = max(hi[axis], a, b)
    if lo[0] == float("inf"):
        return [1.0, 1.0, 1.0]
    return [hi[i] - lo[i] for i in range(3)]


def rot_x(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [[1, 0, 0], [0, c, -s], [0, s, c]]


def rot_y(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [[c, 0, s], [0, 1, 0], [-s, 0, c]]


def rot_z(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [[c, -s, 0], [s, c, 0], [0, 0, 1]]


def mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def apply(matrix, vector):
    return [sum(matrix[i][k] * vector[k] for k in range(3)) for i in range(3)]


def compose(rotation):
    r = rotation or [0, 0, 0]
    return mul(mul(rot_z(r[2]), rot_y(r[1])), rot_x(r[0]))


def accumulated(bones, bone):
    table = by_name(bones)
    chain = []
    current = bone
    guard = 0
    while current is not None and guard < 64:
        chain.append(current)
        current = table.get(current.get("parent") or "")
        guard += 1
    matrix = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
    for item in reversed(chain):
        matrix = mul(matrix, compose(item.get("rotation")))
    return matrix


def stretch_axis(bones, bone):
    size = local_size(bone)
    matrix = accumulated(bones, bone)
    scores = []
    for i, axis in enumerate(AXES):
        direction = apply(matrix, axis)
        dot = sum(direction[k] * VIEW[k] for k in range(3))
        on_screen = [direction[k] - dot * VIEW[k] for k in range(3)]
        scores.append(abs(size[i]) * math.sqrt(sum(v * v for v in on_screen)))
    return max(range(3), key=lambda i: scores[i]), scores


def check(path):
    bones = load_bones(path)
    visible = visible_bones(bones)
    print("=== " + path)
    print("骨骼 %d 根，去掉 FOX 底下隐藏的还剩 %d 根" % (len(bones), len(visible)))

    head = pick_head(visible)
    if head is None:
        print("  !! 没找到头骨骼")
        return
    print("  头: %s（%d 个方块）" % (head.get("name"), len(head.get("cubes") or [])))

    for left in (True, False):
        side = "左" if left else "右"
        picked = ear_bones(visible, left)
        if not picked:
            print("  %s耳: 没选到" % side)
            continue
        for bone in picked:
            axis, scores = stretch_axis(visible, bone)
            size = local_size(bone)
            print("  %s耳: %-10s 拉伸轴 %s  打分 X=%.2f Y=%.2f Z=%.2f  局部尺寸 %.2f/%.2f/%.2f"
                  % (side, bone.get("name"), AXIS_NAMES[axis], scores[0], scores[1], scores[2],
                     size[0], size[1], size[2]))

    head_box = front_box(head)
    if head_box is not None:
        print("  头正面矩形: x %.1f~%.1f  y %.1f~%.1f"
              % (head_box[0], head_box[2], head_box[1], head_box[3]))
        for left in (True, False):
            side = "左" if left else "右"
            picked = ear_bones(visible, left)
            if not picked or picked[0] is None:
                continue
            ear = front_box(picked[0])
            if ear is None:
                continue
            rect_inside = overlap(ear, head_box) / max(1e-6, (ear[2] - ear[0]) * (ear[3] - ear[1]))
            ellipse_inside = ellipse_overlap(ear, head_box, EAR_ZONE_SCALE)
            print("  %s耳判定区: %.1f x %.1f  方框压在头上 %.0f%%  收成椭圆(%.2f)后压在头上 %.0f%%"
                  % (side, ear[2] - ear[0], ear[3] - ear[1],
                     rect_inside * 100.0, EAR_ZONE_SCALE, ellipse_inside * 100.0))
            overlap_x = max(0.0, min(ear[2], head_box[2]) - max(ear[0], head_box[0]))
            overlap_y = max(0.0, min(ear[3], head_box[3]) - max(ear[1], head_box[1]))
            print("        耳朵矩形: x %.1f~%.1f  y %.1f~%.1f（横向探出头外 %.1f，纵向探出 %.1f）"
                  % (ear[0], ear[2], ear[1], ear[3],
                     (ear[2] - ear[0]) - overlap_x, (ear[3] - ear[1]) - overlap_y))

    left_eye = pick_named(visible, LEFT_EYE_NAMES)
    right_eye = pick_named(visible, RIGHT_EYE_NAMES)
    print("  左眼: %s" % (left_eye.get("name") if left_eye else "没找到（按头的比例算）"))
    print("  右眼: %s" % (right_eye.get("name") if right_eye else "没找到（按头的比例算）"))


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return
    for path in sys.argv[1:]:
        check(path)


def bone_box(bone):
    """骨骼所有方块在模型空间里的 min/max（只关心正面看到的 x/y）。"""
    lo = [float("inf")] * 3
    hi = [float("-inf")] * 3
    for cube in bone.get("cubes") or []:
        origin = cube.get("origin") or [0, 0, 0]
        size = cube.get("size") or [0, 0, 0]
        for axis in range(3):
            a = origin[axis]
            b = a + size[axis]
            lo[axis] = min(lo[axis], a, b)
            hi[axis] = max(hi[axis], a, b)
    if lo[0] == float("inf"):
        return None
    return lo, hi


def front_box(bone):
    """正面看过去的矩形：基岩 x 会被取反，所以只取范围（min/max 不影响大小）。"""
    box = bone_box(bone)
    if box is None:
        return None
    lo, hi = box
    return (-hi[0], lo[1], -lo[0], hi[1])


def overlap(box_a, box_b):
    x = max(0.0, min(box_a[2], box_b[2]) - max(box_a[0], box_b[0]))
    y = max(0.0, min(box_a[3], box_b[3]) - max(box_a[1], box_b[1]))
    return x * y


def ellipse_overlap(ear, head, scale, steps=64):
    """缩放后的内切椭圆有多少面积落在头这块里（网格采样）。"""
    cx, cy = (ear[0] + ear[2]) * 0.5, (ear[1] + ear[3]) * 0.5
    rx = (ear[2] - ear[0]) * 0.5 * scale
    ry = (ear[3] - ear[1]) * 0.5 * scale
    inside = 0
    total = 0
    for i in range(steps):
        for j in range(steps):
            x = cx + rx * (2.0 * (i + 0.5) / steps - 1.0)
            y = cy + ry * (2.0 * (j + 0.5) / steps - 1.0)
            total += 1
            if head[0] <= x <= head[2] and head[1] <= y <= head[3]:
                inside += 1
    return inside / max(1, total)


if __name__ == "__main__":
    main()
