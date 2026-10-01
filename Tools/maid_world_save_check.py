#!/usr/bin/env python3
"""检查 MaidHome 的 slot0.json：女仆背包状态、位置和对应 maid.json 是否存在。"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path


def default_save_path() -> Path:
    documents = Path(os.environ.get("USERPROFILE", str(Path.home()))) / "Documents"
    return documents / "MaidHome" / "saves" / "slot0.json"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("save", nargs="?", type=Path, default=default_save_path())
    parser.add_argument("--maid-root", type=Path, default=None)
    args = parser.parse_args()

    save_path = args.save
    maid_root = args.maid_root or save_path.parent / "maid"

    if not save_path.is_file():
        print(f"[错误] 找不到存档: {save_path}")
        return 1

    try:
        data = json.loads(save_path.read_text(encoding="utf-8"))
    except Exception as error:
        print(f"[错误] JSON 解析失败: {error}")
        return 1

    known_ids = set()
    if maid_root.is_dir():
        for folder in maid_root.iterdir():
            if folder.is_dir() and (folder / "maid.json").is_file():
                known_ids.add(folder.name)

    errors: list[str] = []
    records = data.get("maids")
    if not isinstance(records, list):
        errors.append("maids 不是数组")
        records = []

    seen: set[str] = set()
    print(f"存档: {save_path}")
    print(f"version: {data.get('version', '(缺失)')}  house_id: {data.get('house_id', '') or '(空)'}")
    print(f"女仆记录: {len(records)}  本地 maid 文件夹: {len(known_ids)}")
    print()

    for index, record in enumerate(records):
        if not isinstance(record, dict):
            errors.append(f"[{index}] 不是对象")
            continue

        maid_id = str(record.get("id", ""))
        if not maid_id:
            errors.append(f"[{index}] id 为空")
            continue

        if maid_id in seen:
            errors.append(f"{maid_id}: id 重复")
        seen.add(maid_id)

        if known_ids and maid_id not in known_ids:
            errors.append(f"{maid_id}: 找不到对应的 saves/maid/{maid_id}/maid.json")

        in_bag = bool(record.get("in_bag", True))
        house_id = str(record.get("house_id", ""))
        position = (
            float(record.get("x", 0.0)),
            float(record.get("y", 0.0)),
            float(record.get("z", 0.0)),
        )
        print(
            f"{maid_id}: {'在背包' if in_bag else '已放置'}"
            f"  house={house_id or '(空)'}"
            f"  pos=({position[0]:.3f}, {position[1]:.3f}, {position[2]:.3f})"
            f"  yaw={float(record.get('rotation_y', 0.0)):.1f}"
        )

    for maid_id in sorted(known_ids - seen):
        print(f"{maid_id}: 存档里没有记录，首次进入游戏时会按“在背包”处理")

    if errors:
        print()
        print("发现问题:")
        for error in errors:
            print(f"  {error}")
        return 1

    print()
    print("检查通过")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
