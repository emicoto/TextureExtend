// ClaudeNote: 昼夜（Lune 2026-11-16 定：按关门节点判）。游戏没有时段概念，这里规定：
//   关门（打烊）之后 → 夜晚；起床、读档（读档总在早上）→ 白天。
//   内置一条最低权重（−10）的规则：立绘或道具有 night 状态的，夜晚自动切过去；白天撤回。别的规则成立时它让路。
//   对外：DayNight.IsNight（第 2 步的条件表达式也会用它）。
namespace TextureExtend
{
    public static class DayNight
    {
        internal const string Owner = "TextureExtend.DayNight", NightTag = "night";
        private static bool night;

        /// <summary>现在是不是夜晚（打烊后到第二天起床前）。</summary>
        public static bool IsNight => night;

        // 要在统一重算之前订阅（Entry 里先装它），重算时才看得到新的昼夜
        internal static void Install()
        {
            ModHook.OnShutterClosedLate += ToNight;
            ModHook.OnWakingUpLate += ToDay;
            ModHook.OnGameLoadedInit += ToDay;
            Painter.For(Owner, PaintTarget.Portrait, "*").Tag(NightTag).Only(HasNightPortrait).When(_ => night).Weight(-10);
            Painter.For(Owner, PaintTarget.StaticItem, "*").Tag(NightTag).Only(i => PaintRules.HasItemVariant(i?.identifier, NightTag)).When(_ => night).Weight(-10);
        }

        internal static void Uninstall()
        {
            ModHook.OnShutterClosedLate -= ToNight;
            ModHook.OnWakingUpLate -= ToDay;
            ModHook.OnGameLoadedInit -= ToDay;
            Painter.RemoveRules(Owner);
        }

        private static void ToNight() { night = true; }
        private static void ToDay() { night = false; }

        private static bool HasNightPortrait(GameItem item)
        {
            string id = Painter.PortraitOf(item);
            return id != null && (PortraitLibrary.Get(id)?.HasVariant(NightTag) ?? false);
        }
    }
}
