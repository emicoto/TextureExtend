// ClaudeNote: 把来源立绘读成像素。来源贴图多半不可读（原版图集、别的 mod 载入的图），不可读的走一次 GPU 回读。
//   同一张贴图只读一次：记住最近读过的那张（原版立绘一个派系一张图集，连续处理时全部命中）。
//   批量处理结束后调 Release 放掉这份像素（一张图集约 5 MB）。只在主线程用。
using System;
using UnityEngine;

namespace ProbablyExtendFramework
{
    internal static class SourceReader
    {
        private static Texture2D lastTexture;
        private static Color32[] lastPixels;

        /// <summary>立绘所在贴图的全部像素，以及立绘在贴图里的矩形。读不到返回 false。</summary>
        internal static bool Read(Sprite sprite, out Color32[] pixels, out int width, out int height, out RectInt rect)
        {
            pixels = null; width = height = 0; rect = new RectInt();
            Texture2D tex = sprite != null ? sprite.texture : null;
            if (tex == null) return false;
            width = tex.width; height = tex.height;
            rect = RectOf(sprite);
            WasShrunk = Mathf.Max(rect.width, rect.height) > ShrinkOver;
            // ClaudeNote: 1.0.0 大图先在显卡上缩小再读（Lune：读档后集中预处理要卡好几秒；日志：218 张 6.5 秒、217 张 21 秒）。
            //   成品只有一两百像素见方，而高清立绘一张两百多万像素：整张读回内存（每张 8 MB 的数组，两百张就是近 2 GB 的垃圾要回收）、
            //   再逐像素找外框、逐像素平均——大部分时间花在这些用不上的精度上。长边超过 ShrinkOver 的，只把立绘那一块缩到长边 ShrinkTo 再读：
            //   后面的活少到五分之一以下，数组也小。比例、外框、内容占比都是相对量，缩小后不变。小图（原版、像素风的包）不走这条路，逐像素原样。
            if (Mathf.Max(rect.width, rect.height) > ShrinkOver)
            {
                if (!ReferenceEquals(tex, shrunkOf) || shrunkRect.x != rect.x || shrunkRect.y != rect.y || shrunkRect.width != rect.width || shrunkRect.height != rect.height || shrunk == null)
                {
                    shrunk = Shrink(tex, rect, out shrunkW, out shrunkH);
                    shrunkOf = tex; shrunkRect = rect;
                }
                pixels = shrunk; width = shrunkW; height = shrunkH;
                rect = new RectInt(0, 0, width, height);
                return pixels != null && pixels.Length == width * height;
            }
            if (!ReferenceEquals(tex, lastTexture) || lastPixels == null)
            {
                lastPixels = tex.isReadable ? Direct(tex) : null;
                if (lastPixels == null) lastPixels = ReadBack(tex);
                lastTexture = tex;
            }
            pixels = lastPixels;
            return pixels != null && pixels.Length == width * height;
        }

        private const int ShrinkOver = 1024, ShrinkTo = 768;
        /// <summary>最近一次 Read 给的是缩小过的像素（这种图一定不是像素画：取景时不能按“外框很窄 = 像素图”来判断）。</summary>
        internal static bool WasShrunk { get; private set; }
        private static Texture2D shrunkOf;
        private static RectInt shrunkRect;
        private static Color32[] shrunk;
        private static int shrunkW, shrunkH;

        // 贴图的一块 → 缩到长边 ShrinkTo 的像素（走显卡，来源可读不可读都行）
        private static Color32[] Shrink(Texture2D tex, RectInt part, out int w, out int h)
        {
            float scale = ShrinkTo / (float)Mathf.Max(part.width, part.height);
            w = Mathf.Max(1, Mathf.RoundToInt(part.width * scale)); h = Mathf.Max(1, Mathf.RoundToInt(part.height * scale));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(tex, rt, new Vector2(part.width / (float)tex.width, part.height / (float)tex.height), new Vector2(part.x / (float)tex.width, part.y / (float)tex.height));
                RenderTexture.active = rt;
                copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                return copy.GetPixels32();
            }
            catch (Exception ex) { Entry.Warn("shrink failed (" + tex.name + "): " + ex.Message); return null; }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }

        internal static void Release()
        {
            shrunkOf = null;
            shrunk = null;
            lastTexture = null;
            lastPixels = null;
            ExternalImages.Release();   // ClaudeNote: 0.3.0 外部立绘包读进来的整张图一起放掉
        }

        /// <summary>来源的“指纹”：贴图名、贴图尺寸、矩形。别的 mod 换了立绘，指纹就变，缓存随之重做。不读像素。</summary>
        internal static string Signature(Sprite sprite)
        {
            Texture2D tex = sprite != null ? sprite.texture : null;
            if (tex == null) return "";
            RectInt r = RectOf(sprite);
            return tex.name + "|" + tex.width + "x" + tex.height + "|" + r.x + "," + r.y + "," + r.width + "," + r.height;
        }

        private static RectInt RectOf(Sprite sprite)
        {
            Rect r;
            try { r = sprite.textureRect; }          // 紧密打包进图集的 Sprite 取这个会抛错
            catch (Exception) { r = sprite.rect; }
            if (r.width <= 0f || r.height <= 0f) r = sprite.rect;
            return new RectInt(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height));
        }

        private static Color32[] Direct(Texture2D tex)
        {
            try { return tex.GetPixels32(); }
            catch (Exception) { return null; }       // 个别压缩格式读不了，改走回读
        }

        private static Color32[] ReadBack(Texture2D tex)
        {
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0f, 0f, tex.width, tex.height), 0, 0, false);
                return copy.GetPixels32();
            }
            catch (Exception ex)
            {
                Entry.Warn("read back failed (" + tex.name + "): " + ex.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
