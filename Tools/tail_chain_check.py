"""按 moreanimation 的 TailGroups 规则，从基岩模型里算出尾巴链，跟我们 C# 的 MaidTailChain.Build 对照。

用法: python Tools/tail_chain_check.py <geo.json> [<geo.json> ...]
"""

import json
import re
import sys
from pathlib import Path

TAIL_RE = re.compile(r"^(tail|body_tail)[0-9]*$")
WB_RE = re.compile(r"^wb[0-9]*$")


def load_bones(path):
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    geometry = data["minecraft:geometry"][0]
    bones = []
    for b in geometry.get("bones", []):
        bones.append({
            "name": b.get("name", ""),
            "parent": b.get("parent") or "",
            "geometry": len(b.get("cubes", [])) > 0,
        })
    return bones


def is_tail(bone, by_name):
    name = bone["name"].lower()
    if TAIL_RE.match(name):
        return True
    if not WB_RE.match(name):
        return False
    node = bone
    seen = set()
    while node is not None and node["name"] not in seen:
        seen.add(node["name"])
        if node["name"].lower() == "fox":
            return True
        node = by_name.get(node["parent"])
    return False


def build(bones):
    by_name = {b["name"]: b for b in bones}
    children = {}
    for b in bones:
        if is_tail(b, by_name):
            children.setdefault(b["parent"], []).append(b["name"])

    groups = []
    for b in bones:
        if not is_tail(b, by_name):
            continue
        parent = by_name.get(b["parent"])
        if parent is not None and is_tail(parent, by_name) and len(children.get(b["parent"], [])) == 1:
            continue
        if len(children.get(b["name"], [])) > 1:
            continue

        chain = []
        nxt = b["name"]
        while nxt is not None and nxt not in chain:
            chain.append(nxt)
            kids = children.get(nxt, [])
            nxt = kids[0] if len(kids) == 1 else None

        if not any(by_name[n]["geometry"] for n in chain):
            continue
        groups.append((b["name"], chain))

    groups.sort(key=lambda g: g[0])
    return groups, by_name


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    for path in sys.argv[1:]:
        name = Path(path).name
        try:
            bones = load_bones(path)
        except Exception as exc:
            print(f"{name}: 解析失败 {exc}")
            continue

        groups, by_name = build(bones)
        print(f"===== {name}  骨骼 {len(bones)}")
        if not groups:
            print("  (没有可互动的尾巴)")
            continue
        for group_id, chain in groups:
            parts = []
            for bone in chain:
                tag = "有几何" if by_name[bone]["geometry"] else "空骨骼"
                parts.append(f"{bone}({tag})")
            print(f"  尾巴 id={group_id}  段数={len(chain)}")
            print(f"    {' > '.join(parts)}")
            print(f"    映射到段号: " + ", ".join(f"{b}={min(i, 6)}" for i, b in enumerate(chain)))

    return 0


if __name__ == "__main__":
    sys.exit(main())
