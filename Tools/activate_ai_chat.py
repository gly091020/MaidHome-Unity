import argparse
import gzip
import json
import os
import pathlib
import re
import shutil
import textwrap


SETTING_BLOCK = re.compile(r"(?ms)^setting:\s*\|-\s*\n(?P<body>(?:^[ \t]+.*(?:\n|$))*)")
LANGUAGE = re.compile(r"(?m)^\s*language:\s*([^\s#]+)")


def read_preset(path):
    text = pathlib.Path(path).read_text(encoding="utf-8-sig")
    match = SETTING_BLOCK.search(text)
    if not match:
        raise ValueError("预设里没有 YAML block setting")
    setting = textwrap.dedent(match.group("body")).strip()
    if not setting:
        raise ValueError("预设的 setting 为空")
    language_match = LANGUAGE.search(text)
    language = language_match.group(1) if language_match else "zh_cn"
    return setting, language


def build_record(maid_dir, persona, language, model_id):
    maid = json.loads((maid_dir / "maid.json").read_text(encoding="utf-8-sig"))
    return {
        "schema_version": 1,
        "maid_uuid": maid_dir.name,
        "history_summary": "",
        "last_token_usage": 0,
        "base_message_count": 0,
        "llm_ref": {"site": "", "model": ""},
        "profile": {
            "maid_name": maid.get("name", ""),
            "owner_name": maid.get("owner_name", ""),
            "model_id": model_id,
            "chat_language": language,
            "custom_setting": persona,
            "resolved_system_prompt": persona,
            "generated_persona": "",
            "persona_source": "preset",
        },
        "messages": [],
    }


def write_compressed(record, target):
    data = json.dumps(record, ensure_ascii=False, indent=2).encode("utf-8")
    compressed = gzip.compress(data, compresslevel=9, mtime=0)
    if len(data) > 2 * 1024 * 1024 or len(compressed) > 2 * 1024 * 1024:
        raise ValueError("AI 聊天数据超过 2 MiB")

    temp = target.with_name(target.name + ".tmp")
    if target.exists():
        shutil.copy2(target, target.with_name(target.name + ".bak"))
    temp.write_bytes(compressed)
    os.replace(temp, target)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--maid-dir", required=True)
    parser.add_argument("--preset", required=True)
    parser.add_argument("--model-id", default="dazheng_winefox")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    maid_dir = pathlib.Path(args.maid_dir).resolve()
    if not (maid_dir / "maid.json").is_file():
        raise SystemExit(f"不是女仆存档目录: {maid_dir}")
    persona, language = read_preset(args.preset)
    record = build_record(maid_dir, persona, language, args.model_id)
    target = maid_dir / "ai_chat.json.gz"
    if args.dry_run:
        print(f"将写入 {target}")
        print(f"女仆: {record['profile']['maid_name']} / 人设 {len(persona)} 字 / 语言 {language}")
        return
    write_compressed(record, target)
    print(f"已写入 {target}")
    print(f"女仆: {record['profile']['maid_name']} / 人设 {len(persona)} 字")


if __name__ == "__main__":
    main()
