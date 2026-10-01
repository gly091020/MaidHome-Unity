"""MC 风格资源的静态一致性检查（不需要 Unity）：

1. .meta 里的 guid 是否有重复
2. 场景里 m_SkyboxMaterial / GraphicsSettings 里引用的 guid 是否存在，类型对不对
3. 每个 .mat 引用的 shader 是否存在
4. .mat 里存的属性名 shader 是否声明过（写错了 Unity 只会警告然后丢掉）
5. shader 的括号是否配平

用法: python Tools/mc_style_check.py
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def collect_guids():
    guid_to_path = {}
    duplicates = []
    for folder, _, files in os.walk(os.path.join(ROOT, "Assets")):
        for name in files:
            if not name.endswith(".meta"):
                continue
            meta_path = os.path.join(folder, name)
            match = re.search(r"^guid: ([0-9a-f]{32})$", read(meta_path), re.M)
            if not match:
                continue
            guid = match.group(1)
            asset_path = meta_path[: -len(".meta")]
            if guid in guid_to_path:
                duplicates.append((guid, guid_to_path[guid], asset_path))
            else:
                guid_to_path[guid] = asset_path
    return guid_to_path, duplicates


def check_references(guid_to_path, errors):
    targets = [
        os.path.join(ROOT, "Assets", "Scenes", "Main.unity"),
        os.path.join(ROOT, "Assets", "Scenes", "SampleScene.unity"),
    ]
    pattern = re.compile(r"guid: ([0-9a-f]{32}), type: (\d+)")

    for scene in targets:
        if not os.path.isfile(scene):
            continue
        text = read(scene)
        for match in pattern.finditer(text):
            guid, kind = match.group(1), match.group(2)
            if guid.startswith("0000000000000000"):
                continue  # Unity 内置资源
            if guid not in guid_to_path:
                errors.append("%s 引用了不存在的 guid %s" % (os.path.basename(scene), guid))
            elif kind == "2" and not guid_to_path[guid].endswith(".mat"):
                errors.append("%s 期望材质但 guid 指向 %s" % (os.path.basename(scene), guid_to_path[guid]))

    graphics = os.path.join(ROOT, "ProjectSettings", "GraphicsSettings.asset")
    for match in pattern.finditer(read(graphics)):
        guid = match.group(1)
        if guid.startswith("0000000000000000"):
            continue
        if guid not in guid_to_path:
            errors.append("GraphicsSettings 引用了不存在的 guid %s" % guid)


def parse_shader_properties(text):
    # Properties { ... } 里每条声明的第一个标识符，跳过嵌套的 {} 默认值
    block = re.search(r"\bProperties\s*\{(.*?)\n\s*\}", text, re.S)
    if not block:
        return set()
    names = set()
    depth = 0
    for line in block.group(1).splitlines():
        stripped = line.strip()
        # 属性可以带 [Enum(...)] / [Range] 之类的前缀
        stripped = re.sub(r"^(\[[^\]]*\]\s*)+", "", stripped)
        if depth == 0:
            match = re.match(r"(_\w+)\s*\(", stripped)
            if match:
                names.add(match.group(1))
        depth += stripped.count("{") - stripped.count("}")
    return names


def check_materials(guid_to_path, errors):
    for folder, _, files in os.walk(os.path.join(ROOT, "Assets")):
        for name in files:
            if not name.endswith(".mat"):
                continue
            path = os.path.join(folder, name)
            text = read(path)
            rel = os.path.relpath(path, ROOT)

            shader_match = re.search(r"m_Shader: \{fileID: \d+, guid: ([0-9a-f]{32}), type: 3\}", text)
            if not shader_match:
                errors.append("%s 没有指向自定义 shader" % rel)
                continue
            shader_guid = shader_match.group(1)
            shader_path = guid_to_path.get(shader_guid)
            if not shader_path or not shader_path.endswith(".shader"):
                errors.append("%s 的 shader guid 找不到" % rel)
                continue

            shader_text = read(shader_path)
            declared = parse_shader_properties(shader_text)

            saved = set()
            for section in ("m_TexEnvs", "m_Floats", "m_Colors"):
                block = re.search(r"\n    %s:(.*?)(?=\n    m_\w+:|\Z)" % section, text, re.S)
                if not block:
                    continue
                saved.update(re.findall(r"^\s*- (_\w+):", block.group(1), re.M))

            for prop in sorted(saved - declared):
                errors.append("%s 存的属性 %s 在 %s 里没声明" % (rel, prop, os.path.basename(shader_path)))
            if "--notes" in sys.argv:
                for prop in sorted(declared - saved):
                    print("  note: %s 的 %s 没存，用 shader 默认值" % (rel, prop))


def check_shaders(guid_to_path, errors):
    for folder, _, files in os.walk(os.path.join(ROOT, "Assets")):
        for name in files:
            if not name.endswith(".shader"):
                continue
            path = os.path.join(folder, name)
            text = read(path)
            rel = os.path.relpath(path, ROOT)
            if text.count("{") != text.count("}"):
                errors.append("%s 大括号不配平" % rel)
            for macro in ("CGPROGRAM", "ENDCG"):
                if not re.search(r"\b%s\b" % macro, text):
                    errors.append("%s 缺 %s" % (rel, macro))
            if text.count("CGPROGRAM") != text.count("ENDCG"):
                errors.append("%s CGPROGRAM / ENDCG 不成对" % rel)
            if not re.search(r'Shader\s+"[^"]+"', text):
                errors.append("%s 缺 Shader 声明" % rel)
            for include in re.findall(r'#include\s+"([^"]+)"', text):
                if not re.match(r"^[A-Za-z0-9_]+\.cginc$", include):
                    errors.append("%s 的 include 写法可疑: %s" % (rel, include))


def check_scripts(errors):
    # 只查括号配平，真正的编译由用户在 Unity 里做。
    # 必须先剥掉注释和字符串字面量，否则字符串里的花括号会被算进来。
    for folder, _, files in os.walk(os.path.join(ROOT, "Assets", "Scripts")):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(folder, name)
            text = strip_literals(read(path))
            if text.count("{") != text.count("}"):
                errors.append("%s 大括号不配平" % os.path.relpath(path, ROOT))
            if text.count("(") != text.count(")"):
                errors.append("%s 小括号不配平" % os.path.relpath(path, ROOT))


def strip_literals(text):
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            end = text.find("\n", i)
            i = n if end < 0 else end
        elif c == "/" and i + 1 < n and text[i + 1] == "*":
            end = text.find("*/", i + 2)
            i = n if end < 0 else end + 2
        elif c == "@" and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
        elif c == '"' or c == "'":
            quote = c
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == quote:
                    i += 1
                    break
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def main():
    errors = []
    guid_to_path, duplicates = collect_guids()

    for guid, first, second in duplicates:
        errors.append("guid 重复 %s: %s / %s" % (guid, first, second))

    print("资源 guid 共 %d 个" % len(guid_to_path))
    check_references(guid_to_path, errors)
    check_materials(guid_to_path, errors)
    check_shaders(guid_to_path, errors)
    check_scripts(errors)

    if errors:
        print("\n发现问题:")
        for item in errors:
            print("  " + item)
        return 1

    print("检查通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
