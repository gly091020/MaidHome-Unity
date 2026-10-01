#!/usr/bin/env python3
"""按 TLM 的规则检查 saves/sounds/<packId> 的事件名和 OGG 文件。"""

from __future__ import annotations

import argparse
import re
from collections import defaultdict
from pathlib import Path


def event_id(relative: Path) -> str | None:
    parts = relative.parts
    if len(parts) < 3 or parts[0].lower() != "maid":
        return None
    category = parts[1].lower()
    name = re.sub(r"\d+$", "", relative.stem).lower()
    if not name:
        return None
    if category == "other":
        return f"maid.{name}"
    return f"maid.{category}.{name}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pack", type=Path, help="声音包目录，例如 saves/sounds/touhou_little_maid")
    args = parser.parse_args()

    root = args.pack
    if not root.is_dir():
        print(f"[错误] 找不到声音包目录: {root}")
        return 1

    events: dict[str, list[str]] = defaultdict(list)
    for file in root.rglob("*.ogg"):
        key = event_id(file.relative_to(root))
        if key:
            events[key].append(file.name)

    print(f"声音包: {root.name}")
    print(f"事件数: {len(events)}")
    for key in sorted(events):
        names = ", ".join(sorted(events[key]))
        print(f"  {key}: {len(events[key])} 条  [{names}]")

    expected = [
        "maid.mode.idle",
        "maid.mode.attack",
        "maid.ai.hurt",
        "maid.ai.hurt_player",
    ]
    missing = [key for key in expected if key not in events]
    if missing:
        print()
        print("缺少常用事件:")
        for key in missing:
            print(f"  {key}")
        return 1

    print()
    print("检查通过")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
