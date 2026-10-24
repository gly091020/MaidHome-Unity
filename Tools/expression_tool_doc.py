import argparse
import pathlib
import re


PAIR = re.compile(r'\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    source = pathlib.Path(args.source).resolve()
    pairs = PAIR.findall(source.read_text(encoding="utf-8"))
    if not pairs:
        raise SystemExit("没有在 C# 映射里找到动画项")

    lines = [
        "# 女仆 AI 可用动画工具",
        "",
        "- 工具名：`play_maid_expression`",
        "- 参数：`animation`，必填，无默认值",
        f"- 仅允许以下 {len(pairs)} 个中文动画名",
        "- 缺失参数、未知名称或当前模型没有对应 clip 时，不播放任何动画",
        "",
        "## 参数映射",
        "",
        "| 中文参数 | 动画 ID |",
        "| --- | --- |",
    ]
    for chinese, clip in pairs:
        lines.append(f"| `{chinese}` | `{clip}` |")

    lines.extend([
        "",
        "## 调用示例",
        "",
        "```json",
        "{",
        '  "name": "play_maid_expression",',
        '  "arguments": {',
        '    "animation": "开心大笑"',
        "  }",
        "}",
        "```",
        "",
        "工具执行后会自动恢复到播放前的动画。重伤与死亡动作虽在白名单中，"
        "但只应在剧情明确需要时调用；武器和 TAC 动作仍不受此工具控制。",
        "",
    ])

    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines), encoding="utf-8")
    print(f"已生成 {output.resolve()}，共 {len(pairs)} 项")


if __name__ == "__main__":
    main()
