// ClaudeNote: 动态差分规则：按注册的条件，让道具的图随状态自动切换（Lune 2026-11-15 的设计）。
//   认的是 ID：静态道具认物品 ID，立绘衍生道具认立绘 ID。ID 不变，任何 mod 追加的状态标签都能挂上去。
//   写法：
//     Painter.For(modId, PaintTarget.StaticItem, "tentacle_child").Tag("sick").Cond(item => Child.IsSick(item)).Weight(10);
//     Painter.For(modId, PaintTarget.Portrait, "femaleLower1").Tag("love").Cond(item => Chara.IsInLove(item));
//     Painter.For(modId, PaintTarget.Portrait, "*").Tag("infected").Cond(...);        // "*" = 所有立绘
//   图从哪来：
//     立绘 —— 立绘库里这张立绘的同名差分（RegisterVariant 登记的，或贴图表里“字段_标签”自动认到的）。规则会顺带声明这个状态标签。
//     静态道具 —— 依次找：规则上 .Sprite(modId, 贴图键) 指定的 → 登记过的静态差分（CSV 或 Painter.AddItemVariant）→ 道具原图所在 mod 的 “原图键_标签”。
//   多条规则同时成立：权重（Weight）大的先；权重相同按登记先后。排在前面的那条如果没有图，轮到下一条；
//   都不成立或都没图 → 回到本体（只撤规则会给的状态；别的 mod 手动设的、规则里没有的状态保留）。
//   玩家在选图里亲手选了差分 → 物品加锁，规则不再动它，直到选回“自动”。
//     静态道具的图一律是 mod 贴图表里的原图，不经过本 mod 的合成与缓存（合成只用于立绘衍生道具）。
//   什么时候算（Lune 2026-11-15）：不逐帧算。读档后、起床后、开门后、关门后过一遍店里的物品；来客到来 / 离开时只过立绘衍生道具。
//     其余时机由管状态的 mod 自己调 Painter.Refresh(item) 立刻刷新。
//   条件函数收到的是那件物品；不针对具体物品查询时（头像、对话框用 Painter.ActiveTag）收到 null。
using System;
using System.Collections.Generic;
using UnityEngine;
using ProbablyExtendFramework.TagSystem;

namespace ProbablyExtendFramework
{
    public enum PaintTarget
    {
        /// <summary>普通道具，认物品 ID，贴图来自 mod 的贴图表。</summary>
        StaticItem,
        /// <summary>立绘（以及用立绘画出来的道具），认立绘 ID。</summary>
        Portrait
    }

    /// <summary>一条动态差分规则。用 Painter.For(...) 创建，链式补全；缺标签或缺条件的规则不生效。</summary>
    public sealed class PaintRule
    {
        internal string owner, id, tag, spriteMod, spriteKey;
        internal PaintTarget target;
        internal Func<GameItem, bool> cond, only;
        internal int weight, order;
        internal bool warned;

        /// <summary>状态标签（也是差分名）。</summary>
        public PaintRule Tag(string stateTag) { tag = PortraitLibrary.Normal(stateTag); if (target == PaintTarget.Portrait) PortraitLibrary.DeclareVariant(owner, tag); return this; }
        /// <summary>条件：返回 true 时这条规则成立。参数是被检查的物品（可能为 null）。</summary>
        public PaintRule Cond(Func<GameItem, bool> condition) { cond = condition; return this; }
        /// <summary>条件，不关心是哪件物品的写法。</summary>
        public PaintRule Cond(Func<bool> condition) { cond = condition == null ? (Func<GameItem, bool>)null : _ => condition(); return this; }
        /// <summary>同 Cond（总纲里的写法）。</summary>
        public PaintRule When(Func<GameItem, bool> condition) { return Cond(condition); }
        /// <summary>这个目标禁止玩家手动切换（选图窗口能看不能选）。按目标算，任何一条规则或表格行设了就生效。</summary>
        public PaintRule NoManual() { PaintRules.DenyManual(owner, target, id); return this; }
        /// <summary>只管满足这个条件的物品（比如只管 lewd_meme 的少女）。不满足的物品当这条规则不存在：既不会被它切图，也不会被它撤回状态。</summary>
        public PaintRule Only(Func<GameItem, bool> filter) { only = filter; return this; }
        /// <summary>权重，大的先。默认 0。</summary>
        public PaintRule Weight(int value) { weight = value; PaintRules.Unsorted(); return this; }
        /// <summary>静态道具用：这个状态显示哪张图（mod 的贴图表里的键）。</summary>
        public PaintRule Sprite(string modId, string key) { spriteMod = modId; spriteKey = key; return this; }

        internal bool Complete => !string.IsNullOrEmpty(tag) && cond != null;
        // ClaudeNote: 第 3 步 id 可以写 @组名：立绘看立绘的标签（含组标签），道具看游戏分类与标签层的组
        internal bool Covers(string targetId)
        {
            if (id == "*" || id == targetId) return true;
            if (id.Length < 2 || id[0] != '@' || string.IsNullOrEmpty(targetId)) return false;
            string g = id.Substring(1).ToLowerInvariant();
            return target == PaintTarget.Portrait ? PortraitLibrary.Get(targetId)?.Has(g) ?? false : Layer.ItemHas(targetId, g);
        }

        internal bool Applies(GameItem item)
        {
            if (only == null) return true;
            try { return item != null && only(item); }
            catch (Exception) { return false; }
        }

        internal bool Holds(GameItem item)
        {
            try { return cond(item); }
            catch (Exception ex)
            {
                if (!warned) { warned = true; Entry.Warn("rule " + owner + ":" + id + "#" + tag + " condition failed (treated as false): " + ex.Message); }
                return false;
            }
        }
    }

    internal static class PaintRules
    {
        /// <summary>静态道具切过图后，记下原图与当前状态：是否 mod 图|状态|图集或 mod|贴图键。随物品存档。</summary>
        // ClaudeNote: 1.0.0 存档标签前缀由 TEXEXT_ 改为 PEF_（发布前定名，不做旧存档迁移）。
        internal const string BaseTag = "PEF_BASE";

        private static readonly List<PaintRule> rules = new List<PaintRule>();
        private static bool sorted = true;
        private static int counter;

        internal static int Count => rules.Count;
        internal static void Unsorted() { sorted = false; }

        internal static PaintRule Add(string owner, PaintTarget target, string id)
        {
            var rule = new PaintRule { owner = owner ?? "", target = target, id = id ?? "", order = counter++ };
            rules.Add(rule);
            sorted = false;
            return rule;
        }

        // ── 立绘识别钩子：不是本框架画的物品（别的 mod 自己贴图的少女道具…），由那个 mod 告诉框架它是哪张立绘 ──

        private sealed class Resolver
        {
            internal string owner;
            internal Func<GameItem, string> find;
            internal bool takeOver;
        }

        private static readonly List<Resolver> resolvers = new List<Resolver>();

        /// <summary>takeOver = 认出来就接管成框架画的高清图（规则都不成立时显示高清本体）；否则只在有规则成立时才接管。</summary>
        internal static void AddResolver(string owner, Func<GameItem, string> resolver, bool takeOver)
        {
            if (resolver != null) resolvers.Add(new Resolver { owner = owner ?? "", find = resolver, takeOver = takeOver });
        }

        // ── 动态贴图道具 [DynamicTexture]（0.8.0，Lune 2026-11-18） ──
        //   一般的立绘道具“是哪张图”是定死的（这种道具就是这个角色），选图窗口只给它看同一个图 ID 的图。
        //   动态贴图道具的图是生成时才定的（lewd_meme 的少女：抓到谁就是谁的立绘），哪张都可能，选图窗口直接给整个图库。
        //   登记口子三个：Painter.DynamicTexture(modId, 道具 ID 或 判断函数)、表格 dynamic 行、道具标签层里的 dynamictexture 标签（工坊可贴）。
        internal const string DynamicTag = "dynamictexture";

        private sealed class Dynamic
        {
            internal string owner, itemId;
            internal Func<GameItem, bool> test;
        }

        private static readonly List<Dynamic> dynamics = new List<Dynamic>();

        internal static void AddDynamic(string owner, string itemId, Func<GameItem, bool> test)
        {
            if (string.IsNullOrEmpty(itemId) && test == null) return;
            dynamics.Add(new Dynamic { owner = owner ?? "", itemId = itemId, test = test });
        }

        internal static bool IsDynamic(GameItem item)
        {
            if (item == null) return false;
            foreach (Dynamic d in dynamics)
            {
                if (d.itemId != null && d.itemId == item.identifier) return true;
                try { if (d.test != null && d.test(item)) return true; }
                catch (Exception) { }
            }
            return Layer.ItemHas(item.identifier, DynamicTag);
        }

        /// <summary>别的 mod 认出来的立绘 ID；认不出、或立绘库里没有这张返回 null。</summary>
        internal static string ResolvePortrait(GameItem item) { return ResolvePortrait(item, out _); }

        internal static string ResolvePortrait(GameItem item, out bool takeOver)
        {
            takeOver = false;
            foreach (Resolver r in resolvers)
            {
                string id;
                try { id = r.find(item); }
                catch (Exception) { continue; }
                if (string.IsNullOrEmpty(id) || PortraitLibrary.Get(id) == null) continue;
                takeOver = r.takeOver;
                return id;
            }
            return null;
        }

        // ── 手动锁定：玩家在选图里亲手选了某个差分，规则就不再动它，直到选回“自动” ──

        internal const string LockTag = "PEF_LOCK";

        internal static bool IsLocked(GameItem item) { return item != null && item.IsTag(LockTag); }

        // ── 禁止手动切换（注册方的权限设置）：目标类型|ID → 设它的来源 ──

        private static readonly Dictionary<string, string> manualDenied = new Dictionary<string, string>();

        internal static void DenyManual(string owner, PaintTarget target, string id)
        {
            if (!string.IsNullOrEmpty(id)) manualDenied[target + "|" + id] = owner ?? "";
        }

        /// <summary>玩家能不能手动切换这个目标（"*" 的禁令管全部同类目标）。</summary>
        internal static bool ManualAllowed(PaintTarget target, string id)
        {
            if (manualDenied.ContainsKey(target + "|" + id) || manualDenied.ContainsKey(target + "|*")) return false;
            // ClaudeNote: 第 3 步 禁令也可以下在 @组 上
            foreach (string key in manualDenied.Keys)
            {
                int bar = key.IndexOf('|');
                if (bar < 0 || key.Length < bar + 3 || key[bar + 1] != '@' || key.Substring(0, bar) != target.ToString()) continue;
                string g = key.Substring(bar + 2).ToLowerInvariant();
                if (target == PaintTarget.Portrait ? PortraitLibrary.Get(id)?.Has(g) ?? false : Layer.ItemHas(id, g)) return false;
            }
            return true;
        }

        // ClaudeNote: 第 2 步 锁定时记下玩家选的状态：临时状态（pulse）结束后回到它
        internal static void SetLocked(GameItem item, bool locked)
        {
            if (item == null) return;
            if (!locked) { if (IsLocked(item)) item.ModifyTag(LockTag, s => s.Disable()); return; }
            string state = Painter.StateOf(item);
            item.ModifyTag(LockTag, s => { s.Enable(); s.SetString("=" + state); });
        }

        /// <summary>锁定时玩家选的状态（空 = 本体）。
        /// 0.4.x 及更早锁定的物品没记状态，返回 null（不去还原）。</summary>
        internal static string LockedState(GameItem item)
        {
            string text = item?.GetTagReadonly(LockTag)?.GetString();
            return text != null && text.StartsWith("=", StringComparison.Ordinal) ? text.Substring(1) : null;
        }

        internal static void Remove(string owner)
        {
            rules.RemoveAll(r => r.owner == owner);
            foreach (string key in new List<string>(manualDenied.Keys)) if (manualDenied[key] == owner) manualDenied.Remove(key);
            resolvers.RemoveAll(r => r.owner == owner);
            dynamics.RemoveAll(d => d.owner == owner);
            ItemArt.Remove(owner);
            VariantTags.Remove(owner);
        }

        // ── 静态道具的差分：道具贴图组（Core/ItemArt.cs；1.0.0 rc6 起每个来源各存各的，不再后登记的盖掉先登记的） ──

        internal static int ItemVariantCount => ItemArt.Count;

        internal static bool HasItemVariant(string itemId, string tag) { return ItemArt.Pick(itemId, tag) != null; }

        internal static void AddItemVariant(string owner, string itemId, string tag, string spriteMod, string spriteKey)
        {
            ItemArt.Add(owner, itemId, tag, spriteMod, spriteKey);
        }

        /// <summary>登记过贴图扩展的道具 ID（差分、原版外观、指名的规则）。取道具池子用。</summary>
        internal static List<string> KnownItemIds()
        {
            var set = new HashSet<string>();
            foreach (string id in ItemArt.Ids) set.Add(id);
            foreach (string key in nativeLooks.Keys) set.Add(key);
            foreach (PaintRule r in rules) if (r.target == PaintTarget.StaticItem && r.id.Length > 0 && r.id != "*" && r.id[0] != '@') set.Add(r.id);
            return new List<string>(set);
        }

        private static List<PaintRule> Ordered()
        {
            if (!sorted)
            {
                rules.Sort((a, b) => a.weight != b.weight ? b.weight.CompareTo(a.weight) : a.order.CompareTo(b.order));
                sorted = true;
            }
            return rules;
        }

        /// <summary>这个 ID 现在成立的规则，按先后。item 可以为 null。</summary>
        internal static IEnumerable<PaintRule> Holding(PaintTarget target, string id, GameItem item)
        {
            foreach (PaintRule r in Ordered())
                if (r.target == target && r.Complete && r.Covers(id) && r.Applies(item) && r.Holds(item)) yield return r;
        }

        internal static bool Any(PaintTarget target, string id, GameItem item = null)
        {
            foreach (PaintRule r in rules) if (r.target == target && r.Complete && r.Covers(id) && (item == null || r.Applies(item))) return true;
            return false;
        }

        // ── 立绘衍生道具 ──

        internal static void RefreshPortraitItem(GameItem item, PaintId now)
        {
            string portrait = ImageKey.IdOf(now.Key);   // 0.8.0 规则按图 ID 管，不分来源
            // ClaudeNote: 1.0.0 rc6 别人点名要的标签（Wants）先于规则；临时状态（pulse）在时不看。对不上了由框架撤回本体（AutoTag）
            Portrait known = PortraitLibrary.Get(portrait);
            if (known != null && !Pulses.Any(item))
            {
                string want = VariantTags.Best(PaintTarget.Portrait, portrait, known.Variants, Wants.Of(PaintTarget.Portrait, portrait));
                if (want != null && Painter.SetVariant(item, want)) { Auto(item, true); return; }
                if (item.IsTag(AutoTag)) { Auto(item, false); Painter.SetVariant(item, null); }
            }
            if (rules.Count == 0 || !Any(PaintTarget.Portrait, portrait, item)) return;   // 没有规则管它：手动 SetVariant 的结果不动
            bool held = false;
            foreach (PaintRule r in Holding(PaintTarget.Portrait, portrait, item))
            {
                held = true;
                if (Painter.SetVariant(item, r.tag)) return;
            }
            // ClaudeNote: 0.4.2（Lune 2026-11-16 测试后改）有规则成立但那个状态没图、或都不成立 → 接管来的道具都显示高清本体，
            //   不再退回原 mod 那张糊的；只有框架不再管它（识别钩子认不出了）时才还原原图（见 Painter.Refresh）
            if (held || IsAdopted(item)) { Painter.SetVariant(item, null); return; }
            if (Manages(PaintTarget.Portrait, portrait, now.Variant, item)) Painter.SetVariant(item, null);
        }

        // ClaudeNote: 0.3.0 都不成立时，只撤掉“规则会给的”差分。别的 mod 用 SetVariant 手动设的差分（规则里没有这个标签）不动，
        //   免得一个 mod 的通配规则（"*"）把另一个 mod 设好的状态冲掉。
        private static bool Manages(PaintTarget target, string id, string tag, GameItem item)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            foreach (PaintRule r in rules) if (r.target == target && r.Complete && r.tag == tag && r.Covers(id) && r.Applies(item)) return true;
            return false;
        }

        // ── 接管别的 mod 的立绘道具：先记下它原来的图，规则都不成立时还原 ──

        // ClaudeNote: 0.8.0 接管的记号并进 textureInfo（末尾的 |adopted），原图并进 textureOG —— 之前另有一个 PEF_ORIG，
        //   和“经框架画图的道具”的 PEF_PREV 是同一件事记了两套。
        internal static bool IsAdopted(GameItem item) { Painter.ReadInfo(item, out _, out _, out bool adopted); return adopted; }

        /// <summary>把别的 mod 的立绘道具接管过来画成指定状态（空 = 高清本体）。portraitId 可以只写图 ID（自动挑来源）或写明来源。</summary>
        internal static bool Adopt(GameItem item, string portraitId, string tag)
        {
            if (item == null) return false;
            int cells = item.shape != null ? Mathf.Clamp(Mathf.Max(item.shape.width, item.shape.height), 1, 8) : 4;
            PaintId now = Painter.SourceOf(item);
            if (now.Valid) cells = now.Cells;
            Adopting = !now.Valid;   // 第一次接管：Paint 在 textureInfo 里记上 adopted
            try { return Painter.Paint(item, Painter.PortraitGroup, portraitId, cells, tag); }
            finally { Adopting = false; }
        }

        internal static bool Adopting { get; private set; }

        /// <summary>还原成接管前的原图（原图集、原贴图键、原占格），去掉本框架的来源标签。</summary>
        internal static void Release(GameItem item)
        {
            Painter.Restyle(item, item.GetTagReadonly(Painter.OgTag)?.GetString());
            foreach (string tag in new[] { Painter.SourceTag, Painter.InfoTag, Painter.OgTag }) item.ModifyTag(tag, s => s.Disable());
        }

        /// <summary>别的 mod 的立绘道具（经识别钩子认出、还没接管）：有规则成立就接管——那个状态有图画状态，没图画高清本体。都不成立就不碰它。</summary>
        internal static void RefreshForeignPortrait(GameItem item, string portraitId, bool takeOver)
        {
            bool held = false;
            if (rules.Count > 0 && Any(PaintTarget.Portrait, portraitId, item))
                foreach (PaintRule r in Holding(PaintTarget.Portrait, portraitId, item))
                {
                    held = true;
                    if (Adopt(item, portraitId, r.tag)) return;
                }
            if (held || takeOver) Adopt(item, portraitId, null);
        }

        // ── 静态道具 ──

        // ClaudeNote: 1.0.0 rc6 先后：别人点名要的标签（Wants）> 规则 > 时间段标签（openshop / endshop / wakeup）> 本体。
        //   临时状态（pulse）是权重最高的规则，有它时不看意向。带着被排除标签的差分哪一层都不给。
        //   AutoTag：这个状态是框架按标签自动给的（不是规则、不是别人手动设的）——标签对不上了由框架撤回本体。
        internal const string AutoTag = "PEF_AUTO";

        internal static void RefreshStaticItem(GameItem item)
        {
            string id = item.identifier;
            if (string.IsNullOrEmpty(id)) return;
            bool art = ItemArt.Has(id);
            if (!art && !item.IsTag(BaseTag) && rules.Count == 0) return;   // 绝大多数物品在这里就走了
            bool ruled = rules.Count > 0 && Any(PaintTarget.StaticItem, id);
            if (!art && !ruled) { Heal(item); return; }
            List<string> variants = art ? ItemArt.Variants(id) : null;
            if (art && !Pulses.Any(item) && Decided(item, id, variants, true)) return;
            if (ruled)
                foreach (PaintRule r in Holding(PaintTarget.StaticItem, id, item))
                {
                    if (VariantTags.Banned(PaintTarget.StaticItem, id, r.tag)) continue;
                    if (SetStaticState(item, r.tag, r.spriteMod, r.spriteKey)) { Auto(item, false); return; }   // 没有图的规则跳过，轮到下一条
                }
            if (art && Decided(item, id, variants, false)) return;
            string state = StateOf(item);
            if (item.IsTag(AutoTag) || (ruled && Manages(PaintTarget.StaticItem, id, state, item)) || (state.Length > 0 && VariantTags.Banned(PaintTarget.StaticItem, id, state)))
            {
                SetStaticState(item, "", null, null);
                Auto(item, false);
            }
            else Heal(item);
        }

        private static bool Decided(GameItem item, string id, List<string> variants, bool explicitOnly)
        {
            string want = VariantTags.Best(PaintTarget.StaticItem, id, variants, Wants.ForItem(id, explicitOnly));
            if (want == null || !SetStaticState(item, want, null, null)) return false;
            Auto(item, true);
            return true;
        }

        private static void Auto(GameItem item, bool on)
        {
            if (on == item.IsTag(AutoTag)) return;
            if (on) item.EnableTag(AutoTag); else item.ModifyTag(AutoTag, s => s.Disable());
        }

        // 没人管、但切过图的静态道具：图找不到了（差分所在的 mod 被撤了；外部差分读档时还没载入）先试着把图接回来，接不回就退回本体。
        private static void Heal(GameItem item)
        {
            if (!item.IsTag(BaseTag) || !item.useSpriteFromMod || HasSprite(item.spriteAtlasPath, item.spritePath)) return;
            if (item.spriteAtlasPath == Painter.ModId && ItemArt.EnsureKey(item.spritePath)) { item.SetSprite(Painter.ModId, item.spritePath); return; }
            SetStaticState(item, "", null, null);
            Auto(item, false);
        }

        /// <summary>把静态道具切到某个状态的图（空 = 本体）。那个状态没有图时返回 false，物品不动。</summary>
        internal static bool SetStaticState(GameItem item, string tag, string spriteMod, string spriteKey)
        {
            ReadBase(item, out bool baseFromMod, out string state, out string baseAtlas, out string baseKey);
            if (tag.Length == 0)
            {
                if (state.Length == 0) return true;                          // 本来就是本体
                item.useSpriteFromMod = baseFromMod;
                item.SetSprite(baseAtlas, baseKey);
                item.ModifyTag(BaseTag, s => s.Disable());
                Painter.Changed(item, state, "");
                return true;
            }
            string mod = spriteMod, key = spriteKey;
            if (key == null)
            {
                ItemArt.Look look = ItemArt.Pick(item.identifier, tag);
                if (look != null && !ItemArt.Locate(look, out mod, out key)) { mod = null; key = null; }
            }
            if (key == null) { mod = baseFromMod ? baseAtlas : null; key = baseKey + "_" + tag; }
            if (mod == null || !HasSprite(mod, key)) return false;
            // 已经是这个状态、显示的也确实是这张图 → 不动。只比标签不够：读档后图有可能没跟上。
            if (tag == state && item.useSpriteFromMod && item.spriteAtlasPath == mod && item.spritePath == key) return true;
            WriteBase(item, baseFromMod, tag, baseAtlas, baseKey);
            item.useSpriteFromMod = true;
            item.SetSprite(mod, key);
            if (mod == Painter.ModId) ItemArt.Remember(key);
            if (tag != state) Painter.Changed(item, state, tag);
            return true;
        }

        /// <summary>静态道具现在的状态标签；本体返回空字符串。</summary>
        internal static string StateOf(GameItem item)
        {
            ReadBase(item, out _, out string state, out _, out _);
            return state;
        }

        private static void ReadBase(GameItem item, out bool fromMod, out string state, out string atlas, out string key)
        {
            if (item.IsTag(BaseTag))
            {
                string[] parts = (item.GetTagReadonly(BaseTag)?.GetString() ?? "").Split(new[] { '|' }, 4);
                if (parts.Length == 4) { fromMod = parts[0] == "1"; state = parts[1]; atlas = parts[2]; key = parts[3]; return; }
            }
            fromMod = item.useSpriteFromMod; state = ""; atlas = item.spriteAtlasPath ?? ""; key = item.spritePath ?? "";
        }

        private static void WriteBase(GameItem item, bool fromMod, string state, string atlas, string key)
        {
            string text = (fromMod ? "1" : "0") + "|" + state + "|" + atlas + "|" + key;
            item.ModifyTag(BaseTag, s => { s.Enable(); s.SetString(text); });
        }

        // ── 原版的外观循环（ToggleAppearanceHelper.MakeSpriteCyclable：储物舱颜色…）：记下每种物品能换的图，选图里直接列出来 ──

        private static readonly Dictionary<string, KeyValuePair<string, string[]>> nativeLooks = new Dictionary<string, KeyValuePair<string, string[]>>();

        internal static void AddNativeLook(string itemId, string atlas, string[] sprites)
        {
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(atlas) || sprites == null || sprites.Length == 0) return;
            nativeLooks[itemId] = new KeyValuePair<string, string[]>(atlas, (string[])sprites.Clone());
        }

        internal static bool HasNativeLooks(GameItem item) { return item?.identifier != null && nativeLooks.ContainsKey(item.identifier); }

        /// <summary>这件物品原版能换的外观：(贴图名, 图)。</summary>
        internal static List<KeyValuePair<string, Sprite>> NativeLooks(GameItem item)
        {
            var list = new List<KeyValuePair<string, Sprite>>();
            if (item?.identifier == null || !nativeLooks.TryGetValue(item.identifier, out var look)) return list;
            foreach (string name in look.Value)
            {
                Sprite s = null;
                try { s = RenderHandler.TryLoadFromAtlas(look.Key, name); } catch (Exception) { }
                list.Add(new KeyValuePair<string, Sprite>(name, s));
            }
            return list;
        }

        /// <summary>换成原版的某个外观（和原版“切换”做的事一样，只是直接选到那一张）。切过本框架差分的先退回本体。</summary>
        internal static bool SetNativeLook(GameItem item, string name)
        {
            if (item?.identifier == null || !nativeLooks.TryGetValue(item.identifier, out var look) || Array.IndexOf(look.Value, name) < 0) return false;
            if (StateOf(item).Length > 0) SetStaticState(item, "", null, null);
            item.SetSpriteQuick(look.Key, name);
            return true;
        }

        /// <summary>静态道具在选图里能选的图：登记过的静态差分 + 规则上指定了图的。按状态标签去重。</summary>
        internal static List<KeyValuePair<string, Sprite>> StaticOptions(GameItem item)
        {
            var list = new List<KeyValuePair<string, Sprite>>();
            string id = item?.identifier;
            if (string.IsNullOrEmpty(id)) return list;
            var seen = new HashSet<string>();
            foreach (string tag in ItemArt.Variants(id))
            {
                Sprite s = ItemArt.SpriteOf(ItemArt.Pick(id, tag));
                if (s != null && seen.Add(tag)) list.Add(new KeyValuePair<string, Sprite>(tag, s));
            }
            foreach (PaintRule r in Ordered())
            {
                if (r.target != PaintTarget.StaticItem || r.id != id || r.spriteKey == null || string.IsNullOrEmpty(r.tag)) continue;
                Sprite s = ModSprite(r.spriteMod, r.spriteKey);
                if (s != null && seen.Add(r.tag)) list.Add(new KeyValuePair<string, Sprite>(r.tag, s));
            }
            return list;
        }

        /// <summary>静态道具的本体图（切过差分的，取记下的原图）。</summary>
        internal static Sprite StaticBase(GameItem item)
        {
            ReadBase(item, out bool fromMod, out _, out string atlas, out string key);
            if (fromMod) return ModSprite(atlas, key);
            try { return RenderHandler.TryLoadFromAtlas(atlas, key); }
            catch (Exception) { return null; }
        }

        private static Sprite ModSprite(string modId, string key)
        {
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == modId);
            return mod != null && mod.IsEnabled && mod.Sprites != null && mod.Sprites.TryGetValue(key, out Sprite s) ? s : null;
        }

        private static bool HasSprite(string modId, string key)
        {
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == modId);
            return mod != null && mod.IsEnabled && mod.Sprites != null && mod.Sprites.TryGetValue(key, out Sprite s) && s != null;
        }
    }

    public static partial class Painter
    {
        /// <summary>登记一条动态差分规则，返回后链式补全：.Tag("sick").Cond(...).Weight(10)。id 用 "*" 表示该类型的全部。</summary>
        public static PaintRule For(string modId, PaintTarget target, string id) { return PaintRules.Add(modId, target, id); }

        /// <summary>撤掉这个 mod 登记的全部规则与静态道具差分（在 OnDisable 里调）。</summary>
        public static void RemoveRules(string modId) { PaintRules.Remove(modId); }

        /// <summary>立绘识别钩子：告诉框架“这件不是你画的物品其实是哪张立绘”（返回立绘 ID，认不出返回 null）。
        /// 登记后，规则对它同样生效（成立时接管成对应差分），右键“选图”也会出现。RemoveRules(modId) 一并撤掉。</summary>
        public static void AddPortraitResolver(string modId, Func<GameItem, string> resolver) { PaintRules.AddResolver(modId, resolver, false); }

        /// <summary>同上；takeOver = true 时认出来就接管成高清图（没有规则成立也显示高清本体），框架不再管它时还原原图。</summary>
        public static void AddPortraitResolver(string modId, Func<GameItem, string> resolver, bool takeOver) { PaintRules.AddResolver(modId, resolver, takeOver); }

        /// <summary>root_key 组：把一张立绘（命名自由、结构识别认不出的）归到 rootKey 那张立绘名下，成为它的一个差分候选（差分名 = 这张立绘的 ID）。
        /// when 不为空：条件成立时自动替换 rootKey——道具切到这个差分，来客显示成它。见 Core/RootJoin.cs。</summary>
        public static bool Join(string modId, string portraitId, string rootKey, Func<GameItem, bool> when = null) { return RootJoin.Add(modId, portraitId, rootKey, when); }

        /// <summary>自动动态适配：来客取图时实时判断要不要替换（不改游戏怎么抽来客，只改抽到的键显示哪张图）。
        /// root_key 组的成员：条件成立 → 来客显示成它；root_key 自己：条件不成立 → 显示游戏原图。when 为空见 Core/RootJoin.cs。</summary>
        public static void AutoReplace(string modId, string portraitId, Func<GameItem, bool> when = null) { RootJoin.SetAuto(modId, portraitId, when); }

        /// <summary>登记动态贴图道具 [DynamicTexture]：这种道具的图是生成时才定的，选图窗口直接给它整个图库（一般道具只给同一个图 ID 的图）。</summary>
        public static void DynamicTexture(string modId, string itemId) { PaintRules.AddDynamic(modId, itemId, null); }

        /// <summary>同上，用判断函数认（例如“带某个标签的物品”）。RemoveRules(modId) 一并撤掉。</summary>
        public static void DynamicTexture(string modId, Func<GameItem, bool> isDynamic) { PaintRules.AddDynamic(modId, null, isDynamic); }

        /// <summary>这件物品是哪张立绘（图 ID，不带来源）：本框架画的看物品标签，别的 mod 的看识别钩子。都不是返回 null。</summary>
        public static string PortraitOf(GameItem item)
        {
            if (item == null) return null;
            PaintId now = SourceOf(item);
            return now.Valid && now.Group == PortraitGroup ? ImageKey.IdOf(now.Key) : PaintRules.ResolvePortrait(item);
        }

        /// <summary>按规则重算这件物品该显示哪个差分。状态变了就调它。没有规则管这件物品时什么都不做。</summary>
        public static void Refresh(GameItem item)
        {
            if (item == null || Retired) return;
            if (!managedSet.Contains(item)) Manage(item);
            PaintId now = SourceOf(item);
            // 接管来的道具，识别钩子不再认它（兼容层关了、扩展 mod 撤了）：还原原 mod 的原图
            if (now.Valid && PaintRules.IsAdopted(item) && PaintRules.ResolvePortrait(item) == null) { PaintRules.Release(item); return; }
            // ClaudeNote: 第 2 步 四层：临时 > 锁定 > 规则 > 本体。锁定的物品只在有临时状态时交给规则（临时状态的规则权重最高），
            //   临时状态结束后回到玩家选的那个状态
            if (PaintRules.IsLocked(item) && !Pulses.Any(item))
            {
                string want = PaintRules.LockedState(item);
                if (want != null && StateOf(item) != want) SetVariant(item, want.Length > 0 ? want : null);
                return;
            }
            if (now.Valid) { PaintRules.RefreshPortraitItem(item, now); return; }
            string foreign = PaintRules.ResolvePortrait(item, out bool takeOver);
            if (foreign != null) PaintRules.RefreshForeignPortrait(item, foreign, takeOver);
            else PaintRules.RefreshStaticItem(item);
        }

        /// <summary>登记静态道具的一张差分（不带条件）。之后 SetVariant(item, tag) 或同名动态规则会用它。CSV 里 type=item 的行走的也是这里。</summary>
        public static void AddItemVariant(string modId, string itemId, string tag, string spriteKey)
        {
            PaintRules.AddItemVariant(modId, itemId, tag, modId, spriteKey);
        }

        /// <summary>读一份 CSV 批量注册（格式见 CsvLoader.cs 顶部）。路径相对于该 mod 的文件夹。返回登记成功的行数。</summary>
        public static int LoadCsv(string modId, string relativePath)
        {
            try { return CsvLoader.File(modId, relativePath, modId); }
            catch (Exception ex) { Entry.Warn(modId + "/" + relativePath + " not loaded: " + ex.Message); return 0; }
        }

        /// <summary>同上，直接给 CSV 文本。</summary>
        public static int LoadCsvText(string modId, string csv) { return CsvLoader.Text(modId, modId, csv); }

        /// <summary>系列登记（与表格的 series 行等价）：在 modId 的贴图里找 prefix（本体）和 prefix_xxx（状态 xxx），挂到目标 id 上。返回登记了几张。</summary>
        public static int Series(string modId, PaintTarget target, string id, string prefix) { return CsvLoader.Series(modId, modId, target, id, prefix); }

        /// <summary>撤掉这个 mod 在框架里登记的一切（立绘、状态、规则、禁手动、识别钩子、贴图包声明）。在 OnDisable 里调。</summary>
        public static void UnregisterAll(string modId)
        {
            PortraitLibrary.Unregister(modId);
            PaintRules.Remove(modId);
            Packs.Remove(modId);
            Pulses.Remove(modId);
            Layer.Remove(modId);
            Conditions.Remove(modId);
            DialogPlacement.Remove(modId);
            RootJoin.Remove(modId);
            PortraitPools.Remove(modId);
        }

        /// <summary>只重算立绘衍生道具（来客到来 / 离开时本 mod 自动调）。</summary>
        public static void RefreshPortraitItems()
        {
            if (PaintRules.Count == 0 || !PlayerStore.HasInstance || Retired) return;
            Notices.Begin();
            try
            {
                foreach (GameItem item in new List<GameItem>(managed))   // 1.0.0 rc6 每位来客只过受管清单，不再整店遍历
                {
                    if (item == null || PaintRules.IsLocked(item)) continue;
                    try
                    {
                        PaintId now = SourceOf(item);
                        if (now.Valid) { PaintRules.RefreshPortraitItem(item, now); continue; }
                        string foreign = PaintRules.ResolvePortrait(item, out bool takeOver);
                        if (foreign != null) PaintRules.RefreshForeignPortrait(item, foreign, takeOver);
                    }
                    catch (Exception ex) { OneBadItem(item, ex); }
                }
            }
            finally { Notices.End(); }
            SourceReader.Release();
        }

        // ClaudeNote: 1.0.0 rc6 受管清单（Lune：用了储物柜套娃 mod、道具过多会如何？）。
        //   整店遍历（含容器套容器）是唯一跟“店里有多少东西”成正比的开销。它只在读档和入睡结算时做（RefreshAll），
        //   做的时候把“框架管得着的物品”记下来：带框架标签的、道具贴图组里有差分的、有规则 / 识别钩子管的。
        //   其余时候（开店、关店、起床、Notify、自定义检查点）只过这张清单（RefreshManaged），和店里总共多少东西无关。
        //   白天新到手的物品：买入时进清单（Counter.AfterBought）；别的 mod 造出来的由它自己调 Painter.Refresh(item)（顺带进清单）。
        private static readonly List<GameItem> managed = new List<GameItem>();
        private static readonly HashSet<GameItem> managedSet = new HashSet<GameItem>();

        internal static int ManagedCount => managed.Count;

        internal static void Manage(GameItem item)
        {
            if (item != null && IsManaged(item) && managedSet.Add(item)) managed.Add(item);
        }

        private static bool IsManaged(GameItem item)
        {
            if (item.IsTag(SourceTag) || item.IsTag(PaintRules.BaseTag) || item.IsTag(PaintRules.LockTag) || item.IsTag(Pulses.ItemTag)) return true;
            string id = item.identifier;
            if (string.IsNullOrEmpty(id)) return false;
            return ItemArt.Has(id) || (PaintRules.Count > 0 && PaintRules.Any(PaintTarget.StaticItem, id)) || PaintRules.ResolvePortrait(item) != null;
        }

        /// <summary>店里的物品全部重算一遍（整店遍历；框架在读档后、入睡结算时自动调）。</summary>
        public static void RefreshAll()
        {
            if (!PlayerStore.HasInstance || Retired) return;
            float t = Health.Now;
            managed.Clear();
            managedSet.Clear();
            Notices.Begin();
            try
            {
                foreach (GameItem item in Items.All())
                {
                    if (item == null) continue;
                    try { Manage(item); Refresh(item); }
                    catch (Exception ex) { OneBadItem(item, ex); }
                }
            }
            finally { Notices.End(); }
            SourceReader.Release();
            Health.Took("whole-store pass (" + managed.Count + " managed)", t);
        }

        /// <summary>只重算受管清单里的物品（开店、关店、起床、Notify 时用）。</summary>
        public static void RefreshManaged()
        {
            if (!PlayerStore.HasInstance || Retired) return;
            Notices.Begin();
            try
            {
                for (int i = managed.Count - 1; i >= 0; i--)
                {
                    GameItem item = managed[i];
                    if (item == null) { managed.RemoveAt(i); continue; }   // 物品没了（卖掉、销毁）
                    try { Refresh(item); }
                    catch (Exception ex) { OneBadItem(item, ex); }
                }
            }
            finally { Notices.End(); }
            SourceReader.Release();
        }

        // 整店重算时一件物品出错不能连累后面的（这些重算挂在游戏的读档 / 起床 / 开关门事件上，也不该把异常抛回游戏）。记进 Health
        // ClaudeNote: 0.6.4 自检：“哪一处”必须是固定的名字，物品 ID 放进信息里——之前把 ID 拼进了名字，每种物品各算一处，
        //   Health 里“同一处最多分开记 8 种”的上限就管不到它
        internal static void OneBadItem(GameItem item, Exception ex) { Health.Report("refresh item", (item != null ? item.identifier : "?") + ": " + ex.GetType().Name + ": " + ex.Message); }

        /// <summary>这件物品现在显示的状态标签；本体返回空字符串。</summary>
        public static string StateOf(GameItem item)
        {
            if (item == null) return "";
            PaintId now = SourceOf(item);
            return now.Valid ? now.Variant ?? "" : PaintRules.StateOf(item);
        }

        /// <summary>不针对具体物品，问这个 ID 现在该用哪个状态（头像、对话框等用）。没有成立的规则返回空字符串。立绘类只返回有图的差分。</summary>
        public static string ActiveTag(PaintTarget target, string id, GameItem item = null)
        {
            foreach (PaintRule r in PaintRules.Holding(target, id, item))
                if (target != PaintTarget.Portrait || (PortraitLibrary.Get(id)?.HasVariant(r.tag) ?? false)) return r.tag;
            return "";
        }
    }
}
