using System;
using System.Collections.Generic;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>TLM 女仆语音事件 id。播放时也可以直接传这些字符串，或传 "idle" 这种短名。</summary>
    public static class MaidSoundId
    {
        public const string Idle = "maid.mode.idle";
        public const string Attack = "maid.mode.attack";
        public const string RangeAttack = "maid.mode.range_attack";
        public const string DanmakuAttack = "maid.mode.danmaku_attack";
        public const string Farm = "maid.mode.farm";
        public const string Feed = "maid.mode.feed";
        public const string Shears = "maid.mode.shears";
        public const string Milk = "maid.mode.milk";
        public const string Torch = "maid.mode.torch";
        public const string FeedAnimal = "maid.mode.feed_animal";
        public const string Extinguishing = "maid.mode.extinguishing";
        public const string RemoveSnow = "maid.mode.snow";
        public const string Break = "maid.mode.break";
        public const string Furnace = "maid.mode.furnace";
        public const string Brewing = "maid.mode.brewing";

        public const string FindTarget = "maid.ai.find_target";
        public const string Hurt = "maid.ai.hurt";
        public const string HurtFire = "maid.ai.hurt_fire";
        public const string HurtPlayer = "maid.ai.hurt_player";
        public const string Tamed = "maid.ai.tamed";
        public const string ItemGet = "maid.ai.item_get";
        public const string Death = "maid.ai.death";
        public const string GameWin = "maid.ai.game_win";
        public const string GameLost = "maid.ai.game_lost";

        public const string Cold = "maid.environment.cold";
        public const string Hot = "maid.environment.hot";
        public const string Rain = "maid.environment.rain";
        public const string Snow = "maid.environment.snow";
        public const string Morning = "maid.environment.morning";
        public const string Night = "maid.environment.night";

        public const string Credit = "maid.credit";

        static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            { "idle", Idle },
            { "attack", Attack },
            { "range_attack", RangeAttack },
            { "danmaku_attack", DanmakuAttack },
            { "farm", Farm },
            { "feed", Feed },
            { "shears", Shears },
            { "milk", Milk },
            { "torch", Torch },
            { "feed_animal", FeedAnimal },
            { "extinguishing", Extinguishing },
            { "snow", RemoveSnow },
            { "break", Break },
            { "furnace", Furnace },
            { "brewing", Brewing },
            { "find_target", FindTarget },
            { "hurt", Hurt },
            { "hurt_fire", HurtFire },
            { "hurt_player", HurtPlayer },
            { "tamed", Tamed },
            { "item_get", ItemGet },
            { "death", Death },
            { "game_win", GameWin },
            { "game_lost", GameLost },
            { "cold", Cold },
            { "hot", Hot },
            { "rain", Rain },
            { "morning", Morning },
            { "night", Night },
            { "credit", Credit },
        };

        static readonly Dictionary<string, string> Fallbacks = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            { RangeAttack, Attack },
            { DanmakuAttack, Attack },
            { Farm, Idle },
            { Feed, Idle },
            { Shears, Idle },
            { Milk, Idle },
            { Torch, Idle },
            { FeedAnimal, Idle },
            { Extinguishing, Idle },
            { RemoveSnow, Idle },
            { Break, Idle },
            { Furnace, Idle },
            { Brewing, Idle },
            { HurtFire, Hurt },
        };

        public static string Normalize(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "";
            }

            string trimmed = id.Trim();
            if (trimmed.IndexOf('.') >= 0)
            {
                return trimmed.ToLowerInvariant();
            }

            string full;
            return ShortNames.TryGetValue(trimmed, out full) ? full : "maid.mode." + trimmed.ToLowerInvariant();
        }

        public static string Fallback(string id)
        {
            string fallback;
            return !string.IsNullOrEmpty(id) && Fallbacks.TryGetValue(id, out fallback) ? fallback : null;
        }
    }
}
