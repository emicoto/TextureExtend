// ClaudeNote: 第 2 步——条件表达式（表格 rule 行的 when 列）、具名条件、全局旗标、游戏状态读数。
//   写给不会写代码的人；只读、不执行任意代码。表达式登记时编译成一个函数，判定时不再解析字符串。
//   原子：
//     tag:NAME            物品标签开着
//     tag:NAME>=60        物品标签的数值比较（= != > >= < <=）；= / != 右边不是数字时按字符串比
//     day>=10             游戏天数
//     night               关店之后到起床之前（框架的昼夜判定）
//     flag:lune.met       全局旗标开着；flag:lune.count>=3 比数值
//     visitor:lower       当前来客的派系（lower / upper / sec / bm / rev / cartel / church / tourist，或游戏原名 FACTION_…）
//     visitor             柜台上有来客
//     rep:upper>=20       店铺在某派系的声望
//     cond:brood.isSick   代码里注册的具名条件（Painter.Condition）
//     true / false
//   组合：a & b、a | b、!a、括号。& 比 | 先算。& 也可写 &&，| 也可写 ||。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TextureExtend
{
    internal static class When
    {
        /// <summary>编译表达式；写错了抛出带位置的说明（表格加载时报行号）。</summary>
        internal static Func<GameItem, bool> Compile(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("empty condition");
            var p = new Parser(text);
            Func<GameItem, bool> f = p.Or();
            p.SkipSpace();
            if (!p.End) throw p.Error("unexpected \"" + p.Rest + "\"");
            return f;
        }

        private sealed class Parser
        {
            private readonly string s;
            private int i;
            internal Parser(string text) { s = text; }
            internal bool End => i >= s.Length;
            internal string Rest => s.Substring(i);
            internal FormatException Error(string what) { return new FormatException(what + " at column " + (i + 1) + " in \"" + s + "\""); }

            internal void SkipSpace() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            private bool Eat(char c)
            {
                SkipSpace();
                if (i < s.Length && s[i] == c) { i++; if (i < s.Length && s[i] == c && (c == '&' || c == '|')) i++; return true; }
                return false;
            }

            internal Func<GameItem, bool> Or()
            {
                Func<GameItem, bool> left = And();
                while (Eat('|')) { Func<GameItem, bool> a = left, b = And(); left = x => a(x) || b(x); }
                return left;
            }

            private Func<GameItem, bool> And()
            {
                Func<GameItem, bool> left = Not();
                while (Eat('&')) { Func<GameItem, bool> a = left, b = Not(); left = x => a(x) && b(x); }
                return left;
            }

            private Func<GameItem, bool> Not()
            {
                if (Eat('!')) { Func<GameItem, bool> a = Not(); return x => !a(x); }
                if (Eat('('))
                {
                    Func<GameItem, bool> inner = Or();
                    if (!Eat(')')) throw Error("missing )");
                    return inner;
                }
                return Atom();
            }

            private Func<GameItem, bool> Atom()
            {
                SkipSpace();
                int start = i;
                while (i < s.Length && "&|()!<>= \t".IndexOf(s[i]) < 0) i++;
                string word = s.Substring(start, i - start);
                if (word.Length == 0) throw Error("missing condition");
                SkipSpace();
                string op = null, value = null;
                if (i < s.Length && "<>=!".IndexOf(s[i]) >= 0 && !(s[i] == '!' && (i + 1 >= s.Length || s[i + 1] != '=')))
                {
                    int o = i;
                    while (i < s.Length && "<>=!".IndexOf(s[i]) >= 0) i++;
                    op = s.Substring(o, i - o);
                    if (op == "==") op = "=";
                    if (op != "=" && op != "!=" && op != ">" && op != ">=" && op != "<" && op != "<=") throw Error("unknown comparison " + op);
                    SkipSpace();
                    int v = i;
                    while (i < s.Length && "&|() \t".IndexOf(s[i]) < 0) i++;
                    value = s.Substring(v, i - v);
                    if (value.Length == 0) throw Error("missing value after " + op);
                }
                return Build(word, op, value);
            }

            private Func<GameItem, bool> Build(string word, string op, string value)
            {
                int colon = word.IndexOf(':');
                string head = (colon < 0 ? word : word.Substring(0, colon)).ToLowerInvariant();
                string arg = colon < 0 ? "" : word.Substring(colon + 1);
                switch (head)
                {
                    case "true": return _ => true;
                    case "false": return _ => false;
                    case "night": NoCompare(op, word); return _ => DayNight.IsNight;
                    case "day": NeedCompare(op, word); return Number(_ => GameState.Day, op, value);
                    case "tag":
                        NeedArg(arg, word);
                        if (op == null) return item => item != null && item.IsTag(arg);
                        return Compare(item => item != null && item.IsTag(arg) ? item.GetTagReadonly(arg) : null, op, value);
                    case "flag":
                        NeedArg(arg, word);
                        if (op == null) return _ => Flags.IsSet(arg);
                        return Text(_ => Flags.Get(arg), op, value);
                    case "visitor":
                        NoCompare(op, word);
                        if (arg.Length == 0) return _ => GameState.VisitorFaction != null;
                        string want = GameState.FactionId(arg);
                        return _ => GameState.VisitorFaction == want;
                    case "rep":
                        NeedArg(arg, word); NeedCompare(op, word);
                        string faction = GameState.FactionId(arg);
                        return Number(_ => GameState.Reputation(faction), op, value);
                    case "cond":
                        NeedArg(arg, word); NoCompare(op, word);
                        return item => Conditions.Holds(arg, item);
                    default:
                        throw Error("unknown condition \"" + word + "\"");
                }
            }

            private void NeedArg(string arg, string word) { if (arg.Length == 0) throw Error(word + " needs a name after the colon"); }
            private void NeedCompare(string op, string word) { if (op == null) throw Error(word + " needs a comparison, e.g. " + word + ">=10"); }
            private void NoCompare(string op, string word) { if (op != null) throw Error(word + " cannot be compared"); }

            private Func<GameItem, bool> Number(Func<GameItem, double> read, string op, string value)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) throw Error("\"" + value + "\" is not a number");
                return item => Test(read(item).CompareTo(v), op);
            }

            private Func<GameItem, bool> Compare(Func<GameItem, TagState> read, string op, string value)
            {
                bool numeric = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                if (!numeric && op != "=" && op != "!=") throw Error("\"" + value + "\" is not a number");
                return item =>
                {
                    TagState t = read(item);
                    if (t == null) return op == "!=";
                    if (numeric) return Test(TagNumber(t).CompareTo(v), op);
                    bool same = string.Equals(t.GetString() ?? "", value, StringComparison.OrdinalIgnoreCase);
                    return op == "=" ? same : !same;
                };
            }

            private Func<GameItem, bool> Text(Func<GameItem, string> read, string op, string value)
            {
                bool numeric = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                if (!numeric && op != "=" && op != "!=") throw Error("\"" + value + "\" is not a number");
                return item =>
                {
                    string have = read(item);
                    if (numeric) return have != null && double.TryParse(have, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && Test(h.CompareTo(v), op);
                    bool same = string.Equals(have ?? "", value, StringComparison.OrdinalIgnoreCase);
                    return op == "=" ? same : !same;
                };
            }

            private static bool Test(int c, string op)
            {
                switch (op)
                {
                    case "=": return c == 0;
                    case "!=": return c != 0;
                    case ">": return c > 0;
                    case ">=": return c >= 0;
                    case "<": return c < 0;
                    default: return c <= 0;
                }
            }
        }

        // 标签的数值：游戏的标签有 int / long / float / double 几个槽，mod 各用各的，取第一个非零的
        internal static double TagNumber(TagState t)
        {
            if (t.valueInt != 0) return t.valueInt;
            if (t.valueLong != 0) return t.valueLong;
            if (t.valueFloat != 0f) return t.valueFloat;
            if (t.valueDouble != 0d) return t.valueDouble;
            return double.TryParse(t.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0d;
        }
    }

    /// <summary>具名条件：代码里注册，表格里用 cond:名字 引用。</summary>
    internal static class Conditions
    {
        private sealed class Entry2 { internal string owner; internal Func<GameItem, bool> func; internal bool warned; }
        private static readonly Dictionary<string, Entry2> named = new Dictionary<string, Entry2>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> missingWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Add(string owner, string name, Func<GameItem, bool> func)
        {
            if (string.IsNullOrEmpty(name) || func == null) return;
            named[name] = new Entry2 { owner = owner ?? "", func = func };
        }

        internal static void Remove(string owner)
        {
            foreach (string key in new List<string>(named.Keys)) if (named[key].owner == owner) named.Remove(key);
        }

        internal static bool Holds(string name, GameItem item)
        {
            if (!named.TryGetValue(name, out Entry2 e))
            {
                if (missingWarned.Add(name)) TextureExtend.Entry.Warn("condition cond:" + name + " is not registered (treated as false)");
                return false;
            }
            try { return e.func(item); }
            catch (Exception ex)
            {
                if (!e.warned) { e.warned = true; TextureExtend.Entry.Warn("condition cond:" + name + " failed (treated as false): " + ex.Message); }
                return false;
            }
        }

        internal static int Count => named.Count;

        /// <summary>登记过的具名条件的名字（工坊的“可用触发条件”下拉用），排好序。</summary>
        internal static List<string> Names()
        {
            var list = new List<string>(named.Keys);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }
}
