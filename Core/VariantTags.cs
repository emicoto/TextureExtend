// ClaudeNote: 1.0.0 rc6 差分层标签（Lune 2026-11-18）。立绘和道具共用。
//   两层：本体层的标签（派系、性别、道具分类…，立绘库 / 道具标签层管）所有差分都继承；这里只管“这个差分自己的”标签。
//   差分自己的标签 = 差分名按 _ 拆开的每一段（ep1_accept → ep1、accept；sold_bonus → sold、bonus）+ 登记给它的（表格 tag 行的 id 写 图ID#差分、工坊点选差分后加的）。
//   按标签挑差分（Best）：只看“自己的标签全在要求范围内”的差分，挑带得最多的；一样多挑名字短的。
//     于是 要求 {ep1, accept} 时：ep1_accept → 没有就 ep1 → 再没有就不换（调用方回本体）。带了被排除标签（Ban）的差分不参加。
//   不拆名字的：root_key 组的成员差分（差分名 = 另一张立绘的 ID，例如 my_girl_alt，不是状态）。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    internal static class VariantTags
    {
        private sealed class Extra { internal string owner; internal string[] tags; }
        private static readonly Dictionary<string, List<Extra>> extras = new Dictionary<string, List<Extra>>();   // "类型|ID#差分" → 各登记方加的
        private static readonly Dictionary<string, string[]> cache = new Dictionary<string, string[]>();

        private static string Key(PaintTarget kind, string id, string variant) { return kind + "|" + id + "#" + variant; }

        /// <summary>给一个差分登记自己的标签（同一个登记方再登记 = 追加）。</summary>
        internal static void Add(string owner, PaintTarget kind, string id, string variant, params string[] tags)
        {
            variant = PortraitLibrary.Normal(variant);
            if (string.IsNullOrEmpty(id) || variant.Length == 0 || tags == null || tags.Length == 0) return;
            var clean = new List<string>();
            foreach (string t in tags) { string n = PortraitLibrary.Normal(t); if (n.Length > 0 && !clean.Contains(n)) clean.Add(n); }
            if (clean.Count == 0) return;
            string key = Key(kind, id, variant);
            if (!extras.TryGetValue(key, out List<Extra> list)) extras[key] = list = new List<Extra>();
            list.Add(new Extra { owner = owner ?? "", tags = clean.ToArray() });
            cache.Remove(key);
        }

        internal static void Remove(string owner)
        {
            foreach (string key in new List<string>(extras.Keys))
            {
                extras[key].RemoveAll(e => e.owner == owner);
                if (extras[key].Count == 0) extras.Remove(key);
            }
            cache.Clear();
        }

        /// <summary>登记给这个差分的标签（不含名字拆出来的）。工坊显示“哪些是自带的”用。</summary>
        internal static List<string> Registered(PaintTarget kind, string id, string variant)
        {
            var list = new List<string>();
            if (extras.TryGetValue(Key(kind, id, PortraitLibrary.Normal(variant)), out List<Extra> have))
                foreach (Extra e in have) foreach (string t in e.tags) if (!list.Contains(t)) list.Add(t);
            return list;
        }

        /// <summary>差分名拆出来的标签。</summary>
        internal static string[] FromName(string variant)
        {
            return (variant ?? "").ToLowerInvariant().Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>这个差分自己的全部标签（名字拆的 + 登记的）。</summary>
        internal static string[] Of(PaintTarget kind, string id, string variant)
        {
            variant = PortraitLibrary.Normal(variant);
            if (variant.Length == 0) return Array.Empty<string>();
            string key = Key(kind, id, variant);
            if (cache.TryGetValue(key, out string[] have)) return have;
            var list = new List<string>();
            bool member = kind == PaintTarget.Portrait && RootJoin.IsMemberVariant(id, variant);
            if (!member) foreach (string t in FromName(variant)) if (!list.Contains(t)) list.Add(t);
            foreach (string t in Registered(kind, id, variant)) if (!list.Contains(t)) list.Add(t);
            return cache[key] = list.ToArray();
        }

        /// <summary>带着被排除的标签。</summary>
        internal static bool Banned(PaintTarget kind, string id, string variant)
        {
            HashSet<string> banned = Wants.Banned;
            if (banned.Count == 0) return false;
            foreach (string t in Of(kind, id, variant)) if (banned.Contains(t)) return true;
            return false;
        }

        /// <summary>按要求的标签挑一个差分；没有合适的返回 null。</summary>
        internal static string Best(PaintTarget kind, string id, IEnumerable<string> variants, ICollection<string> wanted)
        {
            if (variants == null || wanted == null || wanted.Count == 0) return null;
            return Pick(variants, wanted, Wants.Banned, v => Of(kind, id, v));
        }

        // 纯逻辑（离线测试 verification_match 直接测这一个）
        internal static string Pick(IEnumerable<string> variants, ICollection<string> wanted, ICollection<string> banned, Func<string, string[]> tagsOf)
        {
            string best = null;
            int score = 0;
            foreach (string v in variants)
            {
                string[] tags = tagsOf(v);
                if (tags.Length == 0 || tags.Length < score) continue;
                bool ok = true;
                foreach (string t in tags) if (!wanted.Contains(t) || (banned != null && banned.Contains(t))) { ok = false; break; }
                if (!ok) continue;
                if (tags.Length > score || v.Length < best.Length || (v.Length == best.Length && string.CompareOrdinal(v, best) < 0)) { best = v; score = tags.Length; }
            }
            return best;
        }
    }
}
