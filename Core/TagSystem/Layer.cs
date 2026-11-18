// ClaudeNote: 0.7.1 TagSystem.Layer——群组与层级管理（原 Core/TagLayer.cs；道具自带标签的读取搬到了 TagSystem.ItemTags）。
//   第 3 步——标签层。两棵树：
//   立绘：顶层是派系（scav / upper / lower / rev / bm / sec …）与性别（male / female），两者任一个都可以当第一层取池；下面是职业、aug、named…（PortraitTags 按字段名解析）。
//   道具：顶层是游戏自带的物品分类与标识标签（ItemTags：FOOD、IMPORTANT_TAG…），下面由 mod 往上挂。
//   外部扩展（与表格 tag 行等价）：
//     Painter.Tag(modId, PaintTarget.Portrait, "mutant_cat").Under("lower").Group("mutant").With("female").Without("aug");
//       → 立绘 mutant_cat 进组 mutant；mutant 组挂在 lower 下面（组员自动带上 lower 标签，取 lower 的池子也有它）；
//         进组前检查：必须带 female、不能带 aug，不符合的拒绝并警告。
//     id 写 "*"：符合约束的全部立绘自动进组（按标签圈一批）。id 留空：只声明组的上下级。
//   规则、临时状态、来源选择都可以写 @组名，一次管一批（@mutant、@lower、@FOOD）。
//   取池子：Painter.Pool(PaintTarget.Portrait, "lower", "female") / Pool(PaintTarget.StaticItem, "FOOD")。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework.TagSystem
{
    /// <summary>标签层里的一条登记。用 Regist.Tag(...)（或 Painter.Tag）创建，链式补全。</summary>
    public sealed class TagDef
    {
        internal string owner, id, group, under;
        internal PaintTarget target;
        internal string[] with = Array.Empty<string>(), without = Array.Empty<string>();
        internal bool warned;

        /// <summary>挂在哪个上层组 / 标签下面（组员继承它）。</summary>
        public TagDef Under(string parent) { under = Norm(parent); Layer.Changed(); return this; }
        /// <summary>进哪个组。</summary>
        public TagDef Group(string name) { group = Norm(name); Layer.Changed(); return this; }
        /// <summary>进组必须带的标签。</summary>
        public TagDef With(params string[] tags) { with = NormAll(tags); Layer.Changed(); return this; }
        /// <summary>进组不能带的标签。</summary>
        public TagDef Without(params string[] tags) { without = NormAll(tags); Layer.Changed(); return this; }

        internal static string Norm(string tag) { string t = PortraitLibrary.Normal((tag ?? "").TrimStart('@')); return t.Length == 0 ? null : t; }

        private static string[] NormAll(string[] tags)
        {
            var list = new List<string>();
            if (tags != null) foreach (string t in tags) { string n = Norm(t); if (n != null && !list.Contains(n)) list.Add(n); }
            return list.ToArray();
        }
    }

    internal static class Layer
    {
        private static readonly List<TagDef> defs = new List<TagDef>();
        private static readonly Dictionary<string, string> parentOf = new Dictionary<string, string>();       // 组 → 上层
        private static readonly Dictionary<string, HashSet<string>> itemTags = new Dictionary<string, HashSet<string>>();   // 道具 ID → 组（含上层）
        private static bool dirty = true;

        internal static TagDef Add(string owner, PaintTarget target, string id)
        {
            var d = new TagDef { owner = owner ?? "", target = target, id = (id ?? "").Trim() };
            defs.Add(d);
            Changed();
            return d;
        }

        internal static void Remove(string owner) { if (defs.RemoveAll(d => d.owner == owner) > 0) Changed(); }

        internal static void Changed()
        {
            dirty = true;
            PortraitLibrary.Invalidate();
        }

        internal static int Count => defs.Count;

        private static void Build()
        {
            if (!dirty) return;
            dirty = false;
            parentOf.Clear();
            itemTags.Clear();
            foreach (TagDef d in defs)
                if (d.group != null && d.under != null && d.group != d.under) parentOf[d.group] = d.under;
            foreach (TagDef d in defs)
            {
                if (d.target != PaintTarget.StaticItem || d.group == null || d.id.Length == 0 || d.id == "*") continue;
                if (!Fits(d, ItemTags.Of(d.id))) continue;
                if (!itemTags.TryGetValue(d.id, out HashSet<string> set)) itemTags[d.id] = set = new HashSet<string>();
                foreach (string g in ChainNoBuild(d.group)) set.Add(g);
            }
        }

        private static List<string> ChainNoBuild(string group)
        {
            var list = new List<string>();
            for (string g = group; g != null && !list.Contains(g); g = parentOf.TryGetValue(g, out string p) ? p : null) list.Add(g);
            return list;
        }

        // ── 立绘：立绘库重建时调，给每张立绘加上组标签 ──

        internal static void ApplyPortraits(Dictionary<string, Portrait> portraits)
        {
            Build();
            foreach (TagDef d in defs)
            {
                if (d.target != PaintTarget.Portrait || d.group == null || d.id.Length == 0) continue;
                List<string> chain = ChainNoBuild(d.group);
                if (d.id == "*")
                {
                    foreach (Portrait p in portraits.Values)
                        if ((d.under == null || p.Has(d.under)) && Fits(d, p.Tags)) AddTags(p, chain);
                    continue;
                }
                if (!portraits.TryGetValue(d.id, out Portrait one)) continue;   // 立绘还没出现：等它出现的那次重建
                if (Fits(d, one.Tags)) AddTags(one, chain);
                else if (!d.warned)
                {
                    d.warned = true;
                    Entry.Warn("tag layer: " + d.id + " not added to @" + d.group + " (needs " + string.Join(" ", d.with) + (d.without.Length > 0 ? ", must not have " + string.Join(" ", d.without) : "") + ")");
                }
            }
        }

        private static void AddTags(Portrait p, List<string> chain)
        {
            var list = new List<string>(p.Tags);
            foreach (string t in chain) if (!list.Contains(t)) list.Add(t);
            p.Tags = list.ToArray();
        }

        private static bool Fits(TagDef d, IList<string> have)
        {
            foreach (string t in d.with) if (have.IndexOf(t) < 0) return false;
            foreach (string t in d.without) if (have.IndexOf(t) >= 0) return false;
            return true;
        }

        // ── 道具 ──

        /// <summary>道具 ID 带不带这个标签（游戏自带的分类 / 标识标签，或组——组含上层）。</summary>
        internal static bool ItemHas(string itemId, string tag)
        {
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(tag)) return false;
            Build();
            if (itemTags.TryGetValue(itemId, out HashSet<string> set) && set.Contains(tag)) return true;
            return Array.IndexOf(ItemTags.Of(itemId), tag.ToLowerInvariant()) >= 0;
        }

        /// <summary>取池子：带齐这些标签的立绘 ID / 道具 ID。道具只在登记过贴图扩展的里找。</summary>
        internal static List<string> Pool(PaintTarget target, string[] tags)
        {
            var wanted = new List<string>();
            if (tags != null) foreach (string t in tags) { string n = TagDef.Norm(t); if (n != null) wanted.Add(n); }
            var list = new List<string>();
            if (target == PaintTarget.Portrait)
            {
                foreach (Portrait p in PortraitLibrary.Find(wanted.ToArray())) list.Add(p.Field);
                return list;
            }
            foreach (string id in PaintRules.KnownItemIds())
            {
                bool all = true;
                foreach (string t in wanted) if (!ItemHas(id, t)) { all = false; break; }
                if (all) list.Add(id);
            }
            list.Sort(string.CompareOrdinal);
            return list;
        }
    }
}
