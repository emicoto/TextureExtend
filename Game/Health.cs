// ClaudeNote: 0.6.3 运行中出的错统一记在这里（替换 0.6.1 那几处“只记前 3 次 / 前 5 次 / 连错 30 帧就停”）。
//   需求：后台循环、每帧组件、整店重算这些地方，一处出错不能连累别处（各自兜住），但错了必须看得见、说得清。
//   之前各处自己数次数来压日志——数字没有依据，而且压掉之后就再也查不到了。
//   现在：同一处、同一条错误信息只写一次日志，之后只累加次数；换了一种错会再写一次（两种错交替出现也各只写一次）。
//   调试面板“接触面自检”末尾列出全部：哪一处、什么错、出了几次。所以不靠刷日志，也不丢信息。
using System;
using System.Collections.Generic;
using System.Text;

namespace ProbablyExtendFramework
{
    internal static class Health
    {
        private sealed class Issue { internal string where, message; internal int count; }
        private static readonly Dictionary<string, Issue> issues = new Dictionary<string, Issue>();
        private static readonly Dictionary<string, int> kinds = new Dictionary<string, int>();   // 每一处出过几种不同的错
        // 同一处最多分开记这么多种错误信息，再多的并成一条“其它”（次数照记）。
        // 这是内存上限，不是容错上限：信息里带着物品名之类会变的内容时，不让这张表跟着无限长。
        private const int KindsPerPlace = 8;

        /// <summary>记一次出错。where = 哪一处（固定的短名）。</summary>
        internal static void Report(string where, Exception ex) { Report(where, ex.GetType().Name + ": " + ex.Message); }

        internal static void Report(string where, string message)
        {
            string key = where + "\n" + message;
            if (issues.TryGetValue(key, out Issue i)) { i.count++; return; }
            kinds.TryGetValue(where, out int n);
            if (n >= KindsPerPlace)
            {
                key = where + "\n";
                if (issues.TryGetValue(key, out i)) { i.count++; return; }
                message = "(more kinds of error at this place; latest: " + message + ")";
            }
            else kinds[where] = n + 1;
            issues[key] = new Issue { where = where, message = message, count = 1 };
            Entry.Warn(where + " failed: " + message);
        }

        // ClaudeNote: 0.9.13 耗时记录（Lune：“客人来前后有明显卡顿”）。卡在哪一步不能靠猜：在主线程上可能费时的几处各包一层计时，
        //   超过一帧（SlowMs）的记下来——每处记次数、最长一次、总时长；每处头几次写进日志，之后只累加。状态报告 / 接触面自检末尾列出全部。
        //   计时本身是两次读时钟，不分配内存。
        private const float SlowMs = 12f;
        private const int LogFirst = 3;
        private sealed class Cost { internal int count; internal float worst, total; }
        private static readonly Dictionary<string, Cost> costs = new Dictionary<string, Cost>();

        internal static float Now => UnityEngine.Time.realtimeSinceStartup;

        /// <summary>记一步的耗时（startedAt = 开始时的 Health.Now）。不到 SlowMs 的不记。</summary>
        internal static void Took(string what, float startedAt)
        {
            float ms = (UnityEngine.Time.realtimeSinceStartup - startedAt) * 1000f;
            if (ms < SlowMs) return;
            if (!costs.TryGetValue(what, out Cost c)) costs[what] = c = new Cost();
            c.count++; c.total += ms;
            if (ms > c.worst) c.worst = ms;
            if (c.count <= LogFirst) Entry.Info("slow step: " + what + " took " + ms.ToString("0") + " ms");
        }

        internal static void Clear() { issues.Clear(); kinds.Clear(); costs.Clear(); }

        private static string Costs()
        {
            if (costs.Count == 0) return "slow steps (over " + SlowMs.ToString("0") + " ms on the main thread): none";
            var b = new StringBuilder("slow steps (over " + SlowMs.ToString("0") + " ms on the main thread):");
            foreach (KeyValuePair<string, Cost> kv in costs)
                b.Append("\n  · ").Append(kv.Key).Append("  ×").Append(kv.Value.count).Append("  worst ").Append(kv.Value.worst.ToString("0")).Append(" ms  total ").Append(kv.Value.total.ToString("0")).Append(" ms");
            return b.ToString();
        }

        internal static string Summary()
        {
            if (issues.Count == 0) return "runtime errors: none\n" + Costs();
            var b = new StringBuilder(Costs() + "\nruntime errors:");
            foreach (KeyValuePair<string, Issue> kv in issues)
                b.Append("\n  × ").Append(kv.Value.where).Append("  ×").Append(kv.Value.count).Append("  → ").Append(kv.Value.message);
            return b.ToString();
        }
    }
}
