// ClaudeNote: 框架自带的 lewd_meme 轻量兼容层（Lune 2026-11-16 定）：只做一件事——按好感给少女道具换情绪立绘，只在过夜处理时判定。
//   更细的互动（信任、催眠、淫纹、调教后的即时变化…）交给扩展 mod 自己管理，用规则与 Painter.Refresh 通知框架。
//   启用条件：lewd_meme 已启用，且 TextureExtend 配置里“lewd_meme 好感立绘”开着（默认开）。
//   做法：
//     · 识别钩子：带 MAIDEN_FEMALE 标签的物品 → 立绘 ID = 它的 MAIDEN_SPRITE_SRC（lewd_meme 记的原版立绘字段）。
//     · 过夜处理后：按 MAIDEN_AFFECTION 算一档，写进物品标签 PEF_LM_MOOD，然后重算这件物品。
//         ≤ −60 angry · ≤ −20 unhappy · ≥ 60 delighted · ≥ 20 happy · 其余无（显示高清本体）
//     · 四条规则（权重 1，扩展 mod 的规则权重更高就会盖过它）只认这个标签，所以白天的检查点不会让它变来变去。
//   识别钩子设为“认出就接管”：少女道具一律由框架画高清图（0.4.2，Lune：退回 lewd_meme 原图会变糊）。
//   某档的情绪图没有、或哪档都不是 → 显示高清本体；兼容层关掉后，下一个重算点还原 lewd_meme 原图。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    internal static class LewdMemeCompat
    {
        internal const string Owner = "TextureExtend.Compat.LewdMeme";
        private const string BaseModId = "lewd_meme";
        // ClaudeNote: 1.0.0 存档标签前缀由 TEXEXT_ 改为 PEF_（发布前定名，不做旧存档迁移）。
        internal const string MoodTag = "PEF_LM_MOOD";
        private static readonly string[] Moods = { "angry", "unhappy", "delighted", "happy" };
        private static bool active;

        internal static bool Active => active;

        /// <summary>按当前条件开 / 关兼容层（资源载入后、配置改动时调）。</summary>
        internal static void Apply(bool wanted)
        {
            bool should = wanted && BaseEnabled();
            if (should == active) return;
            active = should;
            Painter.RemoveRules(Owner);
            ModHook.OnHandlingNightlyServicesLate -= Overnight;
            if (!active) return;
            Painter.AddPortraitResolver(Owner, i => IsMaiden(i) ? Str(i, "MAIDEN_SPRITE_SRC") : null, true);   // 认出就接管成高清图
            Painter.DynamicTexture(Owner, IsMaiden);   // 0.8.0 少女的立绘是抓到时才定的：选图窗口直接给整个图库
            foreach (string mood in Moods)
            {
                string m = mood;
                Painter.For(Owner, PaintTarget.Portrait, "*").Tag(m).Only(IsMaiden).When(i => Str(i, MoodTag) == m).Weight(1);
            }
            ModHook.OnHandlingNightlyServicesLate += Overnight;
            Entry.Info("lewd_meme compat on: maiden portraits follow affection overnight");
        }

        // 过夜处理后：每个少女重新算一档，档变了才重算。调试面板也有一个按钮直接跑它。
        internal static void Overnight()
        {
            if (!PlayerStore.HasInstance || Painter.Retired) return;
            int changed = 0;
            foreach (GameItem item in Items.All())
            {
                if (!IsMaiden(item)) continue;
                string mood = Tier(Int(item, "MAIDEN_AFFECTION"));
                if (mood == (Str(item, MoodTag) ?? "")) continue;
                if (mood.Length > 0) item.ModifyTag(MoodTag, s => { s.Enable(); s.SetString(mood); });
                else item.ModifyTag(MoodTag, s => s.Disable());
                Painter.Refresh(item);
                changed++;
            }
            SourceReader.Release();
            if (changed > 0) Entry.Info("lewd_meme compat: " + changed + " maiden mood(s) changed overnight");
        }

        private static string Tier(int affection)
        {
            if (affection <= -60) return "angry";
            if (affection <= -20) return "unhappy";
            if (affection >= 60) return "delighted";
            if (affection >= 20) return "happy";
            return "";
        }

        private static bool BaseEnabled()
        {
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == BaseModId);
            return mod != null && mod.IsEnabled;
        }

        private static bool IsMaiden(GameItem item) { return item != null && item.IsTag("MAIDEN_FEMALE"); }

        private static int Int(GameItem item, string tag) { return item.IsTag(tag) ? item.GetTagReadonly(tag)?.GetInt() ?? 0 : 0; }

        private static string Str(GameItem item, string tag) { return item.IsTag(tag) ? item.GetTagReadonly(tag)?.GetString() : null; }
    }
}
