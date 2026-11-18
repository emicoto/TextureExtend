// ClaudeNote: 外部立绘包的图片：放在别的 mod 文件夹里、游戏不会替它载入的 PNG（自带运行时的包、没启用的只读立绘包）。
//   用到时才读（走 ModHelper.ReadModFileBytes，不需要 files 能力），同一时间只留最近读的一张；
//   SourceReader.Release（后台每秒一次、批量处理结束时）顺带放掉。一张 1152×844 的图约 4 MB，不会越攒越多。
//   只在主线程用。
using System;
using UnityEngine;

namespace ProbablyExtendFramework
{
    internal static class ExternalImages
    {
        private const int MaxBytes = 32 * 1024 * 1024;
        private static string lastKey;
        private static Texture2D lastTexture;
        private static Sprite lastSprite;

        // ClaudeNote: 0.8.4 图片的“身份串”（也当缓存指纹用），两种：
        //   file|模组ID|版本|相对路径   mod 文件夹里的图片（ModHelper.ReadModFileBytes，零权限）
        //   res|模组ID|版本|资源名      打包在启用中的 mod 的 DLL 里的图片（白蛇娘化、城银美化这类；Assembly.GetManifestResourceStream，零权限）
        internal static string FileSig(string modId, string relativePath, string stamp = null)   // stamp：登记方知道的“文件变没变”的记号（大小 + 修改时间），并进版本那一格
        {
            return "file|" + modId + "|" + VersionOf(modId) + (string.IsNullOrEmpty(stamp) ? "" : "+" + stamp.Replace('|', '_')) + "|" + relativePath;
        }
        internal static string ResourceSig(string modId, string resource) { return "res|" + modId + "|" + VersionOf(modId) + "|" + resource; }
        // ClaudeNote: 0.9.9 第三种：ext|模组ID|版本|资源名 —— 打包在【没启用】的 mod 的 DLL 里的图片。游戏没载入那个 DLL，框架读不到；
        //   由伴生 mod（有文件权限）把 DLL 当文件读出那一段交过来（Painter.UseImageReader）。伴生 mod 没装 / 没启用时这类图读不到。
        internal static string ExtSig(string modId, string resource) { return "ext|" + modId + "|" + VersionOf(modId) + "|" + resource; }
        internal static Func<string, string, byte[]> Reader;   // (模组ID, 资源名) → PNG 字节

        private static string VersionOf(string modId)
        {
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == modId);
            return mod?.Manifest?.ModVersion ?? "";
        }

        /// <summary>按身份串读出 PNG 的字节；读不到返回 null。</summary>
        internal static byte[] Bytes(string sig, bool listed = false)   // listed：工坊目录里列着的图，包这次没上架也读（只读、只为显示）
        {
            string[] p = (sig ?? "").Split(new[] { '|' }, 4);
            if (p.Length < 4) return null;
            byte[] bytes = null;
            if (p[0] == "file")
            {
                if (!listed && !PackShelf.CanRead(p[1])) return null;   // 启用中的 mod，或架子上的包
                bytes = ModHelper.ReadModFileBytes(p[1], p[3]);
            }
            else if (p[0] == "ext")
            {
                if (Reader == null || (!listed && !PackShelf.CanRead(p[1]))) return null;
                bytes = Reader(p[1], p[3]);
            }
            else if (p[0] == "res")
            {
                LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == p[1]);
                if (mod == null || !mod.IsEnabled || mod.Assembly == null) return null;
                using (System.IO.Stream stream = mod.Assembly.GetManifestResourceStream(p[3]))
                {
                    if (stream == null || stream.Length > MaxBytes) return null;
                    bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length) { int n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                    if (read != bytes.Length) return null;
                }
            }
            return bytes != null && bytes.Length > 0 && bytes.Length <= MaxBytes ? bytes : null;
        }

        internal static Sprite Get(string sig)
        {
            if (sig == lastKey && lastSprite != null) return lastSprite;
            Release();
            try
            {
                byte[] bytes = Bytes(sig);
                if (bytes == null) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes, false)) { UnityEngine.Object.Destroy(tex); return null; }   // 保持可读：取景直接读像素，省一次 GPU 回读
                tex.name = sig;
                tex.wrapMode = TextureWrapMode.Clamp;
                lastKey = sig;
                lastTexture = tex;
                lastSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                return lastSprite;
            }
            catch (Exception ex)
            {
                Entry.Warn("external image " + sig + " not read: " + ex.Message);
                return null;
            }
        }

        internal static void Release()
        {
            if (lastSprite != null) UnityEngine.Object.Destroy(lastSprite);
            if (lastTexture != null) UnityEngine.Object.Destroy(lastTexture);
            lastSprite = null;
            lastTexture = null;
            lastKey = null;
        }
    }
}
