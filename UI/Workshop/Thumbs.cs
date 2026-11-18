// ClaudeNote: 0.9.7 工坊的立绘缩略图：整张原图的小样（Lune 2026-11-18：“工坊那边是显示全图的”）。
//   之前工坊借的是道具图缓存（裁成方块、对着头的成品），为了让标题界面有图可看，后台还把所有来源（含没激活的候选）都裁了一遍——
//   裁剪成品是给头像系统和立绘衍生道具用的，不该为了“看一眼”去生产。现在两件事分开：
//     · 这里只管“看”：按图的身份串读原图 → 缩到 Max 像素以内 → 只留在内存里，关窗全部放掉；不写任何缓存。
//     · 一帧最多读一张（几 MB 的 PNG 解码要几十毫秒），先摆占位、读到了再换上。
//   取图顺序：有身份串的（mod 文件夹里的文件 / 打包在 DLL 里的资源）直接读；没有的（游戏原图、别的 mod 已载入的贴图）在店面里问立绘库要；
//   标题界面拿不到的（立绘表不在）退回调用方给的后备图（道具图缓存里已有的成品），再没有就是占位。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyExtendFramework.WorkshopUi
{
    internal static class Thumbs
    {
        private const int Max = 320;
        private struct Job { internal string id; internal Image target; internal Func<Sprite> fallback; }
        private static readonly Dictionary<string, Sprite> made = new Dictionary<string, Sprite>();   // 读不到的记 null，不反复试
        private static readonly List<Job> waiting = new List<Job>();

        internal static int Count => made.Count;

        /// <summary>把这张图的小样放进 target。已经读过的当场放上；没读过的排队，读到之前 target 保持调用方摆的占位。</summary>
        internal static void Show(Image target, string key, string variant, Func<Sprite> fallback)
        {
            string id = key + "#" + PortraitLibrary.Normal(variant);
            if (made.TryGetValue(id, out Sprite have)) { Put(target, have ?? fallback?.Invoke()); return; }
            waiting.Add(new Job { id = id, target = target, fallback = fallback });
        }

        /// <summary>窗口重摆：排着队的目标都要销毁了。</summary>
        internal static void Forget() { waiting.Clear(); }

        /// <summary>每帧调一次：最多真读一张。</summary>
        internal static void Step()
        {
            while (waiting.Count > 0)
            {
                Job job = waiting[0];
                waiting.RemoveAt(0);
                if (job.target == null) continue;
                bool fresh = !made.TryGetValue(job.id, out Sprite s);
                if (fresh)
                {
                    try { s = Make(job.id); }
                    catch (Exception ex) { Health.Report("workshop thumbnail", job.id + ": " + ex.Message); s = null; }
                    made[job.id] = s;
                }
                Put(job.target, s ?? job.fallback?.Invoke());
                if (fresh) return;
            }
        }

        internal static void Release()
        {
            waiting.Clear();
            foreach (Sprite s in made.Values)
            {
                if (s == null) continue;
                Texture2D tex = s.texture;
                UnityEngine.Object.Destroy(s);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            made.Clear();
        }

        private static void Put(Image target, Sprite s)
        {
            if (target == null || s == null) return;
            for (int i = target.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(target.transform.GetChild(i).gameObject);   // 占位上的字
            target.sprite = s;
            target.color = Color.white;
            target.type = Image.Type.Simple;
            target.preserveAspect = true;
        }

        private static Sprite Make(string id)
        {
            int hash = id.LastIndexOf('#');
            string key = id.Substring(0, hash), variant = id.Substring(hash + 1);
            bool live = PortraitLibrary.Ready;
            string sig = live ? PortraitLibrary.ImageSignature(key, variant) : Workshop.SigOf(key, variant);
            Texture2D small = null;
            if (sig != null)
            {
                byte[] bytes = ExternalImages.Bytes(sig, true);   // 目录里列着的图：包没上架（标题界面）也读
                if (bytes == null) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);   // 带多级纹理：一次缩到很小也不锯齿
                try
                {
                    if (!tex.LoadImage(bytes, true)) return null;
                    small = Shrink(tex, new Rect(0f, 0f, tex.width, tex.height));
                }
                finally { UnityEngine.Object.Destroy(tex); }
            }
            else if (live)
            {
                Sprite src = PortraitLibrary.ResolveImage(key, variant);
                if (src == null || src.texture == null) return null;
                Rect r;
                try { r = src.textureRect; } catch (Exception) { r = src.rect; }   // 紧密打包进图集的取 textureRect 会抛错
                small = Shrink(src.texture, r);
            }
            if (small == null) return null;
            Sprite s = Sprite.Create(small, new Rect(0f, 0f, small.width, small.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            s.name = id;
            return s;
        }

        // 把贴图的一块缩进 Max 像素以内（走显卡：来源可读不可读都行）
        private static Texture2D Shrink(Texture2D src, Rect part)
        {
            if (part.width < 1f || part.height < 1f) return null;
            float scale = Mathf.Min(1f, Max / Mathf.Max(part.width, part.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(part.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(part.height * scale));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt, new Vector2(part.width / src.width, part.height / src.height), new Vector2(part.x / src.width, part.y / src.height));
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                tex.Apply(false, true);
                tex.filterMode = src.filterMode == FilterMode.Point ? FilterMode.Point : FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
