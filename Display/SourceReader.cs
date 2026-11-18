// ClaudeNote: 把来源立绘读成像素。来源贴图多半不可读（原版图集、别的 mod 载入的图），不可读的走一次 GPU 回读。
//   同一张贴图只读一次：记住最近读过的那张（原版立绘一个派系一张图集，连续处理时全部命中）。
//   批量处理结束后调 Release 放掉这份像素（一张图集约 5 MB）。只在主线程用。
using System;
using UnityEngine;

namespace TextureExtend
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
            if (!ReferenceEquals(tex, lastTexture) || lastPixels == null)
            {
                lastPixels = tex.isReadable ? Direct(tex) : null;
                if (lastPixels == null) lastPixels = ReadBack(tex);
                lastTexture = tex;
            }
            pixels = lastPixels;
            return pixels != null && pixels.Length == width * height;
        }

        internal static void Release()
        {
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
