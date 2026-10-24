# 女仆 AI 可用动画工具

- 工具名：`play_maid_expression`
- 参数：`animation`，必填，无默认值
- 仅允许以下 74 个中文动画名
- 缺失参数、未知名称或当前模型没有对应 clip 时，不播放任何动画

## 参数映射

| 中文参数 | 动画 ID |
| --- | --- |
| `普通睡觉` | `sleep` |
| `普通胜利` | `game_win` |
| `普通失败` | `game_lost` |
| `普通招手` | `come` |
| `普通坐下` | `sit` |
| `向右偏头` | `animation.wine_fox.lean_look_right` |
| `向左偏头` | `animation.wine_fox.lean_look_left` |
| `震惊疑惑` | `!??!` |
| `整理尾巴` | `CLEANTAIL` |
| `输了游戏` | `game_lost2` |
| `安静睡着` | `sleep2` |
| `赢了游戏` | `game_win2` |
| `继续撒娇` | `morebeg` |
| `咬尾巴` | `eattail` |
| `磕头认错` | `kowtow` |
| `溺水挣扎` | `drowning` |
| `尾巴画圈` | `tailcircle` |
| `转圈跳舞` | `circledance` |
| `抱抱` | `hugtogether` |
| `刚睡醒` | `situp` |
| `祈祷` | `pray` |
| `偷偷靠近` | `sneak2` |
| `催促过来` | `come2` |
| `卖萌展示` | `weidu` |
| `开心大笑` | `ha` |
| `跳支舞` | `dance1` |
| `墓地怀念` | `watchtombstone` |
| `非常生气` | `veryangry` |
| `无语` | `wuyu` |
| `难过` | `sosad` |
| `挑衅` | `provoke` |
| `亲亲` | `lips` |
| `得意坏笑` | `sneer` |
| `头晕` | `dizziness` |
| `被悬挂` | `hang` |
| `情绪爆发` | `kuang` |
| `拒绝` | `refuse` |
| `女仆行礼` | `maid_bow` |
| `害怕后退` | `fear_retreat_fall` |
| `放松坐下` | `sit2` |
| `冷得发抖` | `cold_hug_shiver` |
| `熟睡一` | `moresleep2` |
| `熟睡二` | `moresleep3` |
| `熟睡三` | `moresleep4` |
| `熟睡四` | `moresleep5` |
| `熟睡五` | `moresleep6` |
| `生气踢人` | `kick_butt` |
| `正面飞踢` | `kick_launch_front` |
| `抖水` | `water_shake` |
| `撒娇请求二` | `beg2` |
| `撒娇请求三` | `beg3` |
| `糟糕心虚` | `uhoh` |
| `撒娇请求` | `beg` |
| `受击反应` | `attacked` |
| `捡起雪球` | `pick_up_snowball` |
| `被摸头` | `pet_reaction` |
| `被摸头停留` | `pet_reaction_hold` |
| `摸别人头` | `pet_other_head` |
| `摸头鼓励` | `pet_other_head_raise` |
| `受伤` | `hurt` |
| `尾巴被拉` | `tailpull` |
| `左耳被拉` | `ear_pull_left` |
| `右耳被拉` | `ear_pull_right` |
| `坠落死亡` | `death_fall` |
| `溺水死亡` | `death_drown` |
| `燃烧死亡` | `death_burn` |
| `远程死亡` | `death_ranged` |
| `重伤跪地` | `injured_kneel` |
| `倒地受伤` | `ground_hurt` |
| `断腿倒地` | `fallen_broken_leg` |
| `断腿爬行` | `broken_leg_crawl` |
| `左脸挨打` | `slapleft` |
| `右脸挨打` | `slapright` |
| `死亡` | `death` |

## 调用示例

```json
{
  "name": "play_maid_expression",
  "arguments": {
    "animation": "开心大笑"
  }
}
```

工具执行后会自动恢复到播放前的动画。重伤与死亡动作虽在白名单中，但只应在剧情明确需要时调用；武器和 TAC 动作仍不受此工具控制。
