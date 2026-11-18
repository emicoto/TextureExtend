// ClaudeNote: 1.0.0 rc6 道具贴图组（Lune 2026-11-18：道具这边得有个口子接管同一个 itemID 的额外贴图，跟立绘那边合并进同一套）。
//   之前：差分表是“道具ID#状态 → 一张图”，后登记的直接盖掉先登记的；图只能来自已启用 mod 的贴图表。
//   现在：以道具 ID 为准收成一组。本体 = 游戏（或道具自己的 mod）的原图；各来源各自往组里加差分，同名差分每个来源一份、互不覆盖。
//     图可以是：已启用 mod 贴图表里的（mod + 贴图键）；外部图片（没启用的包里的文件、本地图片 —— 身份串 file| / data| / ext|，见 ExternalImages）。
//   同名差分用谁的：道具自己的 mod → 激活中的来源里声明优先级高的 → 先登记的。没激活的包不参加。
//   外部图片怎么上道具：游戏画 mod 道具图 = 按道具记的 mod ID 去那个 mod 的贴图表里查键（GameItemElement.ResolveSpriteByName）。
//     框架把图读成 Sprite 放进自己的贴图表（键 pefi:道具ID#差分@来源），道具指过来即可——和立绘道具的成品图同一条路。
//     大小：按本体图的世界尺寸等比放进去（不改道具占格——改了会打乱货架和背包的摆放）。
//   读档：道具还原时就要按键取图，所以“这局存档用到的外部差分”记在存档位里（usedLooks），读档前先载入（Preload）；
//     那时来源还没登记的（本地图片是读档后才扫的），登记进来时补上（AddFile），店里的物品由读档后的重算接回（PaintRules.Heal）。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyExtendFramework
{
    internal static class ItemArt
    {
        internal sealed class Look
        {
            internal string owner, itemId, variant;
            internal string mod, key;   // 已启用 mod 贴图表里的
            internal string sig;        // 外部图片的身份串
            internal int order;
            internal bool auto;         // 自动认出来的（Discover），不是谁登记的
            internal bool External => sig != null;
            internal string SpriteKey => Prefix + itemId + "#" + variant + "@" + owner;
        }

        internal const string Prefix = "pefi:";
        private const string UsedKey = "usedLooks";
        private static readonly Dictionary<string, List<Look>> looks = new Dictionary<string, List<Look>>();   // 道具 ID → 组里的差分
        private static readonly Dictionary<string, Sprite> made = new Dictionary<string, Sprite>();            // 贴图键 → 外部图片读成的 Sprite
        private static readonly HashSet<string> used = new HashSet<string>();
        private static int counter;

        internal static int Count { get { int n = 0; foreach (List<Look> l in looks.Values) n += l.Count; return n; } }
        internal static bool Has(string itemId) { return itemId != null && looks.ContainsKey(itemId); }
        internal static ICollection<string> Ids => looks.Keys;

        internal static void Add(string owner, string itemId, string variant, string spriteMod, string spriteKey)
        {
            if (string.IsNullOrEmpty(spriteMod) || string.IsNullOrEmpty(spriteKey)) return;
            Put(new Look { owner = owner ?? "", itemId = itemId, variant = PortraitLibrary.Normal(variant), mod = spriteMod, key = spriteKey });
            WarnIfMisplaced(spriteMod, spriteKey);
        }

        // ClaudeNote: 1.0.0 rc9（Lune 实测测试 mod：道具差分一张都不出来）。游戏只把 Textures/Items/ 里的图载入成道具贴图（mod.Sprites），
        //   Textures/ 根目录的只进 mod.Textures（普通贴图），道具用不上。之前这种情况什么都不说，差分就是不出现。
        private static readonly HashSet<string> warnedMisplaced = new HashSet<string>();
        private static void WarnIfMisplaced(string modId, string key)
        {
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == modId);
            if (mod == null || mod.Sprites == null || mod.Sprites.ContainsKey(key) || mod.Textures == null || !mod.Textures.ContainsKey(key)) return;
            if (warnedMisplaced.Add(modId + "/" + key))
                Health.Report("item image in the wrong folder", modId + ": " + key + ".png is in Textures/ — item images must be in Textures/Items/ (the game only loads that folder as item sprites)");
        }

        internal static void AddFile(string owner, string itemId, string variant, string sig)
        {
            if (string.IsNullOrEmpty(sig)) return;
            var look = new Look { owner = owner ?? "", itemId = itemId, variant = PortraitLibrary.Normal(variant), sig = sig };
            if (!Put(look)) return;
            if (used.Contains(look.SpriteKey) && !made.ContainsKey(look.SpriteKey)) Ensure(look);   // 读档前载入时它还没登记：现在补上
        }

        private static bool Put(Look look)
        {
            if (string.IsNullOrEmpty(look.itemId) || look.variant.Length == 0) return false;
            if (!looks.TryGetValue(look.itemId, out List<Look> list)) looks[look.itemId] = list = new List<Look>();
            int at = list.FindIndex(l => l.owner == look.owner && l.variant == look.variant);
            look.order = at >= 0 ? list[at].order : counter++;
            if (at >= 0) { Forget(list[at]); list[at] = look; } else list.Add(look);
            return true;
        }

        // ClaudeNote: 1.0.0 rc8 自动识别（Lune 实测：给 BroodMeme 的神奇乳汁加了 wondrous_milk_delight / wondrous_milk_rich 两张图，工坊里没认出来）。
        //   成因：“道具ID_差分名”这个约定 rc6 只在本地图片里做了；mod 自己 Textures 里的图要写表格或代码登记才进贴图组。
        //   rc9：只扫本地 mod（Source = Local）。工坊订阅的 mod 不扫，靠它自带的注册文件。
        //   现在：每次读档把启用中的 mod 的贴图表过一遍，键 = 道具ID_差分名 的自动进那种道具的组（来源 = 那个 mod）。
        //     道具 ID 取游戏道具目录里对得上的最长的那个（道具 ID 自己也带下划线）。
        //   不认的：这个键本身是某种道具的本体图；差分名以 inspect 开头（检视大图的命名习惯，不是差分）；已经有人登记过同一个来源的同名差分。
        //   只认“道具 ID”开头的，不认“本体贴图键”开头的：brood_milk_inspect、brood_milk_rich 这类是别的用途 / 别的道具的图，按贴图键认会全认错。
        internal static int Discover()
        {
            foreach (string id in new List<string>(looks.Keys))
            {
                looks[id].RemoveAll(l => l.auto);
                if (looks[id].Count == 0) looks.Remove(id);
            }
            int found = 0;
            try
            {
                List<string> ids = new List<string>(DirectoryMaster.GetIdentifierList<GameItem>() ?? new List<string>());
                if (ids.Count == 0 || ModLoader.Mods == null) return 0;
                ids.Sort((a, b) => b.Length.CompareTo(a.Length));
                var baseKeys = new HashSet<string>();
                foreach (string id in ids) { string k = ItemDefs.Of(id).sprite; if (!string.IsNullOrEmpty(k)) baseKeys.Add(k); }
                foreach (LoadedMod mod in ModLoader.Mods)
                {
                    string modId = mod?.Manifest?.ID;
                    // 1.0.0 rc9（Lune：自动扫全部贴图压力比较大）只扫本地 mod（Mods 文件夹里的，作者制作调试用）；工坊订阅来的按它的 csv / json 注册文件读
                    if (modId == null || modId == Painter.ModId || !mod.IsEnabled || mod.Sprites == null || mod.Source != ModSource.Local) continue;
                    foreach (string key in new List<string>(mod.Sprites.Keys))
                    {
                        if (baseKeys.Contains(key) || !LocalImages.SplitItem(key, ids, out string itemId, out string variant)) continue;
                        variant = PortraitLibrary.Normal(variant);
                        if (variant.Length == 0 || variant.StartsWith("inspect", StringComparison.Ordinal)) continue;
                        if (looks.TryGetValue(itemId, out List<Look> have) && have.Exists(l => l.owner == modId && l.variant == variant)) continue;
                        if (Put(new Look { owner = modId, itemId = itemId, variant = variant, mod = modId, key = key, auto = true })) found++;
                    }
                }
            }
            catch (Exception ex) { Health.Report("item look discovery", ex); }
            if (found > 0) Painter.NeedScan = true;   // 工坊目录跟着重存
            if (found > 0) Entry.Info("item looks: " + found + " found by name (itemId_variant) in enabled mods' textures");
            return found;
        }

        internal static void Remove(string owner)
        {
            foreach (string id in new List<string>(looks.Keys))
            {
                foreach (Look l in looks[id]) if (l.owner == owner) Forget(l);
                looks[id].RemoveAll(l => l.owner == owner);
                if (looks[id].Count == 0) looks.Remove(id);
            }
        }

        /// <summary>这种道具有图的差分名（各来源合起来，去重，按登记先后）。</summary>
        internal static List<string> Variants(string itemId)
        {
            var list = new List<string>();
            if (itemId != null && looks.TryGetValue(itemId, out List<Look> have))
                foreach (Look l in have) if (!list.Contains(l.variant) && Usable(l)) list.Add(l.variant);
            return list;
        }

        /// <summary>组里的全部差分（工坊目录用，不看来源现在读不读得到）。</summary>
        internal static List<Look> All(string itemId)
        {
            return itemId != null && looks.TryGetValue(itemId, out List<Look> have) ? new List<Look>(have) : new List<Look>();
        }

        /// <summary>这个差分用哪个来源的图；没有返回 null。</summary>
        internal static Look Pick(string itemId, string variant)
        {
            if (itemId == null || !looks.TryGetValue(itemId, out List<Look> have)) return null;
            string home = ItemDefs.Of(itemId).source;
            Look best = null;
            int bestRank = int.MinValue;
            foreach (Look l in have)
            {
                if (l.variant != variant || !Usable(l)) continue;
                int rank = l.owner == home ? int.MaxValue : Packs.PriorityOf(l.owner);
                if (best == null || rank > bestRank || (rank == bestRank && l.order < best.order)) { best = l; bestRank = rank; }
            }
            return best;
        }

        // 来源现在能不能用：贴图表里的要那个 mod 启用着；外部图片要来源激活着（没激活的包只是候选素材）
        private static bool Usable(Look l)
        {
            if (!l.External) return ModSprite(l.mod, l.key) != null;
            return Sources.Active(l.owner);
        }

        /// <summary>把这个差分变成“道具能指过去的图”：(mod ID, 贴图键)。读不到返回 false。</summary>
        internal static bool Locate(Look look, out string mod, out string key)
        {
            mod = look.mod; key = look.key;
            if (!look.External) return ModSprite(mod, key) != null;
            mod = Painter.ModId; key = look.SpriteKey;
            return Ensure(look) != null;
        }

        /// <summary>预览 / 选图窗口用的图。</summary>
        internal static Sprite SpriteOf(Look look) { return look == null ? null : look.External ? Ensure(look) : ModSprite(look.mod, look.key); }

        private static Sprite Ensure(Look look)
        {
            string key = look.SpriteKey;
            Dictionary<string, Sprite> table = Painter.OwnSprites();
            if (made.TryGetValue(key, out Sprite have) && have != null) { if (table != null) table[key] = have; return have; }
            if (table == null) return null;
            try
            {
                byte[] bytes = ExternalImages.Bytes(look.sig);
                if (bytes == null) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes, false)) { UnityEngine.Object.Destroy(tex); return null; }   // 保持可读：游戏按透明度判断点没点中物品
                tex.name = key;
                tex.wrapMode = TextureWrapMode.Clamp;
                Sprite basis = ItemDefs.Sprite(look.itemId);
                tex.filterMode = basis != null && basis.texture != null && basis.texture.filterMode == FilterMode.Point && tex.width <= basis.rect.width * 2f ? FilterMode.Point : FilterMode.Bilinear;
                Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), Ppu(tex, basis), 0u, SpriteMeshType.FullRect);
                s.name = key;
                made[key] = s;
                table[key] = s;
                return s;
            }
            catch (Exception ex) { Health.Report("item look", key + ": " + ex.Message); return null; }
        }

        // 每单位像素数：让这张图的世界尺寸等比放进本体图的世界尺寸里
        private static float Ppu(Texture2D tex, Sprite basis)
        {
            if (basis == null || basis.pixelsPerUnit <= 0f) return 100f;
            return FitPpu(tex.width, tex.height, basis.rect.width, basis.rect.height, basis.pixelsPerUnit);
        }

        internal static float FitPpu(float w, float h, float baseW, float baseH, float basePpu)
        {
            if (baseW <= 0f || baseH <= 0f || basePpu <= 0f) return 100f;
            return Mathf.Max(w / (baseW / basePpu), h / (baseH / basePpu));
        }

        private static void Forget(Look look)
        {
            if (!look.External) return;
            string key = look.SpriteKey;
            if (!made.TryGetValue(key, out Sprite s)) return;
            made.Remove(key);
            Dictionary<string, Sprite> table = Painter.OwnSprites();
            if (table != null && table.TryGetValue(key, out Sprite t) && t == s) table.Remove(key);
            if (s == null) return;
            Texture2D tex = s.texture;
            UnityEngine.Object.Destroy(s);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }

        private static Sprite ModSprite(string modId, string key)
        {
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == modId);
            return mod != null && mod.IsEnabled && mod.Sprites != null && mod.Sprites.TryGetValue(key, out Sprite s) ? s : null;
        }

        // ── 跟着存档走 ──

        /// <summary>道具用上了一张外部差分：记进这局存档。</summary>
        internal static void Remember(string spriteKey)
        {
            if (spriteKey == null || !spriteKey.StartsWith(Prefix, StringComparison.Ordinal) || !used.Add(spriteKey)) return;
            SaveUsed();
        }

        private static void SaveUsed()
        {
            if (!PlayerStore.HasInstance) return;
            try { ModHelper.SetModData(Painter.ModId, UsedKey, string.Join("\n", used)); }
            catch (Exception ex) { Entry.Warn("used item looks not saved: " + ex.Message); }
        }

        /// <summary>读档、物品还原之前：这局用到的外部差分先载入。</summary>
        internal static void Preload()
        {
            used.Clear();
            string text = ModHelper.GetModData(Painter.ModId, UsedKey, "");
            if (string.IsNullOrEmpty(text)) return;
            int ok = 0;
            foreach (string key in text.Split('\n')) if (key.Length > 0 && used.Add(key) && EnsureKey(key)) ok++;
            if (used.Count > 0) Entry.Info("preloaded " + ok + "/" + used.Count + " item look(s) for this save");
        }

        /// <summary>按贴图键找回那张差分并载入（读档后道具指着的键还没图时）。</summary>
        internal static bool EnsureKey(string spriteKey)
        {
            Look look = ByKey(spriteKey);
            return look != null && Ensure(look) != null;
        }

        private static Look ByKey(string spriteKey)
        {
            if (spriteKey == null || !spriteKey.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            foreach (List<Look> list in looks.Values)
                foreach (Look l in list) if (l.External && l.SpriteKey == spriteKey) return l;
            return null;
        }

        /// <summary>入睡前：用到的清单收成店里实际还指着的那些。</summary>
        internal static void PruneUsed(HashSet<string> alive)
        {
            if (alive.SetEquals(used)) return;
            used.Clear();
            used.UnionWith(alive);
            SaveUsed();
        }

        internal static void Shutdown()
        {
            foreach (List<Look> list in looks.Values) foreach (Look l in list) Forget(l);
            made.Clear();
            used.Clear();
        }
    }
}
