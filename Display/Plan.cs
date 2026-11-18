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

namespace ProbablyExtendFramework
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
            return Framework(plan, p.Source, key);
        }

        /// <summary>框架直接显示某张立绘（Display.Get / ShowOnCounter：调用方点名要这张，不存在让出）。</summary>
        public static Plan ForPortrait(string portraitId)
        {
            var plan = new Plan { Mode = PlanMode.Framework, Scale = 1f, Source = PortraitLibrary.SourceOf(portraitId) };
            return Framework(plan, plan.Source, portraitId);
        }

        private static Plan Framework(Plan plan, string source, string portraitId)
        {
            plan.Mode = PlanMode.Framework;
            plan.Scale = Packs.ImageScaleOf(source, portraitId) ?? Fit.ScaleOf(source);   // 1.0.0 rc9 单张图写了大小就用它
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
            // ClaudeNote: 0.9.6 框架的裁决挪到后置、排在所有 mod 之后（见 Postfix 的说明）；前置只剩“显示键现取”这一件事
            Ports.Patch(Name, "来客等处按选中的来源显示立绘", () => AccessTools.Method(typeof(SpriteDict), nameof(SpriteDict.GetSprite)),
                prefix: new HarmonyMethod(typeof(PlanPort), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(PlanPort), nameof(Postfix)) { priority = Priority.Last });
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
                // ClaudeNote: 0.9.12 立绘池里追加的新立绘（Core/PortraitPools.cs）：键不在游戏立绘表里，原版会报“找不到”并退回随机拾荒者。认出来就现取。
                if (Enabled && __instance != null && __instance.spriteDictionary != null && !__instance.spriteDictionary.ContainsKey(key) && PortraitPools.IsExtra(key))
                {
                    string extraState = Counter.StateFor(key);   // 1.0.0 rc6 自定义 NPC 也按意向 / 规则 / 来客事件定差分
                    Sprite extra = (extraState != null ? Display.Get(key, extraState) : null) ?? Display.Get(key);
                    if (extra == null) return true;
                    __result = extra;
                    return false;
                }
                return true;
            }
            catch (Exception) { return true; }   // 绝不让异常进游戏的显示流程
        }

        // ClaudeNote: 0.9.6 Lune：“柜台来客缺手缺胳膊”，怀疑是自动适配改来客逻辑之后出现的——是的，但错在更早的结构上：
        //   框架原来在前置里就把图换掉。白蛇娘化这类 mod 是在同一个入口的后置里换图的，而且拿“当时的返回值”当原图来算自己那张的大小和轴心。
        //   它拿到的是框架的图（被自动适配缩过、轴心跟着换算过），于是它自己的立绘按错的轴心建出来并缓存：整张往下沉进柜台后面，
        //   手和胳膊被柜台挡掉（实测：截图里的图与白蛇原图逐像素同大，只是下沉了画布高的 17%，正好是缩放 0.75 时的轴心偏移）。
        //   现在：原版和别的 mod 先走完（它们看到的是真正的原图），框架最后才按显示方案决定要不要换。
        //   选中的来源是“图打包在 DLL 里的 mod”、而且那个 mod 已经在这条链上自己换了图：让给它（Yield），大小、像素风格都是它自己的。
        private static void Postfix(string key, ref Sprite __result)
        {
            try
            {
                if (!Enabled || !Painter.Running || Painter.Retired || string.IsNullOrEmpty(key)) return;
                if (key.StartsWith(Display.KeyPrefix, StringComparison.Ordinal)) return;
                // ClaudeNote: 0.8.3 自动动态适配（Core/RootJoin.cs）：游戏抽到的键不变，只换显示哪张。对齐的还是 key 这张原图
                string joined = RootJoin.StateFor(key);
                if (joined == RootJoin.Vanilla) { Sprite og = Originals.Get(key); if (og != null) __result = og; return; }
                Sprite s = joined != null ? Display.Get(key, joined) : null;
                if (s == null)
                {
                    // ClaudeNote: 1.0.0 rc6 这个键现在该用哪个差分（意向 > 规则 > 来客事件，见 Display/Counter.cs）
                    string state = Counter.StateFor(key);
                    if (state != null) s = Display.Get(key, state);
                }
                if (s == null)
                {
                    Plan plan = Plan.ForKey(key);
                    if (plan.Mode == PlanMode.Original) s = Originals.Get(key);
                    else if (plan.Mode == PlanMode.Framework && !ShownByItsOwner(key, __result)) s = Display.Get(key);
                }
                if (s != null) __result = s;
            }
            catch (Exception) { }   // 绝不让异常进游戏的显示流程
        }

        // 选中的那张是某个 mod 打包在自己 DLL 里的图，而链上已经有人把原图换掉了：就是那个 mod 自己在显示
        private static bool ShownByItsOwner(string key, Sprite incoming)
        {
            if (incoming == null || ReferenceEquals(incoming, Originals.Get(key))) return false;
            string sig = PortraitLibrary.FileSignature(key, null);
            return sig != null && sig.StartsWith("res|", StringComparison.Ordinal);
        }
    }
}
