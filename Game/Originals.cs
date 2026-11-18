// ClaudeNote: 0.5.1 原版立绘的“真身”（Lune 实测：装了 Dudi 的 HD Character Portraits 后，开框架反而变糊、Brood 的 NPC 变小）。
//   原因：别的立绘 mod（例如 Dudi 的）会直接把游戏立绘表 SpriteDict.spriteDictionary 里的条目换成自己的图。
//     框架之前把表里现有的那张当“原版”：拿它当尺寸基准（Dudi 的图故意缩到原版的 80%），拿它判断“是不是高清”（表里已经是高清图了 → 判成不是 → 没走高清渲染 → 糊）。
//   做法：原版的图直接从 SpriteDict 自己序列化的条目列表里读（游戏在 Awake 时就是从这些列表建表的，别的 mod 改的是表、不是列表），
//     不依赖谁先谁后。另外认出表里现在那张是谁放的：贴图属于哪个 mod 的 Textures / Sprites，就算那个 mod 的。
//   这些都只在立绘库重建、或第一次问到时算一次。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TextureExtend
{
    public static class Originals
    {
        private static readonly Dictionary<string, Sprite> originals = new Dictionary<string, Sprite>();
        private static SpriteDict readFrom;
        private static readonly Dictionary<Texture, string> textureOwner = new Dictionary<Texture, string>();
        private static int ownersFromMods = -1;

        /// <summary>游戏自带的那张原图（不受别的 mod 改立绘表影响）；不是原版字段返回 null。</summary>
        public static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Read();
            return originals.TryGetValue(key, out Sprite s) ? s : null;
        }

        internal static int Count { get { Read(); return originals.Count; } }

        /// <summary>全部原版立绘键。</summary>
        public static List<string> Keys() { Read(); return new List<string>(originals.Keys); }

        /// <summary>尺寸与轴心的基准：游戏自带的原图；不是原版字段（mod 自己加的键）时用立绘表里那张。</summary>
        public static Sprite Reference(string key)
        {
            Sprite orig = Get(key);
            if (orig != null) return orig;
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            return dict != null && key != null && dict.TryGetValue(key, out Sprite s) ? s : null;
        }

        /// <summary>一张有代表性的原版立绘（下层区女性第一张；没有就任取一张）。原版没有的新立绘拿它当尺寸与轴心的基准。</summary>
        public static Sprite Typical
        {
            get
            {
                Read();
                Sprite any = null;
                foreach (KeyValuePair<string, Sprite> kv in originals)
                {
                    if (kv.Value == null) continue;
                    if (kv.Key.StartsWith("femaleLower", StringComparison.Ordinal)) return kv.Value;
                    if (any == null) any = kv.Value;
                }
                return any;
            }
        }

        // ClaudeNote: 0.8.4 认不出是谁放的（运行时现建的贴图：白蛇娘化这类把图打包在 DLL 里、读出来再写进表的 mod）也算一个来源，
        //   叫 table（“其他 mod 直接写进表的”）。之前记成 "?" 并且不当来源——表里那张被当成不存在，框架显示别的来源或原图时就把它盖掉了。
        /// <summary>立绘表里“认不出是谁放的”那张图所属的来源名。</summary>
        public const string TableSource = "table";

        /// <summary>立绘表里这个键现在那张图是谁放的："game"（原图）、mod ID、或 TableSource（认不出：运行时建的贴图）。</summary>
        public static string OwnerOfCurrent(string key)
        {
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            if (dict == null || key == null || !dict.TryGetValue(key, out Sprite now) || now == null) return null;
            Sprite orig = Get(key);
            if (orig != null && ReferenceEquals(now, orig)) return PortraitLibrary.GameSource;
            return OwnerOf(now.texture) ?? TableSource;
        }

        /// <summary>立绘表里这个键现在那张图（不是原图、也不是本框架的）；没有返回 null。“table”来源取图走这里，每次现读（对方 mod 换了图也跟着换）。</summary>
        internal static Sprite Current(string key)
        {
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            if (dict == null || key == null || !dict.TryGetValue(key, out Sprite now) || now == null) return null;
            return ReferenceEquals(now, Get(key)) ? null : now;
        }

        /// <summary>原图在游戏贴图里的名字 → 用这张原图的立绘键（几张原图同名时有多个）。白蛇娘化这类按原图名字给图的包靠它对上键。</summary>
        internal static Dictionary<string, List<string>> KeysByName()
        {
            Read();
            if (byName != null && byNameFor == readFrom) return byName;
            byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            byNameFor = readFrom;
            foreach (KeyValuePair<string, Sprite> kv in originals)
            {
                if (kv.Value == null || string.IsNullOrEmpty(kv.Value.name)) continue;
                if (!byName.TryGetValue(kv.Value.name, out List<string> keys)) byName[kv.Value.name] = keys = new List<string>();
                keys.Add(kv.Key);
            }
            return byName;
        }

        private static Dictionary<string, List<string>> byName;
        private static SpriteDict byNameFor;

        /// <summary>这张贴图是哪个 mod 载入的（mod 文件夹 Textures 里的图）；不是返回 null。</summary>
        public static string OwnerOf(Texture tex)
        {
            if (tex == null) return null;
            Owners();
            return textureOwner.TryGetValue(tex, out string id) ? id : null;
        }

        /// <summary>立绘表当前内容的指纹（条目数 + 各图的实例号）。别的 mod 换了表里的图（表还是同一张）指纹就变。
        /// 后台每秒算一次：两百多次整数运算，不分配内存。</summary>
        internal static int TableStamp()
        {
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            if (dict == null) return 0;
            int stamp = dict.Count;
            foreach (KeyValuePair<string, Sprite> kv in dict)
            {
                if (kv.Key.StartsWith(Display.KeyPrefix, StringComparison.Ordinal)) continue;   // 我们自己登记的显示键不算
                stamp = unchecked(stamp * 31 + (ReferenceEquals(kv.Value, null) ? 0 : kv.Value.GetInstanceID()));
            }
            return stamp;
        }

        internal static void Invalidate() { readFrom = null; ownersFromMods = -1; }

        // 从 SpriteDict 的序列化列表读原图（字段是私有的，按类型找全部 List<SpriteEntry>，游戏加了新分类也不用改）
        private static void Read()
        {
            SpriteDict d = SpriteDict.Instance;
            if (d == null || ReferenceEquals(d, readFrom)) return;
            readFrom = d;
            originals.Clear();
            try
            {
                foreach (FieldInfo f in AccessTools.GetDeclaredFields(typeof(SpriteDict)))
                {
                    if (f.FieldType != typeof(List<SpriteDict.SpriteEntry>)) continue;
                    if (!(f.GetValue(d) is List<SpriteDict.SpriteEntry> list)) continue;
                    foreach (SpriteDict.SpriteEntry e in list)
                        if (e != null && !string.IsNullOrEmpty(e.key) && e.sprite != null && !originals.ContainsKey(e.key)) originals[e.key] = e.sprite;
                }
            }
            catch (Exception ex) { Entry.Warn("original portraits not read: " + ex.Message); }
        }

        private static void Owners()
        {
            int count = ModLoader.Mods?.Count ?? 0;
            if (count == ownersFromMods) return;
            ownersFromMods = count;
            textureOwner.Clear();
            if (ModLoader.Mods == null) return;
            foreach (LoadedMod mod in ModLoader.Mods)
            {
                if (mod?.Manifest == null) continue;
                string id = mod.Manifest.ID;
                if (mod.Textures != null) foreach (Texture2D t in mod.Textures.Values) if (t != null) textureOwner[t] = id;
                if (mod.Sprites != null) foreach (Sprite s in mod.Sprites.Values) if (s != null && s.texture != null) textureOwner[s.texture] = id;
            }
        }
    }
}
