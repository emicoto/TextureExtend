// ClaudeNote: 0.7.1 TagSystem.ItemTags——道具的内置基础标签库（Lune 2026-11-17 起草的结构）。
//   Main 三行：
//     [Category] 游戏道具的主分类（物品的 itemTypes：FOOD、DOCUMENT…）              —— 动态导入
//     [Marker]   游戏的标识性标签（物品定义里 EnableTag 的字符串标签：IMPORTANT_TAG…） —— 动态导入
//     [Extend]   框架内置的道具扩展标签（WATERING、GROWING、COLORABLE、DYNAMICTEXTURE = 动态贴图道具），mod 可用 Regist.Library 追加
//   “动态导入”：游戏没有静态的道具定义表，分类和标识标签都写在每种道具的工厂函数里，所以读到哪种道具就导入它带的词。
//     读法见 Game/ItemDefs.cs（照道具定义读，每种道具只读一次）。
//     不从店里的真物品上读：真物品身上还挂着运行时状态（TEXEXT_CUR、好感度…），那些不是道具“自带”的标签。
//   比较一律不分大小写（库里按游戏原样存大写，标签层里统一小写）。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework.TagSystem
{
    public static class ItemTags
    {
        public const int Category = 0, Marker = 1, Extend = 2;

        public static readonly List<string[]> Main = new List<string[]>
        {
            new string[0],                                  // 游戏道具的主分类，动态导入
            new string[0],                                  // 游戏标识性标签，动态导入
            new[] { "WATERING", "GROWING", "COLORABLE", "DYNAMICTEXTURE", "SICK", "POISONED" },   // 内置的道具扩展标签
        };

        /// <summary>道具只有这三个标签组（Lune 2026-11-17），和 Main 的三行一一对应：默认分类、标识性标签、扩展标签。</summary>
        public static readonly string[] Groups = { "category", "marker", "extend" };

        private static readonly Dictionary<string, string[]> own = new Dictionary<string, string[]>();   // 道具 ID → 自带标签（小写；分类在前、标识在后）

        /// <summary>这种道具自带的标签（游戏主分类 + 标识性标签），全小写。ID 不存在返回空。</summary>
        public static string[] Of(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return Array.Empty<string>();
            if (own.TryGetValue(itemId, out string[] have)) return have;
            var list = new List<string>();
            ItemDef def = ItemDefs.Of(itemId);   // 0.7.4 道具定义统一由 Game/ItemDefs 读（一种道具只跑一次工厂）
            foreach (string type in def.types) Import(Category, type, list);
            foreach (string marker in def.markers) Import(Marker, marker, list);
            return own[itemId] = list.ToArray();
        }

        /// <summary>道具自带不带这个标签（不分大小写）。</summary>
        public static bool Has(string itemId, string tag) { return tag != null && Array.IndexOf(Of(itemId), tag.ToLowerInvariant()) >= 0; }

        /// <summary>往库里的一行加词（已有的不重复加）。Regist.Library 走这里。</summary>
        public static void Add(int row, params string[] words)
        {
            if (row < 0 || row >= Main.Count || words == null) return;
            foreach (string word in words) Import(row, word, null);
        }

        /// <summary>库里的全部标签（三行合起来），去重，小写。工坊的筛选与条件下拉用。</summary>
        public static List<string> Library()
        {
            var list = new List<string>();
            foreach (string[] row in Main)
                foreach (string word in row) { string t = word.ToLowerInvariant(); if (!list.Contains(t)) list.Add(t); }
            return list;
        }

        private static void Import(int row, string word, List<string> into)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            word = word.Trim();
            string lower = word.ToLowerInvariant();
            if (into != null && !into.Contains(lower)) into.Add(lower);
            foreach (string have in Main[row]) if (string.Equals(have, word, StringComparison.OrdinalIgnoreCase)) return;
            var grown = new string[Main[row].Length + 1];
            Main[row].CopyTo(grown, 0);
            grown[grown.Length - 1] = word;
            Main[row] = grown;
        }
    }
}
