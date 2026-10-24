using System.Collections.Generic;
using MaidHome.Core.Json;
using MaidHome.Core.Settings;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    public static class MaidAiExpressionTool
    {
        public const string ToolName = "play_maid_expression";

        static readonly Dictionary<string, string> ClipByChinese = new Dictionary<string, string>
        {
            { "普通睡觉", "sleep" },
            { "普通胜利", "game_win" },
            { "普通失败", "game_lost" },
            { "普通招手", "come" },
            { "普通坐下", "sit" },
            { "向右偏头", "animation.wine_fox.lean_look_right" },
            { "向左偏头", "animation.wine_fox.lean_look_left" },
            { "震惊疑惑", "!??!" },
            { "整理尾巴", "CLEANTAIL" },
            { "输了游戏", "game_lost2" },
            { "安静睡着", "sleep2" },
            { "赢了游戏", "game_win2" },
            { "继续撒娇", "morebeg" },
            { "咬尾巴", "eattail" },
            { "磕头认错", "kowtow" },
            { "溺水挣扎", "drowning" },
            { "尾巴画圈", "tailcircle" },
            { "转圈跳舞", "circledance" },
            { "抱抱", "hugtogether" },
            { "刚睡醒", "situp" },
            { "祈祷", "pray" },
            { "偷偷靠近", "sneak2" },
            { "催促过来", "come2" },
            { "卖萌展示", "weidu" },
            { "开心大笑", "ha" },
            { "跳支舞", "dance1" },
            { "墓地怀念", "watchtombstone" },
            { "非常生气", "veryangry" },
            { "无语", "wuyu" },
            { "难过", "sosad" },
            { "挑衅", "provoke" },
            { "亲亲", "lips" },
            { "得意坏笑", "sneer" },
            { "头晕", "dizziness" },
            { "被悬挂", "hang" },
            { "情绪爆发", "kuang" },
            { "拒绝", "refuse" },
            { "女仆行礼", "maid_bow" },
            { "害怕后退", "fear_retreat_fall" },
            { "放松坐下", "sit2" },
            { "冷得发抖", "cold_hug_shiver" },
            { "熟睡一", "moresleep2" },
            { "熟睡二", "moresleep3" },
            { "熟睡三", "moresleep4" },
            { "熟睡四", "moresleep5" },
            { "熟睡五", "moresleep6" },
            { "生气踢人", "kick_butt" },
            { "正面飞踢", "kick_launch_front" },
            { "抖水", "water_shake" },
            { "撒娇请求二", "beg2" },
            { "撒娇请求三", "beg3" },
            { "糟糕心虚", "uhoh" },
            { "撒娇请求", "beg" },
            { "受击反应", "attacked" },
            { "捡起雪球", "pick_up_snowball" },
            { "被摸头", "pet_reaction" },
            { "被摸头停留", "pet_reaction_hold" },
            { "摸别人头", "pet_other_head" },
            { "摸头鼓励", "pet_other_head_raise" },
            { "受伤", "hurt" },
            { "尾巴被拉", "tailpull" },
            { "左耳被拉", "ear_pull_left" },
            { "右耳被拉", "ear_pull_right" },
            { "坠落死亡", "death_fall" },
            { "溺水死亡", "death_drown" },
            { "燃烧死亡", "death_burn" },
            { "远程死亡", "death_ranged" },
            { "重伤跪地", "injured_kneel" },
            { "倒地受伤", "ground_hurt" },
            { "断腿倒地", "fallen_broken_leg" },
            { "断腿爬行", "broken_leg_crawl" },
            { "左脸挨打", "slapleft" },
            { "右脸挨打", "slapright" },
            { "死亡", "death" }
        };

        public static bool TryResolve(string chineseName, out string clipName)
        {
            return ClipByChinese.TryGetValue(chineseName ?? "", out clipName);
        }

        public static string BuildToolsJson(MaidAgent agent)
        {
            if (!GameSettingsStore.Current.AiEnabled) return "";
            BedrockAnimationPlayer player = agent != null ? agent.GetComponent<BedrockAnimationPlayer>() : null;
            if (player == null) return "";

            List<JsonValue> available = new List<JsonValue>();
            foreach (string chineseName in ClipByChinese.Keys)
            {
                string clip;
                if (TryResolve(chineseName, out clip) && player.HasClip(clip))
                {
                    available.Add(JsonValue.FromString(chineseName));
                }
            }

            if (available.Count == 0) return "";
            Dictionary<string, JsonValue> property = new Dictionary<string, JsonValue>();
            property["type"] = JsonValue.FromString("string");
            property["enum"] = JsonValue.FromArray(available);
            property["description"] = JsonValue.FromString(
                "必须明确选择一个中文动画名；没有默认值。只控制表情，不改变任务、背包或移动状态。");

            Dictionary<string, JsonValue> properties = new Dictionary<string, JsonValue>();
            properties["animation"] = JsonValue.FromObject(property);

            Dictionary<string, JsonValue> parameters = new Dictionary<string, JsonValue>();
            parameters["type"] = JsonValue.FromString("object");
            parameters["properties"] = JsonValue.FromObject(properties);
            parameters["required"] = JsonValue.FromArray(new List<JsonValue>
            {
                JsonValue.FromString("animation")
            });
            parameters["additionalProperties"] = JsonValue.FromBool(false);

            Dictionary<string, JsonValue> function = new Dictionary<string, JsonValue>();
            function["name"] = JsonValue.FromString(ToolName);
            function["description"] = JsonValue.FromString(
                "播放当前女仆允许的 AI 可用动画。调用时必须提供 animation；未知名称或缺失参数会失败，且不会播放默认动画。");
            function["parameters"] = JsonValue.FromObject(parameters);

            Dictionary<string, JsonValue> tool = new Dictionary<string, JsonValue>();
            tool["type"] = JsonValue.FromString("function");
            tool["function"] = JsonValue.FromObject(function);

            return MiniJsonWriter.Write(JsonValue.FromArray(new List<JsonValue>
            {
                JsonValue.FromObject(tool)
            }));
        }
    }
}
