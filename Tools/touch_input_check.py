"""输入统一化的静态检查（不需要 Unity）。

全工程的交互输入只允许走 Assets/Scripts/Core/Input/PointerInput.cs（触摸路子）。
别处再出现 Input.、鼠标消息函数、或者直接问 EventSystem 有没有压着 UI，都是漏网的旧写法。
Editor 目录不查——IMGUI 那套跟玩法交互无关。

用法: python Tools/touch_input_check.py
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ALLOWED = os.path.join("Assets", "Scripts", "Core", "Input", "PointerInput.cs")
SKIP_DIRS = (os.path.join("Assets", "Scripts", "Editor"),)

# (正则, 说明)
BANNED = [
    (r"(?<![\w])Input\s*\.", "直接读 Input，应该改用 PointerInput"),
    (r"\bOnMouse(Down|Up|Drag|Over|Enter|Exit)\b", "鼠标消息函数在触摸设备上不会触发"),
    (r"IsPointerOverGameObject", "问 EventSystem 缓存会踩 Update 顺序，应该用 PointerInput.IsOverUi"),
]


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


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
    scanned = 0
    for folder, _, files in os.walk(os.path.join(ROOT, "Assets", "Scripts")):
        rel_folder = os.path.relpath(folder, ROOT)
        if rel_folder.startswith(SKIP_DIRS):
            continue

        for name in files:
            if not name.endswith(".cs"):
                continue

            rel = os.path.relpath(os.path.join(folder, name), ROOT)
            if os.path.normpath(rel) == os.path.normpath(ALLOWED):
                continue

            scanned += 1
            text = strip_literals(read(os.path.join(folder, name)))
            for line_no, line in enumerate(text.splitlines(), 1):
                for pattern, reason in BANNED:
                    if re.search(pattern, line):
                        errors.append("%s:%d %s -- %s" % (rel, line_no, line.strip(), reason))

    print("扫了 %d 个脚本（跳过 %s）" % (scanned, ALLOWED))
    if errors:
        print("\n发现问题:")
        for item in errors:
            print("  " + item)
        return 1

    print("检查通过：输入都走 PointerInput")
    return 0


if __name__ == "__main__":
    sys.exit(main())
