// ClaudeNote: 选图窗口（右键菜单最下面的“选图”）。用游戏自带的 CustomUI 搭，不另做界面系统。
//   布局（Lune 2026-11-16 定）：
//     左栏 = 这件物品可选的状态（立绘：自动 / 本体 / 各差分；静态道具：原版外观 / 自动 / 本体 / 各状态），约屏幕 20% 宽、最少 260，带滚动条。
//     右栏（只有立绘道具有），约屏幕 45% 宽、最少 400。0.8.0（Lune 2026-11-18）分两种：
//            同 ID 贴图：这个图 ID 在各个来源里的全部图（来源:图ID~编号）+“自动”。一般道具默认看这个——换的是“用谁画的这张”。
//            其他更多：  整个图库（第一行 来源筛选 + 标签组筛选；第二行 搜索 + 翻页），换的是“换成哪个角色”。
//            动态贴图道具（[DynamicTexture]，如 lewd_meme 少女：图是生成时才定的）默认直接看整个图库。右上角的按钮在两种之间切。
//   亲手选了状态 → 物品加锁（TEXEXT_LOCK），规则不再动它；选“自动”解锁并立刻按规则重算。换立绘不加锁（规则照样管新立绘的状态）。
//   选择结果写在物品自己的标签里，随存档走。禁止手动切换的目标不出“选图”按钮（Picker.HasOptions）。
//   缩略图就是道具成品图（Painter.Get）：缓存里有的直接读；没画过的高清图第一次翻到那一页时现画（每张几十毫秒），所以每页只放 24 张。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyExtendFramework
{
    internal static class Picker
    {
        private const string WindowId = "texext_picker";
        private const int PerPage = 24;
        private const float Thumb = 110f, CellW = 124f, CellH = 168f, Gap = 6f;
        private const float LeftShare = 0.20f, LeftMin = 260f, RightShare = 0.45f, RightMin = 400f;

        private static GameItem target;
        private static int page, sourceIndex, tagIndex;
        private static bool more, moreSet;   // 右栏看整个图库（false = 只看同 ID 的贴图）；moreSet = 玩家这次手动切过
        private static string search = "", typing = "";
        private static float uiScale = 1f;   // 画布缩放：屏幕像素 → 界面单位（上一次打开时量到的）

        /// <summary>这件物品右键要不要出现“选图”。禁止手动切换的目标不出。</summary>
        internal static bool HasOptions(GameItem item)
        {
            if (item == null || Painter.Retired) return false;   // 做过卸载前清理：不再出“选图”
            string portrait = Painter.PortraitOf(item);
            if (portrait != null) return PaintRules.ManualAllowed(PaintTarget.Portrait, portrait);
            if (!PaintRules.ManualAllowed(PaintTarget.StaticItem, item.identifier)) return false;
            return PaintRules.HasNativeLooks(item) || PaintRules.StaticOptions(item).Count > 0;
        }

        internal static void Open(GameItem item)
        {
            if (item == null || CustomUIManager.Instance == null) return;
            if (!ReferenceEquals(item, target)) { page = 0; sourceIndex = 0; tagIndex = 0; search = typing = ""; moreSet = false; }
            if (!moreSet) more = PaintRules.IsDynamic(item);
            target = item;
            Build();
        }

        private static string T(string zh, string en) { return Entry.Chinese ? zh : en; }

        /// <summary>关窗并放开对物品的引用（停用、读档时调）。</summary>
        internal static void Close()
        {
            target = null;
            try { CustomUIManager.Instance?.CloseWindow(WindowId); } catch (Exception) { }
            Painter.ReleasePreviews(null);
        }

        // ClaudeNote: 0.6.3 窗口正显示着的缩略图（见 Painter.Preview）。重排后放掉上一页的；玩家关窗（按钮 / Esc）时全放。
        private static HashSet<string> showing = new HashSet<string>();
        private static bool building;

        private static void Closed()
        {
            if (building) return;   // 重排时旧窗被新窗顶掉（CreateWindow 会先关掉同名旧窗），不算关
            target = null;
            showing.Clear();
            Painter.ReleasePreviews(null);
        }

        private static void Build()
        {
            if (target == null) return;
            CustomUIWindow old = CustomUIManager.Instance.GetWindow(WindowId);
            Vector2? at = old != null && old.IsAlive ? old.Rect.anchoredPosition : (Vector2?)null;
            string portrait = Painter.PortraitOf(target);
            building = true;
            showing = new HashSet<string>();

            float left = Mathf.Max(LeftMin, Screen.width * LeftShare / uiScale);
            float right = portrait != null ? Mathf.Max(RightMin, Screen.width * RightShare / uiScale) : 0f;
            float height = Mathf.Max(480f, Screen.height * 0.75f / uiScale);
            CustomUIBuilder ui = CustomUIManager.Instance.CreateWindow(WindowId, T("选图 · ", "Pick image · ") + target.name + "   (v" + Entry.Version + ")")
                .SetSize(left + right + (right > 0f ? 36f : 24f), height).SetDraggable(true).SetCloseOnEscape().OnClose(Closed);
            try
            {
                ui.BeginRow(12f);
                ui.BeginPanel(left, height - 60f, TextAnchor.UpperLeft, 4f);
                if (portrait != null) BuildStates(ui, portrait, left, height - 60f);
                else BuildStatic(ui, left, height - 60f);
                ui.End();
                if (portrait != null && PaintRules.ManualAllowed(PaintTarget.Portrait, portrait))
                {
                    ui.BeginPanel(right, height - 60f, TextAnchor.UpperLeft, 4f);
                    ui.BeginRow(6f);
                    ui.AddLabel(more ? T("全部贴图", "All images") : T("同 ID 贴图 · ", "Same id · ") + portrait);
                    ui.Last?.SetFixedWidth(Mathf.Max(120f, right - 170f));
                    ui.AddButton(more ? T("[同 ID 贴图]", "[Same id]") : T("[其他更多]", "[More…]"), () => { more = !more; moreSet = true; page = 0; Build(); });
                    ui.End();
                    if (more) BuildLibrary(ui, portrait, right, height - 96f);
                    else BuildSameId(ui, portrait, right, height - 96f);
                    ui.End();
                }
                ui.End();
            }
            catch (Exception ex) { ui.AddLabel("error: " + ex.Message); Entry.Warn("picker failed: " + ex); }
            if (at.HasValue) ui.SetPosition(at.Value); else ui.Center();
            CustomUIWindow shown = ui.Show();
            ClickShield.Guard(shown?.Rect);   // 0.7.3 点击不穿透
            building = false;
            Painter.ReleasePreviews(showing);   // 上一页的缩略图
            SourceReader.Release();
            Measure(shown);
        }

        // 量一下画布缩放；和上次假设的差得多就按新的重排一次（第一次打开时会发生）
        private static void Measure(CustomUIWindow window)
        {
            Canvas canvas = window?.Rect != null ? window.Rect.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            if (Mathf.Abs(scale - uiScale) / uiScale < 0.1f) return;
            uiScale = scale;
            Build();
        }

        private static int ColumnsFor(float width) { return Mathf.Max(1, (int)((width - 24f) / (CellW + Gap))); }

        // ── 左栏：立绘的状态 ──

        private static void BuildStates(CustomUIBuilder ui, string portrait, float width, float height)
        {
            PaintId now = Painter.SourceOf(target);
            int cells = now.Valid ? now.Cells : Mathf.Clamp(target.shape != null ? Mathf.Max(target.shape.width, target.shape.height) : 4, 1, 8);
            string current = now.Valid && now.Group == Painter.PortraitGroup ? now.Variant ?? "" : null;
            bool locked = PaintRules.IsLocked(target);
            Portrait p = PortraitLibrary.Get(portrait);

            ui.AddLabel(portrait + (locked ? T("  ·  已锁定", "  ·  locked") : ""));
            // ClaudeNote: 第 4 步 这张立绘用哪个来源（玩家按角色指定；“自动”= 按组、全局顺序、包的优先级）
            if (p != null && p.candidates.Count > 0)
            {
                var srcs = p.Sources;
                var names = new List<string> { T("来源：自动", "Source: auto") + " (" + Sources.Label(p.Source) + ")" };
                foreach (string src in srcs) names.Add(Sources.Label(src));
                string chosen = Sources.Chosen(portrait);
                int at = chosen != null ? srcs.IndexOf(chosen) + 1 : 0;
                ui.BeginRow(6f);
                ui.AddDropdown(names, Mathf.Max(0, at), i =>
                {
                    Sources.ChooseFor(portrait, i <= 0 ? null : srcs[i - 1]);
                    ModHelper.Delay(Painter.ModId, 0.1f, Build);   // 立绘库重选之后再重排
                });
                ui.Last?.SetFixedWidth(Mathf.Max(120f, width - 110f));
                ui.AddButton(T("来源设置", "Sources"), SourceWindow.Open);
                ui.End();
            }
            ui.BeginScroll(height - 76f);
            ui.BeginGrid(ColumnsFor(width), CellW, CellH, Gap);
            Cell(ui, null, T("自动（按规则）", "Auto (rules)"), !locked, () => { PaintRules.SetLocked(target, false); Painter.Refresh(target); Build(); });
            // 0.8.0 状态按这件物品正用着的那个来源列（点名了别的来源时，它的差分和“当前选中来源”的可能不一样）
            string image = current != null ? now.Key : portrait;
            var variants = new List<string> { "" };
            var states = new List<string>(PortraitLibrary.DeclaredVariants);
            if (p != null) foreach (string v in p.Variants) if (!states.Contains(v)) states.Add(v);
            foreach (string v in states) if (PortraitLibrary.HasVariantAt(image, v)) variants.Add(v);
            foreach (string v in variants)
            {
                string variant = v;
                Cell(ui, Painter.Preview(Painter.PortraitGroup, image, cells, variant, showing), variant.Length == 0 ? T("本体", "Base") : Labels.Of(variant),
                    locked && current == variant, () =>
                    {
                        bool ok = now.Valid && now.Group == Painter.PortraitGroup
                            ? Painter.SetVariant(target, variant)
                            : PaintRules.Adopt(target, portrait, variant);   // 别的 mod 的立绘道具：接管并记下原图
                        if (ok) PaintRules.SetLocked(target, true);
                        Build();
                    });
            }
            ui.End();
            ui.End();
        }

        // ── 右栏：同 ID 贴图（这个图 ID 在各来源里的全部图） ──

        private static void BuildSameId(CustomUIBuilder ui, string portrait, float width, float height)
        {
            PaintId now = Painter.SourceOf(target);
            int cells = now.Valid ? now.Cells : Mathf.Clamp(target.shape != null ? Mathf.Max(target.shape.width, target.shape.height) : 4, 1, 8);
            bool mine = now.Valid && now.Group == Painter.PortraitGroup, pinned = Painter.IsPinned(target);
            ui.BeginScroll(height - 8f);
            ui.BeginGrid(ColumnsFor(width), CellW, CellH, Gap);
            string auto = PortraitLibrary.Pick(portrait, ItemDefs.Of(target.identifier).source);
            Cell(ui, auto != null ? Painter.Preview(Painter.PortraitGroup, auto, cells, null, showing) : null, T("自动", "Auto") + (auto != null ? "·" + SourceLabel(auto) : ""),
                mine && !pinned, () => Use(portrait, cells));
            foreach (string image in PortraitLibrary.ImagesOf(portrait))
            {
                string key = image;
                Cell(ui, Painter.Preview(Painter.PortraitGroup, key, cells, null, showing), SourceLabel(key), mine && pinned && now.Key == key, () => Use(key, cells));
            }
            ui.End();
            ui.End();
        }

        // 来源的显示名；同一来源里同 ID 的第 2、3… 张带编号；没激活的候选素材标出来
        private static string SourceLabel(string key)
        {
            ImageKey.Parse(key, out string source, out _, out int number);
            return Short(Sources.Label(source)) + (number > 0 ? " #" + (number + 1) : "") + (Packs.TopOf(source) ? " ★" : "") + (Sources.Active(source) ? "" : T("（未激活）", " (inactive)"));
        }

        // 来源名太长会折成好几行压到图上：取“/”前面那段（中英双名的包），再长就截断
        private static string Short(string name)
        {
            int slash = name.IndexOf(" / ", StringComparison.Ordinal);
            if (slash > 0) name = name.Substring(Entry.Chinese ? slash + 3 : 0, Entry.Chinese ? name.Length - slash - 3 : slash);
            return name.Length > 14 ? name.Substring(0, 13) + "…" : name;
        }

        // 换图：key 写明来源 = 点名这张；只写图 ID = 自动挑来源。换完解锁并按规则重算状态。
        private static void Use(string key, int cells)
        {
            bool ok = Painter.SourceOf(target).Valid ? Painter.Paint(target, Painter.PortraitGroup, key, cells) : PaintRules.Adopt(target, key, null);
            if (ok && !ImageKey.Full(key)) Painter.Unpin(target);   // 玩家明确选了“自动”：之前点过名的也放开
            if (ok)
            {
                PaintRules.SetLocked(target, false);
                Painter.Refresh(target);
            }
            Build();
        }

        // ── 右栏：其他更多（整个图库） ──

        private static void BuildLibrary(CustomUIBuilder ui, string currentPortrait, float width, float height)
        {
            PaintId now = Painter.SourceOf(target);
            int cells = now.Valid ? now.Cells : 4;
            List<Portrait> all = PortraitLibrary.Everything();   // 连只有候选素材提供的也列出来

            // 来源与标签组的选项（从现有立绘里收）
            var sources = new List<string> { T("全部来源", "All sources") };
            var tags = new List<string> { T("全部标签", "All tags") };
            // ClaudeNote: 0.8.7 来源筛选按“这个来源提供了哪些图”（Lune：只选来源时荡铺丢了很多，同 ID 贴图里又有）。
            //   和工坊 0.8.6 修的是同一个错：之前按“这张立绘现在选中的来源”筛，每张立绘只算在一个来源名下。
            foreach (Portrait q in all)
            {
                foreach (string src in q.Sources) if (!sources.Contains(src)) sources.Add(src);   // 下拉框显示名见 sourceNames
                foreach (string t in q.Tags) if (!tags.Contains(t)) tags.Add(t);
            }
            sourceIndex = Mathf.Clamp(sourceIndex, 0, sources.Count - 1);
            tagIndex = Mathf.Clamp(tagIndex, 0, tags.Count - 1);

            var shown = new List<Portrait>();
            string needle = (search ?? "").Trim().ToLowerInvariant();
            foreach (Portrait q in all)
            {
                if (sourceIndex > 0 && q.CandidateOf(sources[sourceIndex]) == null) continue;
                if (tagIndex > 0 && !q.Has(tags[tagIndex])) continue;
                if (needle.Length > 0 && !Matches(q, needle)) continue;
                shown.Add(q);
            }
            int pages = Mathf.Max(1, (shown.Count + PerPage - 1) / PerPage);
            page = Mathf.Clamp(page, 0, pages - 1);

            float half = (width - 20f) / 2f;
            ui.BeginRow(6f);
            var sourceNames = new List<string> { sources[0] };
            for (int k = 1; k < sources.Count; k++) sourceNames.Add(Sources.Label(sources[k]));
            ui.AddDropdown(sourceNames, sourceIndex, i => { sourceIndex = i; page = 0; Build(); });
            ui.Last?.SetFixedWidth(half);
            ui.AddDropdown(tags, tagIndex, i => { tagIndex = i; page = 0; Build(); });
            ui.Last?.SetFixedWidth(half);
            ui.End();

            ui.BeginRow(6f);
            ui.AddInput(T("按字段或标签搜索…", "Search field or tag…"), search, v => typing = v, null, v => typing = v);
            ui.Last?.SetFixedWidth(Mathf.Max(120f, width - 250f));
            ui.AddButton(T("搜索", "Search"), () => { search = typing ?? ""; page = 0; Build(); });
            ui.AddButton("<", () => { page--; Build(); });
            ui.AddLabel((page + 1) + "/" + pages + " (" + shown.Count + ")");
            ui.AddButton(">", () => { page++; Build(); });
            ui.End();

            ui.BeginScroll(height - 100f);
            ui.BeginGrid(ColumnsFor(width), CellW, CellH, Gap);
            for (int i = page * PerPage; i < Mathf.Min(shown.Count, (page + 1) * PerPage); i++)
            {
                // 筛了来源：显示并换成那个来源的图（点名）；没筛：现在选中的那张（自动）
                string id = shown[i].Field, key = sourceIndex > 0 ? ImageKey.Make(sources[sourceIndex], id) : id;
                Cell(ui, Painter.Preview(Painter.PortraitGroup, key, cells, null, showing), id, id == currentPortrait, () => Use(key, cells));
            }
            ui.End();
            ui.End();
        }

        private static bool Matches(Portrait p, string needle)
        {
            if (p.Field.ToLowerInvariant().Contains(needle)) return true;
            foreach (string t in p.Tags) if (t.Contains(needle)) return true;
            return false;
        }

        // ── 左栏：静态道具 ──

        private static void BuildStatic(CustomUIBuilder ui, float width, float height)
        {
            string state = Painter.StateOf(target);
            bool locked = PaintRules.IsLocked(target);
            ui.AddLabel(target.identifier + (locked ? T("  ·  已锁定", "  ·  locked") : ""));
            ui.BeginScroll(height - 40f);
            ui.BeginGrid(ColumnsFor(width), CellW, CellH, Gap);
            // 原版外观：和原版“切换”轮换的是同几张图，这里直接选
            foreach (var look in PaintRules.NativeLooks(target))
            {
                string name = look.Key;
                Cell(ui, look.Value, T("原版 · ", "Game · ") + name, state.Length == 0 && target.spritePath == name, () => { PaintRules.SetNativeLook(target, name); Build(); });
            }
            if (PaintRules.StaticOptions(target).Count > 0)
            {
                Cell(ui, null, T("自动（按规则）", "Auto (rules)"), !locked, () => { PaintRules.SetLocked(target, false); Painter.Refresh(target); Build(); });
                Cell(ui, PaintRules.StaticBase(target), T("本体", "Base"), locked && state.Length == 0, () =>
                {
                    if (PaintRules.SetStaticState(target, "", null, null)) PaintRules.SetLocked(target, true);
                    Build();
                });
                foreach (var kv in PaintRules.StaticOptions(target))
                {
                    string tag = kv.Key;
                    Cell(ui, kv.Value, Labels.Of(tag), locked && state == tag, () =>
                    {
                        if (Painter.SetVariant(target, tag)) PaintRules.SetLocked(target, true);
                        Build();
                    });
                }
            }
            ui.End();
            ui.End();
        }

        private static void Cell(CustomUIBuilder ui, Sprite thumb, string label, bool selected, Action onPick)
        {
            ui.BeginColumn(2f, TextAnchor.UpperCenter);
            if (thumb != null) ui.AddImage(thumb, Thumb, Thumb);
            else ui.AddLabel(" ");
            ui.AddLabel(selected ? "▶ " + label : label);
            ui.AddButton(T("使用", "Use"), () =>
            {
                try { onPick(); }
                catch (Exception ex) { Entry.Warn("pick failed: " + ex.Message); }
            });
            ui.End();
        }
    }
}
