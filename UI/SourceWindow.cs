// ClaudeNote: 第 4 步——来源设置窗口（选图窗口里的“来源设置”按钮，或 F9 Modding 面板）。
//   从上到下：全局顺序（排在前面的来源先用；没排进来的按包的优先级）、按组 / 标签指定、按角色指定过的（在选图窗口里指定）、只读立绘包的开关。
//   改动即时存进 ModData，立绘库重选，后台按新来源重画。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureExtend
{
    internal static class SourceWindow
    {
        private const string WindowId = "texext_sources";
        private static int groupIndex, sourceIndex;

        private static string T(string zh, string en) { return Entry.Chinese ? zh : en; }

        internal static void Close()
        {
            try { CustomUIManager.Instance?.CloseWindow(WindowId); } catch (Exception) { }
        }

        internal static void Open()
        {
            try { Build(); }
            catch (Exception ex) { Entry.Warn("source window failed: " + ex.Message); }
        }

        private static void Later() { ModHelper.Delay(Painter.ModId, 0.1f, Open); }

        private static void Build()
        {
            if (CustomUIManager.Instance == null) return;
            CustomUIWindow old = CustomUIManager.Instance.GetWindow(WindowId);
            Vector2? at = old != null && old.IsAlive ? old.Rect.anchoredPosition : (Vector2?)null;
            List<string> all = Sources.All();
            List<string> order = Sources.Order;

            CustomUIBuilder ui = CustomUIManager.Instance.CreateWindow(WindowId, T("立绘来源设置", "Portrait sources"))
                .SetSize(560f, 640f).SetDraggable(true).SetCloseOnEscape();
            ui.BeginScroll(570f);

            ui.AddHeader(T("全局顺序", "Global order"));
            ui.AddLabel(T("排在前面的来源先用。没排进顺序的，按立绘包声明的优先级，同级时后载入的先，游戏原图最后。",
                          "Sources higher in the list win. Unlisted ones follow pack priority, then load order; the game's own art comes last."));
            foreach (string src in all)
            {
                string s = src;
                int pos = order.IndexOf(s);
                ui.BeginRow(6f);
                ui.AddLabel((pos >= 0 ? (pos + 1) + ". " : T("（默认）", "(default) ")) + Sources.Label(s));
                ui.Last?.SetFixedWidth(300f);
                ui.AddButton("↑", () => { Sources.Move(s, -1); Later(); });
                ui.AddButton("↓", () => { Sources.Move(s, +1); Later(); });
                if (pos >= 0) ui.AddButton(T("移出", "Unlist"), () => { Sources.Unorder(s); Later(); });
                ui.End();
            }

            ui.AddSeparator();
            ui.AddHeader(T("按组指定", "By group"));
            List<string[]> groups = Sources.Groups;
            if (groups.Count == 0) ui.AddLabel(T("（没有）", "(none)"));
            foreach (string[] g in groups)
            {
                string tag = g[0];
                ui.BeginRow(6f);
                ui.AddLabel("@" + g[0] + "  →  " + Sources.Label(g[1]));
                ui.Last?.SetFixedWidth(400f);
                ui.AddButton("✕", () => { Sources.ChooseForGroup(tag, null); Later(); });
                ui.End();
            }
            List<string> tagOptions = TagOptions();
            groupIndex = Mathf.Clamp(groupIndex, 0, Mathf.Max(0, tagOptions.Count - 1));
            sourceIndex = Mathf.Clamp(sourceIndex, 0, Mathf.Max(0, all.Count - 1));
            var sourceNames = new List<string>();
            foreach (string src in all) sourceNames.Add(Sources.Label(src));
            if (tagOptions.Count > 0)
            {
                ui.BeginRow(6f);
                ui.AddDropdown(tagOptions, groupIndex, i => groupIndex = i);
                ui.Last?.SetFixedWidth(180f);
                ui.AddDropdown(sourceNames, sourceIndex, i => sourceIndex = i);
                ui.Last?.SetFixedWidth(220f);
                ui.AddButton(T("添加", "Add"), () =>
                {
                    if (groupIndex < tagOptions.Count && sourceIndex < all.Count) Sources.ChooseForGroup(tagOptions[groupIndex], all[sourceIndex]);
                    Later();
                });
                ui.End();
            }

            ui.AddSeparator();
            ui.AddHeader(T("按角色指定", "By character"));
            ui.AddLabel(T("在立绘道具的右键“选图”里，左栏上面的“来源”下拉框指定。", "Set from the Source dropdown in an item's Pick image window."));
            Dictionary<string, string> mine = Sources.PerPortrait;
            if (mine.Count == 0) ui.AddLabel(T("（没有）", "(none)"));
            foreach (KeyValuePair<string, string> kv in mine)
            {
                string id = kv.Key;
                ui.BeginRow(6f);
                ui.AddLabel(id + "  →  " + Sources.Label(kv.Value));
                ui.Last?.SetFixedWidth(400f);
                ui.AddButton("✕", () => { Sources.ChooseFor(id, null); Later(); });
                ui.End();
            }

            // ClaudeNote: 0.6.0 只读立绘包：订阅了但没启用的立绘 mod，只读图片、代码不运行（Compat/PackShelf.cs）
            ui.AddSeparator();
            ui.AddHeader(T("只读立绘包（没启用的 mod）", "Read-only packs (inactive mods)"));
            ui.AddLabel(T("只读图片，代码不运行。没激活的只当候选素材（可以点名用）；激活后和启用的 mod 一样参加自动挑选。",
                          "Images only; the mod's code stays inactive. Inactive packs are candidates you can pick by hand; active ones join the automatic choice."));
            List<PackShelf.Shelf> shelf = PackShelf.List();
            if (shelf.Count == 0) ui.AddLabel(T("（没有认得出的包。文件任意命名的包需要装伴生 mod「TextureExtend 立绘包读取」）",
                                                "(none recognised; packs with free-form file names need the companion mod)"));
            foreach (PackShelf.Shelf pack in shelf)
            {
                string id = pack.ModId;
                bool on = Sources.PackOn(id);
                ui.BeginRow(6f);
                ui.AddLabel((on ? "● " : "○ ") + pack.Name + (pack.Count >= 0 ? "  (" + pack.Count + ")" : "") + "  · " + pack.Kind);
                ui.Last?.SetFixedWidth(400f);
                ui.AddButton(on ? T("取消激活", "Deactivate") : T("激活", "Activate"), () => { PackShelf.Set(id, !on); Later(); });
                ui.End();
            }

            ui.AddSeparator();
            ui.AddButton(T("全部恢复默认", "Reset all"), () => { Sources.ResetAll(); Later(); });
            ui.End();

            if (at.HasValue) ui.SetPosition(at.Value); else ui.Center();
            ClickShield.Guard(ui.Show()?.Rect);   // 0.7.3 点击不穿透
        }

        // 能按组指定的标签：派系、性别在前，其余按字母
        private static List<string> TagOptions()
        {
            var seen = new HashSet<string>();
            foreach (Portrait p in PortraitLibrary.All) foreach (string t in p.Tags) seen.Add(t);
            var first = new List<string>();
            foreach (string t in new[] { "female", "male", "lower", "upper", "scav", "rev", "bm", "sec" }) if (seen.Remove(t)) first.Add(t);
            var rest = new List<string>(seen);
            rest.Sort(string.CompareOrdinal);
            first.AddRange(rest);
            return first;
        }
    }
}
