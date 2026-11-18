// ClaudeNote: 1.0.0 rc6 意向——“这张立绘 / 这种道具现在想要哪些标签”（Lune 2026-11-18：框架得有个晓得
//   “这些立绘现在应该用哪个差分、今天登场时应该用哪个版本”的处理接口；状态归各家 mod 管，框架只管选图与画图）。
//   存在哪：游戏给 mod 的存档数据（跟着存档走），本框架名下：
//     want.portrait.<立绘ID> / want.item.<道具ID>   值 = 空格分隔的标签，末尾可带 @sleep（到入睡为止）或 @visit（到来客换人为止）；不带 = 一直有效
//     ban                                           值 = 空格分隔的标签：带这些标签的差分一律不参加挑选
//     flag.<名字>                                   见 Flags（条件表达式 flag:名字 读）
//   谁写：别的 mod。不引用框架也能写（ModHelper.SetModData("Lune.ProbablyExtendFramework", "want.portrait.clara", "ep1 accept")），
//     那样要等下一个检测点才生效；引用框架的调 Painter.Want / Flags.Set / Painter.Notify，当场生效。
//   框架只读（除了到期的 @sleep / @visit 由框架清）。不轮询：读出来的结果按“这一轮”缓存，检测点和 Notify 时作废。
//   环境标签（不存档，框架自己按游戏事件给）：
//     道具：现在的时间段 —— openshop（开店到关店）、endshop（关店到入睡）、wakeup（起床到开店）。
//     柜台上的来客：visit（登场）、sold / bought（成交；带 bonus / malus = 给了折扣 / 加了价）、leave（离开）。到下一个事件或客人离开为止。
//   道具 / 立绘带着名字或标签对得上的差分，就自动换过去（VariantTags.Best）。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    internal static class Wants
    {
        internal const string Prefix = "want.", BanKey = "ban";
        internal const string UntilSleep = "@sleep", UntilVisit = "@visit";
        internal const string OpenShop = "openshop", EndShop = "endshop", WakeUp = "wakeup";

        private static readonly Dictionary<string, HashSet<string>> parsed = new Dictionary<string, HashSet<string>>();
        private static readonly HashSet<string> none = new HashSet<string>();
        private static HashSet<string> banned;

        /// <summary>道具的时间段标签。</summary>
        internal static string TimeTag { get; private set; } = "";
        /// <summary>柜台来客的事件标签（空 = 没有）。</summary>
        internal static string[] CounterEvent { get; private set; } = Array.Empty<string>();

        private static string KeyOf(PaintTarget kind, string id) { return Prefix + (kind == PaintTarget.Portrait ? "portrait." : "item.") + id; }

        /// <summary>这一轮读到的结果作废（检测点、Notify 时调）。</summary>
        internal static void Reload() { parsed.Clear(); banned = null; }

        internal static HashSet<string> Banned
        {
            get
            {
                if (banned != null) return banned;
                banned = new HashSet<string>();
                foreach (string t in Split(Read(BanKey), out _)) banned.Add(t);
                return banned;
            }
        }

        /// <summary>别人给这个目标定的标签（不含环境标签）。没有返回空集合。</summary>
        internal static HashSet<string> Of(PaintTarget kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return none;
            string key = KeyOf(kind, id);
            if (parsed.TryGetValue(key, out HashSet<string> have)) return have;
            string text = Read(key);
            if (string.IsNullOrEmpty(text)) return parsed[key] = none;
            return parsed[key] = new HashSet<string>(Split(text, out _));
        }

        internal static void Set(PaintTarget kind, string id, string until, params string[] tags)
        {
            if (string.IsNullOrEmpty(id) || !PlayerStore.HasInstance) return;
            var clean = new List<string>();
            if (tags != null) foreach (string t in tags) foreach (string part in (t ?? "").Split(' ')) { string n = PortraitLibrary.Normal(part); if (n.Length > 0 && !clean.Contains(n)) clean.Add(n); }
            string key = KeyOf(kind, id);
            if (clean.Count == 0) ModHelper.RemoveModData(Painter.ModId, key);
            else ModHelper.SetModData(Painter.ModId, key, string.Join(" ", clean) + (until == UntilSleep || until == UntilVisit ? " " + until : ""));
            parsed.Remove(key);
        }

        internal static void Ban(string tag, bool on)
        {
            tag = PortraitLibrary.Normal(tag);
            if (tag.Length == 0 || !PlayerStore.HasInstance) return;
            var now = new List<string>(Banned);
            if (on == now.Contains(tag)) return;
            if (on) now.Add(tag); else now.Remove(tag);
            if (now.Count == 0) ModHelper.RemoveModData(Painter.ModId, BanKey); else ModHelper.SetModData(Painter.ModId, BanKey, string.Join(" ", now));
            banned = null;
        }

        /// <summary>到期的意向清掉：入睡清 @sleep，来客换人清 @visit。</summary>
        internal static void Expire(string until)
        {
            if (!PlayerStore.HasInstance) return;
            try
            {
                foreach (string key in ModHelper.GetModDataKeys(Painter.ModId))
                {
                    if (!key.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                    Split(Read(key), out string mark);
                    if (mark == until) { ModHelper.RemoveModData(Painter.ModId, key); parsed.Remove(key); }
                }
            }
            catch (Exception ex) { Health.Report("wants expire", ex); }
        }

        /// <summary>存档位里点了名的道具 ID（受管清单用）。</summary>
        internal static List<string> ItemIds()
        {
            var list = new List<string>();
            if (!PlayerStore.HasInstance) return list;
            const string head = Prefix + "item.";
            try { foreach (string key in ModHelper.GetModDataKeys(Painter.ModId)) if (key.StartsWith(head, StringComparison.Ordinal)) list.Add(key.Substring(head.Length)); }
            catch (Exception) { }
            return list;
        }

        private static string Read(string key)
        {
            try { return PlayerStore.HasInstance ? ModHelper.GetModData(Painter.ModId, key, "") : ""; }
            catch (Exception) { return ""; }
        }

        // 纯解析（离线测试用）：标签小写去重；@sleep / @visit 单独给出
        internal static List<string> Split(string text, out string until)
        {
            until = null;
            var list = new List<string>();
            foreach (string part in (text ?? "").Split(new[] { ' ', ',', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p == UntilSleep || p == UntilVisit) { until = p; continue; }
                if (p.Length > 0 && p[0] != '@' && !list.Contains(p)) list.Add(p);
            }
            return list;
        }

        // ── 环境标签 ──

        internal static void SetTime(string tag) { TimeTag = tag ?? ""; }

        /// <summary>读档后按店铺状态定时间段。</summary>
        internal static void TimeFromStore()
        {
            try
            {
                if (!PlayerStore.HasInstance) { TimeTag = ""; return; }
                switch (PlayerStore.Instance.storeState)
                {
                    case PlayerStore.StoreState.OPEN: TimeTag = OpenShop; break;
                    case PlayerStore.StoreState.CLOSED: TimeTag = EndShop; break;
                    default: TimeTag = WakeUp; break;
                }
            }
            catch (Exception) { TimeTag = ""; }
        }

        internal static void SetCounterEvent(params string[] tags) { CounterEvent = tags ?? Array.Empty<string>(); }

        /// <summary>道具要的标签 = 别人定的 + 时间段。explicitOnly = 只要别人定的。</summary>
        internal static HashSet<string> ForItem(string itemId, bool explicitOnly)
        {
            HashSet<string> set = Of(PaintTarget.StaticItem, itemId);
            if (explicitOnly || TimeTag.Length == 0) return set;
            return new HashSet<string>(set) { TimeTag };
        }

        /// <summary>立绘要的标签 = 别人定的；onCounter = 它正站在柜台上，再加事件标签。</summary>
        internal static HashSet<string> ForPortrait(string portraitId, bool onCounter)
        {
            HashSet<string> set = Of(PaintTarget.Portrait, portraitId);
            if (!onCounter || CounterEvent.Length == 0) return set;
            var all = new HashSet<string>(set);
            foreach (string t in CounterEvent) all.Add(t);
            return all;
        }
    }

    public static partial class Painter
    {
        /// <summary>告诉框架“这张立绘 / 这种道具现在想要这些标签”（一直有效，直到再改；不给标签 = 清掉）。
        /// 框架按标签挑差分：差分自己的标签全在要求范围内、带得最多的那个；没有合适的就不换。正显示着的当场换。</summary>
        public static void Want(PaintTarget kind, string id, params string[] tags) { Wants.Set(kind, id, null, tags); Notify(); }

        /// <summary>同上，只到入睡为止（“今天登场用哪个版本”）。</summary>
        public static void WantToday(PaintTarget kind, string id, params string[] tags) { Wants.Set(kind, id, Wants.UntilSleep, tags); Notify(); }

        /// <summary>同上，只到来客换人为止（对话中途的临时表情）。</summary>
        public static void WantNow(PaintTarget kind, string id, params string[] tags) { Wants.Set(kind, id, Wants.UntilVisit, tags); Notify(); }

        /// <summary>现在给这个目标定的标签（不含框架自己加的时间段 / 来客事件标签）。</summary>
        public static ICollection<string> Wanted(PaintTarget kind, string id) { return new List<string>(Wants.Of(kind, id)); }

        /// <summary>排除带某个标签的差分（例：关掉成人内容时 Ban("r18")）。跟着存档走。</summary>
        public static void Ban(string tag, bool on = true) { Wants.Ban(tag, on); Notify(); }

        // ── 查询与预演（1.0.0 rc7：给测试 mod 和做图的作者用；都不改存档） ──

        /// <summary>这张立绘 / 这种道具现在该用哪个差分（"" = 本体 / 不换）。立绘：意向 > 规则 > 来客事件（它正站在柜台上时）；道具：意向 > 规则 > 时间段。
        /// 道具这里不针对具体哪一件（规则里看物品自身标签的条件按“没有物品”算）；某一件的实际状态用 StateOf(item)。</summary>
        public static string Decide(PaintTarget kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (kind == PaintTarget.Portrait) return Counter.StateFor(id) ?? "";
            List<string> variants = ItemArt.Variants(id);
            string v = VariantTags.Best(kind, id, variants, Wants.ForItem(id, true));
            if (v != null) return v;
            foreach (PaintRule r in PaintRules.Holding(kind, id, null)) if (!VariantTags.Banned(kind, id, r.tag) && PaintRules.HasItemVariant(id, r.tag)) return r.tag;
            return VariantTags.Best(kind, id, variants, Wants.ForItem(id, false)) ?? "";
        }

        /// <summary>道具现在的时间段标签：openshop / endshop / wakeup（不在存档里为空）。</summary>
        public static string TimeTag => Wants.TimeTag;
        /// <summary>柜台上这位来客的立绘键（没人为 null）与它身上的事件标签。</summary>
        public static string CounterKey => Counter.Key;
        public static IList<string> CounterEvents => (string[])Wants.CounterEvent.Clone();
        /// <summary>这个差分自己的标签（名字拆出来的 + 登记的；不含从本体继承的）。</summary>
        public static IList<string> VariantTagsOf(PaintTarget kind, string id, string variant) { return (string[])VariantTags.Of(kind, id, variant).Clone(); }
        /// <summary>这种道具贴图组里现在能用的差分名。</summary>
        public static IList<string> ItemVariants(string itemId) { return ItemArt.Variants(itemId); }
        /// <summary>受管清单里有几件物品（开店、关店、Notify 时只过这些）。</summary>
        public static int ManagedItems => ManagedCount;
        /// <summary>框架记下的问题与慢步骤（调试用）。</summary>
        public static string HealthReport() { return Health.Summary(); }

        /// <summary>预演时间段（不等真的开店 / 关店）：道具按这个时间段重算。传 null = 回到按店铺状态算。</summary>
        public static void PreviewTime(string tag)
        {
            if (tag == null) Wants.TimeFromStore(); else Wants.SetTime(PortraitLibrary.Normal(tag));
            RefreshManaged();
        }

        /// <summary>预演来客事件（不用真的成交）：把 portraitId 当成“柜台上这位”，带上这些事件标签（visit / sold bonus / leave…）。
        /// 柜台上真有这位来客时图会当场换。不给标签 = 清掉事件。真的来客换人时自动复原。</summary>
        public static void PreviewCounter(string portraitId, params string[] tags)
        {
            Counter.Preview(portraitId, tags);
        }

        /// <summary>“我改了存档位 / 道具标签，现在重算”：店里的物品过一遍，柜台上的来客重新定图。
        /// 只写存档位、不调这个的，等下一个检测点（道具：读档 / 开店 / 关店 / 入睡结算；来客：登场 / 买卖 / 离开）。</summary>
        public static void Notify()
        {
            if (!PlayerStore.HasInstance || Retired) return;
            Wants.Reload();
            try { RefreshManaged(); }
            catch (Exception ex) { Health.Report("notify", ex); }
            Counter.Refresh();
        }
    }
}
