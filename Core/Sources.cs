// ClaudeNote: 第 4 步——立绘来源分发。同一张立绘可以由游戏和多个立绘包提供，按这个顺序选一个：
//   1. 玩家按角色指定（选图窗口右栏上面的“来源”下拉框，或来源设置窗口）
//   2. 玩家按组指定（来源设置窗口：例如 @lower 一律用某个包）
//   3. 玩家的全局顺序（来源设置窗口里排在前面的先）
//   4. 默认：见 Choose 上面的说明（0.8.0：激活的来源 → 游戏原图 → 没激活的候选素材）
//   玩家的选择存在 ModData/Lune.TextureExtend/Sources.json（跨存档通用，不写进存档）。
//   游戏里怎么生效：见 Display/Plan.cs（显示方案）。
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using TextureExtend.TagSystem;

namespace TextureExtend
{
    public static class Sources
    {
        private const string File = "Sources.json";

        private sealed class Settings
        {
            public List<string> order = new List<string>();
            public List<string[]> groups = new List<string[]>();                   // [标签, 来源]
            public Dictionary<string, string> portraits = new Dictionary<string, string>();
            public List<string> packsOn = new List<string>();                       // 打开的只读立绘包（未启用的 mod），见 Compat/PackShelf.cs
        }

        private static Settings settings;

        private static Settings S
        {
            get
            {
                if (settings != null) return settings;
                settings = new Settings();
                try
                {
                    string text = ModHelper.ReadModDataFileText(Painter.ModId, File);
                    if (!string.IsNullOrEmpty(text)) settings = JsonConvert.DeserializeObject<Settings>(text) ?? new Settings();
                }
                catch (Exception ex) { Entry.Warn("source settings not read: " + ex.Message); }
                if (settings.order == null) settings.order = new List<string>();
                if (settings.groups == null) settings.groups = new List<string[]>();
                if (settings.portraits == null) settings.portraits = new Dictionary<string, string>();
                if (settings.packsOn == null) settings.packsOn = new List<string>();
                return settings;
            }
        }

        // ── 只读立绘包的开关（PackShelf 用） ──

        internal static bool PackOn(string modId) { return modId != null && S.packsOn.Contains(modId); }

        internal static void SetPackOn(string modId, bool on)
        {
            if (string.IsNullOrEmpty(modId) || S.packsOn.Contains(modId) == on) return;
            if (on) S.packsOn.Add(modId); else S.packsOn.Remove(modId);
            Write();
        }

        // ── 选择 ──

        // ClaudeNote: 0.8.0 自动挑来源的顺序（Lune 2026-11-18）：
        //   1. 玩家的选择：按角色 → 按组 → 全局顺序
        //   1.5 宣称了 TopPriority 的来源（0.8.3，Lune 2026-11-17：同 root_key 同差分、来自不同来源时，宣称 TopPriority 的优先）。
        //       几个都宣称时比 priority 数字，再比载入先后。没激活的候选素材宣称了也不算（它本来就不参加自动挑选）。
        //   2. 道具自己的 mod 提供这张图（owner）：自家道具配自家图
        //   3. 激活中的来源（启用的 mod、玩家在架子上打开的包）：声明的 priority 大的先，同级后载入的先
        //   4. 游戏原图
        //   5. 没激活的候选素材（订阅了没启用、也没在架子上打开的包）：自带优先级声明的按声明，否则按名称排
        //   第 5 档排在游戏原图后面是我定的：没启用的包默认不该顶掉原图，只在“只有它有这张图”时才轮到；玩家随时可以点名用它。
        internal static Candidate Choose(Portrait p, string owner = null)
        {
            if (p.candidates.Count == 0) return null;
            if (S.portraits.TryGetValue(p.Field, out string mine) && p.CandidateOf(mine) is Candidate a) return a;
            foreach (string[] g in S.groups)
                if (g != null && g.Length == 2 && p.Has(g[0]) && p.CandidateOf(g[1]) is Candidate b) return b;
            foreach (string src in S.order)
                if (p.CandidateOf(src) is Candidate c) return c;
            Candidate top = null;
            foreach (Candidate c in p.candidates)
                if (Packs.TopOf(c.source) && Active(c.source) && (top == null || Before(c, top))) top = c;
            if (top != null) return top;
            if (owner != null && owner != PortraitLibrary.GameSource && p.CandidateOf(owner) is Candidate own) return own;
            Candidate best = null;
            foreach (Candidate c in p.candidates) if (best == null || Before(c, best)) best = c;
            return best;
        }

        private static int Tier(string source) { return source == PortraitLibrary.GameSource ? 1 : Active(source) ? 2 : 0; }

        private static bool Before(Candidate a, Candidate b)
        {
            int ta = Tier(a.source), tb = Tier(b.source);
            if (ta != tb) return ta > tb;
            int pa = Packs.PriorityOf(a.source), pb = Packs.PriorityOf(b.source);
            if (pa != pb) return pa > pb;
            if (ta == 2) return a.order > b.order;
            return string.Compare(Label(a.source), Label(b.source), StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>这个来源现在算“激活”：游戏原图、启用中的 mod、玩家在架子上打开的包。没激活的只当候选素材。</summary>
        public static bool Active(string source) { return !PackShelf.Dormant(source); }

        // ── 查询（扩展 API） ──

        /// <summary>这张立绘的全部来源（"game" 或 mod ID）。</summary>
        public static List<string> Of(string portraitId) { return PortraitLibrary.Get(portraitId)?.Sources ?? new List<string>(); }

        /// <summary>这张立绘现在用的来源。</summary>
        public static string Selected(string portraitId) { return PortraitLibrary.Get(portraitId)?.Source; }

        /// <summary>玩家按角色指定的来源；没指定返回 null。</summary>
        public static string Chosen(string portraitId) { return portraitId != null && S.portraits.TryGetValue(portraitId, out string s) ? s : null; }

        /// <summary>出现过的全部来源（游戏 + 提供过立绘的 mod / 立绘包），全局顺序里的排前面。</summary>
        public static List<string> All()
        {
            var list = new List<string>(S.order);
            foreach (Portrait p in PortraitLibrary.All)
                foreach (Candidate c in p.candidates) if (!list.Contains(c.source)) list.Add(c.source);
            if (!list.Contains(PortraitLibrary.GameSource)) list.Add(PortraitLibrary.GameSource);
            return list;
        }

        internal static List<string> Order => new List<string>(S.order);
        internal static List<string[]> Groups => new List<string[]>(S.groups);
        internal static Dictionary<string, string> PerPortrait => new Dictionary<string, string>(S.portraits);

        /// <summary>来源的显示名：声明过 pack 的用包名，游戏原图叫“游戏原图”，其余用 mod 名。</summary>
        public static string Label(string source)
        {
            if (source == PortraitLibrary.GameSource) return Entry.Chinese ? "游戏原图" : "Game";
            if (source == PortraitLibrary.LocalSource) return Entry.Chinese ? "本地图片" : "Local images";
            if (source == ItemDefs.UnknownMod) return Entry.Chinese ? "mod（未识别）" : "Mod (unidentified)";
            if (source == Originals.TableSource) return Entry.Chinese ? "其他 mod 写进表的" : "Written by another mod";
            string pack = Packs.NameOf(source);
            if (pack != null) return pack;
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == source);
            return mod?.Manifest?.Name ?? source;
        }

        // ── 玩家设置 ──

        /// <summary>按角色指定（null = 取消指定）。</summary>
        public static void ChooseFor(string portraitId, string source)
        {
            if (string.IsNullOrEmpty(portraitId)) return;
            if (string.IsNullOrEmpty(source)) S.portraits.Remove(portraitId);
            else S.portraits[portraitId] = source;
            Save();
        }

        /// <summary>按组 / 标签指定（source 为 null = 取消这个组的指定）。</summary>
        public static void ChooseForGroup(string tag, string source)
        {
            tag = TagDef.Norm(tag);
            if (tag == null) return;
            S.groups.RemoveAll(g => g == null || g.Length != 2 || g[0] == tag);
            if (!string.IsNullOrEmpty(source)) S.groups.Add(new[] { tag, source });
            Save();
        }

        /// <summary>全局顺序里把这个来源往前（-1）或往后（+1）挪一位；不在顺序里的先加到末尾。</summary>
        public static void Move(string source, int delta)
        {
            if (string.IsNullOrEmpty(source)) return;
            List<string> order = S.order;
            if (!order.Contains(source)) order.Add(source);
            int i = order.IndexOf(source), j = Mathf.Clamp(i + delta, 0, order.Count - 1);
            if (i == j) { Save(); return; }
            order.RemoveAt(i);
            order.Insert(j, source);
            Save();
        }

        /// <summary>从全局顺序里拿掉（回到默认排法）。</summary>
        public static void Unorder(string source) { if (S.order.Remove(source)) Save(); }

        public static void ResetAll()
        {
            settings = new Settings();
            Save();
        }

        private static void Save() { Write(); Applied(); }

        private static void Write()
        {
            try { ModHelper.WriteModDataFileText(Painter.ModId, File, JsonConvert.SerializeObject(S, Formatting.Indented)); }
            catch (Exception ex) { Entry.Warn("source settings not saved: " + ex.Message); }
        }

        // 选择变了：立绘库重选、显示用的图作废、后台按新来源重画（指纹带来源，画过的道具会自动接回新图）
        internal static void Applied()
        {
            PortraitLibrary.Invalidate();
            Display.Clear();
            Painter.NeedScan = true;
            Painter.RestoreAll();   // 0.8.0 “自动”的道具立刻按新的优先级重挑来源（点名了来源的不动）
        }
    }
}
