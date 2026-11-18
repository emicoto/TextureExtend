// ClaudeNote: TextureExtend（贴图扩展框架，原 ItemPainter）对外接口：道具与立绘的贴图扩展——画图、差分、规则、跟着存档走。
//   别的 mod 的用法（把 Lune.TextureExtend 写进 manifest 的 <Prerequisites>，csproj 引用 TextureExtend.dll）：
//     Painter.DefineVanillaGroup("femaleLower");                    // 来源组 = 原版立绘表里的一个前缀（femaleLower1、femaleLower2…）
//     string key = Painter.Keys("femaleLower")[n];                  // 这一组现在有哪些立绘
//     Painter.Paint(item, "femaleLower", key);                      // 画到物品上，占 4×4 格
//   读档后不用管：本 mod 会按物品上的标签（textureOG / textureCur / textureInfo，见 Painter.SourceTag）把图接回去。
//
//   一张图怎么来：内存里有就用 → 缓存文件且来源没变就读文件 → 否则从来源现画一张并写进缓存。
//   来源暂时拿不到（立绘包被卸了、还没进店面场景）而缓存还在，就继续用缓存里那张，物品不会变成问号。
//   大小（Lune 2026-11-15）：不放大来源像素。成品边长由来源决定，上限 = 格数 × 每格像素（设置“道具图大小”：4×4 格 128 或 256，
//         对应道具在 2K / 4K 屏幕上实际占的像素）。原版像素立绘按原像素裁切（128），高清立绘缩到上限。
//   显示：Sprite 的每单位像素数按成品边长算，摆进 格数×16 的格子里正好占满，屏幕上有多少像素就显示多少细节。
//   注册：原版按“mod id + 键”到该 mod 的 Sprites 表里取图，所以画好的图登记进本 mod 的 Sprites 表，键 ip<格数>_<组>_<键>。
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProbablyExtendFramework
{
    /// <summary>一张成品图的身份：来源组、组内的键、占几格、差分（没有差分为空）。</summary>
    public readonly struct PaintId
    {
        public readonly string Group, Key, Variant;
        public readonly int Cells;

        public PaintId(string group, string key, int cells) : this(group, key, cells, null) { }

        // ClaudeNote: 0.2.0 差分——同一张立绘按状态标签换图（exposed、infected…），标签由别的 mod 向立绘库注册。
        public PaintId(string group, string key, int cells, string variant)
        {
            Group = group ?? ""; Key = key ?? ""; Cells = Mathf.Clamp(cells, 1, 8); Variant = PortraitLibrary.Normal(variant);
        }

        // ClaudeNote: 0.3.1 default(PaintId)（物品没有来源标签时）的字段是 null，不能直接取 Length——0.3.0 在这里抛空引用，
        //   导致统一重算、右键“选图”判断在没画过的物品上全部中断
        public bool Valid => !string.IsNullOrEmpty(Group) && !string.IsNullOrEmpty(Key);
        /// <summary>写在物品标签与缓存索引里的文字：组/键@格数，有差分时再加 #差分。</summary>
        public string Text => Group + "/" + Key + "@" + Cells + (HasVariant ? "#" + Variant : "");
        public string SpriteKey => "ip" + Cells + "_" + Safe(Group) + "_" + Safe(Key) + (HasVariant ? "_v_" + Safe(Variant) : "");
        internal string File => Cache.Folder + "/" + Safe(Group) + "/" + Safe(Key) + (HasVariant ? "~" + Safe(Variant) : "") + "@" + Cells + ".png";
        public bool HasVariant => !string.IsNullOrEmpty(Variant);
        /// <summary>同一张立绘的另一个差分（空 = 本体）。</summary>
        public PaintId With(string variant) { return new PaintId(Group, Key, Cells, variant); }

        public static PaintId Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return default;
            int slash = text.IndexOf('/'), at = text.LastIndexOf('@');
            if (slash <= 0 || at <= slash + 1) return default;
            int hash = text.IndexOf('#', at);
            string number = hash < 0 ? text.Substring(at + 1) : text.Substring(at + 1, hash - at - 1);
            if (!int.TryParse(number, out int cells)) return default;
            return new PaintId(text.Substring(0, slash), text.Substring(slash + 1, at - slash - 1), cells, hash < 0 ? null : text.Substring(hash + 1));
        }

        private static string Safe(string s)
        {
            var b = new StringBuilder(s.Length);
            // ClaudeNote: 0.9.0 自检：编号符 ~ 原样保留。之前也换成 _，于是“来源:角色~1”（同来源第 2 张）和一张恰好叫“角色_1”的立绘会落到同一个缓存文件上
            foreach (char c in s) b.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ImageKey.Alt ? c : '_');
            return b.ToString();
        }
    }

    public static partial class Painter
    {
        public const string ModId = "Lune.ProbablyExtendFramework";   // 1.0.0 改名（原 Lune.TextureExtend）
        // ClaudeNote: 0.8.0 物品上随存档走的三样（Lune 2026-11-18：{ textureOG, textureCur, textureInfo }）：
        //   OgTag     textureOG    第一次经框架换图之前的原样（原图 + 原占格），见 Look / Restyle。
        //   SourceTag textureCur   现在显示的那张图，完整身份：portrait/来源:图ID~编号@格数#差分（PaintId.Text）。
        //   InfoTag   textureInfo  登记信息：怎么挑的来源 + 要的是哪张。
        //                            auto|图ID            自动：每次读档按来源优先级重新挑
        //                            fixed|来源:图ID~编号  点名：只要这个来源还在就用它；不在了先按自动的挑法顶上，它回来了再换回去
        //                          末尾带 |adopted = 这件是从别的 mod 接管来的（识别钩子不再认它时还原成 OG）。
        //   读档时怎么定（Decide）：读 Info → 来源还在不在 → 按现在激活的 mod 排出的优先级，三选一：
        //     照 Cur 载入 / 换一个来源并更新 Cur / 谁都不提供这张图了 → 显示 OG（三个标签留着，来源回来了自动接回）。
        /// <summary>物品标签 textureCur：这件物品现在的图（PaintId.Text）。随物品存档。</summary>
        public const string SourceTag = "TEXEXT_CUR";
        /// <summary>物品标签 textureOG：第一次经框架换图之前，物品原来的图与占格。</summary>
        public const string OgTag = "TEXEXT_OG";
        /// <summary>物品标签 textureInfo：来源是自动挑的还是点名的、要的是哪张。</summary>
        public const string InfoTag = "TEXEXT_INFO";
        private const string AutoMode = "auto", FixedMode = "fixed", AdoptedFlag = "adopted";
        private const string UsedKey = "used";   // 本局存档里用到的图（读档时在物品还原之前先载入）

        private sealed class Group
        {
            internal string name;
            internal int cells;
            internal Func<IList<string>> list;
            internal Func<string, Sprite> resolve;
        }

        /// <summary>框架自带的全立绘来源组：游戏立绘表里的男女全部 + 注册进来的立绘（手动图库除外）。后台默认把它全部预处理进缓存。</summary>
        public const string PortraitGroup = "portrait";

        private static readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
        // ClaudeNote: 0.3.0 立绘类来源组（DefineVanillaGroup / DefineTagGroup 定义的）。它们的图统一记在 portrait 组名下：
        //   同一张立绘只缓存一份，不因调用方用了不同的组名而重复画；物品标签里也写 portrait/字段@格数。
        private static readonly HashSet<string> portraitGroups = new HashSet<string> { PortraitGroup };
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();   // PaintId.Text → 成品
        private static readonly HashSet<string> used = new HashSet<string>();
        private static LoadedMod self;

        /// <summary>每格最多画多少像素（设置项）：32 = 4×4 格 128 像素（1080p / 2K），64 = 256 像素（4K）。</summary>
        public static int PixelsPerCell { get; private set; } = 32;

        public static bool Running { get; private set; }

        // ClaudeNote: 0.7.2 “来源定下来了吗”（Lune 实测：读档后道具上的立绘先糊、过一会儿才变清楚）。
        //   成因：别的立绘 mod（例如 Dudi 的高清包）在“存档读完”这个事件里才把立绘表换成自己的图。在那之前——进了店面场景、
        //     存档还没读完——表里还是游戏原图，框架那时就开扫，把这些立绘按原版像素图重画了一遍并**覆盖了缓存**；
        //     读档时物品从缓存取到的就是这张低清的；等对方换完表、框架察觉、再重画成高清，物品才变清楚。每次读档都重演一遍，
        //     还白画十几张图两次。读档后 1.5 秒那次重接和这个无关（它只是把图重新挂一遍，挂的还是当时缓存里那张）。
        //   需求：缓存里留的应该是“来源定下来之后”的那张；没定下来的时候不能拿半路的状态去覆盖它。
        //   做法：Settled = 这一局的存档读完了（各 mod 的读档处理都跑过了）。没定下来时——后台不扫；物品要图时缓存里有就直接用缓存，
        //     不比对来源、不重画。定下来之后后台才核对，来源真变了再重画。
        //     Unsettle：开始读档时、离开店面时。Settle：存档读完（GameLoadedLate）；起床、开门也算（新开一局不走读档事件时的保底）。
        internal static bool Settled { get; private set; }
        internal static void Unsettle() { Settled = false; }
        internal static void Settle()
        {
            if (Settled) return;
            Settled = true;
            NeedScan = true;
        }
        /// <summary>有新的来源组或来源表换了，后台要再过一遍缓存。</summary>
        internal static bool NeedScan { get; set; }
        internal static int BakedThisSession { get; private set; }

        // ── 来源组 ──

        // ClaudeNote: 0.2.0 起来源走立绘库（PortraitLibrary）：整表读取 + 字段匹配，不再从 1 往上数到缺号为止。
        /// <summary>“前缀 + 编号”的立绘作为一组（femaleLower → femaleLower1、2…）。图按立绘库的取图顺序来，立绘包换过的按换后的画。</summary>
        public static void DefineVanillaGroup(string prefix, int cells = 4)
        {
            DefineGroup(prefix, () => PortraitLibrary.Numbered(prefix), PortraitLibrary.Resolve, cells);
            if (!string.IsNullOrEmpty(prefix)) portraitGroups.Add(prefix);
        }

        /// <summary>按标签取一组立绘：DefineTagGroup("secWomen", 4, "female", "sec")。组内的键就是立绘字段。</summary>
        public static void DefineTagGroup(string group, int cells, params string[] tags)
        {
            DefineGroup(group, () => PortraitLibrary.Find(tags).ConvertAll(p => p.Field), PortraitLibrary.Resolve, cells);
            if (!string.IsNullOrEmpty(group)) portraitGroups.Add(group);
        }

        /// <summary>立绘类的身份统一记到 portrait 组名下（见 portraitGroups）。旧存档里 femaleLower/femaleLower9@4 这类也换成 portrait/femaleLower9@4。</summary>
        // ClaudeNote: 0.8.0 立绘类的键一律钉成完整写法 来源:图ID（见 ImageKey）。只写了图 ID 的 = 自动，按 owner 挑一个来源。
        //   立绘库这时说不出来（不在店面、这个 ID 没人提供）就先原样留着，等读档后的 Decide 再定。
        internal static PaintId Canon(PaintId id, string owner = null)
        {
            if (!id.Valid) return id;
            if (id.Group != PortraitGroup)
            {
                bool portrait = portraitGroups.Contains(id.Group) || (!groups.ContainsKey(id.Group) && PortraitLibrary.Get(ImageKey.IdOf(id.Key)) != null);
                if (!portrait) return id;
            }
            string key = ImageKey.Full(id.Key) ? id.Key : PortraitLibrary.Pick(id.Key, owner) ?? id.Key;
            return id.Group == PortraitGroup && key == id.Key ? id : new PaintId(PortraitGroup, key, id.Cells, id.Variant);
        }

        /// <summary>这件道具是哪个 mod 的（自动挑来源时“自家道具配自家图”用）。原版道具返回 null。</summary>
        private static string OwnerOf(GameItem item)
        {
            string source = item != null ? ItemDefs.Of(item.identifier).source : null;
            return string.IsNullOrEmpty(source) || source == ItemDefs.Game ? null : source;
        }

        private static void DefineBuiltInGroup()
        {
            DefineGroup(PortraitGroup, PortraitLibrary.Prebakeable, PortraitLibrary.Resolve, 4);
        }

        /// <summary>自定义来源组：list 给出当前有哪些键，resolve 按键给出 Sprite（拿不到返回 null）。重复定义以后一次为准。</summary>
        public static void DefineGroup(string group, Func<IList<string>> list, Func<string, Sprite> resolve, int cells = 4)
        {
            if (string.IsNullOrEmpty(group) || list == null || resolve == null) return;
            groups[group] = new Group { name = group, cells = cells, list = list, resolve = resolve };
            NeedScan = true;
        }

        /// <summary>这一组现在能用的键。来源表还没准备好（不在店面场景）时是空的。</summary>
        public static IList<string> Keys(string group)
        {
            if (group != null && groups.TryGetValue(group, out Group g))
            {
                try { return g.list() ?? Array.Empty<string>(); }
                catch (Exception ex) { Entry.Warn("group " + group + " list failed: " + ex.Message); }
            }
            return Array.Empty<string>();
        }

        public static bool HasGroup(string group) { return group != null && groups.ContainsKey(group); }

        /// <summary>原版立绘表可用（在店面场景里）。</summary>
        public static bool VanillaReady => PortraitLibrary.Ready;

        // ── 画到物品上 ──

        /// <summary>把这张图画到物品上，并把物品占格设为 cells×cells。失败（没有来源也没有缓存）返回 false，物品不动。</summary>
        public static bool Paint(GameItem item, string group, string key, int cells = 4)
        {
            return Paint(item, group, key, cells, null);
        }

        /// <summary>同上，直接画指定的差分。这个差分没注册（也没有缓存）时返回 false，物品不动。</summary>
        public static bool Paint(GameItem item, string group, string key, int cells, string variant)
        {
            if (Retired || item == null) return false;   // 做过卸载前清理：不再往物品上画（调用方会走自己的占位图）
            // ClaudeNote: 0.8.0 key 写明了来源（来源:图ID）= 点名；只写图 ID = 自动。
            //   自动、而玩家之前给这件物品的同一个图 ID 点过名、那个来源也还在：沿用玩家点的（别的 mod 重画一次不该冲掉玩家的选择）。
            bool pinned = ImageKey.Full(key);
            ReadInfo(item, out bool wasFixed, out string wanted, out bool adopted);
            if (!pinned && wasFixed && ImageKey.IdOf(wanted) == key && PortraitLibrary.Alive(wanted)) { key = wanted; pinned = true; }
            PaintId id = Canon(new PaintId(group, key, cells, variant), OwnerOf(item));
            if (!id.Valid || Ensure(id) == null) return false;
            // 第一次换图之前记下原样（OG）：来源全没了、卸载前清理、接管的道具放手时靠它还原
            if (!item.IsTag(OgTag))
            {
                string look = Look(item);
                item.ModifyTag(OgTag, s => { s.Enable(); s.SetString(look); });
            }
            Show(item, id);
            item.SetShape(new GridShapeBuilder(id.Cells, id.Cells).SetDataFill(id.Cells, id.Cells, 1).Build());
            WriteCur(item, id);
            if (id.Group == PortraitGroup) WriteInfo(item, pinned, pinned ? id.Key : ImageKey.IdOf(id.Key), adopted || PaintRules.Adopting);
            Remember(id);
            return true;
        }

        private static void WriteCur(GameItem item, PaintId id) { item.ModifyTag(SourceTag, s => { s.Enable(); s.SetString(id.Text); }); }

        private static void WriteInfo(GameItem item, bool pinned, string wanted, bool adopted)
        {
            string text = (pinned ? FixedMode : AutoMode) + "|" + wanted + (adopted ? "|" + AdoptedFlag : "");
            item.ModifyTag(InfoTag, s => { s.Enable(); s.SetString(text); });
        }

        /// <summary>读 textureInfo。没有这个标签：按自动算，wanted 为 null。</summary>
        internal static void ReadInfo(GameItem item, out bool pinned, out string wanted, out bool adopted)
        {
            pinned = false; wanted = null; adopted = false;
            if (item == null || !item.IsTag(InfoTag)) return;
            string[] p = (item.GetTagReadonly(InfoTag)?.GetString() ?? "").Split('|');
            if (p.Length < 2) return;
            pinned = p[0] == FixedMode;
            wanted = p[1];
            adopted = p.Length > 2 && p[2] == AdoptedFlag;
        }

        /// <summary>这件物品的图是玩家（或调用方）点名了来源的；false = 自动挑的。</summary>
        public static bool IsPinned(GameItem item) { ReadInfo(item, out bool pinned, out _, out _); return pinned; }

        /// <summary>回到自动：按来源优先级重新挑一张同 ID 的图。</summary>
        public static bool Unpin(GameItem item)
        {
            PaintId now = SourceOf(item);
            if (!now.Valid || now.Group != PortraitGroup) return false;
            ReadInfo(item, out _, out _, out bool adopted);
            WriteInfo(item, false, ImageKey.IdOf(now.Key), adopted);
            return Restore(item);
        }

        // ClaudeNote: 0.8.0 读档 / 来源变动后，这件物品该显示哪张（见 SourceTag 上面的说明）。
        //   只在“来源定下来了”（Settled：存档读完、各 mod 都登记完）且立绘库可用时才判断——否则半路上看谁都像“来源没了”。
        //   返回 false = 谁都不提供这张图了（调用方显示 OG）。
        private static bool Decide(GameItem item, ref PaintId id)
        {
            if (id.Group != PortraitGroup || !Settled || !PortraitLibrary.Ready) return true;
            ReadInfo(item, out bool pinned, out string wanted, out _);
            string key = pinned && wanted != null && PortraitLibrary.Alive(wanted) ? wanted : PortraitLibrary.Pick(ImageKey.IdOf(wanted ?? id.Key), OwnerOf(item));
            if (key == null) return false;
            if (key == id.Key) return true;
            id = new PaintId(PortraitGroup, key, id.Cells, PortraitLibrary.HasVariantAt(key, id.Variant) ? id.Variant : null);   // 新来源没有这个状态的图：先回本体，规则重算时再定
            WriteCur(item, id);
            return true;
        }

        // 显示 OG：只换回原图，不动占格（读档后改占格会和旁边的物品叠在一起），三个标签留着。
        private static void ShowOg(GameItem item)
        {
            string[] p = (item.IsTag(OgTag) ? item.GetTagReadonly(OgTag)?.GetString() ?? "" : "").Split('|');
            if (p.Length < 3 || (item.spriteAtlasPath == p[1] && item.spritePath == p[2])) return;
            item.useSpriteFromMod = p[0] == "1";
            item.SetSprite(p[1], p[2]);
        }

        /// <summary>物品上记着的图片来源；没有返回无效的 PaintId。</summary>
        public static PaintId SourceOf(GameItem item)
        {
            if (item == null) return default;
            if (!item.IsTag(SourceTag)) return default;
            return Canon(PaintId.Parse(item.GetTagReadonly(SourceTag)?.GetString()));
        }

        /// <summary>按物品上的标签把图接回去（读档后、或图被别人换掉时）。只换图，不动占格。</summary>
        public static bool Restore(GameItem item)
        {
            if (Retired) return false;
            PaintId id = SourceOf(item);
            if (!id.Valid) return false;
            if (!Decide(item, ref id)) { ShowOg(item); return false; }
            if (item.spritePath == id.SpriteKey && item.spriteAtlasPath == ModId && item.useSpriteFromMod && sprites.TryGetValue(id.Text, out Sprite have) && have != null) return true;   // 已经对上了
            if (Ensure(id) == null) return false;
            Show(item, id);
            Remember(id);
            return true;
        }

        /// <summary>成品图（预览用）。没有来源也没有缓存时返回 null。</summary>
        public static Sprite Get(string group, string key, int cells = 4)
        {
            return Get(group, key, cells, null);
        }

        public static Sprite Get(string group, string key, int cells, string variant)
        {
            PaintId id = Canon(new PaintId(group, key, cells, variant));
            if (!id.Valid) return null;
            previews.Remove(id.Text);   // 0.6.4：外面的 mod 来取 = 它要拿去用了，不再算选图窗口的临时图（否则关窗时会把它手里的图销毁）
            return Ensure(id);
        }

        // ClaudeNote: 0.6.3 选图窗口的缩略图。0.6.1 发现它们一直留在内存里，我当时的处理是“入睡时把没上过物品的图全放掉”——
        //   借存档时机顺手清，放的是谁的图也靠排除法猜。成因是：缩略图是窗口为了显示临时要的，却没有任何人对它负责。
        //   现在谁要的谁还：窗口经 Preview 取图，只有“为了这次预览才载入内存”的记进 previews；
        //   窗口翻页 / 关闭时 ReleasePreviews 放掉不再显示的。期间被画到物品上的（shown）不放。
        private static readonly HashSet<string> previews = new HashSet<string>();

        internal static Sprite Preview(string group, string key, int cells, string variant, HashSet<string> showing)
        {
            PaintId id = Canon(new PaintId(group, key, cells, variant));
            if (!id.Valid) return null;
            bool loaded = sprites.TryGetValue(id.Text, out Sprite have) && have != null;
            Sprite s = loaded ? have : Ensure(id);
            if (s != null && !loaded) previews.Add(id.Text);
            if (s != null) showing?.Add(id.Text);
            return s;
        }

        /// <summary>同 Preview，但只拿现成的（内存里的、或缓存里已有的成品），绝不为了预览去裁图。没有返回 null。</summary>
        internal static Sprite PreviewCached(string group, string key, int cells, string variant, HashSet<string> showing)
        {
            PaintId id = Canon(new PaintId(group, key, cells, variant));
            if (!id.Valid) return null;
            if (!(sprites.TryGetValue(id.Text, out Sprite s) && s != null))
            {
                if (Self() == null || !Cache.Has(id, null)) return null;
                Texture2D tex = Cache.Load(id);
                if (tex == null) return null;
                s = Register(id, tex);
                previews.Add(id.Text);
            }
            showing?.Add(id.Text);
            return s;
        }

        /// <summary>放掉预览图；still 里的（窗口还在显示）留着。</summary>
        internal static void ReleasePreviews(HashSet<string> still)
        {
            if (previews.Count == 0) return;
            foreach (string text in new List<string>(previews))
            {
                if (still != null && still.Contains(text)) continue;
                previews.Remove(text);
                if (shown.Contains(text)) continue;   // 预览之后被画到了物品上：归物品了
                if (sprites.TryGetValue(text, out Sprite s) && s != null) Drop(PaintId.Parse(text), s);
            }
        }

        /// <summary>把物品的图切到另一个差分（空 = 切回本体）。只换图，不动占格。差分拿不到时返回 false，物品不动。
        /// 立绘衍生道具用立绘库里的差分（现画 + 缓存）；静态道具用登记的原图。</summary>
        public static bool SetVariant(GameItem item, string variant)
        {
            if (Retired) return false;
            PaintId now = SourceOf(item);
            if (!now.Valid) return item != null && PaintRules.SetStaticState(item, PortraitLibrary.Normal(variant), null, null);   // 静态道具：直接换成登记的原图
            PaintId next = now.With(variant);
            if (next.Text == now.Text) return true;
            if (now.Group == PortraitGroup && Settled && PortraitLibrary.Ready && !PortraitLibrary.Alive(now.Key)) return false;   // 来源没了、正显示着 OG：不拿旧缓存里的差分顶上
            if (Ensure(next) == null) return false;
            Show(item, next);
            WriteCur(item, next);
            Remember(next);
            if ((now.Variant ?? "") != (next.Variant ?? "")) Changed(item, now.Variant ?? "", next.Variant ?? "");
            return true;
        }

        /// <summary>物品现在显示的差分；本体或没画过返回空字符串。</summary>
        public static string VariantOf(GameItem item) { return StateOf(item); }

        // 本次读档以来真正画到过物品上的图：选图窗口放缩略图时（ReleasePreviews）这些不放。
        // 不去枚举“哪件物品还拿着哪张图”——夜间背包、鼠标上拎着的物品枚举不到。
        private static readonly HashSet<string> shown = new HashSet<string>();

        private static void Show(GameItem item, PaintId id)
        {
            shown.Add(id.Text);
            item.useSpriteFromMod = true;
            item.SetSprite(ModId, id.SpriteKey);
        }

        // ── 成品：内存 → 缓存 → 现画 ──

        private static Sprite Ensure(PaintId id)
        {
            if (sprites.TryGetValue(id.Text, out Sprite have) && have != null) return have;
            if (Self() == null) return null;

            // ClaudeNote: 0.3.0 外部立绘包的图不读盘就能给出指纹：缓存对得上就直接读缓存，不去解码几 MB 的原图
            Texture2D tex = null;
            if (!Settled && Cache.Has(id, null)) tex = Cache.Load(id);   // 来源还没定下来：缓存里有就用缓存，不拿半路的来源去比对 / 重画（见 Settled）
            if (tex != null) return Register(id, tex);
            string fileSig = FileSignature(id);
            if (fileSig != null && Cache.Has(id, fileSig)) tex = Cache.Load(id);
            if (tex != null) return Register(id, tex);

            Sprite source = Resolve(id);
            string signature = source != null ? fileSig ?? Signature(source, id) : null;
            if (Cache.Has(id, signature)) tex = Cache.Load(id);              // 来源没变（或来源拿不到）：读缓存
            if (tex == null && source != null) tex = Bake(id, source, signature);
            if (tex == null && source != null && Cache.Has(id, null)) tex = Cache.Load(id);   // 现画失败：退回旧缓存
            if (tex == null) return null;
            return Register(id, tex);
        }

        private static Sprite Register(PaintId id, Texture2D tex)
        {
            // 成品边长 ÷ 格子边长（原版每格 16 像素、每单位 100 像素）：不管成品多大都正好铺满 cells×cells 格
            float ppu = 100f * tex.width / (id.Cells * Mathf.Max(1, UISettings.current != null ? UISettings.current.gridSize : 16));
            tex.name = id.SpriteKey;
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu, 0u, SpriteMeshType.FullRect);
            sprite.name = id.SpriteKey;
            sprites[id.Text] = sprite;
            self.Sprites[id.SpriteKey] = sprite;
            return sprite;
        }

        private static Sprite Resolve(PaintId id)
        {
            if (id.HasVariant || id.Group == PortraitGroup) return PortraitLibrary.ResolveImage(id.Key, id.Variant);   // 写了哪个来源就只取那个来源的；差分只认立绘库里注册的，不拿本体顶替
            if (!groups.TryGetValue(id.Group, out Group g)) return null;
            try { return g.resolve(id.Key); }
            catch (Exception ex) { Entry.Warn("resolve " + id.Text + " failed: " + ex.Message); return null; }
        }

        /// <summary>从来源画一张并写进缓存。</summary>
        // ClaudeNote: 0.7.2 成品贴图保持可读（Lune 的日志 4 MB，几乎全是 Unity 的同一条警告）。
        //   成因：游戏给物品图开了“按透明度判断点没点中”（alphaHitTestMinimumThreshold），这要读贴图像素；
        //     我为了省内存把成品贴图设成了不可读 → 鼠标每在物品上动一下 Unity 就报一条警告，而且透明的地方也算点中。
        //   可读的代价：每张图在内存里多留一份像素（128×128 是 64 KB，256×256 是 256 KB），几十张图也就几 MB。
        private static Texture2D Bake(PaintId id, Sprite source, string signature)
        {
            if (!SourceReader.Read(source, out Color32[] pixels, out int w, out int h, out RectInt rect)) return null;
            // ClaudeNote: 0.6.0 像素已经读出来了，顺手量本体图的内容占比（立绘大小自适配用，见 Display/Fit.cs）
            if (id.Group == PortraitGroup && !id.HasVariant)
            {
                ImageKey.Parse(id.Key, out string from, out string portrait, out int number);
                if (number == 0) Fit.Record(from ?? PortraitLibrary.SourceOf(portrait), portrait, pixels, w, rect);   // 同来源的第 2、3… 张不算这个来源的“标准身材”
            }
            Color32[] framed = Raster.Frame(pixels, w, h, rect, id.Cells * PixelsPerCell, out int size, out bool point);
            if (framed == null) return null;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(framed);
            tex.Apply(false, false);
            Cache.Save(id, tex, signature, point);
            BakedThisSession++;
            return Finish(tex, point);
        }

        /// <summary>缓存指纹 = 来源指纹 + 当前的大小上限：玩家改了“道具图大小”，旧缓存自动重做。</summary>
        private static string Signature(Sprite source, PaintId id)
        {
            // ClaudeNote: 0.3.0 加上“由谁提供”：两个立绘包的图尺寸相同、贴图又没名字时也能分辨
            string by = id.Group == PortraitGroup || id.HasVariant ? "|" + (ImageKey.SourceOf(id.Key) ?? PortraitLibrary.SourceOf(id.Key)) : "";
            return SourceReader.Signature(source) + by + "|max" + id.Cells * PixelsPerCell;
        }

        /// <summary>外部图片的指纹（不读图）；不是外部图片返回 null。</summary>
        private static string FileSignature(PaintId id)
        {
            if (id.Group != PortraitGroup && !id.HasVariant) return null;
            string sig = PortraitLibrary.ImageSignature(id.Key, id.Variant);
            return sig != null ? sig + "|max" + id.Cells * PixelsPerCell : null;
        }

        internal static Texture2D Finish(Texture2D tex, bool point)
        {
            tex.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        /// <summary>后台预处理一张：缓存里没有（或来源变了）才画，只写缓存、不占内存。返回是否真的画了。</summary>
        internal static bool Prebake(PaintId id, bool heavy = false)
        {
            string fileSig = FileSignature(id);
            // ClaudeNote: 1.0.0 外部图片（立绘包文件夹里的、打包在 DLL 里的）后台一律不预画（Lune：“还是有微妙的卡顿”）。
            //   后台预处理当初的前提是“单张一两毫秒”——那是游戏原图和已载入的贴图。外部图片要先把几 MB 的 PNG 读出来解码，一张几十毫秒，
            //   而现在立绘包都是这种：日志里一次进店后台画了 1047 张，等于几分钟里每隔一两百毫秒卡一下。
            //   平时这类图用到才画（Ensure：道具要显示它、选图窗口翻到它），这里只负责认出“来源变了”，让内存里那张作废。
            //   heavy = 集中预处理（读档后、入睡时各一次，见 Baker.Burst；Lune 2026-11-18）：这两个时候卡一下不碍事，外部图片也画。
            if (fileSig != null)
            {
                if (Cache.Has(id, fileSig)) return false;
                if (sprites.TryGetValue(id.Text, out Sprite old) && old != null) Drop(id, old);
                if (!heavy) return false;
                Sprite external = Resolve(id);
                if (external == null) return false;
                Texture2D made = Bake(id, external, fileSig);
                if (made == null) return false;
                UnityEngine.Object.Destroy(made);
                return true;
            }
            Sprite source = Resolve(id);
            if (source == null) return false;
            string signature = Signature(source, id);
            if (Cache.Has(id, signature)) return false;
            if (sprites.TryGetValue(id.Text, out Sprite stale) && stale != null) Drop(id, stale);   // 来源变了：内存里那张也作废
            float t = Health.Now;
            Texture2D tex = Bake(id, source, signature);
            Health.Took("background: bake one image", t);
            if (tex == null) return false;
            UnityEngine.Object.Destroy(tex);
            return true;
        }

        /// <summary>后台过完一遍后调：这一遍里作废过正在用的成品，就把店里的物品接回去。</summary>
        // ClaudeNote: 0.8.0 每扫完一遍都过一次物品：扫描正是在“来源有变动”时才发生的（新登记、包开关、读完档），
        //   自动挑来源的物品要跟着重挑；过一遍只是每件带标签的物品查一次表，图对得上的不动。
        internal static void AfterScan()
        {
            RestoreAll();
        }

        /// <summary>现在还认得的图：“组\n键”的集合（各来源组列得出的 + 本局存档正在用的）。清缓存用。
        /// 有任何一个组列不出来（它的 mod 出错了）就返回 false——这一轮不清，宁可多留。</summary>
        internal static bool TryKnownKeys(out HashSet<string> known)
        {
            known = new HashSet<string>();
            foreach (Group g in new List<Group>(groups.Values))
            {
                IList<string> keys;
                try { keys = g.list(); }
                catch (Exception) { return false; }
                if (keys == null) continue;
                foreach (string key in keys)
                {
                    PaintId id = Canon(new PaintId(g.name, key, g.cells));   // 和缓存里记的写法一致
                    if (id.Valid) known.Add(id.Group + "\n" + id.Key);
                }
            }
            foreach (string text in used)
            {
                PaintId id = Canon(PaintId.Parse(text));
                if (id.Valid) known.Add(id.Group + "\n" + id.Key);
            }
            foreach (PaintId id in EveryImage()) known.Add(id.Group + "\n" + id.Key);   // 0.8.6 各来源的图都留着（工坊、选图窗口要看）
            return true;
        }

        /// <summary>全部来源组里的图，加上本局存档正在用的图（含差分；差分不在来源组里，靠这里才会在来源变了时重画）。后台预处理用。</summary>
        internal static List<PaintId> AllIds()
        {
            var list = new List<PaintId>();
            var seen = new HashSet<string>();
            foreach (Group g in new List<Group>(groups.Values))
                foreach (string key in Keys(g.name))
                {
                    PaintId id = Canon(new PaintId(g.name, key, g.cells));
                    if (seen.Add(id.Text)) list.Add(id);
                }
            foreach (string text in used)
            {
                PaintId id = Canon(PaintId.Parse(text));
                if (id.Valid && seen.Add(id.Text)) list.Add(id);
            }
            return list;
        }

        // ClaudeNote: 0.8.6 每张立绘在每个来源里的图（来源:图ID~编号），含没激活的候选素材。
        //   后台在主清单（当前选中的来源）画完、工坊目录存好之后，再慢慢把这些也画进缓存：
        //   标题界面的工坊拿不到立绘表，缩略图只能来自缓存——没画过的来源就只有名字没有图。
        internal static List<PaintId> EveryImage()
        {
            var list = new List<PaintId>();
            // ClaudeNote: 0.9.7 Lune：裁剪成品是给头像系统和立绘衍生道具用的，只该预处理“激活中的立绘包、被激活中 mod 引用的立绘资源包”，
            //   其余的在手动选图之后才裁。0.8.6 为了让标题界面的工坊有缩略图，把没激活的候选素材也全裁了——工坊现在自己读原图（UI/Workshop/Thumbs.cs），这里收回来。
            //   没激活的候选：选图时由 Ensure 现裁现存；画到过物品上的成品有 kept 标记，清理不会碰。
            foreach (Portrait p in PortraitLibrary.Everything())
                foreach (string key in PortraitLibrary.ImagesOf(p.Field))
                    if (Sources.Active(ImageKey.SourceOf(key) ?? p.Source)) list.Add(new PaintId(PortraitGroup, key, 4));
            return list;
        }

        private static void Drop(PaintId id, Sprite sprite)
        {
            sprites.Remove(id.Text);
            if (self != null && self.Sprites.TryGetValue(id.SpriteKey, out Sprite s) && s == sprite) self.Sprites.Remove(id.SpriteKey);
            Texture2D tex = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }

        private static LoadedMod Self()
        {
            if (self == null) self = ModLoader.Mods.Find(m => m?.Manifest != null && m.Manifest.ID == ModId);
            return self;
        }

        // ── 跟着存档走 ──

        private static void Remember(PaintId id)
        {
            Cache.Keep(id);
            if (used.Add(id.Text)) SaveUsed();
        }

        private static void SaveUsed()
        {
            if (!PlayerStore.HasInstance) return;
            try { ModHelper.SetModData(ModId, UsedKey, string.Join("\n", used)); }
            catch (Exception ex) { Entry.Warn("used list not saved: " + ex.Message); }
        }

        /// <summary>读档、物品还原之前：把这局存档用到的图先载入，物品一还原就能取到。</summary>
        internal static void Preload()
        {
            // ClaudeNote: 读档 = 物品全部重建。先放掉上一局留在内存里的成品（切换差分攒下的也在内），本局要用的下面按清单重新载入。
            ReleaseMemory();
            used.Clear();
            string text = ModHelper.GetModData(ModId, UsedKey, "");
            if (string.IsNullOrEmpty(text)) return;
            int ok = 0;
            foreach (string line in text.Split('\n'))
            {
                PaintId id = Canon(PaintId.Parse(line));
                if (!id.Valid) continue;
                used.Add(id.Text);
                if (Ensure(id) != null) ok++;
                Cache.Keep(id);
            }
            SourceReader.Release();
            Entry.Info("preloaded " + ok + "/" + used.Count + " item image(s) for this save");
        }

        /// <summary>读档完成后兜底：店里带标签的物品逐件接回图（先载入漏掉的、别的 mod 清过表的都在这里补上）。</summary>
        internal static void RestoreAll()
        {
            if (!PlayerStore.HasInstance || Retired || !Running) return;
            if (Settled) PackShelf.Apply();   // 没激活的候选素材也要先登记好，才知道“来源还在不在”
            int fixedCount = 0;
            foreach (GameItem item in Items.All())
            {
                if (item == null || !item.IsTag(SourceTag)) continue;
                try
                {
                    string before = item.spritePath;
                    Restore(item);
                    if (item.spritePath != before) fixedCount++;
                }
                catch (Exception ex) { OneBadItem(item, ex); }
            }
            SourceReader.Release();
            if (fixedCount > 0) Entry.Info("restored " + fixedCount + " item image(s) after load");
        }

        /// <summary>入睡（存档）前：把“用到的图”收成店里实际还在的那些。一晚一次。</summary>
        internal static void PruneUsed()
        {
            if (!PlayerStore.HasInstance) return;
            var alive = new HashSet<string>();
            foreach (GameItem item in Items.All())
            {
                PaintId id = SourceOf(item);
                if (id.Valid) alive.Add(id.Text);
            }
            if (!alive.SetEquals(used))
            {
                used.Clear();
                used.UnionWith(alive);
                SaveUsed();
            }
        }

        // ── 启停与维护 ──

        internal static void Start()
        {
            Running = true;
            NeedScan = true;
            DefineBuiltInGroup();
        }

        internal static void Stop()
        {
            Running = false;
            Settled = false;
            Cache.SaveIndex();
            foreach (var kv in new List<KeyValuePair<string, Sprite>>(sprites))
                if (kv.Value != null) Drop(PaintId.Parse(kv.Key), kv.Value);
            sprites.Clear();
            // ClaudeNote: 来源组、立绘库注册、规则都是别的 mod 登记的，本 mod 停用再启用时它们不会重新登记，所以这里不清。
            used.Clear();
            shown.Clear();
            previews.Clear();
            SourceReader.Release();
            PortraitLibrary.ReleaseWrapped();
            PortraitLibrary.ForgetEmbedded();
            self = null;
        }

        /// <summary>放掉内存里的全部成品（贴图 + Sprite）。缓存文件不动，之后用到再从缓存读。</summary>
        private static void ReleaseMemory()
        {
            shown.Clear();
            previews.Clear();
            foreach (var kv in new List<KeyValuePair<string, Sprite>>(sprites))
                if (kv.Value != null) Drop(PaintId.Parse(kv.Key), kv.Value);
            sprites.Clear();
        }

        /// <summary>设置项改了：换上限，内存里的成品作废，店里的物品立刻按新大小重画，其余的交给后台。</summary>
        internal static void SetPixelsPerCell(int pixels)
        {
            pixels = pixels >= 64 ? 64 : 32;
            if (pixels == PixelsPerCell) return;
            PixelsPerCell = pixels;
            if (!Running) return;
            foreach (var kv in new List<KeyValuePair<string, Sprite>>(sprites))
                if (kv.Value != null) Drop(PaintId.Parse(kv.Key), kv.Value);
            NeedScan = true;
            RestoreAll();
        }

        /// <summary>清空缓存索引并重画全部来源组（来源图换了但指纹没变时手动用）。</summary>
        public static void Rebuild()
        {
            Cache.Forget();   // 磁盘上用不到的旧图由后台扫完这一遍后清（Cache.Prune）
            Fit.Forget();
            PortraitLibrary.Invalidate();
            foreach (var kv in new List<KeyValuePair<string, Sprite>>(sprites))
            {
                PaintId id = PaintId.Parse(kv.Key);
                if (kv.Value != null) Drop(id, kv.Value);
            }
            NeedScan = true;
            RestoreAll();
        }

        /// <summary>当前状态（调试用）。</summary>
        public static string Report()
        {
            var b = new StringBuilder();
            b.Append("TextureExtend ").Append(Entry.Version).Append(" · running ").Append(Running)
                .Append(" · max ").Append(PixelsPerCell).Append("px/cell").Append(" · vanilla sources ").Append(VanillaReady ? "ready" : "not ready")
                .Append(" · cached ").Append(Cache.Count).Append(" · in memory ").Append(sprites.Count)
                .Append(" · baked this session ").Append(BakedThisSession).Append(" · used by save ").Append(used.Count).Append(" · packs ").Append(Packs.All.Count)
                .Append("\n  ").Append(PortraitLibrary.Summary())
                .Append(" · ").Append(PaintRules.Count).Append(" dynamic rule(s) · ")
                .Append(PaintRules.ItemVariantCount).Append(" static item variant(s)");
            foreach (Group g in groups.Values)
                b.Append("\n  group ").Append(g.name).Append(" (").Append(g.cells).Append("x").Append(g.cells).Append("): ").Append(Keys(g.name).Count).Append(" source(s)");
            return b.ToString();
        }
    }
}
