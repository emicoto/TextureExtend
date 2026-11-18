// ClaudeNote: 0.6.0 立绘大小自适配的纯计算部分（不碰 Unity / 游戏类型，可离线测：verification_fit）。
//   依据（Lune 2026-11-16 定，实测九个立绘包）：各包画布大小、留白各不相同，但“人露出来的高度”应当和原版差不多。
//     内容占比 = 不透明内容的高 ÷ 画布高。原版约 0.74；照原版规格画的包 0.72–0.78；裁满画布的高清包 0.94–0.99。
//     画布对齐原版之后，再乘 缩放 = 原版内容占比 ÷ 这个来源的内容占比，人就和原版一样高。
//   按来源取中位数：一个包一个缩放。包里的高矮差保留，个别戴高帽、举手的不会把自己缩小。
//   上限：最多缩小 25%、最多放大 10%（再多就由它大 / 小；放大会糊，所以更保守）。差 4% 以内当作一致，不动。
using System;
using System.Collections.Generic;

namespace TextureExtend
{
    internal static class FitMath
    {
        internal const float FallbackReference = 0.74f;   // 量不到原版时用（四个原版规格的包实测 0.72–0.78）
        internal const float MinScale = 0.75f, MaxScale = 1.10f, DeadBand = 0.04f;
        internal const int MinSamples = 3;                // 少于这么多张不下结论（缩放按 1）
        internal const int WantSamples = 9;               // 每个来源量这么多张就够定中位数

        internal static float Median(List<float> values)
        {
            if (values == null || values.Count == 0) return 0f;
            var sorted = new List<float>(values);
            sorted.Sort();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5f;
        }

        /// <summary>来源的自动缩放。sourceRatio ≤ 0（没量够）→ 1。</summary>
        internal static float Scale(float referenceRatio, float sourceRatio)
        {
            if (sourceRatio <= 0f) return 1f;
            if (referenceRatio <= 0f) referenceRatio = FallbackReference;
            float s = referenceRatio / sourceRatio;
            if (Math.Abs(s - 1f) <= DeadBand) return 1f;
            return s < MinScale ? MinScale : s > MaxScale ? MaxScale : s;
        }

        /// <summary>缩放后底边仍贴着原版画布底边：轴心（归一化，y 从底边算）随缩放换算。</summary>
        internal static float PivotY(float referencePivotY, float scale)
        {
            return scale <= 0f ? referencePivotY : referencePivotY / scale;
        }
    }
}
