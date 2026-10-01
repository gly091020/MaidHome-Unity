#!/usr/bin/env python3
"""基岩动画（winefox.animation.json 这类文件）的离线采样/导出工具，纯标准库。

它实现的是游戏 TLM（geckolib3）那一套采样规则：
  - 关键帧时间单位是秒，easing 只支持 linear / catmullrom
  - 段 [i, i+1] 用第 i 个关键帧的 lerp_mode；若第 i+1 个是 catmullrom 也按 catmullrom
  - 旋转值逐轴相加（骨骼静止旋转 + 动画旋转），再按 Rz*Ry*Rx 合成
  - position 单位是像素，且平移方向是 (-x, +y, +z)/16

用法:
  python Tools/animation_check.py <animation.json> --summary
  python Tools/animation_check.py <animation.json> --model <geometry.json> --anim walk --dump out.json
  python Tools/animation_check.py <animation.json> --anim walk --time 0.25 --bone LeftArm
"""
import argparse
import json
import math
import re
import sys

import bedrock_preview as bp


# ---------------------------------------------------------------- Molang 子集
FUNCTIONS = {
    "math.abs": lambda a: abs(a[0]),
    # Molang 的三角函数吃角度，不是弧度
    "math.sin": lambda a: math.sin(math.radians(a[0])),
    "math.cos": lambda a: math.cos(math.radians(a[0])),
    "math.tan": lambda a: math.tan(math.radians(a[0])),
    "math.asin": lambda a: math.degrees(math.asin(max(-1.0, min(1.0, a[0])))),
    "math.acos": lambda a: math.degrees(math.acos(max(-1.0, min(1.0, a[0])))),
    "math.atan": lambda a: math.degrees(math.atan(a[0])),
    "math.atan2": lambda a: math.degrees(math.atan2(a[0], a[1])),
    "math.exp": lambda a: math.exp(min(60.0, a[0])),
    "math.sqrt": lambda a: math.sqrt(a[0]) if a[0] > 0 else 0.0,
    "math.pow": lambda a: math.pow(a[0], a[1]),
    "math.floor": lambda a: math.floor(a[0]),
    "math.ceil": lambda a: math.ceil(a[0]),
    "math.round": lambda a: math.floor(a[0] + 0.5),
    "math.trunc": lambda a: math.trunc(a[0]),
    "math.min": lambda a: min(a),
    "math.max": lambda a: max(a),
    "math.clamp": lambda a: max(a[1], min(a[2], a[0])),
    "math.mod": lambda a: math.fmod(a[0], a[1]) if a[1] != 0 else 0.0,
    "math.lerp": lambda a: a[0] + (a[1] - a[0]) * a[2],
    "math.hermite_blend": lambda a: a[0] * a[0] * (3 - 2 * a[0]),
    "math.random": lambda a: 0.0,
}

TOKEN_RE = re.compile(r"\s*(?:(\d+\.?\d*(?:[eE][-+]?\d+)?)|([A-Za-z_][A-Za-z0-9_.]*)|(==|!=|<=|>=|&&|\|\||[-+*/%!()?,:<>]))")


def tokenize(text):
    tokens = []
    bad = []
    pos = 0
    while pos < len(text):
        match = TOKEN_RE.match(text, pos)
        if match is None:
            if text[pos:].strip() == "":
                break
            bad.append(text[pos])
            pos += 1
            continue
        pos = match.end()
        number, name, symbol = match.groups()
        if number is not None:
            tokens.append(("num", float(number)))
        elif name is not None:
            tokens.append(("name", name))
        else:
            tokens.append(("op", symbol))
    return tokens, bad


class Parser:
    """把 Molang 表达式编译成一棵小 AST（元组）。不支持的写法会记进 self.warnings。"""

    def __init__(self, text, warnings):
        self.text = text
        self.tokens, bad = tokenize(text)
        self.index = 0
        self.warnings = warnings
        if bad:
            self.warn("认不出的字符 " + "".join(sorted(set(bad))))

    def peek(self):
        return self.tokens[self.index] if self.index < len(self.tokens) else (None, None)

    def take(self, symbol):
        kind, value = self.peek()
        if kind == "op" and value == symbol:
            self.index += 1
            return True
        return False

    def parse(self):
        node = self.ternary()
        if self.index != len(self.tokens):
            self.warn("多余的 token")
        return node

    def ternary(self):
        condition = self.binary(0)
        if self.take("?"):
            yes = self.ternary()
            # 从 YSM 转过来的模型里常见 "条件 ? 值" 少了 else，按 else = 0 处理
            no = self.ternary() if self.take(":") else ("num", 0.0)
            return ("tern", condition, yes, no)
        return condition

    LEVELS = [("||",), ("&&",), ("==", "!="), ("<", "<=", ">", ">="), ("+", "-"), ("*", "/", "%")]

    def binary(self, level):
        if level >= len(self.LEVELS):
            return self.unary()
        left = self.binary(level + 1)
        while True:
            kind, value = self.peek()
            if kind != "op" or value not in self.LEVELS[level]:
                return left
            self.index += 1
            left = ("bin", value, left, self.binary(level + 1))

    def unary(self):
        kind, value = self.peek()
        if kind == "op" and value in ("-", "!", "+"):
            self.index += 1
            return ("un", value, self.unary())
        return self.primary()

    def primary(self):
        kind, value = self.peek()
        if kind == "num":
            self.index += 1
            return ("num", value)
        if kind == "name":
            self.index += 1
            if value in ("true", "false"):
                return ("num", 1.0 if value == "true" else 0.0)
            if self.take("("):
                args = []
                if not self.take(")"):
                    while True:
                        args.append(self.ternary())
                        if self.take(","):
                            continue
                        if not self.take(")"):
                            self.warn("函数调用缺少 )")
                        break
                if value not in FUNCTIONS:
                    self.warn("不支持的函数 " + value + "，按 0 处理")
                return ("call", value, args)
            return ("var", value)
        if kind == "op" and value == "(":
            self.index += 1
            node = self.ternary()
            if not self.take(")"):
                self.warn("缺少 )")
            return node
        self.warn("表达式解析中断")
        self.index += 1
        return ("num", 0.0)

    def warn(self, reason):
        text = "Molang 解析问题（" + reason + "）: " + self.text
        if text not in self.warnings:
            self.warnings.append(text)


def eval_ast(node, context):
    kind = node[0]
    if kind == "num":
        return node[1]
    if kind == "var":
        return context.get(node[1])
    if kind == "call":
        function = FUNCTIONS.get(node[1])
        if function is None:
            return 0.0
        return function([eval_ast(a, context) for a in node[2]])
    if kind == "un":
        value = eval_ast(node[2], context)
        if node[1] == "-":
            return -value
        if node[1] == "!":
            return 0.0 if value != 0.0 else 1.0
        return value
    if kind == "tern":
        return eval_ast(node[2], context) if eval_ast(node[1], context) != 0.0 else eval_ast(node[3], context)
    left = eval_ast(node[2], context)
    right = eval_ast(node[3], context)
    op = node[1]
    if op == "+":
        return left + right
    if op == "-":
        return left - right
    if op == "*":
        return left * right
    if op == "/":
        return left / right if right != 0 else 0.0
    if op == "%":
        return math.fmod(left, right) if right != 0 else 0.0
    if op == "==":
        return 1.0 if abs(left - right) < 1e-9 else 0.0
    if op == "!=":
        return 1.0 if abs(left - right) >= 1e-9 else 0.0
    if op == "<":
        return 1.0 if left < right else 0.0
    if op == "<=":
        return 1.0 if left <= right else 0.0
    if op == ">":
        return 1.0 if left > right else 0.0
    if op == ">=":
        return 1.0 if left >= right else 0.0
    if op == "&&":
        return 1.0 if (left != 0.0 and right != 0.0) else 0.0
    if op == "||":
        return 1.0 if (left != 0.0 or right != 0.0) else 0.0
    return 0.0


class MolangContext:
    def __init__(self, anim_time=0.0, variables=None):
        self.anim_time = anim_time
        self.variables = dict(variables or {})

    def get(self, name):
        if name in ("query.anim_time", "query.life_time", "anim_time", "life_time"):
            return self.anim_time
        return self.variables.get(name, 0.0)


def eval_value(value, context):
    """value 是 float 或 ('expr', ast)。"""
    if isinstance(value, tuple):
        # 表达式里的 query.anim_time 取当前时间，其余变量取 0
        return eval_ast(value[1], MolangContext(context.anim_time, context.variables))
    return float(value)


ANIM_TIME_RE = re.compile(r"anim_time\s*\*\s*([0-9.]+)")


def collect_rates(node, rates):
    """扫出 anim_time 的乘数（Molang 三角函数吃角度，周期 = 360/乘数）。"""
    if isinstance(node, str):
        for match in ANIM_TIME_RE.finditer(node):
            rate = float(match.group(1))
            if rate > 0:
                rates.append(rate)
    elif isinstance(node, list):
        for item in node:
            collect_rates(item, rates)
    elif isinstance(node, dict):
        for item in node.values():
            collect_rates(item, rates)


def estimate_period(rates):
    if not rates:
        return 0.0
    longest = max(360.0 / rate for rate in rates)
    for multiple in range(1, 61):
        candidate = longest * multiple
        if candidate > 60:
            break
        if all(abs(candidate * rate / 360.0 - round(candidate * rate / 360.0)) < 1e-3 for rate in rates):
            return candidate
    return longest


def parse_value(node, warnings):
    """把一个数字 / 字符串 / 三元组解析成 (x, y, z)，元素是 float 或 ('expr', ast)。"""
    if isinstance(node, str):
        ast = Parser(node, warnings).parse()
        return tuple(("expr", ast) for _ in range(3))
    if isinstance(node, (int, float)):
        return (float(node),) * 3
    if isinstance(node, list):
        if len(node) >= 3:
            return tuple(
                ("expr", Parser(item, warnings).parse()) if isinstance(item, str) else float(item)
                for item in node[:3])
        if len(node) == 1:
            return parse_value(node[0], warnings)
        raise ValueError("向量长度不对: " + repr(node))
    raise ValueError("不认识的通道值: " + repr(node))


# ------------------------------------------------------------------ 动画解析
def parse_animation(name, doc, warnings):
    bones = {}
    for bone_name, channels in doc.get("bones", {}).items():
        entry = {"rotation": None, "position": None, "scale": None}
        for channel in ("rotation", "position", "scale"):
            raw = channels.get(channel)
            if raw is None:
                continue
            if isinstance(raw, (str, int, float, list)):
                entry[channel] = {"constant": parse_value(raw, warnings), "keys": None}
                continue
            keys = []
            for time_text, item in raw.items():
                key = {"time": float(time_text), "mode": "linear"}
                if isinstance(item, dict):
                    pre = item.get("pre", item.get("post"))
                    post = item.get("post", item.get("pre"))
                    key["pre"] = parse_value(pre, warnings)
                    key["post"] = parse_value(post, warnings)
                    key["mode"] = str(item.get("lerp_mode", "linear")).lower()
                else:
                    value = parse_value(item, warnings)
                    key["pre"] = value
                    key["post"] = value
                keys.append(key)
            keys.sort(key=lambda k: k["time"])
            entry[channel] = {"constant": None, "keys": keys}
        bones[bone_name] = entry

    rates = []
    collect_rates(doc.get("bones", {}), rates)
    length = doc.get("animation_length")
    unbounded = False
    period = 0.0
    if length is None:
        length = 0.0
        for entry in bones.values():
            for channel in entry.values():
                if channel and channel["keys"]:
                    length = max(length, channel["keys"][-1]["time"])
        if length <= 0:
            unbounded = True
            period = estimate_period(rates)
    loop = doc.get("loop", False)
    return {"name": name, "length": float(length), "loop": loop, "bones": bones,
            "has_length": "animation_length" in doc, "unbounded": unbounded, "period": period}


def effective_length(animation):
    """没有长度也没有关键帧的动画，TLM 里长度是无限的；有 anim_time 就按估出来的周期循环。"""
    if animation["length"] > 1e-6:
        return animation["length"]
    return animation["period"] if animation["period"] > 0 else 0.0


def parse_animation_file(path):
    with open(path, "r", encoding="utf-8-sig") as handle:
        doc = json.load(handle)
    warnings = []
    animations = {}
    for name, body in doc.get("animations", {}).items():
        animations[name] = parse_animation(name, body, warnings)
    return {"format": str(doc.get("format_version", "?")), "animations": animations, "warnings": warnings}


def sample_vector(keys, context):
    """按 TLM 的 KeyFrame 规则取某个通道在 context.anim_time 时的值。"""
    if keys is None:
        return None
    if not keys:
        return None
    time = context.anim_time
    if len(keys) == 1:
        return tuple(eval_value(c, context) for c in keys[0]["post"])
    if time <= keys[0]["time"]:
        return tuple(eval_value(c, context) for c in keys[0]["pre"])
    if time >= keys[-1]["time"]:
        return tuple(eval_value(c, context) for c in keys[-1]["post"])
    index = 0
    for i in range(len(keys) - 1):
        if keys[i]["time"] <= time < keys[i + 1]["time"]:
            index = i
            break
    begin = keys[index]
    end = keys[index + 1]
    span = end["time"] - begin["time"]
    percent = 0.0 if span <= 1e-9 else (time - begin["time"]) / span
    mode = end["mode"] if end["mode"] == "catmullrom" else begin["mode"]
    start = tuple(eval_value(c, context) for c in begin["post"])
    stop = tuple(eval_value(c, context) for c in end["pre"])
    if mode != "catmullrom":
        return tuple(start[i] + (stop[i] - start[i]) * percent for i in range(3))
    left = keys[max(0, index - 1)]
    right = keys[min(len(keys) - 1, index + 2)]
    l = tuple(eval_value(c, context) for c in left["post"])
    r = tuple(eval_value(c, context) for c in right["pre"])
    result = []
    for i in range(3):
        v0 = (stop[i] - l[i]) * 0.5
        v1 = (r[i] - start[i]) * 0.5
        t2 = percent * percent
        t3 = t2 * percent
        result.append((2 * start[i] - 2 * stop[i] + v0 + v1) * t3
                      + (-3 * start[i] + 3 * stop[i] - 2 * v0 - v1) * t2 + v0 * percent + start[i])
    return tuple(result)


def channel_value(channel, context, default):
    if channel is None:
        return default
    if channel["keys"] is None:
        return tuple(eval_value(c, context) for c in channel["constant"])
    value = sample_vector(channel["keys"], context)
    return default if value is None else value


# ------------------------------------------------------------------ 姿态采样
def bone_hierarchy(geometry):
    by_name = {}
    for bone in geometry["bones"]:
        by_name[bone["name"]] = bone
    paths = {}
    for bone in geometry["bones"]:
        parts = []
        current = bone
        guard = 0
        while current is not None and guard < 64:
            parts.append(current["name"])
            parent = current.get("parent")
            current = by_name.get(parent) if parent else None
            guard += 1
        paths[bone["name"]] = "/".join(reversed(parts))
    return by_name, paths


def sample_pose(animation, geometry, time, variables=None):
    """返回 {骨骼名: {"path","position","rotation","scale"}}，位置/旋转已经转成 Unity 坐标系。"""
    convert = bp.Convert()
    by_name, paths = bone_hierarchy(geometry)
    context = MolangContext(time, variables)
    pose = {}
    for bone in geometry["bones"]:
        name = bone["name"]
        entry = animation["bones"].get(name)
        pivot = bone.get("pivot", [0, 0, 0])
        parent_pivot = [0, 0, 0]
        parent_name = bone.get("parent")
        if parent_name and parent_name in by_name:
            parent_pivot = by_name[parent_name].get("pivot", [0, 0, 0])
        rest_euler = bone.get("rotation", [0, 0, 0])
        euler = list(rest_euler)
        offset = [0.0, 0.0, 0.0]
        scale = [1.0, 1.0, 1.0]
        if entry is not None:
            animated = channel_value(entry["rotation"], context, (0.0, 0.0, 0.0))
            euler = [rest_euler[i] + animated[i] for i in range(3)]
            offset = list(channel_value(entry["position"], context, (0.0, 0.0, 0.0)))
            scale = list(channel_value(entry["scale"], context, (1.0, 1.0, 1.0)))
        delta = [pivot[i] - parent_pivot[i] + offset[i] for i in range(3)]
        pose[name] = {
            "path": paths[name],
            "position": convert.point(delta),
            "rotation": convert.rotation(euler[0], euler[1], euler[2]),
            "scale": tuple(scale),
            "euler": tuple(euler),
            "offset": tuple(offset),
        }
    return pose


def sample_times(animation, rate=30.0):
    length = max(effective_length(animation), 1.0 / rate)
    dense_end = dense_range(animation, length)
    times = {0.0, length, dense_end}
    steps = int(math.ceil(dense_end * rate))
    for i in range(steps + 1):
        times.add(i / rate)
    for entry in animation["bones"].values():
        for channel in entry.values():
            if channel and channel["keys"]:
                for key in channel["keys"]:
                    times.add(key["time"])
    return sorted(t for t in times if -1e-6 <= t <= length + 1e-6)


def dense_range(animation, length):
    """需要加密采样的区间。关键帧之后的值是常量，没必要再按 30fps 铺点
    （否则 hold_mainhand:sword 这种 animation_length=1000 的动画会烘出三万帧）。"""
    dense_end = 0.0
    for entry in animation["bones"].values():
        for channel in entry.values():
            if not channel:
                continue
            if channel["keys"] is None:
                if channel["constant"] and any(isinstance(c, tuple) for c in channel["constant"]):
                    return length
                continue
            if channel["keys"]:
                dense_end = max(dense_end, channel["keys"][-1]["time"])
    return min(dense_end, length)


def matrix_to_quaternion(m):
    trace = m[0][0] + m[1][1] + m[2][2]
    if trace > 0:
        s = math.sqrt(trace + 1.0) * 2
        return ((m[2][1] - m[1][2]) / s, (m[0][2] - m[2][0]) / s, (m[1][0] - m[0][1]) / s, 0.25 * s)
    if m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = math.sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2
        return (0.25 * s, (m[0][1] + m[1][0]) / s, (m[0][2] + m[2][0]) / s, (m[2][1] - m[1][2]) / s)
    if m[1][1] > m[2][2]:
        s = math.sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2
        return ((m[0][1] + m[1][0]) / s, 0.25 * s, (m[1][2] + m[2][1]) / s, (m[0][2] - m[2][0]) / s)
    s = math.sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2
    return ((m[0][2] + m[2][0]) / s, (m[1][2] + m[2][1]) / s, 0.25 * s, (m[1][0] - m[0][1]) / s)


def dump(animation, geometry, path, rate=30.0):
    times = sample_times(animation, rate)
    payload = {"animation": animation["name"], "length": animation["length"], "samples": []}
    for time in times:
        pose = sample_pose(animation, geometry, time)
        payload["samples"].append({
            "time": time,
            "bones": {
                name: {
                    "path": entry["path"],
                    "position": list(entry["position"]),
                    "rotation": list(matrix_to_quaternion(entry["rotation"])),
                    "scale": list(entry["scale"]),
                }
                for name, entry in pose.items()
            },
        })
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)
    return payload


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("animation")
    parser.add_argument("--model")
    parser.add_argument("--anim")
    parser.add_argument("--time", type=float)
    parser.add_argument("--bone")
    parser.add_argument("--dump")
    parser.add_argument("--rate", type=float, default=30.0)
    parser.add_argument("--summary", action="store_true")
    args = parser.parse_args()

    data = parse_animation_file(args.animation)
    print("format_version = " + data["format"] + "   动画数 = " + str(len(data["animations"])))

    if args.summary or args.anim is None:
        for name, animation in data["animations"].items():
            channels = sum(1 for entry in animation["bones"].values() for c in entry.values() if c)
            keys = sum(len(c["keys"]) for entry in animation["bones"].values() for c in entry.values() if c and c["keys"])
            note = "" if animation["has_length"] else "(按关键帧推出)"
            if animation["unbounded"]:
                note = "(无限长，静止姿势)" if animation["period"] <= 0 else "(无限长，按 anim_time 周期 %.2f 秒烘)" % animation["period"]
            print("  %-26s 长度=%.4f%s 循环=%s 骨骼=%d 通道=%d 关键帧=%d 采样=%d"
                  % (name, animation["length"], note,
                     animation["loop"], len(animation["bones"]), channels, keys,
                     len(sample_times(animation, 30.0))))
        for warning in data["warnings"]:
            print("  警告: " + warning)
        if args.dump is None:
            return

    if args.anim is not None and args.dump is None and args.model is None:
        animation = data["animations"][args.anim]
        for name, entry in animation["bones"].items():
            print("  " + name + ": " + ", ".join(c for c in ("rotation", "position", "scale") if entry[c]))
        return

    if args.model is None:
        print("--dump 需要同时给 --model")
        return

    geometry = bp.parse_geometry(bp.load_json(args.model))
    animation = data["animations"][args.anim]
    if args.dump:
        payload = dump(animation, geometry, args.dump, args.rate)
        print("已写出 " + args.dump + "：%d 个时间点 x %d 骨骼" % (len(payload["times"]), len(payload["bones"][repr(payload["times"][0])])))
        return

    time = 0.0 if args.time is None else args.time
    pose = sample_pose(animation, geometry, time)
    names = [args.bone] if args.bone else list(pose.keys())
    for name in names:
        entry = pose[name]
        print("%s  path=%s" % (name, entry["path"]))
        print("   euler=%s  offset=%s  scale=%s" % (
            tuple(round(v, 4) for v in entry["euler"]),
            tuple(round(v, 4) for v in entry["offset"]),
            tuple(round(v, 4) for v in entry["scale"])))
        print("   unity pos=%s  quat=%s" % (
            tuple(round(v, 5) for v in entry["position"]),
            tuple(round(v, 5) for v in matrix_to_quaternion(entry["rotation"]))))


if __name__ == "__main__":
    sys.exit(main())
