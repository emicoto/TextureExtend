// ClaudeNote: 2026-11-16 TextureExtend 调试面板。原版 F9 的 Modding 面板只留一个“打开调试面板”按钮，调试项全部收在这里。
//   分页 = 登记时给的页名（框架自己的按钮在“框架”页；别的 mod 经 Painter.DebugButton 登记，各自一页）。
//   点按钮后：动作期间框架写的日志（Entry.Info / Warn）收集起来显示在下方输出区，同时照常写进日志。
//   面板不在按钮回调里重建自己（原版窗口在自己的点击回调里重建会吞掉后续点击），输出区按元素 id 原地改字。
using System;
using System.Collections.Generic;
using System.Text;

namespace TextureExtend
{
    internal static class DevPanel
    {
        private const string WindowId = "texext_devpanel";
        private const string OutputId = "out";
        internal const string FramePage = "框架";

        private sealed class DebugEntry
        {
            internal string owner, page, name;
            internal Action run;
        }

        private static readonly List<DebugEntry> entries = new List<DebugEntry>();
        private static StringBuilder capture;   // 非空 = 正在执行某个按钮，框架日志收进来

        private static string T(string zh, string en) { return Entry.Chinese ? zh : en; }

        // ── 登记 ──

        internal static void Add(string owner, string page, string name, Action run)
        {
            if (string.IsNullOrEmpty(name) || run == null) return;
            entries.Add(new DebugEntry { owner = owner ?? "", page = string.IsNullOrEmpty(page) ? owner : page, name = name, run = run });
        }

        internal static void RemoveOwner(string owner) { entries.RemoveAll(e => e.owner == owner); }

        /// <summary>原版 F9 面板上只挂这一个按钮。</summary>
        internal static void RegisterF9()
        {
            try { ModHelper.RemoveDebugFunctions(Painter.ModId); } catch (Exception) { }
            ModHelper.AddDebugFunction(Painter.ModId, "Modding", T("[TextureExtend] 打开调试面板", "[TextureExtend] Open debug panel"), Open);
        }

        internal static void Clear()
        {
            entries.Clear();
            Close();
            try { ModHelper.RemoveDebugFunctions(Painter.ModId); } catch (Exception) { }
        }

        // ── 面板 ──

        internal static void Close() { CustomUIManager.Instance?.CloseWindow(WindowId); }

        internal static void Open()
        {
            if (CustomUIManager.Instance == null) return;
            Close();
            CustomUIBuilder b = CustomUIManager.Window(WindowId, T("TextureExtend 调试面板", "TextureExtend Debug"))
                .SetSize(620f, 600f).Center().SetDraggable(true).SetCloseOnEscape();

            b.BeginTabs();
            foreach (string page in Pages())
            {
                b.BeginTab(page == FramePage ? T("框架", "Framework") : page);
                b.BeginScroll(330f);
                b.BeginGrid(2, 286f, 34f);
                foreach (DebugEntry e in entries)
                {
                    if (e.page != page) continue;
                    DebugEntry x = e;
                    b.AddButton(x.name, () => Run(x));
                }
                b.End();
                b.End();
                b.End();
            }
            b.EndTabs();

            b.AddSeparator();
            b.BeginScroll(130f);
            b.AddLabel(T("点按钮后，结果显示在这里（同时写进日志）。", "Results show here (and in the log)."), OutputId);
            b.End();
            ClickShield.Guard(b.Show()?.Rect);   // 0.7.3 点击不穿透
        }

        // 页签顺序：框架页在前，其余按登记顺序
        private static List<string> Pages()
        {
            var pages = new List<string> { FramePage };
            foreach (DebugEntry e in entries) if (!pages.Contains(e.page)) pages.Add(e.page);
            return pages;
        }

        private static void Run(DebugEntry e)
        {
            capture = new StringBuilder();
            bool failed = false;
            try { e.run(); }
            catch (Exception ex)
            {
                failed = true;
                Entry.Warn("debug \"" + e.name + "\" failed: " + ex);
            }
            string got = capture.ToString().TrimEnd();
            capture = null;
            // 游戏字体没有 ✔ ✘，用 √ ×
            Show((failed ? "× " : "√ ") + e.name + (got.Length > 0 ? "\n" + got : ""));
        }

        /// <summary>Entry.Info / Warn 调用：面板正在执行按钮时，把这行也收进输出区。</summary>
        internal static void Capture(string line)
        {
            if (capture == null) return;
            if (capture.Length > 4000) return;   // 太长的（立绘库全表之类）只看日志
            capture.AppendLine(line);
        }

        internal static void Show(string text)
        {
            CustomUIManager.Instance?.GetWindow(WindowId)?.Get(OutputId)?.SetText(text);
        }
    }

    public static partial class Painter
    {
        /// <summary>往 TextureExtend 调试面板登记一个按钮（不进原版 F9 面板）。page 为空时用 modId 当页名。卸载时调 RemoveDebugButtons。</summary>
        public static void DebugButton(string modId, string page, string name, Action run) { DevPanel.Add(modId, page, name, run); }

        /// <summary>撤掉这个 mod 登记的全部调试按钮。</summary>
        public static void RemoveDebugButtons(string modId) { DevPanel.RemoveOwner(modId); }
    }
}
