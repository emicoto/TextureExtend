// ClaudeNote: 取景与重采样（纯计算，不碰 Unity 对象，可以单独测）。
//   输入：整张来源贴图的像素（Unity 顺序：第 0 行在最下面）与立绘在贴图里的矩形；输出：一张正方形的像素。
//   取景：找出立绘不透明部分的外框，取“顶端对齐、左右居中”的正方形（头 + 胸）。外框比窗口矮时改为上下居中。
//   原则（Lune 2026-11-15）：不放大来源像素。输出多大由来源决定，上限是 target（道具在屏幕上实际占的像素，玩家选 128 或 256）：
//     像素图（外框宽 ≤ 160）：按原像素裁一个 128 的窗口（原版立绘的头胸正好这么大；两侧多出来的肩膀裁掉），输出就是窗口大小，点采样；
//                            很小的图（外框宽 ≤ 64）窗口相应减半，同样不放大。
//     大图（高清立绘）：窗口 = 外框宽。比 target 大就按面积平均缩到 target（带透明度加权，边缘不发黑）；不比 target 大就原样裁切。双线性。
//   输出不足 target 的，由显示时铺满格子（Sprite 的每单位像素数按输出大小算），不在这里插值。
//   矩形以外的像素一律当透明：来源常在图集里，不能把隔壁那张带进来。
using System;
using UnityEngine;

namespace TextureExtend
{
    internal static class Raster
    {
        internal const byte AlphaCut = 16;        // 低于这个透明度的像素不算进外框
        internal const int NativeWindow = 128;    // 像素图的取景窗口（来源像素）
        internal const int PixelArtMax = 160;     // 外框宽不超过这个数就按像素图处理
        internal const int HeadRoom = 2;          // 原像素裁切：头顶上方留几行空

        /// <summary>取景并重采样。size = 输出的边长（≤ target）。立绘完全透明时返回 null。pixelArt = 输出该用点采样。</summary>
        internal static Color32[] Frame(Color32[] src, int texW, int texH, RectInt rect, int target, out int size, out bool pixelArt)
        {
            pixelArt = true; size = 0;
            rect.ClampToBounds(new RectInt(0, 0, texW, texH));
            if (src == null || rect.width <= 0 || rect.height <= 0 || !Bounds(src, texW, rect, out RectInt box)) return null;

            pixelArt = box.width <= PixelArtMax;
            if (pixelArt)
            {
                size = NativeWindow / Mathf.Max(1, NativeWindow / box.width);   // 128；外框宽 ≤ 64 时 64，以此类推
                return Crop(src, texW, rect, box, size);
            }
            if (box.width <= target) { size = box.width; return Crop(src, texW, rect, box, size); }
            size = target;
            return AreaAverage(src, texW, rect, box, size);
        }

        /// <summary>矩形内不透明像素的外框。</summary>
        internal static bool Bounds(Color32[] src, int texW, RectInt rect, out RectInt box)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                int row = y * texW;
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    if (src[row + x].a < AlphaCut) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            box = maxX < 0 ? new RectInt() : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return maxX >= 0;
        }

        /// <summary>原像素裁切：边长 window 的正方形窗口，一个来源像素对一个输出像素。</summary>
        private static Color32[] Crop(Color32[] src, int texW, RectInt rect, RectInt box, int window)
        {
            int left = box.xMin + (box.width - window) / 2;          // 左右居中（窗口比外框宽就两侧留空，窄就两侧裁掉）
            int top = box.height >= window
                ? Mathf.Min(box.yMax - 1 + HeadRoom, rect.yMax - 1)  // 顶端对齐，头顶留一点空
                : box.yMin + (box.height + window) / 2 - 1;          // 外框比窗口矮：上下居中
            var dst = new Color32[window * window];
            for (int oy = 0; oy < window; oy++)
            {
                int sy = top - (window - 1 - oy);
                if (sy < rect.yMin || sy >= rect.yMax) continue;
                int row = sy * texW, outRow = oy * window;
                for (int ox = 0; ox < window; ox++)
                {
                    int sx = left + ox;
                    if (sx < rect.xMin || sx >= rect.xMax) continue;
                    dst[outRow + ox] = src[row + sx];
                }
            }
            return dst;
        }

        /// <summary>按面积平均缩小：每个输出像素 = 它盖住的来源像素按覆盖面积、透明度加权的平均。</summary>
        private static Color32[] AreaAverage(Color32[] src, int texW, RectInt rect, RectInt box, int size)
        {
            float step = box.width / (float)size;                    // 一个输出像素盖住几个来源像素（> 1）
            float window = step * size;
            float left = box.xMin;
            float top = box.height >= window ? box.yMax : box.yMin + (box.height + window) * 0.5f;   // 窗口上沿（不含）
            var dst = new Color32[size * size];
            for (int oy = 0; oy < size; oy++)
            {
                float y1 = top - (size - 1 - oy) * step, y0 = y1 - step;
                int iy0 = Mathf.Max(rect.yMin, Mathf.FloorToInt(y0)), iy1 = Mathf.Min(rect.yMax, Mathf.CeilToInt(y1));
                for (int ox = 0; ox < size; ox++)
                {
                    float x0 = left + ox * step, x1 = x0 + step;
                    int ix0 = Mathf.Max(rect.xMin, Mathf.FloorToInt(x0)), ix1 = Mathf.Min(rect.xMax, Mathf.CeilToInt(x1));
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = iy0; sy < iy1; sy++)
                    {
                        float wy = Mathf.Min(sy + 1, y1) - Mathf.Max(sy, y0);
                        if (wy <= 0f) continue;
                        int row = sy * texW;
                        for (int sx = ix0; sx < ix1; sx++)
                        {
                            float w = wy * (Mathf.Min(sx + 1, x1) - Mathf.Max(sx, x0));
                            if (w <= 0f) continue;
                            Color32 c = src[row + sx];
                            float wa = w * c.a;
                            r += c.r * wa; g += c.g * wa; b += c.b * wa; a += wa;
                        }
                    }
                    if (a <= 0f) continue;
                    float cover = a / (step * step);                 // 窗口伸出矩形的部分算透明
                    dst[oy * size + ox] = new Color32(Byte(r / a), Byte(g / a), Byte(b / a), Byte(cover));
                }
            }
            return dst;
        }

        private static byte Byte(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255); }
    }
}
