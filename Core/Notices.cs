// ClaudeNote: 0.6.2 状态变化通知（Painter.OnStateChanged）的派发。替换掉 0.6.1 的“嵌套超过 4 层就不通知”。
//   通知是干什么的：物品换了状态之后，让扩展 mod 挂演出、音效、日志。它是“事后告知”，不是“参与决定”。
//   原来的毛病：换图的那一行代码里当场回调监听方。于是——
//     · 监听方在回调里又动了物品状态，就在上一次换图还没收尾时套进了下一次换图；
//     · 整店重算（读档 / 起床 / 开关门）正遍历到一半，监听方的代码就插了进来；
//     · 同一件物品在一次重算里先后换两次，监听方收到两条，其实只有最后的结果有意义。
//   根治的做法：
//     1. 换图只记账，不回调。等手上的这批操作做完再统一告知（Begin / End 圈出“一批”；不在批里的单次操作做完立刻告知）。
//     2. 每件物品只告知净变化：一批里 A→B→C 告知 A→C，A→B→A 不告知。
//     3. 监听方在回调里又改了状态：照样记账，本轮告知完接着告知（排队，不嵌套）。
//     4. 什么时候会没完没了：只有监听方跟“状态由谁决定”对着干——状态本该由四层判定（临时 > 锁定 > 规则 > 本体）唯一决定，
//        调 Painter.Refresh 多少次结果都一样，不会再产生变化；想影响状态应该登记规则或临时状态，而不是在通知里手动改。
//        在通知里手动改、又被另一个监听方或规则改回去，才会来回翻。这种情况不靠数层数猜，而是直接认出来：
//        同一轮告知里某件物品回到了它这一轮已经待过的状态 = 来回翻。记一条说清楚是哪件物品、在哪几个状态之间翻，
//        这件物品这一轮不再告知（它停在最后那个状态）。物品的状态数有限，所以一轮告知必然结束。
using System;
using System.Collections.Generic;

namespace TextureExtend
{
    internal static class Notices
    {
        private struct Change { internal string from, to; }

        private static readonly List<GameItem> order = new List<GameItem>();                       // 记账顺序
        private static readonly Dictionary<GameItem, Change> pending = new Dictionary<GameItem, Change>();
        private static int batches;
        private static bool telling;

        /// <summary>开始一批操作（整店重算、检查点等）：这期间的状态变化攒到 End 再告知。可以套着用，最外层 End 才告知。</summary>
        internal static void Begin() { batches++; }

        internal static void End()
        {
            if (batches > 0) batches--;
            if (batches == 0) Tell();
        }

        /// <summary>记一笔状态变化。不在批里、也不在告知途中时立刻告知。</summary>
        internal static void Record(GameItem item, string from, string to)
        {
            if (item == null || !Painter.HasStateListeners) return;
            from = from ?? ""; to = to ?? "";
            if (pending.TryGetValue(item, out Change c)) c.to = to;           // 这一批里它已经变过：只更新终点
            else { c = new Change { from = from, to = to }; order.Add(item); }
            pending[item] = c;
            if (batches == 0) Tell();
        }

        private static void Tell()
        {
            if (telling) return;                // 正在告知：新记的账由下面的循环接着处理（排队，不嵌套）
            telling = true;
            var been = new Dictionary<GameItem, HashSet<string>>();           // 这一轮里每件物品待过的状态
            try
            {
                while (order.Count > 0)
                {
                    var items = new List<GameItem>(order);
                    var changes = new Dictionary<GameItem, Change>(pending);
                    order.Clear();
                    pending.Clear();
                    foreach (GameItem item in items)
                    {
                        Change c = changes[item];
                        if (c.from == c.to) continue;                         // 净变化为零
                        if (!been.TryGetValue(item, out HashSet<string> states)) been[item] = states = new HashSet<string> { c.from };
                        if (!states.Add(c.to))
                        {
                            Entry.Warn("item " + item.identifier + " keeps flipping between states (" + string.Join(" / ", states)
                                + "): something changes its state inside OnStateChanged against the rules. Use a rule or a pulse to decide the state; left at \"" + c.to + "\"");
                            continue;
                        }
                        Painter.TellStateChanged(item, c.from, c.to);
                    }
                }
            }
            finally { telling = false; }
        }

        /// <summary>读档 / 停用：没告知完的账作废（物品都换了一批）。</summary>
        internal static void Reset()
        {
            order.Clear();
            pending.Clear();
            batches = 0;
        }
    }
}
