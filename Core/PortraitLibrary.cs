// ClaudeNote: 立绘库：现在有哪些立绘、各带什么标签、图由谁提供、有哪些差分。底层只管“认得出、取得到”，不含任何玩法。
//   立绘从哪来：
//     a. 游戏的立绘表 —— 整张 SpriteDict.spriteDictionary 一次读完，别的 mod 往表里加的字段一并收（“字段_后缀”收成差分）。
//     b. 字段结构识别 —— 各 mod 的 Textures/Items 与 Textures 里按“性别 + 派系或职业 + 编号 + _后缀”命名的图（PortraitTags.TryStructure）。
//     c. 注册 —— 表格（TextureExtend.csv）或代码 Register 登记的立绘（ID 自定；与游戏字段同名 = 接管那个字段的图）。
//   一张立绘的图怎么取（Resolve），前面的优先：注册 → 字段结构识别到的 mod 图（加载顺序靠后的生效）→ SpriteDict.GetSprite(字段)。
//   标签：游戏字段的标签由 PortraitTags 按字段名解析（派系、性别、职业…）；mod 可用 AddTags 追加。
//   差分：同一张立绘在某个状态下的另一张图，用状态标签区分（love、infected、night…）。
//   唯一 ID = 字段名 / 注册 ID。换了来源，同 ID 的缓存被新图替换；撤掉来源，缓存还在，仍按 ID 读得到（由 Painter 负责）。
//   重建时机：立绘表换了实例（进店面 / 换场景）、有新的注册、mod 载入、或手动 Invalidate。重建只过一遍各 mod 的贴图键，不读像素。
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TextureExtend.TagSystem;

namespace TextureExtend
{
    /// <summary>一张立绘。</summary>
    public sealed class Portrait
    {
        /// <summary>唯一 ID：游戏字段名（femaleLower9、merchant…）或注册时给的 ID。</summary>
        public string Field { get; internal set; }
        /// <summary>标签，全小写。</summary>
        public string[] Tags { get; internal set; }
        /// <summary>本体图现在由谁提供："game" 或 mod 的 ID。</summary>
        public string Source { get; internal set; } = PortraitLibrary.GameSource;
        /// <summary>在游戏的立绘表里（来客可能用到）。只靠注册进来的为 false。</summary>
        public bool InGameTable { get; internal set; }
        /// <summary>图要从磁盘现读（外部立绘包），取一次比较贵。</summary>
        public bool External { get; internal set; }

        // ClaudeNote: 第 4 步 同一张立绘可以由多个来源提供（游戏、各立绘包），各自带自己的差分；
        //   Sources.Choose 选出一个，选中的那个的图与差分抄到下面这几个“当前”字段上。没有本体、只补差分的来源算共享差分，跟着谁都能用。
        internal List<Candidate> candidates = new List<Candidate>();
        internal Dictionary<string, Func<Sprite>> sharedVariants;
        internal Dictionary<string, string> sharedSigs;
        // ClaudeNote: 0.6.4 专用取景图（腰上 / 胸上）：注册方自己给的那一张，不从全身图裁。键 = PortraitLibrary.FrameKey(用途, 状态)。
        //   和差分一样分“来源自己的”和“共享的”（来源没有本体、只补图）；frames = 当前选中来源生效的那份。
        internal Dictionary<string, Framed> sharedFrames, frames;

        /// <summary>提供这张立绘本体的全部来源（"game" 或 mod ID），按登记先后。</summary>
        public List<string> Sources { get { var l = new List<string>(); foreach (Candidate c in candidates) l.Add(c.source); return l; } }

        /// <summary>只有没激活的候选素材提供这张（订阅了没启用、也没在架子上激活的包）。这种立绘不进自动的池子、不预处理，
        /// 只在选图窗口和工坊里看得到，由玩家点名用。</summary>
        public bool CandidateOnly => candidates.TrueForAll(c => !TextureExtend.Sources.Active(c.source));

        internal Candidate CandidateOf(string source) { return candidates.Find(c => c.source == source); }

        internal string sortName;                            // 排序用：去掉编号的名字
        internal int sortNumber;                             // 排序用：编号
        internal Func<Sprite> provider;                      // 本体图；null = 走 GetSprite
        internal string providerMod;
        internal Dictionary<string, Func<Sprite>> variants;  // 状态标签 → 图
        internal string fileSig;                             // 外部图片的指纹（不读图就能算），本体
        internal Dictionary<string, string> variantSigs;     // 同上，差分

        public bool Has(string tag) { return Array.IndexOf(Tags, tag) >= 0; }

        /// <summary>带齐这些标签（标签须已是小写）。</summary>
        public bool HasAll(IList<string> tags)
        {
            if (tags == null) return true;
            for (int i = 0; i < tags.Count; i++) if (!Has(tags[i])) return false;
            return true;
        }

        public bool HasVariant(string variant) { return variants != null && variants.ContainsKey(PortraitLibrary.Normal(variant)); }

        /// <summary>这个用途（腰上 / 胸上）在这个状态下有没有注册方专门给的图（没有 = 取图时从全身图裁）。</summary>
        public bool HasFrame(Display.Usage usage, string variant = null) { return frames != null && frames.ContainsKey(PortraitLibrary.FrameKey(usage, PortraitLibrary.Normal(variant))); }

        /// <summary>这张立绘有图的差分。</summary>
        public ICollection<string> Variants => variants != null ? (ICollection<string>)variants.Keys : Array.Empty<string>();
    }

    /// <summary>一张专用取景图：怎么取、外部图片的指纹（不是外部图片为 null）。</summary>
    internal sealed class Framed
    {
        internal Func<Sprite> sprite;
        internal string fileSig;
    }

    /// <summary>一个来源对一张立绘的供图。</summary>
    internal sealed class Candidate
    {
        internal string source;
        internal Func<Sprite> provider;      // null = 游戏立绘表里的那张
        internal int keyLength;
        internal string fileSig;
        internal bool external;
        internal int order;                  // 登记先后（越大越晚）
        internal Dictionary<string, Func<Sprite>> variants;
        internal Dictionary<string, string> variantSigs;
        internal Dictionary<string, Framed> frames;
        // ClaudeNote: 0.8.0 同一个来源里同 ID 的图不止一张时：键名最短的是本体（上面的 provider），其余的收在这里，
        //   不再被丢掉或互相覆盖。编号 = 下标 + 1，写法 来源:ID~编号（见 ImageKey）。
        internal string name;                // 本体的贴图键名（按字段结构识别到的）
        internal List<Alt> alts;
    }

    /// <summary>同一来源、同一 ID 的另一张图。</summary>
    internal sealed class Alt
    {
        internal Func<Sprite> provider;
        internal string name, fileSig;
    }

    public static class PortraitLibrary
    {
        public const string GameSource = "game";
        /// <summary>只靠注册进来的立绘自动带这个标签。</summary>
        public const string CustomTag = "custom";
        /// <summary>只进手动选图图库的立绘带这个标签：不进后台预处理，规则 "*" 也照样能管到它。</summary>
        public const string ManualTag = "manual";

        // 一条注册。sprite 为空 = 只加标签；variant 非空 = 差分。按登记顺序生效，后来的盖前面的。
        internal sealed class Registration
        {
            internal string source, id, variant;
            internal Func<Sprite> sprite;
            internal string[] tags;
            internal bool external;
            internal string fileSig;
            internal Display.Usage frame;   // 不是 FullBody = 这条登记的是专用取景图
        }

        private static readonly List<Registration> registrations = new List<Registration>();
        private static readonly Dictionary<string, string> declared = new Dictionary<string, string>();   // 状态标签 → 声明它的 mod
        private static Dictionary<string, Portrait> portraits = new Dictionary<string, Portrait>();   // 当前这张完整的表（重建时整张换）
        private static Dictionary<string, int> perMod = new Dictionary<string, int>();           // mod ID → 字段匹配上几张（本体 + 差分）
        private static SpriteDict builtFor;
        private static bool dirty = true;

        /// <summary>立绘表可用（在店面场景里）。注册进来的立绘不受这个限制。</summary>
        public static bool Ready => SpriteDict.Instance != null && SpriteDict.Instance.spriteDictionary != null;

        // ── 注册（别的 mod 调；在自己的 OnEnable 里登记，OnDisable 里 Unregister） ──

        /// <summary>登记一张立绘。id 与游戏字段同名 = 接管那个字段的图（标签追加到原有标签上）。sprite 每次取图时调用，拿不到就返回 null。</summary>
        public static void Register(string sourceId, string id, Func<Sprite> sprite, params string[] tags)
        {
            if (string.IsNullOrEmpty(sourceId) || !UsableId(id) || sprite == null) return;
            Add(new Registration { source = sourceId, id = id, sprite = sprite, tags = Normal(tags) });
        }

        /// <summary>登记一张存在磁盘上的立绘（另一个 mod 文件夹里的图片，用到时才读，读完随即放掉）。外部立绘包的兼容层用。</summary>
        internal static void RegisterFile(string sourceId, string id, string modId, string relativePath, string variant, params string[] tags)
        {
            Registration r = FileRegistration(sourceId, id, modId, relativePath, variant, tags);
            if (r != null) Add(r);
        }

        // ClaudeNote: 0.9.1 本地来源（Lune 2026-11-17：像 ItemPortraitHD 那样，玩家自己把图塞进指定文件夹就自动加载）。
        //   列目录要“文件”权限，所以由伴生 mod 来扫（见 companion/src），扫到的每张图经这里登记；图片仍由框架用到时才读。
        /// <summary>本地来源的名字（伴生 mod 的 Local 文件夹里玩家自己放的图）。</summary>
        public const string LocalSource = "local";

        /// <summary>登记一张放在某个已启用 mod 文件夹里的图片（用到时才读）。id 与已有立绘同名 = 多一个来源；variant 非空 = 差分。
        /// stamp：文件变没变的记号（例如 大小 + 修改时间），变了缓存会重画；tags 只对本体有效。</summary>
        public static void RegisterImageFile(string sourceId, string id, string modId, string relativePath, string variant, string stamp, params string[] tags)
        {
            Registration r = FileRegistration(sourceId, id, modId, relativePath, variant, tags);
            if (r == null) return;
            r.fileSig = ExternalImages.FileSig(modId, relativePath, stamp);
            string sig = r.fileSig;
            r.sprite = () => ExternalImages.Get(sig);
            Add(r);
        }

        /// <summary>造一条“磁盘图片”登记，不加进全局表（外部立绘包兼容层在重建时整批交进来）。差分会顺带声明状态标签。</summary>
        internal static Registration FileRegistration(string sourceId, string id, string modId, string relativePath, string variant, string[] tags)
        {
            if (string.IsNullOrEmpty(sourceId) || !UsableId(id) || string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(relativePath)) return null;
            string sig = ExternalImages.FileSig(modId, relativePath);   // 包更新了版本号就重画
            Func<Sprite> load = () => ExternalImages.Get(sig);
            variant = Normal(variant);
            if (variant.Length == 0) return new Registration { source = sourceId, id = id, sprite = load, tags = Normal(tags), external = true, fileSig = sig };
            if (!declared.ContainsKey(variant)) declared[variant] = sourceId;
            return new Registration { source = sourceId, id = id, variant = variant, sprite = load, external = true, fileSig = sig };
        }

        /// <summary>登记一张差分：立绘 id 在状态 variant 下用这张图。同时声明了这个状态标签。</summary>
        public static void RegisterVariant(string sourceId, string id, string variant, Func<Sprite> sprite)
        {
            variant = Normal(variant);
            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(id) || variant.Length == 0 || sprite == null) return;
            if (!declared.ContainsKey(variant)) declared[variant] = sourceId;
            Add(new Registration { source = sourceId, id = id, variant = variant, sprite = sprite });
        }

        // ── 专用取景图（0.6.4） ──
        //   需求：腰上 / 胸上的图原来只能从全身图上部按固定比例裁（腰上 62%、胸上 42%），构图不合适的立绘会裁到奇怪的位置。
        //   注册方可以给某张立绘（的某个状态）单独交一张腰上 / 胸上的图；没交的照旧裁。
        //   取图时的先后：这个状态的专用图 → 这个状态的全身图裁出来。状态优先于取景：
        //     “calm 的全身图 + 本体的专用胸像”时，要 calm 的胸像给的是 calm 全身裁出来的（表情对比构图重要）。

        /// <summary>登记一张专用取景图：立绘 id（在状态 variant 下，空 = 本体）用作 usage 时用这张。usage 只能是腰上 / 胸上。</summary>
        public static void RegisterFrame(string sourceId, string id, Display.Usage usage, Func<Sprite> sprite, string variant = null)
        {
            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(id) || usage == Display.Usage.FullBody || sprite == null) return;
            Add(new Registration { source = sourceId, id = id, variant = Normal(variant), sprite = sprite, frame = usage });
        }

        /// <summary>同上，图是另一个 mod 文件夹里的图片（用到时才读）。</summary>
        internal static void RegisterFrameFile(string sourceId, string id, Display.Usage usage, string modId, string relativePath, string variant)
        {
            if (usage == Display.Usage.FullBody) return;
            Registration r = FileRegistration(sourceId, id, modId, relativePath, null, null);
            if (r == null) return;
            r.variant = Normal(variant);
            r.tags = null;
            r.frame = usage;
            Add(r);
        }

        internal static string FrameKey(Display.Usage usage, string variant) { return (int)usage + "|" + (variant ?? ""); }

        /// <summary>取景名 → 用途：waist / 腰上 / 半身，bust / 胸上 / 胸像。认不出返回 FullBody。表格的 frame 列走这里。</summary>
        public static Display.Usage FrameOf(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "waist": case "waistup": case "腰上": case "半身": return Display.Usage.WaistUp;
                case "bust": case "bustup": case "胸上": case "胸像": return Display.Usage.BustUp;
                default: return Display.Usage.FullBody;
            }
        }

        /// <summary>这张立绘现在生效的专用取景图；没有返回 null。</summary>
        internal static Framed Frame(string id, string variant, Display.Usage usage)
        {
            Portrait p = Get(id);
            return p?.frames != null && p.frames.TryGetValue(FrameKey(usage, Normal(variant)), out Framed f) ? f : null;
        }

        /// <summary>声明一个状态标签。之后各 mod 贴图表里“字段 + 该标签”的键自动认作那个字段的差分。</summary>
        public static void DeclareVariant(string sourceId, string variant)
        {
            variant = Normal(variant);
            if (string.IsNullOrEmpty(sourceId) || variant.Length == 0 || declared.ContainsKey(variant)) return;
            declared[variant] = sourceId;
            dirty = true;
        }

        /// <summary>给一张立绘追加标签（动态标签）。立绘现在不存在也可以先登记，等它出现时生效。</summary>
        public static void AddTags(string sourceId, string id, params string[] tags)
        {
            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(id) || tags == null || tags.Length == 0) return;
            Add(new Registration { source = sourceId, id = id, tags = Normal(tags) });
        }

        /// <summary>撤掉这个来源登记的全部立绘、差分、标签与状态声明。</summary>
        public static void Unregister(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return;
            if (registrations.RemoveAll(r => r.source == sourceId) > 0) dirty = true;
            foreach (string variant in new List<string>(declared.Keys))
                if (declared[variant] == sourceId) { declared.Remove(variant); dirty = true; }
        }

        // ID 会写进物品标签（组/键@格数#差分）和缓存文件名，这三个符号留给格式用。
        private static bool UsableId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id.IndexOfAny(ReservedId) < 0) return true;
            Entry.Warn("portrait id \"" + id + "\" rejected: must not contain / @ # | : ~");
            return false;
        }

        private static void Add(Registration r)
        {
            registrations.Add(r);
            dirty = true;
            Painter.NeedScan = true;
        }

        // ── 查询 ──

        /// <summary>全部立绘。</summary>
        public static ICollection<Portrait> All { get { Ensure(); return portraits.Values; } }

        /// <summary>已声明的状态标签。</summary>
        public static ICollection<string> DeclaredVariants => declared.Keys;

        public static Portrait Get(string id)
        {
            Ensure();
            return id != null && portraits.TryGetValue(id, out Portrait p) ? p : null;
        }

        /// <summary>带齐这些标签的立绘，按名字、编号排好。例：Find("female", "sec")。不给标签 = 全部。
        /// 0.8.0：这是“取池子”用的，只有没激活的候选素材才有的立绘不算在内（见 Portrait.CandidateOnly）；要连它们一起列用 Everything。</summary>
        public static List<Portrait> Find(params string[] tags) { return Listed(Normal(tags), false); }

        /// <summary>全部立绘，连只有候选素材提供的也在内（选图窗口、明细用），排好序。</summary>
        public static List<Portrait> Everything() { return Listed(null, true); }

        private static List<Portrait> Listed(string[] wanted, bool candidatesToo)   // 标签在调用前规范化一次，不在逐张比对时反复做
        {
            Ensure();
            var list = new List<Portrait>();
            foreach (Portrait p in portraits.Values) if (p.HasAll(wanted) && (candidatesToo || !p.CandidateOnly)) list.Add(p);
            list.Sort(Compare);
            return list;
        }

        /// <summary>有这个差分的立绘。</summary>
        public static List<Portrait> FindWithVariant(string variant)
        {
            Ensure();
            var list = new List<Portrait>();
            foreach (Portrait p in portraits.Values) if (p.HasVariant(variant)) list.Add(p);
            list.Sort(Compare);
            return list;
        }

        /// <summary>后台预处理的范围：全部立绘（男女都算），手动图库除外。</summary>
        public static IList<string> Prebakeable()
        {
            Ensure();
            var list = new List<string>();
            foreach (Portrait p in portraits.Values) if (!p.Has(ManualTag) && !p.CandidateOnly) list.Add(p.Field);
            return list;
        }

        /// <summary>“前缀 + 编号”的立绘（femaleLower → femaleLower1、2…），按编号排，缺号不影响后面的。</summary>
        public static List<string> Numbered(string prefix)
        {
            Ensure();
            var list = new List<string>();
            foreach (KeyValuePair<string, Portrait> kv in portraits) if (!kv.Value.CandidateOnly && PortraitTags.IsNumberedUnder(kv.Key, prefix)) list.Add(kv.Key);
            list.Sort((a, b) => PortraitTags.NumberOf(a).CompareTo(PortraitTags.NumberOf(b)));
            return list;
        }

        /// <summary>这张立绘现在的本体图。拿不到返回 null。</summary>
        public static Sprite Resolve(string id) { return Resolve(id, null); }

        /// <summary>这张立绘在某个状态下的图。variant 为空 = 本体。该差分没有图时返回 null（不拿本体顶替，要不要退回本体由调用方决定）。</summary>
        public static Sprite Resolve(string id, string variant)
        {
            Portrait p = Get(id);
            if (p == null) return null;
            variant = Normal(variant);
            try
            {
                if (variant.Length > 0) return p.variants != null && p.variants.TryGetValue(variant, out Func<Sprite> v) ? v() : null;
                Sprite s = p.provider?.Invoke();
                if (s != null) return s;
                if (!p.InGameTable || !Ready) return null;
                // ClaudeNote: 0.5.1 “游戏”来源 = 游戏自带的原图（别的 mod 改了立绘表也不算）；读不到原图才用表里现在那张。不走 GetSprite（第 4 步在那里挂了前缀）
                return Originals.Get(id) ?? (SpriteDict.Instance.spriteDictionary.TryGetValue(id, out Sprite g) ? g : null);
            }
            catch (Exception ex)
            {
                Entry.Warn("portrait " + id + (variant.Length > 0 ? "#" + variant : "") + " not resolved: " + ex.Message);
                return null;
            }
        }

        /// <summary>不读图就能给出的来源指纹（外部图片）；给不出返回 null，由调用方读图后自己算。</summary>
        internal static string FileSignature(string id, string variant)
        {
            Portrait p = Get(id);
            if (p == null) return null;
            variant = Normal(variant);
            if (variant.Length == 0) return p.provider != null ? p.fileSig : null;
            return p.variantSigs != null && p.variantSigs.TryGetValue(variant, out string sig) ? sig : null;
        }

        /// <summary>本体图现在由谁提供（"game" 或 mod ID）；没有这张立绘返回空字符串。</summary>
        internal static string SourceOf(string id) { return Get(id)?.Source ?? ""; }

        // ── 按“来源:ID~编号”取（0.8.0，见 ImageKey） ──
        //   上面的 Resolve(id) 给的是“这个 ID 现在选中的来源”那张（来客显示用，mod 的图取不到时会退回游戏原图）；
        //   下面这组指名道姓：写了哪个来源就只认哪个来源，取不到就是 null，不拿别人的图顶替（否则会以它的名义进缓存）。

        private static bool Locate(string key, out Portrait p, out Candidate c, out int number)
        {
            ImageKey.Parse(key, out string source, out string id, out number);
            p = Get(id);
            c = p?.CandidateOf(source ?? p.Source);
            return c != null && (number == 0 || (c.alts != null && number <= c.alts.Count));
        }

        /// <summary>这张图的来源还在（来源被卸掉、包被删了就是 false）。只写图 ID 时问的是“有没有任何来源提供它”。</summary>
        public static bool Alive(string key) { return Locate(key, out _, out _, out _); }

        /// <summary>这个 ID 的全部图：每个来源的本体 + 同来源的其余几张，完整写法。</summary>
        public static List<string> ImagesOf(string id)
        {
            var list = new List<string>();
            Portrait p = Get(id);
            if (p == null) return list;
            foreach (Candidate c in p.candidates)
            {
                list.Add(ImageKey.Make(c.source, id));
                if (c.alts != null) for (int i = 1; i <= c.alts.Count; i++) list.Add(ImageKey.Make(c.source, id, i));
            }
            return list;
        }

        /// <summary>自动挑来源：图 ID → 完整写法。owner = 要用这张图的道具是哪个 mod 的（那个 mod 自己提供这张图就用它的）。
        /// 顺序见 Sources.Choose。没有任何来源提供返回 null。</summary>
        public static string Pick(string id, string owner = null)
        {
            Portrait p = Get(id);
            Candidate c = p != null ? TextureExtend.Sources.Choose(p, owner) : null;
            return c != null ? ImageKey.Make(c.source, id) : null;
        }

        private static Func<Sprite> VariantAt(Portrait p, Candidate c, string variant, out string sig)
        {
            sig = null;
            if (c.variants != null && c.variants.TryGetValue(variant, out Func<Sprite> own))
            {
                if (c.variantSigs != null) c.variantSigs.TryGetValue(variant, out sig);
                return own;
            }
            if (p.sharedVariants != null && p.sharedVariants.TryGetValue(variant, out Func<Sprite> shared))
            {
                if (p.sharedSigs != null) p.sharedSigs.TryGetValue(variant, out sig);
                return shared;
            }
            return null;
        }

        /// <summary>这张图（写明来源的）在这个状态下有没有图。来源自己的差分 + 不属于任何来源的共享差分。</summary>
        public static bool HasVariantAt(string key, string variant)
        {
            variant = Normal(variant);
            return Locate(key, out Portrait p, out Candidate c, out _) && (variant.Length == 0 || VariantAt(p, c, variant, out _) != null);
        }

        /// <summary>按完整写法取图。variant 为空 = 本体。</summary>
        public static Sprite ResolveImage(string key, string variant = null)
        {
            if (!Locate(key, out Portrait p, out Candidate c, out int number)) return null;
            variant = Normal(variant);
            try
            {
                if (variant.Length > 0) return VariantAt(p, c, variant, out _)?.Invoke();
                if (number > 0) return c.alts[number - 1].provider?.Invoke();
                if (c.provider != null) return c.provider();
                if (c.source != GameSource || !Ready) return null;
                return Originals.Get(p.Field) ?? (SpriteDict.Instance.spriteDictionary.TryGetValue(p.Field, out Sprite g) ? g : null);
            }
            catch (Exception ex)
            {
                Entry.Warn("image " + key + (variant.Length > 0 ? "#" + variant : "") + " not resolved: " + ex.Message);
                return null;
            }
        }

        /// <summary>同 FileSignature，按完整写法。</summary>
        internal static string ImageSignature(string key, string variant)
        {
            if (!Locate(key, out Portrait p, out Candidate c, out int number)) return null;
            variant = Normal(variant);
            if (variant.Length > 0) { VariantAt(p, c, variant, out string sig); return sig; }
            if (number > 0) return c.alts[number - 1].fileSig;
            return c.provider != null ? c.fileSig : null;
        }

        /// <summary>下次用到时重建（别的 mod 中途换了贴图表时手动调）。</summary>
        public static void Invalidate() { dirty = true; Originals.Invalidate(); }

        /// <summary>标签统一成小写、去空白；null → 空字符串。含 / @ # | 的标签不能用（这些符号留给物品标签的格式），按空处理并警告。</summary>
        public static string Normal(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "";
            if (tag.IndexOfAny(Reserved) >= 0) { Entry.Warn("tag \"" + tag + "\" ignored: must not contain / @ # |"); return ""; }
            return tag.Trim().ToLowerInvariant();
        }

        private static readonly char[] Reserved = { '/', '@', '#', '|' };
        private static readonly char[] ReservedId = { '/', '@', '#', '|', ImageKey.Sep, ImageKey.Alt };   // 0.8.0：: 与 ~ 留给“来源:ID~编号”

        private static string[] Normal(string[] tags)
        {
            var list = new List<string>();
            if (tags != null) foreach (string t in tags) { string n = Normal(t); if (n.Length > 0 && !list.Contains(n)) list.Add(n); }
            return list.ToArray();
        }

        // ── 重建 ──

        // ClaudeNote: 0.6.3 重建 = 在一张新表（work）上建，建完才换上去（portraits）。
        //   原因：0.6.1 我加了个“正在重建”标记防套娃、出错时“保留建了一半的表”。两样都不对症——
        //   需求其实是：查立绘库的人任何时候看到的都得是一张完整的表。原地清空重建的话，重建途中有人来查（标签层、来源选择会回头查）
        //   看到的就是半张；重建中途出错，留下的也是半张。换成“建好再换”之后：途中来查的看到上一张完整的表，出错则上一张原样保留。
        //   重建过程里没有任何一步会把立绘库重新标脏，所以这里不需要防套娃的标记。
        private static void Ensure()
        {
            SpriteDict current = Ready ? SpriteDict.Instance : null;
            if (!dirty && ReferenceEquals(current, builtFor)) return;
            dirty = false;
            builtFor = current;
            work = new Dictionary<string, Portrait>();
            workMod = new Dictionary<string, int>();
            try
            {
                order = 0;
                if (current != null) ReadGameTable(current);
                MatchModSprites();
                SortAlts();                                                   // 按结构识别到的同组图排好号；登记进来的同名图排在它们后面（按登记先后）
                Apply(EmbeddedImages());                                      // 0.8.4 打包在启用中的 mod 的 DLL 里的图
                Apply(registrations);
                Layer.ApplyPortraits(work);                                   // 第 3 步：组标签（来源选择可以按组，所以排在选来源之前）
                foreach (Portrait p in work.Values) Activate(p, TextureExtend.Sources.Choose(p));
                foreach (Portrait p in work.Values)                              // 排序键只在重建时算一次
                {
                    p.sortNumber = PortraitTags.NumberOf(p.Field);
                    p.sortName = p.sortNumber > 0 ? p.Field.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') : p.Field;
                }
                portraits = work;
                perMod = workMod;
            }
            catch (Exception ex) { Health.Report("portrait library rebuild (previous table kept)", ex); }
            finally { work = null; workMod = null; }
        }

        private static Dictionary<string, Portrait> work;   // 只在重建过程中有值
        private static Dictionary<string, int> workMod;

        private static void ReadGameTable(SpriteDict dict)
        {
            foreach (KeyValuePair<string, Sprite> kv in dict.spriteDictionary)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || kv.Key.StartsWith(Display.KeyPrefix, StringComparison.Ordinal)) continue;   // 本框架给来客用的显示键不算立绘
                var p = new Portrait { Field = kv.Key, Tags = PortraitTags.Of(kv.Key), InGameTable = true };
                p.candidates.Add(new Candidate { source = GameSource, order = order++ });
                work[kv.Key] = p;
                // ClaudeNote: 0.5.1 表里现在这张不是原图、且认得出是哪个 mod 放的（它文件夹里的贴图）→ 那个 mod 也算一个来源
                //   （像 Dudi 的 HD Character Portraits 这种直接改表的；按结构命名的图下面 MatchModSprites 也会收到，同一来源只留一份）
                string owner = Originals.OwnerOfCurrent(kv.Key);
                if (owner != null && owner != GameSource && owner != Painter.ModId)
                {
                    // ClaudeNote: 0.8.4 认不出是谁放的（TableSource）也收：每次现读表里那张，对方 mod 换了图也跟着换
                    string key = kv.Key;
                    Sprite now = kv.Value;
                    Func<Sprite> live = owner == Originals.TableSource ? (Func<Sprite>)(() => Originals.Current(key)) : () => now;
                    p.candidates.Add(new Candidate { source = owner, provider = live, keyLength = kv.Key.Length, name = kv.Key, order = order++ });
                }
            }
            // 别的 mod 塞进立绘表的“字段_后缀”键（femaleLower3_night）：本体在表里就收成本体的差分，不单列
            foreach (KeyValuePair<string, Sprite> kv in dict.spriteDictionary)
            {
                if (kv.Value == null || !PortraitTags.TryStructure(kv.Key, out string field, out string variant) || variant.Length == 0) continue;
                if (field == kv.Key || !work.TryGetValue(field, out Portrait baseP)) continue;
                Sprite s = kv.Value;
                AddVariant(baseP.CandidateOf(GameSource), variant, () => s, null);
                work.Remove(kv.Key);
            }
        }

        // ClaudeNote: 0.4.0 字段结构识别（Lune 2026-11-16）：各 mod 的 Textures/Items 与 Textures 里，凡是按“性别 + 派系或职业 + 编号 + _后缀”
        //   命名的图一律自动处理——本体换图、新编号收为新立绘、带后缀的收为差分。不按这个结构命名的不碰（要走注册）。
        //   两遍：先收本体（新立绘要先存在），再挂差分。多个 mod 提供同一张时加载顺序靠后的生效。
        private static void MatchModSprites()
        {
            if (ModLoader.Mods == null) return;
            var variants = new List<KeyValuePair<string, KeyValuePair<string, Func<Sprite>>>>();   // 字段 → (差分, 图)
            var variantMods = new List<string>();
            foreach (LoadedMod mod in ModLoader.Mods)
            {
                if (mod?.Manifest == null || !mod.IsEnabled || mod.Manifest.ID == Painter.ModId) continue;
                string modId = mod.Manifest.ID;
                int count = 0;
                if (mod.Sprites != null)
                    foreach (KeyValuePair<string, Sprite> kv in mod.Sprites)
                    {
                        Sprite sprite = kv.Value;
                        if (sprite != null && Collect(modId, kv.Key, sprite.rect.height, () => sprite, variants, variantMods)) count++;
                    }
                if (mod.Textures != null)
                    foreach (KeyValuePair<string, Texture2D> kv in mod.Textures)
                    {
                        Texture2D tex = kv.Value;
                        if (tex == null || kv.Key.StartsWith("Paper/", StringComparison.Ordinal)) continue;
                        if (Collect(modId, kv.Key, tex.height, () => SpriteOf(tex), variants, variantMods)) count++;
                    }
                if (count > 0) workMod[modId] = count;
            }
            for (int i = 0; i < variants.Count; i++)
            {
                if (!work.TryGetValue(variants[i].Key, out Portrait p)) continue;   // 本体哪里都没有：差分不单独成立
                Variant(p, variantMods[i], variants[i].Value.Key, variants[i].Value.Value, null);
            }
        }

        // ClaudeNote: 0.8.4 打包在 DLL 里的图（Lune 2026-11-17：“来源还是没加载全、还是互相覆盖”，参考 ItemPortraitHD 的探测法）。
        //   白蛇娘化、城银美化这类包把 PNG 编进自己的 DLL（嵌入资源），运行时读出来、现建贴图、直接写进游戏的立绘表。
        //   之前框架只看各 mod 的 Textures / Sprites 表，这类图完全看不见；表里那张又被当成“认不出是谁的”丢掉，结果框架一显示别的来源就把它盖掉。
        //   现在：启用中的 mod 的 DLL 里的 PNG 资源逐个看名字（只看名字，不读图），对得上立绘键的登记成这个 mod 的来源：
        //     资源名去掉 .png，取最后一段（/ 或 . 之后）：
        //       就是立绘键（CutePortraits.portraits.femaleLower9.png）→ 那个键
        //       按字段结构命名（…femaleLower9_night.png）→ 那个键的那个差分
        //       是某张原图在游戏贴图里的名字（白蛇：Portraits.femaleLower_9.png；R18Full/femaleLower_9.png）→ 用这张原图的键
        //         名字后面带 -数字 的（白蛇给同名原图加的区分：femaleLower_0-1087）去掉后缀再对；对上的原图不止一张时分不出是谁的，不收
        //   同一个 mod 里同一个键的几张（白蛇 R18 包的全身 / 半身）→ 同来源的第 1、2… 张，不覆盖。
        //   图用到时才读（ExternalImages，res|… 身份串），读法是 Assembly.GetManifestResourceStream，不需要任何权限。
        //   每个 mod 的结果按 mod 记一次（DLL 不会在运行中变）。
        private static readonly Dictionary<string, List<Registration>> embedded = new Dictionary<string, List<Registration>>();

        private static List<Registration> EmbeddedImages()
        {
            var all = new List<Registration>();
            if (ModLoader.Mods == null) return all;
            foreach (LoadedMod mod in ModLoader.Mods)
            {
                if (mod?.Manifest == null || !mod.IsEnabled || mod.Assembly == null || mod.Manifest.ID == Painter.ModId) continue;
                string modId = mod.Manifest.ID;
                if (!embedded.TryGetValue(modId, out List<Registration> list))
                {
                    list = new List<Registration>();
                    try { ScanResources(mod, list); }
                    catch (Exception ex) { Health.Report("embedded images", modId + ": " + ex.Message); }
                    if (Ready) embedded[modId] = list;   // 立绘表还没就绪时认不全原图名字，下次再扫
                }
                all.AddRange(list);
                if (list.Count > 0) workMod[modId] = (workMod.TryGetValue(modId, out int had) ? had : 0) + list.Count;
            }
            return all;
        }

        private static void ScanResources(LoadedMod mod, List<Registration> into)
        {
            string modId = mod.Manifest.ID;
            string[] names = mod.Assembly.GetManifestResourceNames();
            Array.Sort(names, StringComparer.Ordinal);   // R18Full 排在 R18Half 前面：全身当本体、半身当第 2 张
            Dictionary<string, List<string>> byName = Originals.KeysByName();
            foreach (string resource in names)
            {
                if (!resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                string stem = resource.Substring(0, resource.Length - 4);
                stem = stem.Substring(Math.Max(stem.LastIndexOf('/'), stem.LastIndexOf('.')) + 1);
                if (stem.Length == 0) continue;
                string sig = ExternalImages.ResourceSig(modId, resource);
                Func<Sprite> load = () => ExternalImages.Get(sig);
                if (Originals.Get(stem) != null || work.ContainsKey(stem))
                {
                    into.Add(new Registration { source = modId, id = stem, sprite = load, tags = Array.Empty<string>(), external = true, fileSig = sig });
                    continue;
                }
                if (PortraitTags.TryStructure(stem, out string field, out string variant) && stem.StartsWith(field, StringComparison.Ordinal))
                {
                    into.Add(variant.Length == 0
                        ? new Registration { source = modId, id = field, sprite = load, tags = Array.Empty<string>(), external = true, fileSig = sig }
                        : new Registration { source = modId, id = field, variant = variant, sprite = load, external = true, fileSig = sig });
                    continue;
                }
                string named = stem;
                int dash = stem.LastIndexOf('-');
                if (!byName.ContainsKey(named) && dash > 0 && int.TryParse(stem.Substring(dash + 1), out _)) named = stem.Substring(0, dash);
                // 几张原图同名时（游戏把 femaleLower1 和 augLLFemale1 切自同一张图，名字都叫 femaleLower_0）分不出这张是给谁的：不收，
                //   这几个键由“其他 mod 写进表的”来源（对方 mod 自己按它的表写进去的那张）提供，不会配错。
                if (!byName.TryGetValue(named, out List<string> keys) || keys.Count != 1) continue;
                into.Add(new Registration { source = modId, id = keys[0], sprite = load, tags = Array.Empty<string>(), external = true, fileSig = sig });
            }
        }

        /// <summary>停用时 / mod 列表变了：打包在 DLL 里的图下次重扫。</summary>
        internal static void ForgetEmbedded() { embedded.Clear(); }

        // ClaudeNote: 0.4.3 尺寸门槛（Lune 实测：lewd_meme 给少女道具注入的 64×64 小图叫 maiden_femaleScav4，结构对得上，
        //   被当成了本体，重建缓存后全变糊、来源变成 lewd_meme）。按结构命名的图至少要有游戏里同字段原图 75% 高；原版没有的新编号至少 128 像素高。
        private const float MinHeightRatio = 0.75f, MinNewHeight = 128f;

        private static bool BigEnough(string field, float height)
        {
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            Sprite vanilla = Originals.Get(field);
            if (vanilla == null && dict != null) dict.TryGetValue(field, out vanilla);
            if (vanilla != null) return height >= vanilla.rect.height * MinHeightRatio;
            return height >= MinNewHeight;
        }

        private static bool Collect(string modId, string key, float height, Func<Sprite> sprite, List<KeyValuePair<string, KeyValuePair<string, Func<Sprite>>>> variants, List<string> variantMods)
        {
            if (!PortraitTags.TryStructure(key, out string field, out string variant))
            {
                // ClaudeNote: 0.8.4 “池名_任意”命名的（路人立绘扩展：augBMFemale_psrl_augBM_f01）：池里的一张新立绘，ID 就用键名，标签照池子
                string pool = PortraitTags.PoolOf(key);
                if (pool == null || key.Length <= pool.Length + 1 || key[pool.Length] != '_' || key.IndexOfAny(ReservedId) >= 0 || height < MinNewHeight) return false;
                if (!work.TryGetValue(key, out Portrait np)) work[key] = np = new Portrait { Field = key, Tags = PortraitTags.Of(pool + "1"), InGameTable = false };
                if (np.CandidateOf(modId) != null) return false;
                np.candidates.Add(new Candidate { source = modId, provider = sprite, keyLength = key.Length, name = key, order = order++ });
                return true;
            }
            if (!BigEnough(field, height)) return false;
            if (variant.Length > 0)
            {
                variants.Add(new KeyValuePair<string, KeyValuePair<string, Func<Sprite>>>(field, new KeyValuePair<string, Func<Sprite>>(variant, sprite)));
                variantMods.Add(modId);
                return true;
            }
            if (!work.TryGetValue(field, out Portrait p))
                work[field] = p = new Portrait { Field = field, Tags = PortraitTags.Of(field), InGameTable = false };
            Candidate c = p.CandidateOf(modId);
            if (c == null)
            {
                p.candidates.Add(new Candidate { source = modId, provider = sprite, keyLength = key.Length, name = key, order = order++ });
                return true;
            }
            // ClaudeNote: 0.8.0 同一个 mod 里有好几张图认成同一个 ID（Lune：合并成一个组，键最短的为本体）。
            //   之前：只留键最短的那张，其余的直接丢 —— 玩家想用也找不到。现在其余的收作同组的第 2、3… 张（来源:ID~编号）。
            //   键名完全相同的（贴图表与 Sprite 表各有一份、或它已经把这张放进了游戏立绘表）是同一张图，不重复收。
            if (c.name == key || (c.alts != null && c.alts.Exists(a => a.name == key))) return false;
            if (c.alts == null) c.alts = new List<Alt>();
            if (Shorter(key, c.name))
            {
                c.alts.Add(new Alt { provider = c.provider, name = c.name, fileSig = c.fileSig });   // 原来的本体让位
                c.provider = sprite;
                c.keyLength = key.Length;
                c.name = key;
                c.fileSig = null;
            }
            else c.alts.Add(new Alt { provider = sprite, name = key });
            return true;
        }

        // 键短的在前；一样长按字典序。只和 mod 的内容有关，所以同一批图每次启动编号都一样。
        private static bool Shorter(string a, string b)
        {
            if (b == null) return false;
            return a.Length != b.Length ? a.Length < b.Length : string.CompareOrdinal(a, b) < 0;
        }

        private static void SortAlts()
        {
            foreach (Portrait p in work.Values)
                foreach (Candidate c in p.candidates)
                    if (c.alts != null && c.alts.Count > 1) c.alts.Sort((a, b) => a.name == b.name ? 0 : Shorter(a.name, b.name) ? -1 : 1);
        }

        // 别的 mod 的高清贴图包一层 Sprite（每张贴图只包一次；贴图归那个 mod 管，这里不销毁贴图）。
        private static readonly Dictionary<Texture2D, Sprite> wrapped = new Dictionary<Texture2D, Sprite>();

        internal static Sprite SpriteOf(Texture2D tex)
        {
            if (tex == null) return null;
            if (wrapped.TryGetValue(tex, out Sprite s) && s != null) return s;
            s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            wrapped[tex] = s;
            return s;
        }

        /// <summary>停用时放掉包装用的 Sprite。</summary>
        internal static void ReleaseWrapped()
        {
            foreach (Sprite s in wrapped.Values) if (s != null) UnityEngine.Object.Destroy(s);
            wrapped.Clear();
        }

        // 两遍：先本体（来源要先成立），再差分（差分归到同一来源；那个来源没有本体时算共享差分）
        private static void Apply(List<Registration> list)
        {
            foreach (Registration r in list)
            {
                if (r.variant != null || r.frame != Display.Usage.FullBody) continue;
                work.TryGetValue(r.id, out Portrait p);
                if (r.sprite != null)
                {
                    // ClaudeNote: 0.6.0 登记的新立绘按字段结构命名的（femaleMiner3）自动带上性别 / 派系标签，其余只有 custom
                    if (p == null)
                        work[r.id] = p = new Portrait { Field = r.id, Tags = PortraitTags.TryStructure(r.id, out string f, out string v) && f == r.id && v.Length == 0 ? PortraitTags.Of(r.id) : new[] { CustomTag } };
                    Candidate c = p.CandidateOf(r.source);
                    if (c == null) p.candidates.Add(c = new Candidate { source = r.source });
                    // ClaudeNote: 0.8.0 同来源同名的另一张图（Lune：自动分个编号）。认得出“是另一张”的只有磁盘图片（路径不同 = 指纹不同）：
                    //   例如一个包在 Textures/ 和 Portraits/Special/ 下各放了一张同名图。之前后登记的直接盖掉前面的。
                    //   代码登记的图（一个取图函数）分不出是不是同一张，照旧算“重新登记”，换掉原来的。
                    else if (r.external && c.external && r.fileSig != null && c.fileSig != null && r.fileSig != c.fileSig)
                    {
                        if (c.alts == null) c.alts = new List<Alt>();
                        if (!c.alts.Exists(a => a.fileSig == r.fileSig)) c.alts.Add(new Alt { provider = r.sprite, fileSig = r.fileSig });
                        if (r.tags != null && r.tags.Length > 0) p.Tags = Merge(p.Tags, r.tags);
                        continue;
                    }
                    c.provider = r.sprite;
                    c.external = r.external;
                    c.fileSig = r.fileSig;
                    c.keyLength = 0;
                    c.order = order++;
                }
                if (p != null && r.tags != null && r.tags.Length > 0) p.Tags = Merge(p.Tags, r.tags);
            }
            foreach (Registration r in list)
            {
                if (r.variant == null || !work.TryGetValue(r.id, out Portrait p)) continue;
                if (r.frame == Display.Usage.FullBody) { Variant(p, r.source, r.variant, r.sprite, r.fileSig); continue; }
                // 专用取景图：归到同一来源；那个来源没有本体时算共享
                Candidate c = p.CandidateOf(r.source);
                Dictionary<string, Framed> into = c != null ? c.frames ?? (c.frames = new Dictionary<string, Framed>()) : p.sharedFrames ?? (p.sharedFrames = new Dictionary<string, Framed>());
                into[FrameKey(r.frame, r.variant)] = new Framed { sprite = r.sprite, fileSig = r.fileSig };
            }
        }

        private static int order;

        private static void Variant(Portrait p, string source, string variant, Func<Sprite> sprite, string fileSig)
        {
            Candidate c = p.CandidateOf(source);
            if (c != null) { AddVariant(c, variant, sprite, fileSig); return; }
            if (p.sharedVariants == null) p.sharedVariants = new Dictionary<string, Func<Sprite>>();
            p.sharedVariants[variant] = sprite;
            if (fileSig != null) { if (p.sharedSigs == null) p.sharedSigs = new Dictionary<string, string>(); p.sharedSigs[variant] = fileSig; }
            else p.sharedSigs?.Remove(variant);
        }

        private static void AddVariant(Candidate c, string variant, Func<Sprite> sprite, string fileSig)
        {
            if (c == null) return;
            if (c.variants == null) c.variants = new Dictionary<string, Func<Sprite>>();
            c.variants[variant] = sprite;
            if (fileSig != null) { if (c.variantSigs == null) c.variantSigs = new Dictionary<string, string>(); c.variantSigs[variant] = fileSig; }
            else c.variantSigs?.Remove(variant);
        }

        // 把选中的来源抄到立绘的“当前”字段上：本体、来源名、指纹，差分 = 共享差分 + 这个来源自己的（同名时来源自己的优先）
        private static void Activate(Portrait p, Candidate c)
        {
            p.variants = null;
            p.variantSigs = null;
            if (p.sharedVariants != null) p.variants = new Dictionary<string, Func<Sprite>>(p.sharedVariants);
            if (p.sharedSigs != null) p.variantSigs = new Dictionary<string, string>(p.sharedSigs);
            p.frames = p.sharedFrames != null ? new Dictionary<string, Framed>(p.sharedFrames) : null;
            if (c?.frames != null)
            {
                if (p.frames == null) p.frames = new Dictionary<string, Framed>();
                foreach (KeyValuePair<string, Framed> kv in c.frames) p.frames[kv.Key] = kv.Value;   // 同一用途同一状态：来源自己的优先
            }
            if (c == null) { p.provider = null; p.providerMod = null; p.Source = GameSource; p.External = false; p.fileSig = null; return; }
            p.provider = c.provider;
            p.providerMod = c.source == GameSource ? null : c.source;
            p.Source = c.source;
            p.External = c.external;
            p.fileSig = c.fileSig;
            if (c.variants != null)
            {
                if (p.variants == null) p.variants = new Dictionary<string, Func<Sprite>>();
                foreach (var kv in c.variants)
                {
                    p.variants[kv.Key] = kv.Value;
                    if (c.variantSigs != null && c.variantSigs.TryGetValue(kv.Key, out string sig)) { if (p.variantSigs == null) p.variantSigs = new Dictionary<string, string>(); p.variantSigs[kv.Key] = sig; }
                    else p.variantSigs?.Remove(kv.Key);
                }
            }
        }

        private static string[] Merge(string[] have, string[] add)
        {
            var list = new List<string>(have);
            foreach (string t in add) if (!list.Contains(t)) list.Add(t);
            return list.ToArray();
        }

        private static int Compare(Portrait a, Portrait b)
        {
            int c = string.CompareOrdinal(a.sortName, b.sortName);
            return c != 0 ? c : a.sortNumber.CompareTo(b.sortNumber);
        }

        // ── 自检 ──

        /// <summary>概况：多少立绘、各 mod 靠字段匹配提供了几张、多少条注册、声明了哪些状态。</summary>
        internal static string Summary()
        {
            Ensure();
            var b = new StringBuilder();
            b.Append("portrait library: ").Append(portraits.Count).Append(" portrait(s)").Append(Ready ? "" : " (game sprite table not ready)");
            foreach (KeyValuePair<string, int> kv in perMod) b.Append(" · ").Append(kv.Key).Append(" matched ").Append(kv.Value);
            if (Ready && perMod.Count == 0) b.Append(" · no mod sprite matched by field structure");
            b.Append(" · ").Append(registrations.Count).Append(" registration(s)");
            if (declared.Count > 0) b.Append(" · variants: ").Append(string.Join(", ", declared.Keys));
            return b.ToString();
        }

        /// <summary>全表明细（ID、标签、来源、图的尺寸、差分），写进 ModData 供核对。</summary>
        internal static string Dump()
        {
            var b = new StringBuilder("[\n");
            List<Portrait> all = Everything();
            for (int i = 0; i < all.Count; i++)
            {
                Portrait p = all[i];
                Sprite s = p.External ? null : Resolve(p.Field);   // 外部立绘包的图不为了列表逐张读盘
                b.Append("  {\"id\":\"").Append(p.Field).Append("\",\"tags\":\"").Append(string.Join(" ", p.Tags))
                    .Append("\",\"source\":\"").Append(p.Source).Append("\",\"images\":\"").Append(string.Join(" ", ImagesOf(p.Field))).Append("\",\"size\":\"")
                    .Append(p.External ? "file" : s != null ? Mathf.RoundToInt(s.rect.width) + "x" + Mathf.RoundToInt(s.rect.height) : "none")
                    .Append("\",\"variants\":\"").Append(string.Join(" ", p.Variants))
                    .Append("\",\"frames\":\"").Append(p.frames != null ? string.Join(" ", p.frames.Keys) : "").Append("\"}")
                    .Append(i < all.Count - 1 ? ",\n" : "\n");
            }
            return b.Append("]").ToString();
        }
    }
}
