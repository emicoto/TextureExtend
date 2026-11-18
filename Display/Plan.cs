// ClaudeNote: 0.6.0 显示方案——“这张立绘现在怎么显示”只在这里裁决一次，取图入口、柜台显示、高清渲染、对话框位置、诊断都问它。
//   四样东西一起定：用哪个来源、谁来画、多大（缩放与轴心）、走哪条渲染路。
//   谁来画（Mode）：
//     Vanilla    不归框架管（不认识的键、没选别的来源）：原样走游戏
//     Yield      选中的来源正好是立绘表里现在那张的主人（别的 mod 直接改了表，例如自带界面层叠的高清包）：整张让出，
//                它的尺寸、它的清晰显示方式都由它自己负责，框架不碰
//     Original   玩家选了游戏原图，但表被别的 mod 改过：给真正的原图
//     Framework  框架显示选中来源的图：画布对齐原图，再乘来源的缩放（作者写的，或按内容占比自动适配），底边贴齐
//   渲染路（只对 Framework）：像素风来源走原版像素画布；其余标记为高清，由 HdRender 在它上柜台时让店面按屏幕分辨率画。
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TextureExtend
{
    public enum PlanMode { Vanilla, Yield, Original, Framework }

    public struct Plan
    {
        public PlanMode Mode;
        /// <summary>选中的来源（"game" 或 mod ID）；不归框架管时为 null。</summary>
        public string Source;
        /// <summary>立绘表里现在那张是谁放的（"game"、mod ID、Originals.TableSource）；不是表里的键为 null。</summary>
        public string TableOwner;
        /// <summary>相对“画布对齐原图”的缩放（Framework 才有意义）。</summary>
        public float Scale;
        /// <summary>像素风：点采样、走原版渲染路。</summary>
        public bool Pixel;

        /// <summary>立绘表的键（来客的 spriteName）现在怎么显示。</summary>
        public static Plan ForKey(string key)
        {
            var plan = new Plan { Mode = PlanMode.Vanilla, Scale = 1f };
            if (string.IsNullOrEmpty(key) || key.StartsWith(Display.KeyPrefix, StringComparison.Ordinal)) return plan;   // 框架自己登记的显示键：已经是成品
            Portrait p = PortraitLibrary.Get(key);
            if (p == null || !p.InGameTable) return plan;
            plan.Source = p.Source;
            plan.TableOwner = Originals.OwnerOfCurrent(key);
            if (p.Source == plan.TableOwner) { plan.Mode = p.Source == PortraitLibrary.GameSource ? PlanMode.Vanilla : PlanMode.Yield; return plan; }
            if (p.Source == PortraitLibrary.GameSource) { plan.Mode = PlanMode.Original; return plan; }
            return Framework(plan, p.Source);
        }

        /// <summary>框架直接显示某张立绘（Display.Get / ShowOnCounter：调用方点名要这张，不存在让出）。</summary>
        public static Plan ForPortrait(string portraitId)
        {
            var plan = new Plan { Mode = PlanMode.Framework, Scale = 1f, Source = PortraitLibrary.SourceOf(portraitId) };
            return Framework(plan, plan.Source);
        }

        private static Plan Framework(Plan plan, string source)
        {
            plan.Mode = PlanMode.Framework;
            plan.Scale = Fit.ScaleOf(source);
            plan.Pixel = Packs.PixelOf(source);
            return plan;
        }

        /// <summary>按方案算 Sprite 的每单位像素与轴心。reference = 对齐的原图（没有时按 100 像素 / 单位、底边居中）。</summary>
        public void Place(Texture2D tex, Sprite reference, out float pixelsPerUnit, out Vector2 pivot)
        {
            pixelsPerUnit = 100f;
            pivot = new Vector2(0.5f, 0f);
            if (reference != null && reference.rect.height > 0f)
            {
                pixelsPerUnit = tex.height / (reference.rect.height / reference.pixelsPerUnit);   // 画布与原图同高
                pivot = new Vector2(reference.pivot.x / reference.rect.width, reference.pivot.y / reference.rect.height);
            }
            float scale = Scale > 0f ? Scale : 1f;
            pixelsPerUnit /= scale;
            pivot.y = FitMath.PivotY(pivot.y, scale);   // 底边仍贴着原图画布的底边
        }

        public override string ToString()
        {
            return Mode + (Source != null ? " · source " + Source : "") + (TableOwner != null ? " · table " + TableOwner : "")
                + (Mode == PlanMode.Framework ? " · scale " + Scale.ToString("0.00") + (Pixel ? " · pixel" : " · hd") : "");
        }
    }

    /// <summary>游戏取立绘的入口（SpriteDict.GetSprite）按显示方案给图。配置项 visitorPortraits 可关。</summary>
    internal static class PlanPort
    {
        internal const string Name = "SpriteDict.GetSprite";
        internal static bool Enabled = true;

        internal static void Install()
        {
            Ports.Patch(Name, "来客等处按选中的来源显示立绘", () => AccessTools.Method(typeof(SpriteDict), nameof(SpriteDict.GetSprite)), prefix: new HarmonyMethod(typeof(PlanPort), nameof(Prefix)));
            Ports.Read("SpriteDict original entries", "原图基准（尺寸对齐、游戏原图来源）", () => SpriteDict.Instance == null ? null : Originals.Count > 0 ? null : "no original entries found");
        }

        private static bool Prefix(SpriteDict __instance, string key, ref Sprite __result)
        {
            try
            {
                if (!Painter.Running || Painter.Retired || string.IsNullOrEmpty(key)) return true;
                // ClaudeNote: 0.6.1 自检：texext_ 显示键是登记进立绘表的，但立绘表换场景 / 读档会重建，登记就没了；
                //   来客的 spriteName 却可能随存档留着这个键 → 原版找不到键，会退回随机拾荒者。这里认出键就现取，不依赖登记还在不在。
                if (key.StartsWith(Display.KeyPrefix, StringComparison.Ordinal))
                {
                    Dictionary<string, Sprite> dict = __instance != null ? __instance.spriteDictionary : null;
                    if (dict == null || dict.ContainsKey(key)) return true;
                    string portrait = Display.PortraitOfKey(key, out string state);
                    Sprite again = portrait != null ? Display.Get(portrait, state) : null;
                    if (again == null) return true;
                    __result = again;
                    return false;
                }
                if (!Enabled) return true;
                // ClaudeNote: 0.8.3 自动动态适配（Core/RootJoin.cs）：游戏抽到的键不变，只换显示哪张。对齐的还是 key 这张原图
                string joined = RootJoin.StateFor(key);
                if (joined == RootJoin.Vanilla) { Sprite og = Originals.Get(key); if (og == null) return true; __result = og; return false; }
                Sprite s = joined != null ? Display.Get(key, joined) : null;
                Plan plan = Plan.ForKey(key);
                if (s == null) s = plan.Mode == PlanMode.Framework ? Display.Get(key) : plan.Mode == PlanMode.Original ? Originals.Get(key) : null;
                if (s == null) return true;
                __result = s;
                return false;
            }
            catch (Exception) { return true; }   // 绝不让异常进游戏的显示流程
        }
    }
}
