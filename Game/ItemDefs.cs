// ClaudeNote: 0.7.4 道具定义的读取（一种道具只读一次）。标签库（TagSystem.ItemTags）、工坊的目录与缩略图都从这里拿，不各读各的。
//   游戏没有静态的道具定义表：分类、标识标签、用哪张图都写在每种道具的工厂函数里。
//   读法：DirectoryMaster.Create<GameItem>——只跑工厂、不登记、不占存档里的物品编号，读完就丢。
//   来源（这种道具是谁加的）：看工厂函数挂在哪个程序集的对象上——是某个 mod 的程序集就是那个 mod；
//     看不出来时，图是从某个 mod 的贴图表里取的就算那个 mod；都不是就算游戏原版。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureExtend
{
    internal sealed class ItemDef
    {
        internal string[] types = Array.Empty<string>();     // 游戏主分类（itemTypes，原样）
        internal string[] markers = Array.Empty<string>();   // 标识性标签（定义里启用的字符串标签，原样）
        internal string source = ItemDefs.Game;              // "game" 或 mod ID
        internal string atlas, sprite;                       // 图：图集路径（或 mod ID）+ 图名
        internal string name = "";                           // 显示名（游戏给这种道具起的名字，按当前语言）
        internal bool modSprite;
    }

    internal static class ItemDefs
    {
        internal const string Game = "game";
        private static readonly Dictionary<string, ItemDef> defs = new Dictionary<string, ItemDef>();
        private static readonly ItemDef none = new ItemDef();

        /// <summary>这种道具的定义。ID 不存在、或读的时候出错，返回一份空定义（不会是 null）。</summary>
        internal static ItemDef Of(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return none;
            if (defs.TryGetValue(itemId, out ItemDef have)) return have;
            var def = new ItemDef();
            try
            {
                GameItem sample = DirectoryMaster.Create<GameItem>(itemId);
                if (sample != null)
                {
                    if (sample.itemTypes != null) def.types = sample.itemTypes.ToArray();
                    var markers = new List<string>();
                    if (sample.state?.dict != null)
                        foreach (KeyValuePair<string, TagState> kv in sample.state.dict)
                            if (kv.Value != null && kv.Value.IsEnabled() && !kv.Key.StartsWith("TYPE-", StringComparison.Ordinal)) markers.Add(kv.Key);   // TYPE- 开头的是枚举 / 数字标签，不是词
                    def.markers = markers.ToArray();
                    def.name = sample.GetDisplayName() ?? "";
                    def.atlas = sample.spriteAtlasPath;
                    def.sprite = sample.spritePath;
                    def.modSprite = sample.useSpriteFromMod;
                    def.source = SourceOf(itemId, def, sample);
                }
            }
            catch (Exception ex) { Health.Report("item definition", itemId + ": " + ex.GetType().Name + ": " + ex.Message); }
            // ClaudeNote: 0.9.2 自检：读不到定义时不记下来。道具目录是店面场景里的东西，标题界面没有——
            //   之前在标题界面问过的道具会被记成“空定义”，进了店面也一直是空的（没分类、没图、来源算原版）。
            if (def.sprite == null && def.types.Length == 0 && def.markers.Length == 0) return def;
            return defs[itemId] = def;
        }

        /// <summary>这种道具的图（游戏 Resources 里的，或 mod 贴图表里的）。只借用，不销毁。拿不到返回 null。</summary>
        internal static Sprite Sprite(string itemId)
        {
            ItemDef def = Of(itemId);
            return Sprite(def.atlas, def.sprite, def.modSprite);
        }

        /// <summary>按“图集（或 mod ID）+ 图名”直接取图：不需要道具目录，标题界面也能用（工坊的目录里记着这三样）。</summary>
        internal static Sprite Sprite(string atlas, string sprite, bool modSprite)
        {
            if (string.IsNullOrEmpty(sprite) || string.IsNullOrEmpty(atlas)) return null;
            try
            {
                if (!modSprite) return RenderHandler.TryLoadFromAtlas(atlas, sprite);
                LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == atlas);
                return mod?.Sprites != null && mod.Sprites.TryGetValue(sprite, out Sprite s) ? s : null;
            }
            catch (Exception ex) { Health.Report("item image", atlas + "/" + sprite + ": " + ex.Message); return null; }
        }

        // ClaudeNote: 0.8.5 道具是哪个 mod 的（Lune：道具那边按 mod 来源没了）。
        //   查她的目录文件：566 种道具的来源全是 game，连 Brood、lewd_meme 的道具也是——来源筛选其实从 0.7.4 起就没生效过，只是只剩一种来源时下拉不显示，看不出来。
        //   成因：之前只看“工厂函数挂在哪个对象上”（Target）。mod 用静态方法当工厂时 Target 是空的；道具图是工厂之外才换成 mod 图的话，第二条线索也落空。
        //   现在四条线索依次看：工厂函数本身定义在哪个程序集 → 它挂的对象 → 造出来的道具是哪个程序集的类 → 图取自哪个 mod。
        //   都看不出、但这种道具登记在 mod 道具目录里的，归到“mod（未识别）”，不混进原版。
        internal const string UnknownMod = "mod";
        private static Dictionary<System.Reflection.Assembly, string> modOf;
        private static HashSet<string> modItems;

        private static string SourceOf(string itemId, ItemDef def, GameItem sample)
        {
            if (modOf == null)
            {
                modOf = new Dictionary<System.Reflection.Assembly, string>();
                if (ModLoader.Mods != null)
                    foreach (LoadedMod mod in ModLoader.Mods)
                        if (mod?.Manifest != null && mod.Assembly != null) modOf[mod.Assembly] = mod.Manifest.ID;
                modItems = new HashSet<string>(DirectoryMaster.GetIdentifierList<GameItem>(nameof(ModItemDirectory)) ?? new List<string>());
            }
            Func<GameItem> factory = DirectoryMaster.GetFactory<GameItem>(itemId);
            string id;
            if (factory?.Method?.DeclaringType != null && modOf.TryGetValue(factory.Method.DeclaringType.Assembly, out id)) return id;
            if (factory?.Target != null && modOf.TryGetValue(factory.Target.GetType().Assembly, out id)) return id;
            if (sample != null && modOf.TryGetValue(sample.GetType().Assembly, out id)) return id;
            if (def.modSprite && !string.IsNullOrEmpty(def.atlas)) return def.atlas;
            return modItems.Contains(itemId) ? UnknownMod : Game;
        }

        internal static void Clear() { defs.Clear(); modOf = null; modItems = null; }
    }
}
