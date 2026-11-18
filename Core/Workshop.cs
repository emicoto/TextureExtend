// ClaudeNote: 0.7.0 贴图扩展工坊的数据层（界面在 UI/WorkshopWindow.cs，标题界面入口在 UI/WorkshopEntry.cs）。
//   做什么：给已有的立绘 / 道具贴自定义标签、建标签组、定抽选条件，导出成一份注册表格（和别的 mod 写的 TextureExtend.csv 同格式）。
//   三份文件，都在 ModData/<本 mod>/Workshop/（只用游戏给的 ModData 接口，不需要 files 能力）：
//     Catalog.json        目录快照：有哪些立绘 / 道具、各自带什么标签。标题界面拿不到游戏的立绘表（只在店面场景里有），
//                         所以每次在店面里后台扫完一遍就存一份，工坊读这份。
//     Workshop.json       工坊的编辑状态（可以再打开接着改）。
//     TextureExtend.csv   导出的注册表格。框架启动时把它当作来源 Owner 读进来；放进任何 mod 的文件夹也能用。
//   立绘与道具各管各的（scope）：标签、标签组、抽选都按类别分开。
//   标签组 = 标签的归类（一棵树）；标签可以带“必须带 / 不能带”的条件。两者在表格里都是 tag 行（见 Csv）。
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using TextureExtend.TagSystem;

namespace TextureExtend
{
    internal static class Workshop
    {
        internal const string Owner = "Lune.TextureExtend.Workshop";
        internal const string Portrait = "portrait", Item = "item";
        private const string Folder = "Workshop/", StateFile = Folder + "Workshop.json", CatalogFile = Folder + "Catalog.json";
        internal const string CsvFile = Folder + "TextureExtend.csv";

        internal sealed class Target
        {
            public string id, src, key, name; public string[] tags; public string[] types; public string[] variants;
            public string atlas, sprite; public bool modSprite;   // 道具的图在哪（0.9.2：标题界面没有道具目录，缩略图靠这三样直接取）
        }   // variants：立绘的差分（工坊预览用）   // types：道具的主分类（小写）；立绘没有。key：只有 images 里的有，完整写法 来源:图ID~编号
        // ClaudeNote: 0.8.0 images = 全部贴图，一个来源一张（Lune：工坊窗口是全列出）。portrait 仍是一个图 ID 一条——
        //   标签、标签组、抽选都是按图 ID 定的（不分来源），池子与导出照旧用它；images 只给“目标”页的总览用。
        internal sealed class CatalogData
        {
            public List<Target> portrait = new List<Target>(), item = new List<Target>(), images = new List<Target>();
            public string version = "", stamp = "";   // 存这份目录的框架版本、当时的 mod 清单指纹（见 Stale）
            public Dictionary<string, string> names = new Dictionary<string, string>();   // 来源 ID → 显示名（标题界面问不到立绘包的名字）
        }

        // ClaudeNote: 0.8.6 目录过期提示（Lune 连着几版说“没变化”——她是在标题界面看的，而目录是 0.7.5 时在店面里存的那份，
        //   之后的版本从没机会重存；窗口上又没有任何地方说“这是旧数据”）。
        //   目录里记下存它的框架版本 + 当时启用的 mod 与激活的包；现在的对不上就是过期，窗口底部常驻提示。
        internal static string StampNow()
        {
            var parts = new List<string>();
            if (ModLoader.Mods != null)
                foreach (LoadedMod mod in ModLoader.Mods)
                    if (mod?.Manifest != null && (mod.IsEnabled || Sources.PackOn(mod.Manifest.ID))) parts.Add(mod.Manifest.ID + "@" + mod.Manifest.ModVersion);
            parts.Sort(string.CompareOrdinal);
            return string.Join("|", parts);
        }

        /// <summary>目录过期的原因；没过期返回 null。</summary>
        internal static string Stale()
        {
            if (List(Portrait).Count == 0 && List(Item).Count == 0) return null;   // 没有目录：另有提示
            bool zh = Entry.Chinese;
            if (Catalog.version != Entry.Version)
                return zh ? "这份目录是旧版框架（" + (Catalog.version.Length > 0 ? Catalog.version : "0.8.5 之前") + "）存的，内容不是现在的。读一次档、在店里等几秒，再回来。"
                          : "This catalog was saved by an older framework version. Load a save, wait a few seconds in the shop, then come back.";
            if (Catalog.stamp != StampNow())
                return zh ? "存这份目录之后，启用的 mod / 激活的立绘包变过。读一次档、在店里等几秒，再回来。" : "Mods or packs changed since this catalog was saved. Load a save, wait a few seconds in the shop, then come back.";
            return null;
        }
        internal sealed class Tag
        {
            public string scope;
            public bool group;
            public string under = "";
            public List<string> with = new List<string>(), without = new List<string>();
            internal bool HasConditions => with.Count > 0 || without.Count > 0;
        }
        // ClaudeNote: 0.8.1 标签组的含义照 Lune 的图例改了（2026-11-17）：
        //   之前：标签组 = 带条件的池子（上级 / 必须带 / 不能带），标签 = 只点名成员。
        //   现在：标签组 = 标签的归类（一棵树，“上游标签组”= 它挂在谁下面）；标签绑定到某个标签组。
        //         group = true 的 under 是上游标签组；group = false 的 under 是它绑定的标签组。“必须带 / 不能带”只属于标签（抽选页）。
        //   默认标签组不存在这里（见 Groups）：立绘 派系 / 性别 / 体型，道具 默认分类 / 标识性标签 / 扩展标签。
        internal sealed class State
        {
            public Dictionary<string, Tag> tags = new Dictionary<string, Tag>();
            public Dictionary<string, List<string>> mine = new Dictionary<string, List<string>>();   // "类别|目标 ID" → 贴上的标签 / 组
            public List<string> noManual = new List<string>();                                       // "类别|目标 ID"
            public Dictionary<string, string> limit = new Dictionary<string, string>();              // "类别|目标 ID" → 限制条件（触发条件的写法）
            public List<string> auto = new List<string>();                                           // "类别|目标 ID"：勾了“自动动态适配”（只有立绘）
            public Dictionary<string, string> join = new Dictionary<string, string>();               // 立绘 ID → 它归到的 root_key
            public Dictionary<string, string> place = new Dictionary<string, string>();              // "类别|自带标签" → 玩家把它拖进的标签组（"" = 拖到了未分组）
        }

        private static CatalogData catalog;
        private static State state;

        // ── 目录快照 ──

        internal static CatalogData Catalog
        {
            get
            {
                if (catalog != null) return catalog;
                catalog = Read<CatalogData>(CatalogFile) ?? new CatalogData();
                if (catalog.images == null) catalog.images = new List<Target>();
                if (catalog.names == null) catalog.names = new Dictionary<string, string>();
                catalog.version = catalog.version ?? "";
                catalog.stamp = catalog.stamp ?? "";
                return catalog;
            }
        }

        internal static List<Target> List(string scope) { return scope == Item ? Catalog.item : Catalog.portrait; }

        /// <summary>“目标”页的总览：道具一种一条；立绘每个来源的每张图一条（旧目录没有这份时退回一个图 ID 一条）。</summary>
        // portrait：一个 root_key 一条（标签、抽选按它）。images：每个来源的每张图一条（来源筛选、预览用）。

        /// <summary>来源的显示名：先用目录里记的（标题界面问不到没载入的立绘包叫什么）。</summary>
        internal static string SourceLabel(string source)
        {
            if (source == ItemDefs.Game) return Entry.Chinese ? "原版" : "Game";
            return Catalog.names != null && Catalog.names.TryGetValue(source, out string name) ? name : Sources.Label(source);
        }

        /// <summary>这张立绘在这个来源里的那张图（本体）；这个来源不提供返回 null。</summary>
        internal static Target ImageOf(string id, string source)
        {
            if (Catalog.images == null) return null;
            Target found = null;
            foreach (Target t in Catalog.images)
                if (t.id == id && t.src == source && (found == null || string.CompareOrdinal(t.key, found.key) < 0)) found = t;   // 同来源几张时取本体（没有 ~编号 的排最前）
            return found;
        }

        // 在店面里、后台完整扫完一遍后由 Baker 调（道具逐个分帧记）。building = 正在拼的新快照
        private static CatalogData building;
        private static string lastSaved;

        internal static void SnapshotBegin()
        {
            building = new CatalogData();
            foreach (Portrait p in PortraitLibrary.All)
            {
                string[] tags = Without(p.Tags, Portrait);
                building.portrait.Add(new Target { id = p.Field, src = p.Source, key = ImageKey.Make(p.Source, p.Field), tags = tags, variants = new List<string>(p.Variants).ToArray() });
                foreach (string key in PortraitLibrary.ImagesOf(p.Field))
                    building.images.Add(new Target { id = p.Field, key = key, src = ImageKey.SourceOf(key), tags = tags });
            }
        }

        internal static List<string> SnapshotItemIds() { return DirectoryMaster.GetIdentifierList<GameItem>() ?? new List<string>(); }

        internal static void SnapshotItem(string itemId)
        {
            if (building == null || string.IsNullOrEmpty(itemId)) return;
            ItemDef def = ItemDefs.Of(itemId);
            building.item.Add(new Target { id = itemId, name = def.name, src = def.source, atlas = def.atlas, sprite = def.sprite, modSprite = def.modSprite, tags = Without(ItemTags.Of(itemId), Item), types = Array.ConvertAll(def.types, t => t.ToLowerInvariant()) });
        }

        internal static void SnapshotEnd()
        {
            if (building == null) return;
            building.version = Entry.Version;
            building.stamp = StampNow();
            foreach (Target t in building.images) if (t.src != null && !building.names.ContainsKey(t.src)) building.names[t.src] = Sources.Label(t.src);
            foreach (Target t in building.item) if (t.src != null && !building.names.ContainsKey(t.src)) building.names[t.src] = Sources.Label(t.src);
            building.portrait.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            building.images.Sort((a, b) => a.id != b.id ? string.CompareOrdinal(a.id, b.id) : string.CompareOrdinal(a.key, b.key));
            building.item.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            string text = JsonConvert.SerializeObject(building);
            catalog = building;
            building = null;
            if (text == lastSaved) return;   // 内容没变不写盘
            lastSaved = text;
            ModHelper.WriteModDataFileText(Painter.ModId, CatalogFile, text);
        }

        // 快照里只记“自带的”标签：工坊自己加上去的（已经随表格生效、出现在立绘标签里）要去掉，否则下次打开会被当成自带的
        private static string[] Without(string[] tags, string scope)
        {
            var list = new List<string>();
            foreach (string t in tags) if (!(Current.tags.TryGetValue(t, out Tag mine) && mine.scope == scope)) list.Add(t);
            return list.ToArray();
        }

        // ── 编辑状态 ──

        internal static State Current
        {
            get
            {
                if (state != null) return state;
                state = Read<State>(StateFile) ?? new State();
                if (state.limit == null) state.limit = new Dictionary<string, string>();
                if (state.auto == null) state.auto = new List<string>();
                if (state.join == null) state.join = new Dictionary<string, string>();
                if (state.place == null) state.place = new Dictionary<string, string>();
                return state;
            }
        }

        /// <summary>丢掉没导出的改动，回到上次导出时的样子。</summary>
        internal static void Reload() { state = null; }

        internal static string Key(string scope, string id) { return scope + "|" + id; }

        internal static List<string> Mine(string scope, string id)
        {
            return Current.mine.TryGetValue(Key(scope, id), out List<string> list) ? list : empty;
        }
        private static readonly List<string> empty = new List<string>();

        internal static void Stick(string scope, string id, string tag)
        {
            if (!Current.mine.TryGetValue(Key(scope, id), out List<string> list)) Current.mine[Key(scope, id)] = list = new List<string>();
            if (!list.Contains(tag)) list.Add(tag);
        }

        internal static void Peel(string scope, string id, string tag)
        {
            if (!Current.mine.TryGetValue(Key(scope, id), out List<string> list)) return;
            list.Remove(tag);
            if (list.Count == 0) Current.mine.Remove(Key(scope, id));
        }

        /// <summary>当前类别里我建的标签 / 组的名字。kind：null = 都要，true = 只要组，false = 只要标签。</summary>
        internal static List<string> Names(string scope, bool? group = null)
        {
            var list = new List<string>();
            foreach (KeyValuePair<string, Tag> kv in Current.tags)
                if (kv.Value.scope == scope && (group == null || kv.Value.group == group.Value)) list.Add(kv.Key);
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>这个类别里自带的标签：内置基础标签库 + 目录里出现过的。</summary>
        internal static List<string> BuiltIn(string scope)
        {
            int at = scope == Item ? 1 : 0;
            if (builtIn[at] == null || builtInFor != Catalog)   // 每份目录只算一次（一览页一次重摆要问几百遍）
            {
                if (builtInFor != Catalog) { builtIn[0] = builtIn[1] = null; itemTypes = null; builtInFor = Catalog; }
                var set = new SortedSet<string>(scope == Item ? ItemTags.Library() : PortraitTags.Library(), StringComparer.Ordinal);   // 内置基础标签库
                foreach (Target t in List(scope)) foreach (string tag in t.tags) set.Add(tag);                                     // + 目录里实际出现过的
                builtIn[at] = new List<string>(set);
            }
            return new List<string>(builtIn[at]);
        }

        private static readonly List<string>[] builtIn = new List<string>[2];
        private static HashSet<string> itemTypes;
        private static CatalogData builtInFor;

        // ── 标签组 ──

        // 默认标签组：{ ID, 中文名, 英文名 }。归类规则在 TagSystem（PortraitTags.Groups / GroupOf、ItemTags.Groups）
        private static readonly string[][] groupNames =
        {
            new[] { PortraitTags.FactionGroup, "派系", "Faction" },
            new[] { PortraitTags.GenderGroup, "性别", "Gender" },
            new[] { PortraitTags.BodyGroup, "体型", "Body" },
            new[] { PortraitTags.EmotionGroup, "情绪", "Emotion" },
            new[] { ItemTags.Groups[ItemTags.Category], "默认分类", "Category" },
            new[] { ItemTags.Groups[ItemTags.Marker], "标识性标签", "Markers" },
            new[] { ItemTags.Groups[ItemTags.Extend], "扩展标签", "Extended" },
        };

        internal static string GroupLabel(string id)
        {
            foreach (string[] g in groupNames) if (g[0] == id) return Entry.Chinese ? g[1] : g[2];
            return id;
        }

        /// <summary>这个类别的默认标签组（固定顺序）。</summary>
        internal static List<string> DefaultGroups(string scope)
        {
            var list = new List<string>();
            if (scope == Item) list.AddRange(ItemTags.Groups);
            else foreach (string[] g in PortraitTags.Groups) list.Add(g[0]);
            return list;
        }

        /// <summary>这个类别的全部标签组：默认的在前，自建的在后。道具只有默认的三个。</summary>
        internal static List<string> GroupIds(string scope)
        {
            List<string> list = DefaultGroups(scope);
            if (scope != Item) list.AddRange(Names(scope, true));
            return list;
        }

        /// <summary>这个组挂在谁下面；顶层（或上游已经不在了）返回 null。默认组没有上游。</summary>
        internal static string Upstream(string scope, string group)
        {
            if (!Current.tags.TryGetValue(group, out Tag g) || !g.group || g.under.Length == 0 || g.under == group) return null;
            return GroupIds(scope).Contains(g.under) ? g.under : null;
        }

        /// <summary>层级：顶层 = 1。默认组按表（派系、性别 1，体型 2）；自建组 = 上游 + 1。</summary>
        internal static int Level(string scope, string group) { return Level(scope, group, 0); }

        private static int Level(string scope, string group, int depth)
        {
            foreach (string[] g in PortraitTags.Groups) if (g[0] == group) return int.Parse(g[1]);
            string up = depth < 16 ? Upstream(scope, group) : null;
            return up == null ? 1 : Level(scope, up, depth + 1) + 1;
        }

        /// <summary>这个标签归哪个标签组；不归任何组返回 null。</summary>
        internal static string GroupOfTag(string scope, string tag) { return GroupOfTag(scope, tag, null, null); }

        // groups / types：调用方成批问时先算好传进来（见 TagsByGroup），单问时现算
        private static string GroupOfTag(string scope, string tag, HashSet<string> groups, HashSet<string> types)
        {
            if (groups == null) groups = new HashSet<string>(GroupIds(scope));
            if (Current.tags.TryGetValue(tag, out Tag mine) && mine.scope == scope && !mine.group)
                return groups.Contains(mine.under) ? mine.under : scope == Item ? ItemTags.Groups[ItemTags.Extend] : null;
            if (!Fixed(scope, tag) && Current.place.TryGetValue(Key(scope, tag), out string placed))   // 扩展 / DLC 的标签被拖过：以拖到的地方为准（那个组后来删了就回默认）
            {
                if (placed.Length == 0) return null;
                if (groups.Contains(placed)) return placed;
            }
            if (scope != Item) return PortraitTags.GroupOf(tag);
            if ((types ?? TypeSet()).Contains(tag)) return ItemTags.Groups[ItemTags.Category];
            foreach (string word in ItemTags.Main[ItemTags.Extend]) if (string.Equals(word, tag, StringComparison.OrdinalIgnoreCase)) return ItemTags.Groups[ItemTags.Extend];
            return ItemTags.Groups[ItemTags.Marker];
        }

        /// <summary>这个类别的全部标签（自带的 + 我建的，不含标签组），排好序。</summary>
        internal static List<string> AllTags(string scope)
        {
            List<string> list = BuiltIn(scope);
            foreach (string name in Names(scope, false)) if (!list.Contains(name)) list.Add(name);
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>这个标签组里的标签；group 为 null = 全部。</summary>
        internal static List<string> TagsIn(string scope, string group)
        {
            List<string> all = AllTags(scope);
            if (group != null) all.RemoveAll(t => GroupOfTag(scope, t) != group);
            return all;
        }

        // ClaudeNote: 0.8.8 默认标签不能拖（Lune：总不能把 female / male 拖到派系、把派系拖到性别）。
        //   拖动是给扩展与 DLC 的自定义标签整理用的：你建的、以及各 mod / 立绘包登记进来的（custom、deepspace、hd…）。
        //   固定的 = 框架内置基础标签库里的（立绘：性别、派系 / 职业、aug、wanted、named、generic；道具：凡不是你建的）。
        internal static bool Fixed(string scope, string tag)
        {
            if (Current.tags.TryGetValue(tag, out Tag mine) && mine.scope == scope) return false;
            if (scope == Item) return true;
            if (fixedTags == null || fixedFor != PortraitTags.Classes.Count)   // 基础标签库只在登记新派系词时变：按条数认，不每问一次重建一遍
            {
                fixedTags = new HashSet<string>(PortraitTags.Library(), StringComparer.Ordinal);
                fixedFor = PortraitTags.Classes.Count;
            }
            return fixedTags.Contains(tag);
        }

        private static HashSet<string> fixedTags;
        private static int fixedFor;

        /// <summary>这个类别的全部标签按标签组分好（键 "" = 未分组）。标签一览一次重摆只算这一遍。</summary>
        internal static Dictionary<string, List<string>> TagsByGroup(string scope)
        {
            var map = new Dictionary<string, List<string>>();
            var groups = new HashSet<string>(GroupIds(scope));
            HashSet<string> types = scope == Item ? TypeSet() : null;
            foreach (string tag in AllTags(scope))
            {
                string g = GroupOfTag(scope, tag, groups, types) ?? "";
                if (!map.TryGetValue(g, out List<string> list)) map[g] = list = new List<string>();
                list.Add(tag);
            }
            return map;
        }

        /// <summary>把一个标签放进标签组（group 为空 = 未分组）。自建的改绑定；自带的只记“归在哪一类”（只影响工坊里的归类，不改它贴在谁身上）。</summary>
        internal static void Place(string scope, string tag, string group)
        {
            if (Current.tags.TryGetValue(tag, out Tag mine) && mine.scope == scope && !mine.group) mine.under = group ?? "";
            else Current.place[Key(scope, tag)] = group ?? "";
        }

        /// <summary>绑定：标签 → 归到这个组；标签组 → 挂到这个上游下面。会绕成圈的不绑，返回 false。</summary>
        internal static bool Bind(string name, string group)
        {
            if (!Current.tags.TryGetValue(name, out Tag t)) return false;
            group = group ?? "";
            if (t.group)
            {
                var seen = new HashSet<string>();
                for (string up = group; !string.IsNullOrEmpty(up) && seen.Add(up); up = Current.tags.TryGetValue(up, out Tag u) ? u.under : null)
                    if (up == name) return false;
            }
            t.under = group;
            return true;
        }

        /// <summary>新名字能不能用；不能用返回原因，能用返回 null。名字全局不重复（立绘、道具两边也不重名，免得弄混）。</summary>
        internal static string NameProblem(string name)
        {
            if (name.Length < 2 || name.Length > 24) return Entry.Chinese ? "名字要 2 到 24 个字。" : "Use 2 to 24 characters.";
            foreach (char c in name) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return Entry.Chinese ? "只能用小写字母、数字、下划线。" : "Lowercase letters, digits and _ only.";
            bool taken = Current.tags.ContainsKey(name) || BuiltIn(Portrait).Contains(name) || BuiltIn(Item).Contains(name);
            foreach (string[] g in groupNames) taken |= g[0] == name;
            return taken ? (Entry.Chinese ? "“" + name + "”已经有了。" : "\"" + name + "\" already exists.") : null;
        }

        internal static Tag Create(string name, string scope, bool group) { return Current.tags[name] = new Tag { scope = scope, group = group }; }

        /// <summary>删掉一个标签 / 组：贴在目标上的、被别的标签当条件用的、绑在它下面的，一起摘掉。</summary>
        internal static void Delete(string name)
        {
            if (!Current.tags.Remove(name)) return;
            foreach (string key in new List<string>(Current.mine.Keys))
            {
                Current.mine[key].Remove(name);
                if (Current.mine[key].Count == 0) Current.mine.Remove(key);
            }
            foreach (Tag g in Current.tags.Values)
            {
                g.with.Remove(name);
                g.without.Remove(name);
                if (g.under == name) g.under = "";
            }
        }

        /// <summary>现在收得到哪些目标。标签：点名贴上的 + 符合“必须带 / 不能带”的（和框架标签层的判定一致）。标签组：组里各标签、各下级组的合集。</summary>
        internal static List<Target> Pool(string name) { return Pool(name, new HashSet<string>()); }

        private static List<Target> Pool(string name, HashSet<string> seen)
        {
            var list = new List<Target>();
            if (name == null || !seen.Add(name) || !Current.tags.TryGetValue(name, out Tag g)) return list;
            if (g.group)
            {
                foreach (KeyValuePair<string, Tag> kv in Current.tags)
                    if (kv.Value.scope == g.scope && kv.Value.under == name)
                        foreach (Target t in Pool(kv.Key, seen)) if (!list.Contains(t)) list.Add(t);
                return list;
            }
            foreach (Target t in List(g.scope))
            {
                List<string> mine = Mine(g.scope, t.id);
                if (mine.Contains(name)) { list.Add(t); continue; }
                if (!g.HasConditions) continue;
                bool fits = true;
                foreach (string w in g.with) if (!Has(t, mine, w)) { fits = false; break; }
                if (fits) foreach (string w in g.without) if (Has(t, mine, w)) { fits = false; break; }
                if (fits) list.Add(t);
            }
            return list;
        }

        private static bool Has(Target t, List<string> mine, string tag) { return Array.IndexOf(t.tags, tag) >= 0 || mine.Contains(tag); }

        // ── 限制条件（0.8.2）：内置预设 + 各 mod 登记的具名条件。写法和表格 when 列一样 ──
        //   每行 = { 条件, 中文名, 英文名 }。改预设只改这张表。
        private static readonly string[][] presets =
        {
            new[] { "night", "夜间（关店后）", "Night (after closing)" },
            new[] { "!night", "白天（营业时）", "Daytime (open)" },
            new[] { "visitor:lower", "柜台来客是下层区的", "Visitor is Lower" },
            new[] { "visitor:upper", "柜台来客是上层区的", "Visitor is Upper" },
            new[] { "visitor:sec", "柜台来客是安保", "Visitor is Security" },
            new[] { "visitor:bm", "柜台来客是黑市的", "Visitor is Black Market" },
            new[] { "visitor:rev", "柜台来客是革命军", "Visitor is Revolution" },
            new[] { "visitor:cartel", "柜台来客是卡特尔", "Visitor is Cartel" },
            new[] { "visitor:church", "柜台来客是教会的", "Visitor is Church" },
            new[] { "day>=7", "第 7 天起", "From day 7" },
            new[] { "day>=30", "第 30 天起", "From day 30" },
            new[] { "rep:lower>=20", "下层区声望 20 以上", "Lower rep 20+" },
            new[] { "rep:upper>=20", "上层区声望 20 以上", "Upper rep 20+" },
        };

        /// <summary>可选的限制条件（条件写法）：预设在前，各 mod 登记的具名条件（cond:名字）在后。</summary>
        internal static List<string> Triggers()
        {
            var list = new List<string>();
            foreach (string[] p in presets) list.Add(p[0]);
            foreach (string name in Conditions.Names()) list.Add("cond:" + name);
            return list;
        }

        /// <summary>条件的显示名：预设用中文名，其余原样。</summary>
        internal static string TriggerLabel(string expr)
        {
            foreach (string[] p in presets) if (p[0] == expr) return Entry.Chinese ? p[1] : p[2];
            return expr;
        }

        internal static string LimitOf(string portraitId)
        {
            return Current.limit.TryGetValue(Key(Portrait, portraitId), out string expr) ? expr ?? "" : "";
        }


        /// <summary>道具的主分类一览（目录里出现过的，小写）。工坊“分类”下拉用。</summary>
        internal static List<string> ItemTypes()
        {
            var list = new List<string>(TypeSet());
            list.Sort(string.CompareOrdinal);
            return list;
        }

        private static HashSet<string> TypeSet()
        {
            if (itemTypes != null && builtInFor == Catalog) return itemTypes;
            BuiltIn(Item);   // 目录换了的话顺带把缓存清掉
            itemTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (Target t in Catalog.item) if (t.types != null) foreach (string type in t.types) itemTypes.Add(type);
            return itemTypes;
        }

        // ── 导出与生效 ──

        internal static string Csv()
        {
            var b = new StringBuilder("# TextureExtend Workshop export\nkind,target,id,group,under,with,without,manual,when\n");
            foreach (string scope in new[] { Portrait, Item })
            {
                var rows = new List<string>();
                foreach (KeyValuePair<string, List<string>> kv in Current.mine)
                {
                    if (!kv.Key.StartsWith(scope + "|", StringComparison.Ordinal)) continue;
                    string id = kv.Key.Substring(scope.Length + 1);
                    foreach (string tag in kv.Value) rows.Add("tag," + scope + "," + id + "," + tag + ",,,,,");
                }
                // 自建标签 / 标签组：上下级一行（只有上游也是自建组时才写——默认组只是归类，不是贴在目标上的标签），带条件的标签再一行
                foreach (string name in Names(scope))
                {
                    Tag g = Current.tags[name];
                    if (g.under.Length > 0 && Current.tags.TryGetValue(g.under, out Tag up) && up.group && up.scope == scope) rows.Add("tag," + scope + ",," + name + "," + g.under + ",,,,");
                    if (!g.group && g.HasConditions) rows.Add("tag," + scope + ",*," + name + ",," + string.Join(" ", g.with) + "," + string.Join(" ", g.without) + ",,");
                }
                foreach (string key in Current.noManual)
                    if (scope == Item && key.StartsWith(scope + "|", StringComparison.Ordinal)) rows.Add("tag," + scope + "," + key.Substring(scope.Length + 1) + ",,,,,no,");   // 手动选图只管道具
                if (scope == Portrait)
                {
                    foreach (KeyValuePair<string, string> kv in Current.join) rows.Add("join," + scope + "," + kv.Key + "," + kv.Value + ",,,,," + LimitOf(kv.Key));
                    foreach (string key in Current.auto)
                        if (key.StartsWith(scope + "|", StringComparison.Ordinal)) { string id = key.Substring(scope.Length + 1); rows.Add("auto," + scope + "," + id + ",,,,,," + LimitOf(id)); }
                }
                if (rows.Count == 0) continue;
                rows.Sort(string.CompareOrdinal);
                b.Append("# ── ").Append(scope).Append(" ──\n");
                foreach (string row in rows) b.Append(row).Append('\n');
            }
            return b.ToString();
        }

        /// <summary>导出：存下编辑状态、写注册表格、立刻让它生效。出错返回原因，成功返回 null。</summary>
        internal static string Export()
        {
            try
            {
                ModHelper.WriteModDataFileText(Painter.ModId, StateFile, JsonConvert.SerializeObject(Current));
                ModHelper.WriteModDataFileText(Painter.ModId, CsvFile, Csv());
                Apply();
                return null;
            }
            catch (Exception ex) { Health.Report("workshop export", ex); return ex.Message; }
        }

        /// <summary>把导出的表格当作来源 Owner 读进框架（启动时、每次导出后）。先撤掉上一次读进来的。</summary>
        internal static void Apply()
        {
            try
            {
                Painter.UnregisterAll(Owner);
                string text = ModHelper.ReadModDataFileText(Painter.ModId, CsvFile);
                if (!string.IsNullOrEmpty(text)) CsvLoader.Text(Owner, Painter.ModId, text, "workshop");
            }
            catch (Exception ex) { Health.Report("workshop apply", ex); }
        }

        internal static void Shutdown()
        {
            Painter.UnregisterAll(Owner);
            catalog = null;
            ItemDefs.Clear();
            building = null;
            state = null;
            lastSaved = null;
        }

        private static T Read<T>(string file) where T : class
        {
            try
            {
                string text = ModHelper.ReadModDataFileText(Painter.ModId, file);
                return string.IsNullOrEmpty(text) ? null : JsonConvert.DeserializeObject<T>(text);
            }
            catch (Exception ex) { Health.Report("workshop read " + file, ex); return null; }
        }
    }
}
