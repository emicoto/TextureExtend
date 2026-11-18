// ClaudeNote: 0.6.0 只读立绘包（Lune 2026-11-16）：玩家订阅了、但没启用的立绘 mod，只读它文件夹里的图片，它的代码保持不运行。
//   好处：那个 mod 的补丁、界面组件一概不生效（不会互相打架）；图片用到哪张才读哪张，不像启用时启动就全载进内存。
//   怎么读：游戏给 mod 的官方接口 ModHelper.ReadModFileBytes / ModFileExists（按 mod ID + 相对路径，不需要任何权限）。
//   限制：没有“文件”权限不能列目录，只能读“猜得到名字”的文件。所以这里内置的读法只有三种：
//     keyed  文件按原版立绘键命名：Textures/<键>.png、Textures/Items/cp_portrait_<键>.png、Portraits/Special/<键>.png
//     index  自带索引 portraits.json（dictionary: [{runtimeKey, relativePath}]）
//     csv    包里自带 TextureExtend.csv（数据型立绘包的推荐写法；图片用 “模组ID:相对路径” 指）
//   文件名猜不到的包（按池分文件夹、任意命名）由可选的伴生 mod（Lune.TextureExtendPacks，声明“文件”权限）列目录后经 Offer 交进来；
//   框架本体保持零权限。附带登记表的包（深空娘化，见 BundledTables）也上这个架子。
//   ClaudeNote: 0.8.0（Lune 2026-11-18：加载时全读；没激活的只当候选素材）
//     之前：玩家在“来源设置”里打开的包才登记，没打开的包里有什么图框架完全不知道。
//     现在：架子上认得出的包全部登记进立绘库（只登记“有哪些图”，图片用到哪张才读哪张），开关的含义改成“激活”：
//       激活的包  和启用中的 mod 一样参加自动挑选（排在游戏原图前面），来客也会用到它。
//       没激活的  只当候选：选图窗口、工坊里看得到、玩家可以点名用它；自动挑选时排在游戏原图后面，
//                 互相之间按包自带的优先级声明排，没声明的按名称排（Sources.Choose）。
//     包自带的声明：包文件夹根目录放一个 TextureExtend.json（整个包的注册文件）：
//       { "name": "显示名", "priority": 10（或 "top" / "top 10" = TopPriority）, "images": [ { "id": "femaleLower9", "path": "Textures/a.png", "variant": "" } ] }
//       name / priority / images 都可以省；images 省了就按下面三种内置读法找图。
//   激活的包记在 ModData 的 Sources.json 里（packsOn）。
//   启用中的 mod 不上架：它的图游戏已经载入，走立绘库原来的识别（MatchModSprites）与显示方案里的让出。
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using ProbablyExtendFramework.TagSystem;

namespace ProbablyExtendFramework
{
    /// <summary>伴生 mod 交进来的一张图：立绘 ID（原版键，或 池名+编号 的新立绘）、图片在包文件夹里的相对路径、差分名（本体为空）。</summary>
    public struct PackEntry
    {
        public string Id, Path, Variant;
        /// <summary>0.9.9 这张图不是文件夹里的文件，而是打包在这个 mod 的 DLL 里的资源（Path = 资源名）。</summary>
        public bool Embedded;
        /// <summary>0.9.12 子来源的文件夹名（包里带着几套完整的图时，每套一个；见 PortraitLibrary.ResourceTargets）。一般为空。</summary>
        public string Sub;
        public PackEntry(string id, string path, string variant = null, bool embedded = false) { Id = id; Path = path; Variant = variant; Embedded = embedded; Sub = null; }
    }

    public static class PackShelf
    {
        public sealed class Shelf
        {
            public string ModId, Name, Kind;
            /// <summary>登记了几张（打开过才知道；没打开为 -1）。</summary>
            public int Count = -1;
            internal List<PackEntry> offered;
        }

        private static readonly string[] keyedPaths = { "Textures/{0}.png", "Textures/Items/cp_portrait_{0}.png", "Portraits/Special/{0}.png" };
        private static readonly string[] sentinels = { "femaleLower1", "maleLower1", "femaleScav1", "eleanor1" };
        private const string IndexFile = "portraits.json", CsvFile = "TextureExtend.csv", JsonFile = "TextureExtend.json";
        private const string RetextureFile = "retexture.tsv", F10Xml = "portraits.xml", F10Shim = "PortraitPackShim.dll";

        private static readonly Dictionary<string, Shelf> offers = new Dictionary<string, Shelf>();
        private static readonly Dictionary<string, int> applied = new Dictionary<string, int>();   // 包 → 登记张数

        /// <summary>这个 mod 的图片现在允许读吗：启用中的 mod，或登记在架子上的包（0.8.0：没激活的也能读，玩家可以点名用它的图）。</summary>
        public static bool CanRead(string modId)
        {
            LoadedMod mod = Find(modId);
            return mod != null && (mod.IsEnabled || applied.ContainsKey(modId));
        }

        // 登记用的来源名 → 包的 mod ID（一般就是包自己的 ID；附带登记表的包用的是表里的归属名）
        private static readonly Dictionary<string, string> packOf = new Dictionary<string, string>();

        /// <summary>这个来源是架子上没激活的包（只当候选素材）。</summary>
        internal static bool Dormant(string source)
        {
            return source != null && packOf.TryGetValue(source, out string pack) && applied.ContainsKey(pack) && !Sources.PackOn(pack);
        }

        /// <summary>架子上这个包登记用的来源名（一般就是包的 mod ID；附带登记表的包用表里的归属名）。</summary>
        internal static string SourceOf(string packModId) { return BundledTables.Has(packModId) ? BundledTables.OwnerOf(packModId) : packModId; }

        /// <summary>这个包登记了哪些来源（带几套图的包有几个子来源；还没登记时就是包自己）。</summary>
        internal static List<string> SourcesOf(string packModId)
        {
            var list = new List<string>();
            foreach (KeyValuePair<string, string> kv in packOf) if (kv.Value == packModId) list.Add(kv.Key);
            if (list.Count == 0) list.Add(SourceOf(packModId));
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>来源名 → 包的 mod ID（子来源也认）；不是架子上的包返回 null。</summary>
        internal static string PackOf(string source) { return source != null && packOf.TryGetValue(source, out string pack) ? pack : null; }

        /// <summary>伴生 mod 用：把一个未启用的包的图片清单交给框架（上架，默认只当候选素材）。再交一次 = 换清单。</summary>
        public static void Offer(string packModId, IEnumerable<PackEntry> entries)
        {
            LoadedMod mod = Find(packModId);
            if (mod == null || entries == null) return;
            var list = new List<PackEntry>();
            foreach (PackEntry e in entries) if (!string.IsNullOrEmpty(e.Id) && !string.IsNullOrEmpty(e.Path)) list.Add(e);
            if (list.Count == 0) { offers.Remove(packModId); return; }
            kinds.Remove(packModId);
            offers[packModId] = new Shelf { ModId = packModId, Name = mod.Manifest.Name, Kind = "listed", offered = list };
            Withdraw(packModId);   // 已经登记过的：换成新清单
            Apply();
            Painter.NeedScan = true;
        }

        // ClaudeNote: 0.9.10 Lune：“白蛇不启用了，作为来源没被探测到”——她是在标题界面看的。伴生 mod 的清单要等读档后才交得出来
        //   （认图要对原版立绘表，标题界面没有），所以读档之前架子上根本没有这个包，工坊“来源”页也就没有它。
        //   现在分两步：伴生 mod 在标题界面先“报到”（只说这个没启用的 mod 是个立绘包，够在来源页露面、激活、设为主来源），读档后再交完整清单。
        private static readonly HashSet<string> announced = new HashSet<string>();

        /// <summary>伴生 mod 用：报到一个没启用的立绘包（清单稍后经 Offer 交来）。</summary>
        public static void Announce(string packModId)
        {
            LoadedMod mod = Find(packModId);
            if (mod != null && !mod.IsEnabled) announced.Add(packModId);
        }

        /// <summary>伴生 mod 用：这个文件名 / 资源名的最后一段（不带扩展名）看起来是不是一张立绘。标题界面也能问（那时对的是工坊目录里记的原版立绘键）。</summary>
        public static bool LooksLikePortrait(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return false;
            if (PortraitLibrary.Ready ? Originals.Get(stem) != null : Workshop.KnowsGameKey(stem)) return true;
            return ResourceAliases.Of(stem) != null || (PortraitTags.TryStructure(stem, out string field, out _) && stem.StartsWith(field, StringComparison.Ordinal));
        }

        /// <summary>架子上有哪些包（未启用的 mod 里认得出的）。只做几次“文件在不在”的探测，开窗口时才调。</summary>
        public static List<Shelf> List()
        {
            var list = new List<Shelf>();
            if (ModLoader.Mods == null) return list;
            foreach (LoadedMod mod in ModLoader.Mods)
            {
                string id = mod?.Manifest?.ID;
                if (id == null || mod.IsEnabled || id == Painter.ModId) continue;
                string kind = KindOf(id);
                if (kind == null) continue;
                list.Add(new Shelf { ModId = id, Name = mod.Manifest.Name ?? id, Kind = kind, Count = applied.TryGetValue(id, out int n) ? n : -1 });
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return list;
        }

        private static readonly Dictionary<string, string> kinds = new Dictionary<string, string>();   // 探测结果（本次运行内不变；"" = 不是立绘包）

        private static string KindOf(string modId)
        {
            if (offers.ContainsKey(modId) || announced.Contains(modId)) return "listed";
            if (!kinds.TryGetValue(modId, out string kind)) kinds[modId] = kind = Probe(modId) ?? "";
            return kind.Length > 0 ? kind : null;
        }

        private static string Probe(string modId)
        {
            try
            {
                if (BundledTables.Has(modId)) return "table";
                if (ModHelper.ModFileExists(modId, JsonFile)) return "json";
                if (ModHelper.ModFileExists(modId, CsvFile)) return "csv";
                if (ModHelper.ModFileExists(modId, IndexFile)) return "index";
                if (ModHelper.ModFileExists(modId, RetextureFile)) return "tsv";
                if (ModHelper.ModFileExists(modId, F10Xml) || ModHelper.ModFileExists(modId, F10Shim)) return "f10";
                foreach (string pattern in keyedPaths)
                    foreach (string key in sentinels)
                        if (ModHelper.ModFileExists(modId, string.Format(pattern, key))) return "keyed";
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>玩家激活 / 取消激活一个包。登记不动，只是自动挑选时的档次变了。</summary>
        public static void Set(string modId, bool on)
        {
            Sources.SetPackOn(modId, on);
            Apply();
            Sources.Applied();
        }

        /// <summary>把架子上还没登记的包登记进立绘库；已经被玩家启用成正常 mod 的、文件夹没了的撤下。原版立绘表就绪后调（读档后、后台扫描开头、开关包时）。</summary>
        internal static void Apply()
        {
            if (!PortraitLibrary.Ready || ModLoader.Mods == null) return;
            bool changed = false;
            foreach (string id in new List<string>(applied.Keys))
            {
                LoadedMod mod = Find(id);
                if (mod == null || mod.IsEnabled) { Withdraw(id); changed = true; }
            }
            foreach (LoadedMod mod in ModLoader.Mods)
            {
                string id = mod?.Manifest?.ID;
                if (id == null || mod.IsEnabled || id == Painter.ModId || applied.ContainsKey(id) || KindOf(id) == null) continue;
                if (announced.Contains(id) && !offers.ContainsKey(id) && Probe(id) == null) continue;   // 只报到了、清单还没交来：等 Offer
                int n = 0;
                try { n = Register(id, mod); }
                catch (Exception ex) { Entry.Warn("read-only pack " + id + " not registered: " + ex.Message); }
                applied[id] = n;
                changed = true;
                Entry.Info("read-only pack " + id + ": " + n + " image(s) listed" + (Sources.PackOn(id) ? " (active)" : " (candidate only)") + "; the mod itself stays inactive");
            }
            if (changed) PortraitLibrary.Invalidate();
        }

        private static void Withdraw(string modId)
        {
            if (!applied.Remove(modId)) return;
            if (BundledTables.Has(modId)) { BundledTables.Remove(modId); return; }
            foreach (string source in SourcesOf(modId)) { Painter.UnregisterAll(source); if (source != modId) packOf.Remove(source); }
        }

        internal static void Reset()
        {
            foreach (string id in new List<string>(applied.Keys)) Withdraw(id);
            offers.Clear();
            announced.Clear();
            kinds.Clear();
        }

        private static int Register(string id, LoadedMod mod)
        {
            if (BundledTables.Has(id)) { packOf[BundledTables.OwnerOf(id)] = id; return BundledTables.Load(id); }
            packOf[id] = id;
            int declared = Declare(id, mod);
            if (declared > 0) return declared;
            if (offers.TryGetValue(id, out Shelf offer))
            {
                bool plain = false;
                foreach (PackEntry e in offer.offered)
                {
                    string source = PortraitLibrary.SubSource(id, e.Sub);
                    plain |= source == id;
                    packOf[source] = id;
                    if (e.Embedded) PortraitLibrary.RegisterEmbedded(source, e.Id, id, e.Path, e.Variant);
                    else PortraitLibrary.RegisterFile(source, e.Id, id, e.Path, e.Variant);
                }
                if (!plain && offer.offered.Count > 0) packOf.Remove(id);   // 图全在子来源里：包自己这个名字下面没有图，不占一行
                return offer.offered.Count;
            }
            if (ModHelper.ModFileExists(id, CsvFile)) return CsvLoader.File(id, CsvFile, id);
            if (ModHelper.ModFileExists(id, IndexFile)) return RegisterIndex(id);
            if (ModHelper.ModFileExists(id, RetextureFile)) return RegisterRetexture(id);
            int n = RegisterKeyed(id);
            if (ModHelper.ModFileExists(id, F10Xml) || ModHelper.ModFileExists(id, F10Shim)) n += RegisterF10(id);
            return n;
        }

        // ClaudeNote: 0.8.4 retexture.tsv（深空荡铺·立绘库）：每行 目标键 \t 图片相对路径 \t 参数。# 开头是注释。
        private static int RegisterRetexture(string id)
        {
            int n = 0;
            foreach (string raw in (ModHelper.ReadModFileText(id, RetextureFile) ?? "").Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] cells = line.Split('\t');
                if (cells.Length < 2 || cells[0].Trim().Length == 0 || cells[1].Trim().Length == 0) continue;
                PortraitLibrary.RegisterFile(id, cells[0].Trim(), id, cells[1].Trim().Replace('\\', '/'), null);
                n++;
            }
            return n;
        }

        // ClaudeNote: 0.8.4 “立绘库前置-F10管理器”格式的包（魔裁、Overnight 这类，带 PortraitPackShim.dll / portraits.xml）。
        //   F10 管理器读的是这几处（见它的 portrait_catalog.txt）：
        //     Portraits/Special/<键>.png                 具名角色（上面的 RegisterKeyed 已经收了）
        //     Portraits/Variants/<差分ID>/[Special/]<键>.png  portraits.xml 里 <Variant id=… pools="键,键…"> 列的键的另一张图 → 同来源的第 2、3… 张
        //     Portraits/<池>/<槽位数字>.png               换掉原版这个槽位（femaleLower 的 3 = femaleLower3）
        //     Portraits/<池>/auto_<名字>.png              往这个池子里追加一张新立绘
        //   池文件夹也可以写成“池-中文名”（Overnight 就是：femaleLower-下层市民女）。框架不能列目录（零权限），
        //   所以只能按猜得到的名字探测：文件夹名用下面这张表（F10 管理器的标准写法），auto_ 后面只认数字（auto_1、auto_2…，中间可以缺号，连续 10 个没有就停）。
        //   起了别的名字的（auto_xiaoming.png）猜不到，要装伴生 mod 列目录。
        private static readonly string[] f10Folders =
        {
            "augBMFemale-改造人黑市女", "augBMMale-改造人黑市男", "augLLFemale-改造人下层女", "augLLMale-改造人下层男", "augULFemale-改造人上层女", "augULMale-改造人上层男",
            "femaleBM-黑市商人女", "femaleChef-厨师女", "femaleEng-工程师女", "femaleFarmer-农夫女", "femaleLower-下层市民女", "femaleMed-医生女", "femaleMerc-雇佣兵女",
            "femaleRev-革命者女", "femaleScav-拾荒者女", "femaleSci-科学家女", "femaleSec-保安女", "femaleSecCorpo-企业保安女", "femaleSecOff-便衣安保女", "femaleUpper-上层市民女",
            "maleBM-黑市商人男", "maleChef-厨师男", "maleEng-工程师男", "maleFarmer-农夫男", "maleLower-下层市民男", "maleMed-医生男", "maleMerc-雇佣兵男",
            "maleRev-革命者男", "maleScav-拾荒者男", "maleSci-科学家男", "maleSec-保安男", "maleSecCorpo-企业保安男", "maleSecOff-便衣安保男", "maleUpper-上层市民男",
        };

        private static int RegisterF10(string id)
        {
            int n = 0;
            // 池：原版里“前缀 + 编号”的那些前缀，各有几个槽位
            var pools = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string key in Originals.Keys())
            {
                int end = key.Length;
                while (end > 0 && char.IsDigit(key[end - 1])) end--;
                if (end == key.Length || end == 0 || PortraitTags.PoolOf(key.Substring(0, end)) == null) continue;
                string pool = key.Substring(0, end);
                pools[pool] = Math.Max(pools.TryGetValue(pool, out int had) ? had : 0, int.Parse(key.Substring(end)));
            }
            foreach (KeyValuePair<string, int> pool in pools)
            {
                var folders = new List<string> { "Portraits/" + pool.Key };
                foreach (string f in f10Folders) if (f.StartsWith(pool.Key + "-", StringComparison.Ordinal)) folders.Add("Portraits/" + f);
                string[] tags = PortraitTags.Of(pool.Key + "1");
                foreach (string folder in folders)
                {
                    for (int slot = 1; slot <= pool.Value; slot++)
                    {
                        string path = folder + "/" + slot + ".png";
                        if (!ModHelper.ModFileExists(id, path)) continue;
                        PortraitLibrary.RegisterFile(id, pool.Key + slot, id, path, null);
                        n++;
                    }
                    for (int k = 1, miss = 0; miss < 10 && k <= 500; k++)
                    {
                        string path = folder + "/auto_" + k + ".png";
                        if (!ModHelper.ModFileExists(id, path)) { miss++; continue; }
                        miss = 0;
                        PortraitLibrary.RegisterFile(id, pool.Key + "_" + id + "_auto" + k, id, path, null, tags);   // 追加的新立绘：ID 带上包名，免得两个包的 auto_1 撞在一起
                        n++;
                    }
                }
            }
            // portraits.xml：优先级、差分
            string xml = ModHelper.ModFileExists(id, F10Xml) ? ModHelper.ReadModFileText(id, F10Xml) ?? "" : "";
            System.Text.RegularExpressions.Match pack = System.Text.RegularExpressions.Regex.Match(xml, "<Pack\\b[^>]*\\bpriority=\"(-?\\d+)\"");
            if (pack.Success) Packs.Declare(id, id, Find(id)?.Manifest?.Name ?? id, int.Parse(pack.Groups[1].Value));
            foreach (System.Text.RegularExpressions.Match v in System.Text.RegularExpressions.Regex.Matches(xml, "<Variant\\b[^>]*>"))
            {
                string vid = Attr(v.Value, "id"), list = Attr(v.Value, "pools");
                if (vid == null || list == null) continue;
                foreach (string raw in list.Split(','))
                {
                    string key = raw.Trim();
                    if (key.Length == 0) continue;
                    foreach (string sub in new[] { "/", "/Special/" })   // 魔裁的差分放在 Variants/<差分ID>/Special/<键>.png
                    {
                        string path = "Portraits/Variants/" + vid + sub + key + ".png";
                        if (!ModHelper.ModFileExists(id, path)) continue;
                        PortraitLibrary.RegisterFile(id, key, id, path, null);   // 同来源同 ID 的另一张 → 立绘库分编号
                        n++;
                    }
                }
            }
            return n;
        }

        private static string Attr(string element, string name)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(element, "\\b" + name + "=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // 包自带的声明文件：名字、优先级、（可选）图片清单。返回清单登记了几张；没有清单返回 0，接着走内置读法。
        private static int Declare(string id, LoadedMod mod)
        {
            string name = mod.Manifest.Name ?? id;
            int priority = 0, n = 0;
            bool top = false;
            JArray images = null;
            if (ModHelper.ModFileExists(id, JsonFile))
            {
                try
                {
                    JObject root = JObject.Parse(ModHelper.ReadModFileText(id, JsonFile) ?? "{}");
                    name = (string)root["name"] ?? name;
                    JToken pr = root["priority"];   // 数字，或 "top" / "top 10"
                    priority = pr == null ? 0 : pr.Type == JTokenType.Integer ? (int)pr : Packs.ParsePriority((string)pr, out top);
                    top |= (bool?)root["topPriority"] ?? false;
                    images = root["images"] as JArray;
                }
                catch (Exception ex) { Entry.Warn(id + "/" + JsonFile + " not read: " + ex.Message); }
            }
            Packs.Declare(id, id, name, priority, null, top);
            if (images == null) return 0;
            foreach (JToken row in images)
            {
                string key = (string)row["id"], path = (string)row["path"];
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(path)) continue;
                PortraitLibrary.RegisterFile(id, key, id, path.Replace('\\', '/'), (string)row["variant"]);
                n++;
            }
            return n;
        }

        // ClaudeNote: 0.8.0 一个键在几个文件夹下各有一张图的，全部登记（同来源同名 → 立绘库自动分编号）。之前只取先找到的那张。
        private static int RegisterKeyed(string id)
        {
            int n = 0;
            foreach (string key in Originals.Keys())
                foreach (string pattern in keyedPaths)
                {
                    string path = string.Format(pattern, key);
                    if (!ModHelper.ModFileExists(id, path)) continue;
                    PortraitLibrary.RegisterFile(id, key, id, path, null);
                    n++;
                }
            return n;
        }

        private static int RegisterIndex(string id)
        {
            int n = 0;
            JObject root = JObject.Parse(ModHelper.ReadModFileText(id, IndexFile) ?? "{}");
            if (!(root["dictionary"] is JArray rows)) return 0;
            foreach (JToken row in rows)
            {
                string key = (string)row["runtimeKey"], path = (string)row["relativePath"];
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(path) || Originals.Get(key) == null) continue;
                PortraitLibrary.RegisterFile(id, key, id, path.Replace('\\', '/'), null);
                n++;
            }
            return n;
        }

        private static LoadedMod Find(string modId) { return ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == modId); }
    }

    /// <summary>框架附带登记表的立绘包（表在框架的 DLC 文件夹里，图片在那个包自己的文件夹里）。目前只有深空娘化。
    /// 那个包启用着：自动登记（配置项 dlcDeepSpace 可关）。没启用：上只读架子，由玩家打开。</summary>
    internal static class BundledTables
    {
        private static readonly Dictionary<string, string[]> tables = new Dictionary<string, string[]>
        {
            // 包的 mod ID → { 登记归属名, 表的路径 }
            { "player.launchgiftportraits", new[] { "TextureExtend.DLC.DeepSpace", "DLC/DeepSpace/TextureExtend.csv" } },
        };
        private static readonly HashSet<string> loaded = new HashSet<string>();
        internal static bool AutoForEnabled = true;

        internal static bool Has(string packModId) { return tables.ContainsKey(packModId); }
        internal static string OwnerOf(string packModId) { return tables[packModId][0]; }

        internal static int Load(string packModId)
        {
            if (!tables.TryGetValue(packModId, out string[] t) || !loaded.Add(packModId)) return 0;
            return CsvLoader.File(Painter.ModId, t[1], t[0]);
        }

        internal static void Remove(string packModId)
        {
            if (!tables.TryGetValue(packModId, out string[] t) || !loaded.Remove(packModId)) return;
            Painter.UnregisterAll(t[0]);
        }

        /// <summary>启用中的包：按配置自动登记 / 撤掉。资源载入完成时、那个包载入时调。</summary>
        internal static void ApplyEnabled()
        {
            foreach (string id in tables.Keys)
            {
                LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == id);
                if (mod == null || !mod.IsEnabled) continue;   // 没启用的归只读架子管
                if (AutoForEnabled) { try { Load(id); } catch (Exception ex) { Entry.Warn("bundled table for " + id + " not loaded: " + ex.Message); } }
                else Remove(id);
            }
            PortraitLibrary.Invalidate();
            Painter.NeedScan = true;
        }

        internal static void Reset() { foreach (string id in new List<string>(loaded)) Remove(id); }
    }
}
