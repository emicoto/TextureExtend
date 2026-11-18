// ClaudeNote: 0.9.12 立绘池（Lune 2026-11-18）。
//   游戏怎么给来客挑立绘：SpriteDict.GetPool("femaleLower") 给出 femaleLower1、2、3… 这些连续编号的键，再从里面挑一个最近没出现过的。
//   名字不是“池名 + 连续编号”的立绘（路人立绘扩展的 femaleBM_psrl_bm_f01、包里多出来的新编号、mod 登记的新角色）游戏自己抽不到。
//   这里做两件事：
//   1. 追加：游戏来要某个池时，把“属于这个池的新立绘”接在后面（Extend，挂在 GetPool 的后置上）。
//      · 只追加【不在游戏立绘表里】的立绘——原有的键怎么抽完全不动；别的 mod 自己写进表里的（那个 mod 启用着）也不归这里管。
//      · 来源要激活；归进 root_key 组的成员（它是别人的差分）不进池。
//      · 绝不改游戏给的那张表（那是游戏自己的缓存）：有东西要加时另建一张，没有就原样放行。
//      · 不往立绘表里写任何东西：追加的键只存在于“池”和来客的 spriteName 里，取图时由 PlanPort 的前置现取。
//        所以“表里现在是谁的图”“原图是哪张”这些判断不会被搅乱；包撤掉后存档里留下的键，游戏自己会退回随机拾荒者。
//   2. 归类：一张新立绘属于哪个池——mod 登记的（Assign / 表格 pool 行）→ 名字开头（femaleSec_xxx）→ 标签（generic + 性别 + 派系 / 职业）→ 都没有就是自定义池 Custom。
//      原版没有的池（femaleMiner、你们自己起的名字）游戏不会来要，等于只做了归类：mod 自己用 Members / Pick 取用。
//      Custom 是“自定义 NPC 的默认池”：自家 mod 加的角色立绘不用起池名也有地方待着、查得到。
//   成员表按立绘库的版本缓存；立绘库没变时，游戏每次来要池子只是一次字典查找。
using System;
using System.Collections.Generic;
using ProbablyExtendFramework.TagSystem;

namespace ProbablyExtendFramework
{
    public static class PortraitPools
    {
        /// <summary>自定义 NPC 的默认池：归不进任何池的新立绘都在这里。游戏不从这里抽。</summary>
        public const string Custom = "custom";
        internal const string PortName = "SpriteDict.GetPool";
        internal static bool Enabled = true;

        private static readonly Dictionary<string, KeyValuePair<string, string>> assigned = new Dictionary<string, KeyValuePair<string, string>>();   // 立绘 → (登记者, 池)
        private static Dictionary<string, List<string>> members;      // 池 → 新立绘
        private static int builtFor = -1;
        private static readonly Dictionary<string, KeyValuePair<List<string>, List<string>>> merged = new Dictionary<string, KeyValuePair<List<string>, List<string>>>();   // 池 → (游戏给的那张, 接好的那张)

        /// <summary>登记：这张立绘归这个池（原版的池名如 femaleLower，或自己起的名字）。pool 为空 = 取消登记。</summary>
        public static void Assign(string owner, string portraitId, string pool)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(portraitId)) return;
            if (string.IsNullOrEmpty(pool)) assigned.Remove(portraitId);
            else assigned[portraitId] = new KeyValuePair<string, string>(owner, pool.Trim());
            builtFor = -1;
        }

        internal static void Remove(string owner)
        {
            foreach (string id in new List<string>(assigned.Keys)) if (assigned[id].Key == owner) assigned.Remove(id);
            builtFor = -1;
        }

        /// <summary>这张立绘归哪个池。游戏立绘表里的键返回它自己的池名（femaleLower9 → femaleLower），认不出规律的返回 null。</summary>
        public static string PoolOf(string portraitId)
        {
            Portrait p = PortraitLibrary.Get(portraitId);
            return p == null ? null : PoolOf(p);
        }

        private static string PoolOf(Portrait p)
        {
            if (assigned.TryGetValue(p.Field, out KeyValuePair<string, string> a)) return a.Value;
            string byName = PortraitTags.PoolOf(p.Field) ?? PortraitTags.PoolOf(p.Field.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + "_");
            if (byName != null) return byName;
            string byTags = FromTags(p);
            return byTags ?? (p.InGameTable ? null : Custom);
        }

        // 标签 → 池名：要有“随机路人”标签（generic）+ 性别 + 标签全对得上的派系 / 职业词（表里长的在前，所以 SecCorpo 先于 Sec）。
        //   没有 generic 的不归：有名有姓的角色（剧情 NPC、自家 mod 的角色）哪怕带着 female、lower，也不该被当成随机路人抽出来。
        //   想让一张图进来客池：给它加 generic 标签（工坊里加，或登记时带上），或者直接 Assign。
        private static string FromTags(Portrait p)
        {
            if (!p.Has(PortraitTags.GenericTag) || p.Has(PortraitTags.Aug) || p.Has(PortraitTags.Wanted)) return null;
            string gender = p.Has(PortraitTags.Female) ? "female" : p.Has(PortraitTags.Male) ? "male" : null;
            if (gender == null) return null;
            foreach (string[] c in PortraitTags.Classes)
            {
                bool all = true;
                for (int i = 1; i < c.Length && all; i++) all = p.Has(c[i]);
                if (all) return gender + c[0];
            }
            return null;
        }

        /// <summary>这个池里的新立绘（不含游戏立绘表里原有的键）。</summary>
        public static List<string> Members(string pool)
        {
            Build();
            return pool != null && members.TryGetValue(pool, out List<string> list) ? new List<string>(list) : new List<string>();
        }

        /// <summary>有成员的池名（含 Custom）。</summary>
        public static List<string> Names()
        {
            Build();
            var names = new List<string>(members.Keys);
            names.Sort(string.CompareOrdinal);
            return names;
        }

        /// <summary>从池里挑一张（用游戏自己的挑法：尽量挑最近没出现过的）。原版的池连同游戏原有的键一起挑；空池返回 null。</summary>
        public static string Pick(string pool)
        {
            if (string.IsNullOrEmpty(pool)) return null;
            List<string> all = Members(pool);
            if (PortraitTags.PoolOf(pool + "_") != null && PortraitLibrary.Ready && SpriteDict.Instance.spriteDictionary.ContainsKey(pool + 1))
                foreach (string key in SpriteDict.GetPool(pool)) if (!all.Contains(key)) all.Add(key);
            return all.Count == 0 ? null : ClientSpriteVariety.Pick(all);
        }

        private static void Build()
        {
            int version = PortraitLibrary.Builds;
            if (members != null && builtFor == version) return;
            var next = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (Portrait p in PortraitLibrary.All)
            {
                if (p.InGameTable || !Sources.Active(p.Source) || RootJoin.IsMember(p.Field)) continue;
                string pool = PoolOf(p);
                if (pool == null) continue;
                if (!next.TryGetValue(pool, out List<string> list)) next[pool] = list = new List<string>();
                list.Add(p.Field);
            }
            foreach (List<string> list in next.Values) list.Sort(string.CompareOrdinal);
            members = next;
            builtFor = PortraitLibrary.Builds;   // 上面读立绘库时可能刚重建过
            merged.Clear();
        }

        /// <summary>游戏来要一个池：有新立绘就接在后面（另建一张表），没有就原样返回。cap 有限的（游戏只要前几张）不动。</summary>
        internal static List<string> Extend(string prefix, int cap, List<string> vanilla)
        {
            if (!Enabled || !PlanPort.Enabled || vanilla == null || cap != int.MaxValue || string.IsNullOrEmpty(prefix) || !PortraitLibrary.Ready) return vanilla;
            Build();
            if (!members.TryGetValue(prefix, out List<string> extra) || extra.Count == 0) return vanilla;
            if (merged.TryGetValue(prefix, out KeyValuePair<List<string>, List<string>> have) && ReferenceEquals(have.Key, vanilla)) return have.Value;
            var all = new List<string>(vanilla.Count + extra.Count);
            all.AddRange(vanilla);
            foreach (string id in extra) if (!all.Contains(id)) all.Add(id);
            merged[prefix] = new KeyValuePair<List<string>, List<string>>(vanilla, all);
            return all;
        }

        /// <summary>这个键是池里追加的新立绘（不在游戏立绘表里、立绘库认得）。PlanPort 的前置用：是就现取，不让游戏当成“找不到”。</summary>
        internal static bool IsExtra(string key)
        {
            Portrait p = PortraitLibrary.Get(key);
            return p != null && !p.InGameTable;
        }

        internal static void Install()
        {
            Ports.Patch(PortName, "新立绘加入来客立绘池", () => HarmonyLib.AccessTools.Method(typeof(SpriteDict), nameof(SpriteDict.GetPool)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(PortraitPools), nameof(Postfix)) { priority = HarmonyLib.Priority.Last });
        }

        private static void Postfix(string prefix, int cap, ref List<string> __result)
        {
            try { if (Painter.Running && !Painter.Retired) __result = Extend(prefix, cap, __result); }
            catch (Exception) { }   // 绝不让异常进游戏的来客流程
        }

        internal static string Report()
        {
            Build();
            var b = new System.Text.StringBuilder("portrait pools (new portraits only):");
            foreach (string name in Names()) b.Append("\n  ").Append(name).Append(": ").Append(members[name].Count);
            if (members.Count == 0) b.Append(" none");
            return b.ToString();
        }
    }
}
