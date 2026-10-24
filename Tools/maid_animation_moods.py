import argparse
import json
import pathlib
import re


ROW = re.compile(r"^\|\s*`([^`]+)`\s*\|(.*)\|\s*$")
PAIR = re.compile(r'\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}')

BASE_ROWS = {
    "sleep": ("困倦、睡眠", "基础睡眠循环，是 sleep2 和 moresleep 系列的普通版本"),
    "game_win": ("开心、胜利", "基础胜利姿势，是 game_win2 的普通版本"),
    "game_lost": ("失落、失败", "基础失败姿势，是 game_lost2 的普通版本"),
    "come": ("期待、招呼", "普通招手动作，是 come2 的基础版本"),
    "sit": ("放松、休息", "基础坐姿，是 sit2 的普通版本"),
}

CATEGORY_ORDER = [
    "普通版与基础动作",
    "开心与展示",
    "害羞、亲近与请求",
    "低落、生气与害怕",
    "困惑、吐槽与调皮",
    "睡眠与休息",
    "玩耍、身体反应与动作",
    "受击与疼痛",
    "重伤与死亡",
]

CATEGORY = {
    "sleep": "普通版与基础动作",
    "game_win": "普通版与基础动作",
    "game_lost": "普通版与基础动作",
    "come": "普通版与基础动作",
    "sit": "普通版与基础动作",
    "game_win2": "开心与展示",
    "circledance": "开心与展示",
    "ha": "开心与展示",
    "dance1": "开心与展示",
    "kuang": "开心与展示",
    "maid_bow": "害羞、亲近与请求",
    "morebeg": "害羞、亲近与请求",
    "beg": "害羞、亲近与请求",
    "beg2": "害羞、亲近与请求",
    "beg3": "害羞、亲近与请求",
    "kowtow": "害羞、亲近与请求",
    "hugtogether": "害羞、亲近与请求",
    "lips": "害羞、亲近与请求",
    "weidu": "害羞、亲近与请求",
    "pet_reaction": "害羞、亲近与请求",
    "pet_reaction_hold": "害羞、亲近与请求",
    "pet_other_head": "害羞、亲近与请求",
    "pet_other_head_raise": "害羞、亲近与请求",
    "game_lost2": "低落、生气与害怕",
    "veryangry": "低落、生气与害怕",
    "wuyu": "低落、生气与害怕",
    "sosad": "低落、生气与害怕",
    "watchtombstone": "低落、生气与害怕",
    "refuse": "低落、生气与害怕",
    "fear_retreat_fall": "低落、生气与害怕",
    "cold_hug_shiver": "低落、生气与害怕",
    "uhoh": "低落、生气与害怕",
    "drowning": "低落、生气与害怕",
    "hang": "低落、生气与害怕",
    "animation.wine_fox.lean_look_right": "困惑、吐槽与调皮",
    "animation.wine_fox.lean_look_left": "困惑、吐槽与调皮",
    "!??!": "困惑、吐槽与调皮",
    "CLEANTAIL": "困惑、吐槽与调皮",
    "sneak2": "困惑、吐槽与调皮",
    "provoke": "困惑、吐槽与调皮",
    "sneer": "困惑、吐槽与调皮",
    "dizziness": "困惑、吐槽与调皮",
    "pray": "困惑、吐槽与调皮",
    "situp": "睡眠与休息",
    "sleep2": "睡眠与休息",
    "sit2": "睡眠与休息",
    "moresleep2": "睡眠与休息",
    "moresleep3": "睡眠与休息",
    "moresleep4": "睡眠与休息",
    "moresleep5": "睡眠与休息",
    "moresleep6": "睡眠与休息",
    "eattail": "玩耍、身体反应与动作",
    "tailcircle": "玩耍、身体反应与动作",
    "come2": "玩耍、身体反应与动作",
    "kick_butt": "玩耍、身体反应与动作",
    "kick_launch_front": "玩耍、身体反应与动作",
    "water_shake": "玩耍、身体反应与动作",
    "pick_up_snowball": "玩耍、身体反应与动作",
    "attacked": "受击与疼痛",
    "hurt": "受击与疼痛",
    "tailpull": "受击与疼痛",
    "ear_pull_left": "受击与疼痛",
    "ear_pull_right": "受击与疼痛",
    "ground_hurt": "受击与疼痛",
    "slapleft": "受击与疼痛",
    "slapright": "受击与疼痛",
    "death_fall": "重伤与死亡",
    "death_drown": "重伤与死亡",
    "death_burn": "重伤与死亡",
    "death_ranged": "重伤与死亡",
    "injured_kneel": "重伤与死亡",
    "fallen_broken_leg": "重伤与死亡",
    "broken_leg_crawl": "重伤与死亡",
    "death": "重伤与死亡",
}


def read_existing(path):
    rows = {}
    if not path.exists():
        return rows
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        match = ROW.match(line)
        if match:
            clip = match.group(1)
            cells = [cell.strip() for cell in match.group(2).split("|")]
            if len(cells) == 4:
                mood, explanation = cells[0], cells[1]
            elif len(cells) == 5:
                mood, explanation = cells[1], cells[2]
            else:
                continue
            rows[clip] = (mood, explanation)
    return rows


def loop_text(value):
    if value is True:
        return "循环"
    if value == "hold_on_last_frame":
        return "停在末帧"
    if value is False:
        return "单次"
    return "—"


def duration_text(node):
    value = node.get("animation_length")
    return f"{value:g}s" if isinstance(value, (int, float)) else "—"


def build(source_doc, animation_json, tool_source):
    rows = read_existing(source_doc)
    for clip, (mood, explanation) in BASE_ROWS.items():
        rows.setdefault(clip, (mood, explanation))

    data = json.loads(animation_json.read_text(encoding="utf-8-sig"))
    animations = data.get("animations", {})
    pairs = PAIR.findall(tool_source.read_text(encoding="utf-8"))
    chinese_by_clip = {clip: chinese for chinese, clip in pairs}
    missing_tool = sorted(clip for clip in rows if clip not in chinese_by_clip)
    missing_animation = sorted(clip for clip in rows if clip not in animations)
    if missing_tool or missing_animation:
        raise SystemExit(
            f"映射不一致: missing_tool={missing_tool}, missing_animation={missing_animation}"
        )

    grouped = {name: [] for name in CATEGORY_ORDER}
    for clip, (mood, explanation) in rows.items():
        node = animations[clip]
        group = CATEGORY.get(clip, "困惑、吐槽与调皮")
        grouped[group].append(
            (clip, chinese_by_clip[clip], mood, explanation, loop_text(node.get("loop")),
             duration_text(node))
        )

    lines = [
        "# 更多动画-大正女仆酒狐：AI 可用动画与心情",
        "",
        f"- 当前可调用动画：**{len(rows)}**",
        "- AI 工具：`play_maid_expression`",
        "- 参数：`animation`，必填，无默认值",
        "- 下表中文参数与工具白名单同步；普通版动画也已加入可调用范围。",
        "",
        "> 心情与用途按你当前维护的文档重新分组；受伤、重伤和死亡动画虽然可调用，",
        "> 但对话中只应在剧情明确需要时使用。",
        "",
    ]

    for group in CATEGORY_ORDER:
        items = grouped[group]
        if not items:
            continue
        lines.extend([
            f"## {group}（{len(items)}）",
            "",
            "| 动画 | 中文参数 | 可能心情 | 解释/用途 | Loop | 时长 |",
            "| --- | --- | --- | --- | --- | --- |",
        ])
        for clip, chinese, mood, explanation, loop, duration in items:
            lines.append(
                f"| `{clip}` | `{chinese}` | {mood} | {explanation} | {loop} | {duration} |"
            )
        lines.append("")

    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--document", required=True)
    parser.add_argument("--animation-json", required=True)
    parser.add_argument("--tool-source", required=True)
    args = parser.parse_args()

    source = pathlib.Path(args.document)
    text = build(source, pathlib.Path(args.animation_json), pathlib.Path(args.tool_source))
    temporary = source.with_name(source.name + ".tmp")
    temporary.write_text(text, encoding="utf-8")
    temporary.replace(source)
    rows = sum(1 for line in text.splitlines() if line.startswith("| `"))
    print(f"已重写 {source.resolve()}，共 {rows} 行动画")


if __name__ == "__main__":
    main()
