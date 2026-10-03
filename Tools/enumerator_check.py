"""找出协程（返回 IEnumerator 的方法）里非法的裸 `return;`，不需要 Unity。

迭代器块里只能 `yield return` / `yield break`，写 `return;` 编译器直接报错
（踩过：MaidManager.RestoreMaidsRoutine）。这个检查先把注释和字符串剥掉，
再按大括号配对圈出每个 IEnumerator 方法的方法体。

用法: python Tools/enumerator_check.py
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SELFTEST = """
class A {
    IEnumerator Bad() { if (x) { return; } yield return null; }
    IEnumerator Good() {
        // return;
        string s = "return;";
        yield break;
    }
    void Normal() { return; }
}
"""


def strip_code(text):
    """去掉注释和字符串字面量，保留换行以便定位。"""
    out = []
    i = 0
    length = len(text)
    while i < length:
        c = text[i]
        if c == '/' and i + 1 < length and text[i + 1] == '/':
            while i < length and text[i] != '\n':
                i += 1
            continue
        if c == '/' and i + 1 < length and text[i + 1] == '*':
            i += 2
            while i + 1 < length and not (text[i] == '*' and text[i + 1] == '/'):
                i += 1
            i += 2
            continue
        if c == '"':
            i += 1
            while i < length:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == "'":
            i += 1
            while i < length:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def enumerator_bodies(text):
    for match in re.finditer(r"\bIEnumerator\s+(?P<name>\w+)\s*\(", text):
        brace = text.find("{", match.end())
        if brace < 0:
            continue
        depth = 0
        for i in range(brace, len(text)):
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
                if depth == 0:
                    yield match.group("name"), brace, i
                    break


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def selftest():
    """确认这个检查真的抓得到裸 return;（不然它只是个摆设）。"""
    text = strip_code(SELFTEST)
    hits = []
    names = []
    for method, start, end in enumerator_bodies(text):
        names.append(method)
        if re.search(r"\breturn\s*;", text[start:end]):
            hits.append(method)

    print("自测识别到协程: " + ", ".join(names))
    print("自测报出裸 return: " + (", ".join(hits) if hits else "(无)"))
    if hits != ["Bad"]:
        print("自测失败：应该只报 Bad")
        return 1

    print("自测通过：能抓到坏例子，注释/字符串里的 return 不算，普通方法的 return 不算")
    return 0


def main():
    if "--selftest" in sys.argv:
        return selftest()

    errors = []
    scanned = 0
    for base, _dirs, files in os.walk(os.path.join(ROOT, "Assets", "Scripts")):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(base, name)
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                raw = fh.read()
            text = strip_code(raw)
            for method, start, end in enumerator_bodies(text):
                scanned += 1
                body = text[start:end]
                for hit in re.finditer(r"\breturn\s*;", body):
                    errors.append("%s:%d %s() 里写了裸 return;（协程要用 yield break）"
                                  % (os.path.relpath(path, ROOT), line_of(text, start + hit.start()), method))

    print("扫了 %d 个协程方法" % scanned)
    if errors:
        print("\n发现问题:")
        for item in errors:
            print("  " + item)
        return 1

    print("检查通过：协程里没有裸 return;")
    return 0


if __name__ == "__main__":
    sys.exit(main())
