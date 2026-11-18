// ClaudeNote: 0.6.3 运行中出的错统一记在这里（替换 0.6.1 那几处“只记前 3 次 / 前 5 次 / 连错 30 帧就停”）。
//   需求：后台循环、每帧组件、整店重算这些地方，一处出错不能连累别处（各自兜住），但错了必须看得见、说得清。
//   之前各处自己数次数来压日志——数字没有依据，而且压掉之后就再也查不到了。
//   现在：同一处、同一条错误信息只写一次日志，之后只累加次数；换了一种错会再写一次（两种错交替出现也各只写一次）。
//   调试面板“接触面自检”末尾列出全部：哪一处、什么错、出了几次。所以不靠刷日志，也不丢信息。
using System;
using System.Collections.Generic;
using System.Text;

namespace TextureExtend
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

        internal static void Clear() { issues.Clear(); kinds.Clear(); }

        internal static string Summary()
        {
            if (issues.Count == 0) return "runtime errors: none";
            var b = new StringBuilder("runtime errors:");
            foreach (KeyValuePair<string, Issue> kv in issues)
                b.Append("\n  × ").Append(kv.Value.where).Append("  ×").Append(kv.Value.count).Append("  → ").Append(kv.Value.message);
            return b.ToString();
        }
    }
}
