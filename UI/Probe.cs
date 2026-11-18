// ClaudeNote: 显示探针（取证用，Lune 2026-11-16：测试立绘没锯齿了但依然模糊）。
//   不改任何东西，只把“柜台来客这张图在屏幕上实际多大、游戏相机怎么渲染”写进日志：
//     · 像素完美相机（PixelPerfectCamera）开没开、参考分辨率、是否先渲到低分辨率再放大（upscaleRT）
//     · 主相机的正交尺寸、屏幕分辨率
//     · 来客图的贴图像素、在屏幕上占多少像素 → 缩放倍数
//   像素完美相机若先渲到低分辨率，世界里的高清图会先被压到参考分辨率再放大，这是“怎么都糊”的来源，得换渲染路径（第 5 步）。
using System;
using System.Text;
using UnityEngine;
using UnityEngine.U2D;

namespace ProbablyExtendFramework
{
    internal static class Probe
    {
        internal static void Run()
        {
            var b = new StringBuilder("display probe:");
            try
            {
                b.Append("\n  screen ").Append(Screen.width).Append("x").Append(Screen.height);
                Camera cam = Camera.main;
                if (cam != null)
                {
                    b.Append(" · main camera ").Append(cam.name).Append(" ortho ").Append(cam.orthographic).Append(" size ").Append(cam.orthographicSize)
                        .Append(" · pixel rect ").Append(cam.pixelWidth).Append("x").Append(cam.pixelHeight)
                        .Append(" · targetTexture ").Append(cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height : "none");
                    // ClaudeNote: 2026-11-16 直接按类型读像素完美相机，不再按名字反射读属性（游戏的 mod 扫描会判成“反射访问内部实现”）
                    PixelPerfectCamera ppc = cam.GetComponent<PixelPerfectCamera>();
                    if (ppc != null)
                        b.Append("\n  PixelPerfectCamera enabled ").Append(ppc.enabled)
                            .Append(" · assetsPPU ").Append(ppc.assetsPPU)
                            .Append(" · ref ").Append(ppc.refResolutionX).Append("x").Append(ppc.refResolutionY)
                            .Append(" · upscaleRT ").Append(ppc.upscaleRT)
                            .Append(" · pixelSnapping ").Append(ppc.pixelSnapping)
                            .Append(" · cropFrame ").Append(ppc.cropFrameX).Append("/").Append(ppc.cropFrameY)
                            .Append(" · stretchFill ").Append(ppc.stretchFill)
                            .Append(" · pixelRatio ").Append(ppc.pixelRatio);
                    else b.Append("\n  no PixelPerfectCamera on the main camera");
                }
                else b.Append(" · no main camera");

                SpriteRenderer image = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
                Sprite s = image != null ? image.sprite : null;
                if (s == null) b.Append("\n  no visitor on the counter");
                else
                {
                    Texture2D tex = s.texture;
                    b.Append("\n  visitor sprite ").Append(s.name).Append(" · rect ").Append(s.rect.width).Append("x").Append(s.rect.height)
                        .Append(" ppu ").Append(s.pixelsPerUnit)
                        .Append(" · texture ").Append(tex != null ? tex.width + "x" + tex.height + " mips " + tex.mipmapCount + " filter " + tex.filterMode : "none");
                    if (cam != null)
                    {
                        Bounds bounds = image.bounds;
                        Vector3 lo = cam.WorldToScreenPoint(bounds.min), hi = cam.WorldToScreenPoint(bounds.max);
                        float h = Mathf.Abs(hi.y - lo.y);
                        b.Append(" · on screen ").Append(Mathf.RoundToInt(Mathf.Abs(hi.x - lo.x))).Append("x").Append(Mathf.RoundToInt(h))
                            .Append(" px · scale ").Append(h > 0f ? (h / s.rect.height).ToString("0.###") : "?");
                    }
                    b.Append(" · layer ").Append(image.sortingLayerName).Append(" order ").Append(image.sortingOrder)
                        .Append(" · material ").Append(image.sharedMaterial != null ? image.sharedMaterial.name : "none");
                    b.Append("\n  ").Append(Verdict(cam, image, s));
                }
            }
            catch (Exception ex) { b.Append("\n  probe failed: ").Append(ex.Message); }
            Entry.Info(b.ToString());
        }

        // ClaudeNote: 0.5.1 判定这一张“为什么糊 / 为什么清楚”（Lune：到底是渲染层、贴图大小，还是别的）。
        //   判定顺序：谁在画（店面里的 SpriteRenderer，还是别的 mod 的界面层叠）→ 店面画布多大（像素画布 960×540 还是屏幕分辨率）
        //   → 贴图本身够不够大（贴图像素 vs 在屏幕上实际占的像素）。
        internal static string Verdict(Camera cam, SpriteRenderer image, Sprite s)
        {
            var v = new StringBuilder("verdict: ");
            string key = ClientKey();
            string owner = key != null ? Originals.OwnerOfCurrent(key) : null;
            v.Append("key ").Append(key ?? "?").Append(" · plan ").Append(key != null ? Plan.ForKey(key).ToString() : "-").Append(" · table owner ").Append(owner ?? "-")
             .Append(" · sprite is ").Append(Origin(key, s)).Append(" · marked HD ").Append(HdRender.IsHd(s));

            // 1. 谁在画：别的 mod 把店面里那张关掉（forceRenderingOff）、改在界面层上画清晰版时，店面画布怎么样都不影响它
            if (image.forceRenderingOff)
            {
                v.Append("\n  drawn by: another mod's UI overlay (the shop renderer is switched off) → CLEAR path: not affected by the pixel canvas");
                return v.ToString();
            }

            // 2. 店面画布
            PixelPerfectCamera ppc = cam != null ? cam.GetComponent<PixelPerfectCamera>() : null;
            bool lowCanvas = ppc != null && ppc.enabled && ppc.upscaleRT;
            float onScreen = 0f;
            if (cam != null) { Bounds bo = image.bounds; onScreen = Mathf.Abs(cam.WorldToScreenPoint(bo.max).y - cam.WorldToScreenPoint(bo.min).y); }
            float canvasPx = lowCanvas && ppc.pixelRatio > 0 ? onScreen / ppc.pixelRatio : onScreen;   // 画进画布时实际用到的像素高
            v.Append("\n  drawn by: shop SpriteRenderer · canvas ")
             .Append(lowCanvas ? "LOW-RES " + ppc.refResolutionX + "x" + ppc.refResolutionY + " then x" + ppc.pixelRatio : "screen resolution")
             .Append(ppc != null && !ppc.enabled ? " (PixelPerfectCamera off: non-integer window size)" : "")
             .Append(" · portrait uses ").Append(Mathf.RoundToInt(canvasPx)).Append(" px of canvas height for ").Append(Mathf.RoundToInt(s.rect.height)).Append(" px of texture");

            // 3. 结论
            float texPx = s.rect.height;
            if (lowCanvas && texPx > canvasPx * 1.5f)
                v.Append("\n  → BLURRY because of the RENDER LAYER: the texture is squeezed into the low-res canvas first, then enlarged. Texture size is not the problem.");
            else if (texPx < onScreen * 0.75f)
                v.Append("\n  → BLURRY because of TEXTURE SIZE: the image itself has fewer pixels than its size on screen (enlarged ").Append((onScreen / Mathf.Max(1f, texPx)).ToString("0.#")).Append("x).");
            else if (!lowCanvas)
                v.Append("\n  → CLEAR path: drawn at screen resolution with enough texture pixels").Append(texPx > onScreen * 2.5f ? " (strongly downscaled: may look slightly soft)" : "").Append('.');
            else
                v.Append("\n  → pixel-art path (texture about canvas size): looks like vanilla.");
            return v.ToString();
        }

        private static string ClientKey()
        {
            try { return PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.GetSprite() : null; }
            catch (Exception) { return null; }
        }

        private static string Origin(string key, Sprite s)
        {
            if (key == null) return "?";
            if (ReferenceEquals(s, Originals.Get(key))) return "game original";
            string owner = Originals.OwnerOf(s.texture);
            if (owner != null) return "from mod " + owner;
            if (s.name.IndexOf('#') >= 0 || s.name.StartsWith(Display.KeyPrefix, StringComparison.Ordinal)) return "TextureExtend display";
            return "runtime-made (" + s.name + ")";
        }

        // ── 自动诊断：开着时，柜台上的图每换一次写一行（下一帧写，等别的 mod 的层叠先更新） ──
        internal static bool Auto;
        private static Sprite lastSeen;
        private static int pendingFrames = -1;

        internal static void Tick()
        {
            if (!Auto) return;
            SpriteRenderer image = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
            Sprite s = image != null && image.enabled ? image.sprite : null;
            if (!ReferenceEquals(s, lastSeen)) { lastSeen = s; pendingFrames = s != null ? 2 : -1; return; }
            if (pendingFrames < 0 || --pendingFrames > 0) return;
            pendingFrames = -1;
            try { Entry.Info("auto probe · " + Verdict(Camera.main, image, s)); }
            catch (Exception ex) { Entry.Warn("auto probe failed: " + ex.Message); }
        }
    }
}
