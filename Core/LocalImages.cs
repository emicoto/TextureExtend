// ClaudeNote: 1.0.0 rc6 本地图片（Lune 2026-11-18 确认放哪、怎么认）。
//   放哪：存档目录下 ModData/Lune_ProbablyExtendFramework/Local/（和 Player.log 同一个目录里的 ModData）。
//     之前在伴生 mod 自己的文件夹里：从工坊订阅时那是 steamapps/workshop/content/…/<条目ID>/Local，难找，退订会被 Steam 连同里面的图一起删；
//     往 mod 文件夹里放东西还会改变游戏给它算的文件夹指纹（ModFingerprint.HashFolder），Mods 菜单里一直标“已更新”。
//   谁做什么：列目录要“文件”权限 → 伴生 mod 只负责列出文件（相对路径 + 大小-修改时间），经 Painter.UseLocalLister 交给框架；
//     怎么认、怎么登记都在这里；图片内容由框架用到时自己读（读自己的 ModData 不需要权限）。
//   什么时候扫：每次读档后一次（只是列文件、比记号，很轻）；工坊“来源一览”里“本地图片”那行的“重新扫描”。
//   怎么认（按顺序；所在的每层文件夹名都成为标签，文件夹名是来客池时见下）：
//     1. 文件名是原版立绘键 / 按原版结构命名（femaleLower3、femaleLower3_angry）      → 那张立绘多一个来源 / 一个差分
//     2. 文件名是 道具ID_差分名（道具 ID 取游戏道具目录里对得上的最长的那个）          → 那种道具的一个差分（进道具贴图组）
//     3. 同一文件夹里有“更短的同前缀文件”（Clara_story.png 之于 Clara_story_ep1_accept.png）→ 最短的那个是本体，后面整段是差分名（ep1_accept）
//     4. 其余                                                                          → 一张新立绘，ID = 文件名
//   系列没有本体图（只有 Clara_story_ep1 / _ep2…，没有 Clara_story.png）：不猜公共前缀（cat_a / cat_b 这种无关的图会被并成一组），
//     各自按第 3、4 条处理，并在日志里提示“像是一个系列但缺本体图”。
//   文件夹名是来客池（femaleLower、femaleSec-保安女…）：里面的新立绘带上那个池的标签，并且直接进那个池（会作为来客被抽到）。
using System;
using System.Collections.Generic;
using ProbablyExtendFramework.TagSystem;

namespace ProbablyExtendFramework
{
    internal static class LocalImages
    {
        internal const string Folder = "Local";
        internal static Func<IList<string[]>> Lister;   // 伴生 mod 给的：每项 { 相对 ModData 文件夹的路径（Local/…，用 /）, 记号 }
        internal static int Count { get; private set; }
        internal static bool Available => Lister != null;

        private struct LocalFile { internal string rel, dir, stem, stamp; }

        /// <summary>重新列一遍并登记。返回登记了几张；没有伴生 mod 返回 -1。</summary>
        internal static int Scan()
        {
            Painter.UnregisterAll(PortraitLibrary.LocalSource);
            Count = 0;
            if (Lister == null) return -1;
            IList<string[]> files;
            try { files = Lister(); }
            catch (Exception ex) { Health.Report("local images", ex); return 0; }
            if (files == null || files.Count == 0) return 0;

            var entries = new List<LocalFile>();
            foreach (string[] f in files)
            {
                if (f == null || f.Length < 1 || string.IsNullOrEmpty(f[0])) continue;
                string rel = f[0].Replace('\\', '/');
                int slash = rel.LastIndexOf('/'), dot = rel.LastIndexOf('.');
                if (dot <= slash + 1) continue;
                entries.Add(new LocalFile { rel = rel, dir = slash < 0 ? "" : rel.Substring(0, slash), stem = rel.Substring(slash + 1, dot - slash - 1), stamp = f.Length > 1 ? f[1] : "" });
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.rel, b.rel));

            var stems = new Dictionary<string, List<string>>();   // 文件夹 → 里面的文件名
            foreach (LocalFile e in entries)
            {
                if (!stems.TryGetValue(e.dir, out List<string> list)) stems[e.dir] = list = new List<string>();
                list.Add(e.stem);
            }
            List<string> itemIds = ItemIds();
            var warned = new HashSet<string>();

            foreach (LocalFile e in entries)
            {
                try
                {
                    string[] tags = FolderTags(e.dir, out string pool);
                    string id, variant;
                    if (Originals.Get(e.stem) != null) { id = e.stem; variant = null; pool = null; }
                    else if (PortraitTags.TryStructure(e.stem, out string field, out string suffix) && e.stem.StartsWith(field, StringComparison.Ordinal)) { id = field; variant = suffix.Length > 0 ? suffix : null; pool = null; }
                    else if (SplitItem(e.stem, itemIds, out string itemId, out string look))
                    {
                        ItemArt.AddFile(PortraitLibrary.LocalSource, itemId, look, ExternalImages.DataSig(e.rel, e.stamp));
                        if (tags.Length > 0) VariantTags.Add(PortraitLibrary.LocalSource, PaintTarget.StaticItem, itemId, look, tags);
                        Count++;
                        continue;
                    }
                    else
                    {
                        Group(e.stem, stems[e.dir], out id, out variant, out string missing);
                        if (missing != null && warned.Add(e.dir + "/" + missing))
                            Entry.Warn("local images: " + e.dir + "/" + missing + "_… looks like a series but has no base image (" + missing + ".png); each file is treated on its own");
                        id = CleanId(id);
                    }
                    if (id.Length == 0) continue;
                    PortraitLibrary.RegisterDataFile(PortraitLibrary.LocalSource, id, e.rel, variant, e.stamp, tags);
                    if (pool != null && variant == null) PortraitPools.Assign(PortraitLibrary.LocalSource, id, pool);
                    Count++;
                }
                catch (Exception ex) { Health.Report("local image", e.rel + ": " + ex.Message); }
            }
            if (Count > 0) Entry.Info("local images: " + Count + " registered from ModData/" + Folder + "/");
            return Count;
        }

        private static List<string> ItemIds()
        {
            try
            {
                List<string> ids = DirectoryMaster.GetIdentifierList<GameItem>() ?? new List<string>();
                ids = new List<string>(ids);
                ids.Sort((a, b) => b.Length.CompareTo(a.Length));   // 长的先：道具 ID 自己也带下划线
                return ids;
            }
            catch (Exception) { return new List<string>(); }
        }

        // ── 纯逻辑（离线测试 verification_match 直接测） ──

        /// <summary>文件名 = 道具ID_差分名？ids 按长度从长到短。</summary>
        internal static bool SplitItem(string stem, List<string> ids, out string itemId, out string variant)
        {
            itemId = null; variant = null;
            foreach (string id in ids)
            {
                if (string.IsNullOrEmpty(id) || stem.Length <= id.Length + 1 || stem[id.Length] != '_' || !stem.StartsWith(id, StringComparison.Ordinal)) continue;
                itemId = id;
                variant = stem.Substring(id.Length + 1);
                return true;
            }
            return false;
        }

        /// <summary>同一文件夹里的归组：有更短的同前缀文件 → 最短的那个是本体，后面整段是差分名；没有 → 自己就是一张立绘。
        /// missing：这张像是某个系列的一员（还有别的文件和它共用“去掉最后一段”的前缀）但那个前缀本身没有文件。</summary>
        internal static void Group(string stem, List<string> siblings, out string id, out string variant, out string missing)
        {
            id = stem; variant = null; missing = null;
            string root = null;
            foreach (string s in siblings)
                if (s.Length < stem.Length - 1 && stem[s.Length] == '_' && stem.StartsWith(s, StringComparison.Ordinal) && (root == null || s.Length < root.Length)) root = s;
            if (root != null) { id = root; variant = stem.Substring(root.Length + 1); return; }
            int bar = stem.LastIndexOf('_');
            if (bar <= 0) return;
            string prefix = stem.Substring(0, bar);
            foreach (string s in siblings)
            {
                if (s == stem || s.Length <= prefix.Length || s[prefix.Length] != '_' || !s.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (s.Length > stem.Length && s[stem.Length] == '_' && s.StartsWith(stem, StringComparison.Ordinal)) continue;   // 那是这张自己的差分（这张就是本体），不算“兄弟”
                missing = prefix;
                return;
            }
        }

        // 立绘 ID 里不能有 / @ # | : ~（框架留给“来源:图ID~编号”等写法用）
        internal static string CleanId(string stem)
        {
            var b = new System.Text.StringBuilder(stem.Length);
            foreach (char c in stem.Trim()) b.Append("/@#|:~".IndexOf(c) >= 0 ? '_' : c);
            return b.ToString();
        }

        // 所在的每一层文件夹名 → 标签（Local 这一层不算）；文件夹名是来客池时带上那个池的标签，并给出池名
        private static string[] FolderTags(string dir, out string pool)
        {
            pool = null;
            var tags = new List<string>();
            string[] parts = dir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                string p = PortraitTags.PoolOf(part);
                if (p != null)
                {
                    pool = p;
                    foreach (string t in PortraitTags.Of(p + "1")) if (t != PortraitTags.GenericTag && !tags.Contains(t)) tags.Add(t);
                }
                else if (part.IndexOfAny(new[] { '@', '#', '|' }) < 0)
                {
                    string tag = part.Trim().ToLowerInvariant();
                    if (tag.Length > 0 && !tags.Contains(tag)) tags.Add(tag);
                }
            }
            return tags.ToArray();
        }
    }

    public static partial class Painter
    {
        /// <summary>伴生 mod 交来“本地图片文件夹里有哪些文件”的列举函数（列目录要文件权限，框架本体没有）。
        /// 每项 { 相对本框架 ModData 文件夹的路径（Local/…）, 文件变没变的记号 }。传 null = 撤掉。</summary>
        public static void UseLocalLister(Func<IList<string[]>> lister)
        {
            LocalImages.Lister = lister;
            if (lister == null) UnregisterAll(PortraitLibrary.LocalSource);
        }

        /// <summary>重新扫描本地图片（读档后框架自己会扫一次）。返回登记了几张；没有伴生 mod 返回 -1。</summary>
        public static int RescanLocalImages() { return LocalImages.Scan(); }
    }
}
