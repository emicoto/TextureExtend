// ClaudeNote: 0.6.0 从 When.cs 分出来：条件表达式要读的游戏状态（GameState）与全局旗标（Flags）。
//   When.cs 只剩纯解析与求值，离线测试（verification_when）用桩代替这里的两个类。
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace ProbablyExtendFramework
{
    /// <summary>全局旗标：mod 写、表格的 flag:名字 读。存在 ModData（跨存档通用，不写进存档）。</summary>
    public static class Flags
    {
        private const string File = "Flags.json";
        private static Dictionary<string, string> values;
        private static bool dirty;

        private static Dictionary<string, string> All
        {
            get
            {
                if (values != null) return values;
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string text = ModHelper.ReadModDataFileText(Painter.ModId, File);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(text);
                        if (loaded != null) foreach (var kv in loaded) values[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex) { ProbablyExtendFramework.Entry.Warn("flags not read: " + ex.Message); }
                return values;
            }
        }

        public static bool IsSet(string name) { return name != null && All.TryGetValue(name, out string v) && v != "" && v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase); }
        public static string Get(string name) { return name != null && All.TryGetValue(name, out string v) ? v : null; }
        public static void Set(string name, bool on = true) { Set(name, on ? "1" : null); }
        public static void Set(string name, int value) { Set(name, value.ToString(CultureInfo.InvariantCulture)); }

        /// <summary>value 为 null = 清掉。写完不立刻存盘，入睡时或停用时一起存。</summary>
        public static void Set(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (value == null) { if (All.Remove(name)) dirty = true; return; }
            if (All.TryGetValue(name, out string old) && old == value) return;
            All[name] = value;
            dirty = true;
        }

        internal static void Save()
        {
            if (!dirty || values == null) return;
            dirty = false;
            try { ModHelper.WriteModDataFileText(Painter.ModId, File, JsonConvert.SerializeObject(values, Formatting.Indented)); }
            catch (Exception ex) { ProbablyExtendFramework.Entry.Warn("flags not saved: " + ex.Message); }
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
