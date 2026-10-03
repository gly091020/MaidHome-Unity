"""扫出基岩动画里用到的 Molang 标识符，标出哪些是工程里没实现的（运行时会被当 0）。

工程只实现了 Molang 的一个子集（`query.anim_time` + `math.*`），其余变量（`ysm.*`、
`query.ground_speed`、`variable.*` 等）一律取 0。动画要是有这种依赖，烘焙出来就会不对
（抖动、卡在某个姿势、速度不对），所以换模型/换动画先跑这个看一眼。

用法:
    python Tools/molang_usage.py <animation.json>              # 所有动画的汇总
    python Tools/molang_usage.py <animation.json> --anim idle  # 只看某条动画（逐骨骼列出来）
"""

import argparse
import json
import re
import sys

# 工程 MolangExpression 里实现了的东西
SUPPORTED_PREFIX = (
    "query.anim_time",
    "q.anim_time",
    "math.",
)

# 和 C# 的 MolangContext.FixedValues 一致：没有真数据源，按 TLM 实际取值固定（不是 0）
FIXED = {
    "ysm.food_level": "20",
    "ysm.rendering_in_inventory": "false(0)",
    "ysm.texture_name": "空串(数值 0)",
    "query.player_level": "0",
    "query.has_cape": "false(0)",
}

# 认得出但直接给 0 的函数（TLM 里就是空的）
NOOP_FUNCTIONS = {"ysm.bone_pivot_abs"}

IDENT = re.compile(r"[A-Za-z_][A-Za-z0-9_.]*")
NUMBER = re.compile(r"^[0-9.eE+-]+$")
KEYWORDS = {"true", "false", "return", "this", "null"}


def collect_strings(node, path, out):
    if isinstance(node, str):
        out.append((path, node))
    elif isinstance(node, dict):
        for key, value in node.items():
            collect_strings(value, path + "." + str(key), out)
    elif isinstance(node, list):
        for index, value in enumerate(node):
            collect_strings(value, path + "[%d]" % index, out)


def identifiers_of(expression):
    found = []
    for token in IDENT.findall(expression):
        if token in KEYWORDS or NUMBER.match(token):
            continue
        found.append(token)
    return found


def scan_animation(animation):
    """返回 {标识符: [(骨骼通道路径, 表达式原文)]}。只看骨骼的 rotation/position/scale，
    lerp_mode / loop 那种结构性字段不算 Molang。"""
    strings = []
    bones = animation.get("bones") if isinstance(animation, dict) else None
    if isinstance(bones, dict):
        for bone_name, bone in bones.items():
            if not isinstance(bone, dict):
                continue
            for channel in ("rotation", "position", "scale"):
                data = bone.get(channel)
                if data is None:
                    continue
                collected = []
                collect_strings(data, bone_name + "." + channel, collected)
                for path, text in collected:
                    if path.endswith(".lerp_mode"):
                        continue
                    strings.append((path, text))

    uses = {}
    for path, text in strings:
        # 时间轴上的键（"0.5"）不是表达式
        if NUMBER.match(text):
            continue
        for token in identifiers_of(text):
            uses.setdefault(token, []).append((path, text))
    return uses


def supported(token):
    return any(token == prefix or token.startswith(prefix) for prefix in SUPPORTED_PREFIX)


def classify(token):
    if token in FIXED:
        return "固定值 " + FIXED[token]
    if token in NOOP_FUNCTIONS:
        return "支持（恒 0）"
    return "支持" if supported(token) else "**没实现，运行时取 0**"


def load_animations(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        root = json.load(fh)
    animations = root.get("animations")
    if not isinstance(animations, dict):
        raise SystemExit("这个文件里没有 animations 段: " + path)
    return animations


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("path")
    parser.add_argument("--anim", default="", help="只看这条动画，逐骨骼列出用到的表达式")
    args = parser.parse_args()

    animations = load_animations(args.path)
    if args.anim:
        animation = animations.get(args.anim)
        if animation is None:
            print("没有这条动画: " + args.anim)
            return 1

        uses = scan_animation(animation)
        print("动画 %s：%d 种标识符" % (args.anim, len(uses)))
        for token in sorted(uses):
            print("  %-28s %s（%d 处）" % (token, classify(token), len(uses[token])))
            for path, text in uses[token][:6]:
                print("      %s = %s" % (path, text))
        return 0

    problems = []
    print("动画 %d 条" % len(animations))
    for name in sorted(animations):
        uses = scan_animation(animations[name])
        unsupported = sorted(t for t in uses
                             if not supported(t) and t not in FIXED and t not in NOOP_FUNCTIONS)
        if not uses:
            continue
        text = ", ".join("%s%s" % (t, "" if classify(t) in ("支持", "支持（恒 0）")
                                   or t in FIXED else "*")
                         for t in sorted(uses))
        print("  %-34s %s" % (name, text))
        if unsupported:
            problems.append((name, unsupported))

    print("\n带 * 的是工程没实现的变量（运行时会取 0）：")
    if not problems:
        print("  没有，所有动画只依赖 query.anim_time / math.*")
        return 0

    for name, tokens in problems:
        print("  %-34s %s" % (name, ", ".join(tokens)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
