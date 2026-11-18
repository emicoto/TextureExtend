// ClaudeNote: 1.0.0 rc6 柜台上的来客现在该显示哪个差分（Lune 2026-11-18）。
//   之前：来客的图只在进门那一刻取一次，对话中途不会变；规则（rule 行）只管立绘道具，不管柜台上的人。
//   现在这里统一回答“这个立绘键现在用哪个差分”（StateFor），取图入口（PlanPort）问它；事件发生时把柜台上的图重新定一次（Refresh）。
//   先后：别人点名要的标签（Wants）> 规则 > 来客事件标签 > 不换（本体 / 来源设置选定的那张）。
//   框架自己给的事件标签（检测点就这几个，不轮询）：
//     visit   登场（PlayerStore.GetNextClient 取图之前就定好，进门那一张就是它）
//     sold / bought        成交（PlayerStore.SellItem / OnItemBought）。讨价还价的结果看这件物品成交那一刻带着的“讨价特性”
//                          （BargainUIManager 的 8 个固定 ID；客人接受了才会挂上）：
//       sold_bonus  bargainDiscount（给了客人折扣）          sold_malus  bargainMarkup / blackmailMarkup / threatenMarkup（加价、勒索、威胁）
//       bought_bonus bargainBuyingMarkup（加价收购）          bought_malus bargainBuyingDiscount / blackmailDiscount / threatenDiscount（压价收购）
//       事件、状态、零售加价这些别的价格特性不看，不会误判。
//     leave   离开（PlayerStore.DismissCurrentClient 开头）
//   事件标签有效到下一个事件、或这位客人走掉为止。差分名 / 差分标签对得上就换（sold_bonus 没有就退到 sold，再没有就不换）。
//   其他时机：由管状态的 mod 写意向后调 Painter.Notify / Painter.Want（当场生效）。
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ProbablyExtendFramework
{
    internal static class Counter
    {
        private static string current;   // 柜台上这位的立绘键（进门取图前记下；人走了清掉）

        // 讨价特性 → 标签（表）
        private static readonly string[][] soldMarks =
        {
            new[] { "bargainDiscount", "bonus" }, new[] { "bargainMarkup", "malus" }, new[] { "blackmailMarkup", "malus" }, new[] { "threatenMarkup", "malus" },
        };
        private static readonly string[][] boughtMarks =
        {
            new[] { "bargainBuyingMarkup", "bonus" }, new[] { "bargainBuyingDiscount", "malus" }, new[] { "blackmailDiscount", "malus" }, new[] { "threatenDiscount", "malus" },
        };

        internal static string Key => current;

        internal static void Install()
        {
            Ports.Patch("PlayerStore.GetNextClient", "来客登场时按 visit 标签定图", () => AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.GetNextClient)),
                prefix: new HarmonyMethod(typeof(Counter), nameof(BeforeArrive)));
            Ports.Patch("PlayerStore.SellItem", "卖出时按 sold 标签换来客差分", () => AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.SellItem), new[] { typeof(GameItem) }),
                prefix: new HarmonyMethod(typeof(Counter), nameof(BeforeSold)), postfix: new HarmonyMethod(typeof(Counter), nameof(AfterTrade)));
            Ports.Patch("PlayerStore.OnItemBought", "买入时按 bought 标签换来客差分", () => AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.OnItemBought), new[] { typeof(GameItem), typeof(int) }),
                prefix: new HarmonyMethod(typeof(Counter), nameof(BeforeBought)), postfix: new HarmonyMethod(typeof(Counter), nameof(AfterBought)));
            Ports.Patch("PlayerStore.DismissCurrentClient", "来客离开时按 leave 标签换差分", () => AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient), new[] { typeof(bool), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Counter), nameof(BeforeLeave)));
        }

        internal static void Reset() { current = null; pending = null; Wants.SetCounterEvent(); }

        // ── 现在用哪个差分 ──

        /// <summary>这个立绘键现在该显示的差分；null = 不换。</summary>
        internal static string StateFor(string key)
        {
            Portrait p = PortraitLibrary.Get(key);
            if (p == null) return null;
            ICollection<string> variants = p.Variants;
            if (variants.Count == 0) return null;
            string v = VariantTags.Best(PaintTarget.Portrait, key, variants, Wants.Of(PaintTarget.Portrait, key));
            if (v != null) return v;
            if (PaintRules.Count > 0)
                foreach (PaintRule r in PaintRules.Holding(PaintTarget.Portrait, key, null))
                {
                    // root_key 组的成员差分不在这里管：来客换不换成它由“自动动态适配”那个开关定（RootJoin.StateFor，取图入口先问过了）
                    if (!p.HasVariant(r.tag) || RootJoin.IsMemberVariant(key, r.tag) || VariantTags.Banned(PaintTarget.Portrait, key, r.tag)) continue;
                    return r.tag;
                }
            if (key != current || Wants.CounterEvent.Length == 0) return null;
            return VariantTags.Best(PaintTarget.Portrait, key, variants, Wants.ForPortrait(key, true));
        }

        /// <summary>把柜台上这位的图按现在的状态重新定一次（图没变就不动）。</summary>
        internal static void Refresh()
        {
            try
            {
                if (current == null || !PlanPort.Enabled || StoreClientMono.Instance == null || SpriteDict.Instance == null) return;
                if (Normal(PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint()?.spriteName) != current) return;   // 柜台上站的不是它（预演别的立绘、人已经走了）：不动柜台
                SpriteRenderer image = StoreClientMono.Instance.image;
                if (image == null) return;
                Sprite s = SpriteDict.Instance.GetSprite(current);   // 走取图入口：来源、大小、差分都按显示方案
                if (s == null || image.sprite == s) return;
                image.sprite = s;
                Display.RaiseCounterShown(current, StateFor(current) ?? "");
            }
            catch (Exception ex) { Health.Report("counter refresh", ex); }
        }

        // 来客记的键可能是框架的显示键（texext_图ID…）：还原成图 ID
        private static string Normal(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return key.StartsWith(Display.KeyPrefix, StringComparison.Ordinal) ? Display.PortraitOfKey(key, out _) ?? key : key;
        }

        internal static void Preview(string key, string[] tags)
        {
            if (!string.IsNullOrEmpty(key)) current = key;
            var clean = new List<string>();
            if (tags != null) foreach (string t in tags) foreach (string part in (t ?? "").Split(' ', '_')) { string n = PortraitLibrary.Normal(part); if (n.Length > 0 && !clean.Contains(n)) clean.Add(n); }
            Event(clean.ToArray());
        }

        private static void Event(params string[] tags)
        {
            Wants.SetCounterEvent(tags);
            Refresh();
        }

        /// <summary>后台每秒看一眼：柜台上没人了 → 事件标签清掉、到“来客换人”为止的意向到期。</summary>
        internal static void ClientChanged(bool present)
        {
            Wants.Expire(Wants.UntilVisit);
            if (present) return;
            current = null;
            Wants.SetCounterEvent();
        }

        // ── 补丁（都只读游戏状态；出错不进游戏流程） ──

        private static void BeforeArrive(PlayerStore __instance)
        {
            try
            {
                List<StoreClient> stack = __instance?.storeClientManager?.clientStack;
                if (stack == null || stack.Count == 0 || stack[0] == null) return;
                Wants.Expire(Wants.UntilVisit);
                current = Normal(stack[0].GetSprite());
                Wants.SetCounterEvent("visit");
            }
            catch (Exception) { }
        }

        private static string[] pending;

        private static void BeforeSold(GameItem item) { pending = Marks("sold", item, soldMarks); }
        private static void BeforeBought(GameItem gameItem) { pending = Marks("bought", gameItem, boughtMarks); }

        private static void AfterTrade()
        {
            string[] tags = pending;
            pending = null;
            if (tags != null && current != null) Event(tags);
        }

        private static void AfterBought(GameItem gameItem)
        {
            AfterTrade();
            try { if (gameItem != null) { Painter.Manage(gameItem); Painter.Refresh(gameItem); } }   // 新到手的物品：进受管清单，按规则 / 时间段定一次图
            catch (Exception ex) { Health.Report("bought item refresh", ex); }
        }

        private static void BeforeLeave(PlayerStore __instance)
        {
            try { if (__instance != null && __instance.currentClientInstance != null && current != null) Event("leave"); }
            catch (Exception) { }
        }

        // 捐赠（bribeDonation）不算卖出
        private static string[] Marks(string what, GameItem item, string[][] table)
        {
            try
            {
                if (item == null) return null;
                if (what == "sold" && Has(item, "bribeDonation")) return null;
                foreach (string[] row in table) if (Has(item, row[0])) return new[] { what, row[1] };
                return new[] { what };
            }
            catch (Exception) { return null; }
        }

        private static bool Has(GameItem item, string featureId)
        {
            ItemFeature f = item.FindItemFeatureByID(featureId);
            return f != null && !f.isDisabled;
        }
    }
}
