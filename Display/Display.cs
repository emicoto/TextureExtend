// ClaudeNote: 立绘不画成道具、直接拿去显示（柜台来客、对话框等）时用的整张图。
//   · 怎么显示（多大、轴心、渲染路）由显示方案 Plan 裁决，这里只负责读图、造 Sprite、留存与回收。
//   · 一直留着（外部立绘包的图读进来后不随后台每秒释放）；最多留 16 张，旧的先放；登记进立绘表的显示键不回收。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyExtendFramework
{
    public static class Display
    {
        private const int Keep = 16;   // ClaudeNote: 第 4 步起 GetSprite 前缀也从这里取图，留多一点
        /// <summary>登记进游戏立绘表的显示键前缀（Display.Key）。</summary>
        public const string KeyPrefix = "texext_";
        private static readonly HashSet<string> pinned = new HashSet<string>();   // 登记进立绘表的：不能被挤掉
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        private static readonly LinkedList<string> order = new LinkedList<string>();

        /// <summary>这张立绘某个状态的整张图（空 = 本体），尺寸与轴心对齐到游戏里 refField 那张原图（不给就用立绘 ID 本身）。拿不到返回 null。</summary>
        public static Sprite Get(string portraitId, string state = null, string refField = null) { return Get(portraitId, state, refField, Usage.FullBody); }

        /// <summary>按用途取图（第 5 步）：全身 = 整张；腰上 / 胸上 = 注册方专门给的那张（0.6.4，见 PortraitLibrary.RegisterFrame），
        /// 没给就从整张的上部裁出（同一张贴图，不复制像素）。</summary>
        public static Sprite Get(string portraitId, string state, string refField, Usage usage)
        {
            if (usage == Usage.FullBody) return Full(portraitId, state, refField);
            if (string.IsNullOrEmpty(portraitId)) return null;
            string key = FullKey(portraitId, PortraitLibrary.Normal(state), refField) + "~" + usage;
            if (cache.TryGetValue(key, out Sprite have) && have != null) { Touch(key); return have; }
            Sprite framed = FrameSprite(portraitId, state, refField, usage, key);   // 专用图：不用先把全身图读进来
            if (framed != null) return framed;
            Sprite full = Full(portraitId, state, refField);
            if (full == null) return null;
            Rect r = full.rect;
            float share = usage == Usage.WaistUp ? WaistShare : BustShare;
            var crop = new Rect(r.x, r.y + r.height * (1f - share), r.width, r.height * share);
            Sprite s = Sprite.Create(full.texture, crop, new Vector2(0.5f, 0.5f), full.pixelsPerUnit, 0u, SpriteMeshType.FullRect);
            s.name = key;
            if (HdRender.IsHd(full)) HdRender.Mark(s);
            return Remember(key, s, false);
        }

        /// <summary>立绘的用途（决定取景）。</summary>
        public enum Usage { FullBody, WaistUp, BustUp }
        // 取景比例：从图的顶端往下取多少（立绘包构图各不相同，这是通用的近似；注册方要精确取景请直接给那个用途的图）
        private const float WaistShare = 0.62f, BustShare = 0.42f;

        private static string FullKey(string portraitId, string state, string refField) { return portraitId + "#" + state + "@" + (refField ?? portraitId); }

        // ClaudeNote: 0.6.4 专用取景图。大小的约定和裁出来的一致：这张图的画布高 = 原图画布高 × 这个用途的份额（腰上 62%、胸上 42%），
        //   轴心在正中——调用方不管拿到的是专用图还是裁的，摆放方式都一样。
        //   不套来源的自动缩放：自动缩放是替“全身图把画布占得太满”的包做修正；专用图的构图是作者自己定的，按份额铺满就是它该有的大小。
        private static Sprite FrameSprite(string portraitId, string state, string refField, Usage usage, string key)
        {
            Framed f = PortraitLibrary.Frame(portraitId, state, usage);
            if (f == null) return null;
            Texture2D tex = null;
            bool own = false;
            try
            {
                if (f.fileSig != null) { tex = LoadFile(f.fileSig); own = tex != null; }
                else
                {
                    Sprite src = f.sprite?.Invoke();
                    if (src == null) return null;
                    tex = src.texture;
                    if (src.rect.width != tex.width || src.rect.height != tex.height) return src;   // 图集里的一格：直接用（是别人的）
                }
            }
            catch (Exception ex) { Entry.Warn("frame image of " + portraitId + " not read: " + ex.Message); return null; }
            if (tex == null) return null;
            bool pixel = Packs.PixelOf(PortraitLibrary.SourceOf(portraitId));
            if (pixel) { tex.filterMode = FilterMode.Point; tex.anisoLevel = 0; }
            Sprite reference = Originals.Reference(refField ?? portraitId) ?? Originals.Typical;
            float share = usage == Usage.WaistUp ? WaistShare : BustShare;
            float units = (reference != null && reference.rect.height > 0f ? reference.rect.height / reference.pixelsPerUnit : tex.height / 100f / share) * share;
            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.height / units, 0u, SpriteMeshType.FullRect);
            s.name = key;
            if (!pixel && tex.filterMode != FilterMode.Point && (reference == null || tex.height > reference.rect.height * share * 1.5f)) HdRender.Mark(s);
            return Remember(key, s, own);
        }

        private static Sprite Full(string portraitId, string state, string refField)
        {
            if (string.IsNullOrEmpty(portraitId)) return null;
            state = PortraitLibrary.Normal(state);
            string key = FullKey(portraitId, state, refField);
            if (cache.TryGetValue(key, out Sprite have) && have != null) { Touch(key); return have; }

            Texture2D tex = null;
            bool own = false;
            string sig = PortraitLibrary.FileSignature(portraitId, state);
            if (sig != null) { float t = Health.Now; tex = LoadFile(sig); own = tex != null; Health.Took("display: read and decode a portrait image", t); }
            else
            {
                Sprite src = PortraitLibrary.Resolve(portraitId, state);
                if (src != null) tex = src.texture;   // 别的 mod 或游戏自己的贴图：借用，不销毁
                if (src != null && (src.rect.width != tex.width || src.rect.height != tex.height)) return src;   // 图集里的一格：直接用，不缓存也不销毁（是别人的）
            }
            if (tex == null) return null;

            // ClaudeNote: 0.6.0 尺寸、轴心、渲染路都按显示方案（Plan）走，这里只负责造 Sprite
            Plan plan = Plan.ForPortrait(portraitId);
            if (plan.Pixel) { tex.filterMode = FilterMode.Point; tex.anisoLevel = 0; }   // 像素风包：这张图只当立绘用，改成点采样
            Sprite reference = Originals.Reference(refField ?? portraitId) ?? Originals.Typical;   // 原版没有的新立绘：按原版立绘的通常画布高
            plan.Place(tex, reference, out float ppu, out Vector2 pivot);
            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), pivot, ppu, 0u, SpriteMeshType.FullRect);
            s.name = key;
            // 高清：不是像素风、且比原图大出一截 → 上柜台时由 HdRender 让店面按屏幕分辨率画
            if (!plan.Pixel && tex.filterMode != FilterMode.Point && (reference == null || tex.height > reference.rect.height * 1.5f)) HdRender.Mark(s);
            return Remember(key, s, own);
        }

        /// <summary>把这张立绘的某个状态登记进游戏的立绘表，返回键（texext_立绘[_状态]）。来客的 spriteName 填它，
        /// 从走进门开始就显示这张，不用等对话。立绘表还没准备好、或图拿不到时返回 refField（游戏原来的字段）。</summary>
        public static string Key(string portraitId, string state = null, string refField = null)
        {
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            string fallback = refField ?? portraitId;
            if (dict == null) return fallback;
            state = PortraitLibrary.Normal(state);
            string key = KeyPrefix + portraitId + (state.Length > 0 ? "_" + state : "");
            Sprite s = Get(portraitId, state, refField);
            if (s == null) return fallback;
            pinned.Add(s.name);
            dict[key] = s;
            Sweep();   // 这个键原来指着的旧图若在待销毁名单里，现在没人用了
            return key;
        }

        /// <summary>把柜台上的来客换成这张立绘的某个状态。返回是否换了。</summary>
        public static bool ShowOnCounter(string portraitId, string state = null, string refField = null)
        {
            SpriteRenderer image = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
            if (image == null) return false;
            Sprite s = Get(portraitId, state, refField);
            if (s == null) return false;
            image.sprite = s;
            // ClaudeNote: 0.5.0 通知跟着柜台走的显示（例：BroodMeme 给 lewd_meme 客人格打的补丁，让左上角的客人跟着换表情）
            if (OnCounterShown != null)
            {
                try { OnCounterShown(portraitId, PortraitLibrary.Normal(state)); }
                catch (Exception ex) { Entry.Warn("OnCounterShown listener failed: " + ex.Message); }
            }
            return true;
        }

        /// <summary>柜台上的来客换了图之后（立绘 ID、状态；本体为空字符串）。</summary>
        public static event Action<string, string> OnCounterShown;

        /// <summary>立绘表的键 → 立绘 ID 与状态：femaleLower2 → (femaleLower2, "")；texext_femaleLower2_calm → (femaleLower2, "calm")。认不出返回 null。</summary>
        public static string PortraitOfKey(string key, out string state)
        {
            state = "";
            if (string.IsNullOrEmpty(key)) return null;
            if (key.StartsWith(KeyPrefix, StringComparison.Ordinal)) key = key.Substring(KeyPrefix.Length);
            if (PortraitLibrary.Get(key) != null) return key;
            // ClaudeNote: 0.8.2 状态名里也可以有下划线（root_key 组的差分名就是那张立绘的 ID，例如 my_girl_alt）：
            //   从左往右找第一个“前半段是立绘”的下划线，之前只认最后一个下划线
            for (int bar = key.IndexOf('_'); bar > 0; bar = key.IndexOf('_', bar + 1))
            {
                if (PortraitLibrary.Get(key.Substring(0, bar)) == null) continue;
                state = PortraitLibrary.Normal(key.Substring(bar + 1));
                return key.Substring(0, bar);
            }
            return null;
        }

        // ClaudeNote: 0.6.0 登记进立绘表的显示键（pinned）不当场销毁：来客可能正拿着这个键走进来，销毁了就是一片空白。
        //   0.6.1 自检：这类“现在不能销毁”的图（还在立绘表里、或正显示在柜台上）进待销毁名单 detached，
        //   能销毁的时机只有两个：柜台换人、立绘表里的键被换成新图——就在这两个时机清（Sweep），不轮询。
        //   之前是一直留到停用，换一次来源漏一批（一张带多级纹理的高清图约 5 MB）。
        internal static void Clear()
        {
            // 取图入口的补丁在时，显示键从立绘表里撤掉：下次有人按这个键取图，补丁会按新方案现做一张（见 PlanPort）。
            // 补丁没装上时键留在表里（否则原版找不到键），旧图就一直用着。
            Dictionary<string, Sprite> dict = Ports.Ok(PlanPort.Name) && SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            foreach (string key in new List<string>(cache.Keys))
            {
                if (pinned.Remove(key))
                {
                    Sprite old = cache[key];
                    if (dict != null)
                        foreach (string tableKey in new List<string>(dict.Keys))
                            if (tableKey.StartsWith(KeyPrefix, StringComparison.Ordinal) && ReferenceEquals(dict[tableKey], old)) dict.Remove(tableKey);
                    Detach(old, owned.Remove(key));
                    cache.Remove(key);
                    order.Remove(key);
                    continue;
                }
                Drop(key);
            }
            Sweep();
        }

        private static void Detach(Sprite s, bool ownTexture)
        {
            if (s != null) detached.Add(new KeyValuePair<Sprite, bool>(s, ownTexture));
        }

        /// <summary>待销毁名单里已经没人用的图销毁掉（不在柜台上、也不在立绘表里）。名单空时什么都不做。</summary>
        internal static void Sweep()
        {
            if (detached.Count == 0) return;
            SpriteRenderer image = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
            Sprite shown = image != null ? image.sprite : null;
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            for (int i = detached.Count - 1; i >= 0; i--)
            {
                Sprite s = detached[i].Key;
                if (s != null)
                {
                    if (ReferenceEquals(s, shown) || (dict != null && dict.ContainsValue(s))) continue;
                    Destroy(s, detached[i].Value);
                }
                detached.RemoveAt(i);
            }
        }

        private static void Destroy(Sprite s, bool ownTexture)
        {
            Texture2D tex = ownTexture ? s.texture : null;
            HdRender.Unmark(s);
            UnityEngine.Object.Destroy(s);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }

        /// <summary>停用时：连登记进立绘表的显示键一起撤掉。</summary>
        internal static void Shutdown()
        {
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            if (dict != null)
                foreach (string key in new List<string>(dict.Keys))
                    if (key.StartsWith(KeyPrefix, StringComparison.Ordinal)) dict.Remove(key);
            pinned.Clear();
            foreach (string key in new List<string>(cache.Keys)) Drop(key);
            order.Clear();
            foreach (KeyValuePair<Sprite, bool> d in detached) if (d.Key != null) Destroy(d.Key, d.Value);
            detached.Clear();
            owned.Clear();
        }

        private static readonly List<KeyValuePair<Sprite, bool>> detached = new List<KeyValuePair<Sprite, bool>>();   // (图, 贴图是不是自己的)

        private static readonly HashSet<string> owned = new HashSet<string>();

        private static Sprite Remember(string key, Sprite s, bool ownTexture)
        {
            cache[key] = s;
            if (ownTexture) owned.Add(key);
            Touch(key);
            for (LinkedListNode<string> n = order.Last; order.Count - pinned.Count > Keep && n != null;)
            {
                LinkedListNode<string> prev = n.Previous;
                if (!pinned.Contains(n.Value)) Drop(n.Value);
                n = prev;
            }
            return s;
        }

        private static void Touch(string key)
        {
            order.Remove(key);
            order.AddFirst(key);
        }

        private static void Drop(string key)
        {
            if (!cache.TryGetValue(key, out Sprite s)) return;
            if (key.IndexOf('~') < 0)   // 整张图要走了：从它裁出来的取景先走（共用同一张贴图）
                foreach (string k in new List<string>(cache.Keys)) if (k.StartsWith(key + "~", StringComparison.Ordinal)) Drop(k);
            cache.Remove(key);
            order.Remove(key);
            if (s == null) return;
            // ClaudeNote: 柜台上正显示着的那张不销毁（只从缓存拿掉），免得来客变成空白
            SpriteRenderer image = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
            bool ownTexture = owned.Remove(key);
            if (image != null && image.sprite == s) { Detach(s, ownTexture); return; }   // 等来客走了再销毁（Sweep）
            Destroy(s, ownTexture);
        }

        // 身份串（file|… 或 res|…，见 ExternalImages）→ 读图（自己留一份）
        private static Texture2D LoadFile(string sig)
        {
            try
            {
                byte[] bytes = ExternalImages.Bytes(sig);
                if (bytes == null) return null;
                // ClaudeNote: 0.4.3 带多级渐远纹理：大图缩到柜台那么小时不再锯齿（Lune 实测：高清图像被像素化滤镜处理过）
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!tex.LoadImage(bytes, true)) { UnityEngine.Object.Destroy(tex); return null; }
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (Exception ex) { Entry.Warn("display image not read (" + sig + "): " + ex.Message); return null; }
        }
    }
}
