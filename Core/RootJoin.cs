// ClaudeNote: 0.8.2 root_key 组 + 0.8.3 自动动态适配（Lune 2026-11-17）。
//   root_key 组：命名过于自由的立绘（别的包里叫 my_girl_alt 这种）结构识别认不出它和哪张原版立绘是一组。
//     玩家（或 mod）指定它属于 femaleLower9 之后，它成为 femaleLower9 的一个差分候选（差分名 = 它自己的 ID，小写），
//     选图窗口、工坊预览里都看得到，可以手动切过去。图不复制：差分的取图函数直接转去取那张立绘（换了来源也跟着换）。
//     带限制条件的：道具上条件成立时自动切到它（一条规则）。
//   自动动态适配：不改游戏的来客机制——游戏照旧按派系抽一个立绘键（femaleLower9），框架只决定这个键显示哪张图。
//     勾了的立绘（Auto）在来客取图时实时判断：
//       · 归进 root_key 组的成员：限制条件成立（没写 = 一直成立）→ 来客显示成它。几个都成立时先登记的优先。
//       · root_key 自己：写了限制条件时，成立 → 显示框架选中的来源（TopPriority / 来源设置）；不成立 → 游戏原图。
//     没勾的不会自己变：来客照“来源设置”选定的那张显示（静态），成员只是候选。
//   表格：join 行（id = 这张立绘，group = root_key，when = 限制条件）、auto 行（id，when）；代码：Painter.Join、Painter.AutoReplace。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    internal static class RootJoin
    {
        private sealed class Join
        {
            internal string owner, root, member, state;
            internal Func<GameItem, bool> when;
        }

        private sealed class Auto
        {
            internal string owner, portrait;
            internal Func<GameItem, bool> when;
        }

        private static readonly List<Join> joins = new List<Join>();
        private static readonly List<Auto> autos = new List<Auto>();

        /// <summary>member 进 root 的组。when 为空 = 只当候选；不为空 = 道具上成立时自动切到它。</summary>
        internal static bool Add(string owner, string member, string root, Func<GameItem, bool> when)
        {
            string state = PortraitLibrary.Normal(member);
            if (string.IsNullOrEmpty(owner) || state.Length == 0 || string.IsNullOrEmpty(root) || member == root) return false;
            PortraitLibrary.RegisterVariant(owner, root, state, () => PortraitLibrary.Resolve(member));
            if (when != null) PaintRules.Add(owner, PaintTarget.Portrait, root).Tag(state).When(when);   // 道具：规则成立就切到这个差分
            joins.Add(new Join { owner = owner, root = root, member = member, state = state, when = when });
            return true;
        }

        /// <summary>勾上自动动态适配。when 为空 = 按它在 root_key 组里的条件（成员）/ 一直（root_key 自己）。</summary>
        internal static void SetAuto(string owner, string portrait, Func<GameItem, bool> when)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(portrait)) return;
            autos.RemoveAll(a => a.owner == owner && a.portrait == portrait);
            autos.Add(new Auto { owner = owner, portrait = portrait, when = when });
        }

        /// <summary>这张立绘归进了别人的 root_key 组（它是那张立绘的差分候选，不是独立的来客立绘）。</summary>
        internal static bool IsMember(string portrait) { return joins.Exists(j => j.member == portrait); }

        /// <summary>root 的这个差分其实是归进来的另一张立绘（差分名是那张立绘的 ID，不是状态名——不拆成标签）。</summary>
        internal static bool IsMemberVariant(string root, string variant) { return joins.Exists(j => j.root == root && j.state == variant); }

        internal static void Remove(string owner)
        {
            joins.RemoveAll(j => j.owner == owner);
            autos.RemoveAll(a => a.owner == owner);
        }

        internal const string Vanilla = "\n";   // StateFor 的特殊结果：显示游戏原图

        /// <summary>来客取 root 这张图时：换成哪个差分（成员）；Vanilla = 显示游戏原图；null = 照常（来源设置选定的那张）。</summary>
        internal static string StateFor(string root)
        {
            if (autos.Count == 0 || string.IsNullOrEmpty(root)) return null;
            foreach (Join j in joins)
            {
                if (j.root != root || PortraitLibrary.Get(j.member) == null) continue;
                Auto a = Find(j.member);
                if (a != null && Holds(a.when ?? j.when)) return j.state;
            }
            Auto self = Find(root);
            return self != null && self.when != null && !Holds(self.when) ? Vanilla : null;
        }

        private static Auto Find(string portrait)
        {
            foreach (Auto a in autos) if (a.portrait == portrait) return a;
            return null;
        }

        private static bool Holds(Func<GameItem, bool> when)
        {
            if (when == null) return true;
            try { return when(null); }
            catch (Exception) { return false; }
        }

        internal static int Count => joins.Count + autos.Count;
    }
}
