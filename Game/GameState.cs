// ClaudeNote: 0.6.0 从 When.cs 分出来：条件表达式要读的游戏状态（GameState）与全局旗标（Flags）。
//   When.cs 只剩纯解析与求值，离线测试（verification_when）用桩代替这里的两个类。
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace ProbablyExtendFramework
{
    /// <summary>旗标：mod 写、表格的 flag:名字 读。</summary>
    // ClaudeNote: 1.0.0 rc6 改存进存档位（Lune 2026-11-18：框架给个统一的 flag 存档位）。之前存在 ModData 文件里、所有存档共用，
    //   记不了某一局的剧情进度。现在是游戏给 mod 的存档数据（跟着存档走），键 = 本框架名下的 flag.<名字>：
    //   别的 mod 不引用框架也能写 —— ModHelper.SetModData("Lune.ProbablyExtendFramework", "flag.brood.clara.ep1", "accept")。
    //   框架只读不管：谁写的谁负责改、负责清。没读档时（标题界面）读到的都是空。
    public static class Flags
    {
        internal const string Prefix = "flag.";

        public static string Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try { return ModHelper.GetModData(Painter.ModId, Prefix + name, null); }
            catch (Exception) { return null; }
        }

        public static bool IsSet(string name) { string v = Get(name); return !string.IsNullOrEmpty(v) && v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase); }
        public static void Set(string name, bool on = true) { Set(name, on ? "1" : null); }
        public static void Set(string name, int value) { Set(name, value.ToString(CultureInfo.InvariantCulture)); }

        /// <summary>value 为 null = 清掉。改完当场通知框架重算（正显示着的立刻换）。</summary>
        public static void Set(string name, string value)
        {
            if (string.IsNullOrEmpty(name) || !PlayerStore.HasInstance) return;
            if (Get(name) == value) return;
            if (value == null) ModHelper.RemoveModData(Painter.ModId, Prefix + name);
            else ModHelper.SetModData(Painter.ModId, Prefix + name, value);
            Painter.Notify();
        }
    }

    /// <summary>条件要读的游戏状态。全部容错：不在店面里时给 0 / null。</summary>
    internal static class GameState
    {
        internal static int Day
        {
            get { try { return StoreStation.Instance != null ? StoreStation.Instance.dayCounter : 0; } catch (Exception) { return 0; } }
        }

        /// <summary>当前来客的派系（游戏原名 FACTION_…）；没有来客返回 null。</summary>
        internal static string VisitorFaction
        {
            get
            {
                try
                {
                    StoreClient c = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() : null;
                    return c == null ? null : FactionId(c.clientFaction ?? "");
                }
                catch (Exception) { return null; }
            }
        }

        internal static double Reputation(string factionId)
        {
            try
            {
                if (!PlayerStore.HasInstance || PlayerStore.Instance.storeReputations == null) return 0d;
                foreach (StoreReputation r in PlayerStore.Instance.storeReputations)
                    if (r != null && r.factionId == factionId) return r.GetReputationExact();
            }
            catch (Exception) { }
            return 0d;
        }

        /// <summary>短名 → 游戏派系名。认不出的原样大写返回（mod 自己的派系）。</summary>
        internal static string FactionId(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "lower": case "ll": case "faction_lower_level": return "FACTION_LOWER_LEVEL";
                case "upper": case "ul": case "faction_upper_level": return "FACTION_UPPER_LEVEL";
                case "sec": case "security": case "faction_security": return "FACTION_SECURITY";
                case "bm": case "blackmarket": case "faction_black_market": return "FACTION_BLACK_MARKET";
                case "rev": case "revolution": case "faction_revolution": return "FACTION_REVOLUTION";
                case "cartel": case "faction_cartel": return "FACTION_CARTEL";
                case "church": case "faction_church": return "FACTION_CHURCH";
                case "tourist": case "faction_tourist": return "FACTION_TOURIST";
                default: return (name ?? "").Trim().ToUpperInvariant();
            }
        }
    }
}
