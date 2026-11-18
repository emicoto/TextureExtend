// ClaudeNote: 0.7.2 贴图扩展工坊的窗口，重做（Lune 2026-11-18：0.7.0 借游戏的界面工具拼出来的和设计稿差太远——用 Unity 自己的 UI 组件画）。
//   设计稿：项目根目录 Claude的贴图扩展工坊设计稿.html（v2）。皮肤与零件在 UI/Workshop/（Skin、Ui），数据在 Core/Workshop.cs，这里只管版式与操作。
//   结构：自己的一块画布（参考分辨率 1280×720，随屏幕等比缩放）→ 半透明遮罩 → 窗口。
//         标题条右侧 立绘扩展 / 道具扩展 两个入口；下面四页：目标 / 标签 / 抽选 / 导出。
//   状态记在静态字段里，每次操作把窗口内容整个重摆（一百来个零件）。输入框只在回车 / 按“新增”时才重摆，打字不丢焦点。
//   0.8.1 “目标”“标签”两页的版式照 Lune 2026-11-17 给的两张图例重摆。
//   清理（Close，关窗 / 按 Esc / 换场景 / 停用 mod 都走它）：销毁画布、现算的皮肤贴图、预览用的缩略图，状态字段留着（下次打开接着改）。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TextureExtend.TagSystem;
using TextureExtend.WorkshopUi;

namespace TextureExtend
{
    internal static class WorkshopWindow
    {
        private const float W = 1100f, H = 640f, CW = 1044f, CH = 420f, LeftW = 560f, RightX = 580f, RightW = 464f;
        private const int Columns = 4, PerPage = 12;
        private static readonly string[] tabs = { "target", "tags", "pool", "out" };

        private static GameObject canvas;
        private static RectTransform window;
        private static string scope = Workshop.Portrait, tab = "target";
        private static string picked, poolPicked;
        private static int page, sourceAt, typeAt, filterAt, groupAt, addAt, condAt, viewAt, upAt, bindAt, dirty;
        private static string search = "", newGroup = "", newTag = "", said = "";
        private static bool saidBad;
        private static HashSet<string> showing = new HashSet<string>();

        private static string T(string zh, string en) { return Entry.Chinese ? zh : en; }
        private static string ScopeName => scope == Workshop.Item ? T("道具", "item") : T("立绘", "portrait");

        internal static void Open()
        {
            if (canvas != null) return;
            try
            {
                Skin.Prepare(Skin.ScaleFor(1280f, 720f));
                canvas = new GameObject("TextureExtend_Workshop", typeof(RectTransform));   // 不设 DontDestroyOnLoad：换场景时随场景销毁
                Canvas c = canvas.AddComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = 5000;
                c.pixelPerfect = true;   // 色块位置吸附到整像素（见 Skin.Prepare）
                CanvasScaler scaler = canvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.matchWidthOrHeight = 0.5f;
                canvas.AddComponent<GraphicRaycaster>();
                canvas.AddComponent<Host>();
                var full = (RectTransform)canvas.transform;
                Image shade = canvas.AddComponent<Image>();   // 遮罩：挡住后面的标题菜单
                shade.sprite = null; shade.color = Skin.Shade; shade.raycastTarget = true;
                window = Ui.Rect(full, "window", 0f, 0f, W, H);
                window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
                window.anchoredPosition = Vector2.zero;
                Ui.Root = window;
                ClickShield.Guard(full);   // 0.7.3 点击不穿透（整屏：工坊开着时后面的标题菜单一概点不到）
                said = "";
                Build();
            }
            catch (Exception ex) { Health.Report("workshop window", ex); Close(); }
        }

        /// <summary>关窗并收拾干净。重复调用无害。</summary>
        internal static void Close()
        {
            Popup.Close();
            Tip.Hide();
            if (canvas != null) UnityEngine.Object.Destroy(canvas);
            canvas = null;
            window = null;
            Ui.Root = null;
            Skin.Release();
            chipRects.Clear();   // 这些指着已经销毁的界面零件，不留着
            tagScroll = null;
            byGroup.Clear();
            showing.Clear();
            Painter.ReleasePreviews(null);
        }

        // 画布上的小组件：Esc 关窗；画布被销毁（换场景）时把其余的也收掉
        private sealed class Host : MonoBehaviour
        {
            private void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Close(); }
            private void OnDestroy() { if (canvas == gameObject) { canvas = null; Close(); } }
        }

        private static void Touch() { dirty++; said = ""; Build(); }
        private static void Say(string text, bool bad = false) { said = text; saidBad = bad; }

        // ── 整窗 ──

        private static void Build()
        {
            if (window == null) return;
            if (tagScroll != null) tagScrollAt = tagScroll.verticalNormalizedPosition;
            tagScroll = null;
            Popup.Close();
            Tip.Hide();
            for (int i = window.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(window.GetChild(i).gameObject);
            showing = new HashSet<string>();
            try
            {
                Ui.Box(window, 0f, 0f, W, H, Skin.Window, Color.white, true);
                // 标题条：整条青色 + 左下一段带斜切
                Ui.Box(window, 3f, 3f, W - 6f, 36f, Skin.White, Skin.Teal);
                Ui.Box(window, 3f, 39f, 640f, 22f, Skin.White, Skin.Teal);
                Ui.Box(window, 643f, 39f, 22f, 22f, Skin.Corner, Skin.Teal);
                Ui.Label(window, T("贴图扩展工坊", "Texture Workshop"), 28f, 6f, 360f, 50f, 34f, Skin.Ink, TextAlignmentOptions.MidlineLeft, true);
                Ui.Label(window, "v" + Entry.Version + "  ·  " + T("未导出 ", "Unsaved ") + dirty, 400f, 30f, 230f, 24f, 13f, Skin.Ink);   // 版本号摆在明处：装没装上新版一眼能看出来
                Mode(T("立绘扩展", "Portraits"), Workshop.Portrait, W - 372f);
                Mode(T("道具扩展", "Items"), Workshop.Item, W - 208f);
                Image close = Ui.Box(window, W - 38f, 8f, 26f, 26f, Skin.White, Skin.Ink, true);
                Ui.Label(close.transform, "x", 0f, 0f, 26f, 24f, 18f, Skin.Teal, TextAlignmentOptions.Center, true);
                Ui.Clickable(close, Close);

                // 四页
                string[] names = { T("目标", "Targets"), T("标签", "Tags"), T("抽选", "Pools"), T("导出", "Export") };
                string[] subs = { T("给", "Tag ") + ScopeName + T("加标签", "s"), T("标签与标签组", "Tags and groups"), T("条件与限制", "Conditions"), T("注册文件", "Registration file") };
                for (int i = 0; i < tabs.Length; i++)
                {
                    string to = tabs[i];
                    Image t = Ui.Box(window, 295f + i * 130f, 72f, 120f, 48f, Skin.Tab, tab == to ? Skin.Pick : Skin.Teal, true);
                    Ui.Label(t.transform, names[i], 0f, 3f, 120f, 26f, 22f, Skin.Ink, TextAlignmentOptions.Center, true);
                    Ui.Label(t.transform, subs[i], 0f, 27f, 120f, 16f, 11f, Skin.Ink, TextAlignmentOptions.Center);
                    Ui.Clickable(t, () => { tab = to; said = ""; Build(); });
                }
                Ui.Box(window, 28f, 130f, CW, 1f, Skin.White, Skin.Line);

                RectTransform content = Ui.Rect(window, "content", 28f, 142f, CW, CH);
                if (Workshop.List(scope).Count == 0)
                    Ui.Label(content, T("还没有目录：先进一次存档、在店里待几秒，再回标题界面打开这里。", "No catalog yet: load a save, wait a few seconds in the shop, then come back."), 0f, 150f, CW, 60f, 18f, Skin.Warn, TextAlignmentOptions.Center, false, true);
                else if (tab == "target") Targets(content);
                else if (tab == "tags") Tags(content);
                else if (tab == "pool") Pools(content);
                else Out(content);

                Ui.Box(window, 28f, 572f, CW, 1f, Skin.White, Skin.Line);
                Ui.Button(window, T("导出", "Export"), W / 2f - 215f, 582f, 130f, 34f, DoExport, dirty > 0);
                Ui.Button(window, T("复制文本", "Copy text"), W / 2f - 65f, 582f, 130f, 34f, () => { GUIUtility.systemCopyBuffer = Workshop.Csv(); Say(T("注册表格的文本已复制到剪贴板。", "Registration text copied.")); Build(); });
                Ui.Button(window, T("撤销改动", "Discard"), W / 2f + 85f, 582f, 130f, 34f, () => { Workshop.Reload(); dirty = 0; Say(T("已撤销。", "Discarded.")); Build(); });
                string stale = said.Length == 0 ? Workshop.Stale() : null;   // 目录过期：没有别的话要说时常驻提示
                if (said.Length > 0 || stale != null) Ui.Label(window, stale ?? said, 28f, 616f, CW, 18f, 13f, stale != null || saidBad ? Skin.Warn : Skin.Note, TextAlignmentOptions.Center);
            }
            catch (Exception ex) { Health.Report("workshop window", ex); }
            Painter.ReleasePreviews(showing);   // 上一页的缩略图
            SourceReader.Release();
        }

        private static void Mode(string label, string to, float x)
        {
            bool on = scope == to;
            Image b = Ui.Box(window, x, 6f, 156f, 30f, Skin.White, on ? Skin.Pick : Skin.Ink, true);
            Ui.Label(b.transform, label, 0f, 0f, 156f, 30f, 20f, on ? Skin.Ink : Skin.Teal, TextAlignmentOptions.Center, true);
            Ui.Clickable(b, () =>
            {
                if (scope == to) return;
                scope = to; picked = poolPicked = null;
                picks.Clear(); tagSearch = ""; tagScrollAt = 1f;
                page = sourceAt = typeAt = filterAt = groupAt = addAt = condAt = upAt = bindAt = 0;
                search = newGroup = newTag = said = "";
                Build();
            });
        }

        private static void DoExport()
        {
            string problem = Workshop.Export();
            if (problem == null) dirty = 0;
            Say(problem == null ? T("已导出，下次读档生效：ModData/", "Exported, applies on next load: ModData/") + Painter.ModId + "/" + Workshop.CsvFile : T("导出失败：", "Export failed: ") + problem, problem != null);
            Build();
        }

        private static Sprite FrameOf(string name)
        {
            return Workshop.Current.tags.TryGetValue(name, out Workshop.Tag tag) && tag.scope == scope ? (tag.group ? Skin.ChipGroup : Skin.ChipMine) : Skin.Chip;
        }

        // ── 目标：给已有的立绘 / 道具贴标签 ──

        // 来源的显示名；同一来源里同 ID 的第 2、3… 张带上编号
        private static string SourceName(Workshop.Target t)
        {
            string name = Workshop.SourceLabel(t.src);
            if (t.key == null) return name;
            ImageKey.Parse(t.key, out _, out _, out int number);
            return number > 0 ? name + " #" + (number + 1) : name;
        }

        private static void Targets(RectTransform c)
        {
            List<Workshop.Target> all = Workshop.List(scope);   // 0.8.2 立绘按 root_key 一张卡
            // 筛选：来源（原版 / 各 mod）、分类（只有道具有：游戏的主分类）、标签，右边一个搜索框
            bool items = scope == Workshop.Item;
            // ClaudeNote: 0.8.6 立绘的来源筛选按“这个来源提供了哪些图”（Lune：原版只剩两张、白蛇不在来源里）。
            //   之前按“这张立绘现在选中的来源”筛：每张立绘只算在一个来源名下，选“原版”就只剩没被别的包换掉的那两张。
            //   现在一张立绘有几个来源就出现在几个来源的筛选结果里，卡片上显示的是那个来源自己的图。
            var sourceIds = new List<string>();
            var bySource = new HashSet<string>();   // "来源\n立绘ID"
            if (items) { foreach (Workshop.Target t in all) if (!string.IsNullOrEmpty(t.src) && !sourceIds.Contains(t.src)) sourceIds.Add(t.src); }
            else
                foreach (Workshop.Target t in Workshop.Catalog.images)
                {
                    if (string.IsNullOrEmpty(t.src)) continue;
                    if (!sourceIds.Contains(t.src)) sourceIds.Add(t.src);
                    bySource.Add(t.src + "\n" + t.id);
                }
            if (!items && sourceIds.Count == 0) foreach (Workshop.Target t in all) if (!string.IsNullOrEmpty(t.src) && !sourceIds.Contains(t.src)) { sourceIds.Add(t.src); }   // 旧目录没有按来源的清单
            bool perSource = !items && bySource.Count > 0;
            sourceIds.Sort((a, b) => a == ItemDefs.Game ? -1 : b == ItemDefs.Game ? 1 : string.CompareOrdinal(Workshop.SourceLabel(a), Workshop.SourceLabel(b)));   // 原版排最前
            var sources = new List<string> { T("全部来源", "All sources") };
            foreach (string id in sourceIds) sources.Add(Workshop.SourceLabel(id));
            List<string> typeIds = items ? Workshop.ItemTypes() : new List<string>();
            var types = new List<string> { T("全部分类", "All categories") };
            types.AddRange(typeIds);
            var filters = new List<string> { T("全部标签", "All tags") };
            foreach (string tag in Workshop.AllTags(scope)) if (!typeIds.Contains(tag)) filters.Add(tag);   // 分类已经单独一栏，不在标签里重复
            string source = sourceAt > 0 && sourceAt <= sourceIds.Count ? sourceIds[sourceAt - 1] : null;
            string type = typeAt > 0 && typeAt <= typeIds.Count ? typeIds[typeAt - 1] : null;
            string filter = filterAt > 0 && filterAt < filters.Count ? filters[filterAt] : null;
            List<Workshop.Target> inFilter = filter != null && Workshop.Current.tags.ContainsKey(filter) ? Workshop.Pool(filter) : null;
            string needle = search.Trim().ToLowerInvariant();
            var shown = new List<Workshop.Target>();
            foreach (Workshop.Target t in all)
            {
                if (source != null && (perSource ? !bySource.Contains(source + "\n" + t.id) : t.src != source)) continue;
                if (type != null && (t.types == null || Array.IndexOf(t.types, type) < 0)) continue;
                if (needle.Length > 0 && t.id.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) < 0 && (t.name ?? "").ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                if (inFilter != null ? !inFilter.Exists(x => x.id == t.id) : filter != null && Array.IndexOf(t.tags, filter) < 0 && !Workshop.Mine(scope, t.id).Contains(filter)) continue;
                shown.Add(t);
            }
            int pages = Mathf.Max(1, (shown.Count + PerPage - 1) / PerPage);
            page = Mathf.Clamp(page, 0, pages - 1);

            // 左：筛选 + 总览（版式照 Lune 2026-11-17 的图例：4 列 3 行）
            Ui.Info(c, items ? T("选一个道具", "Pick an item") : T("选一张立绘", "Pick a portrait"), T("框架认得的全部" + ScopeName + "。目录是上次在店面里存下的；立绘的缩略图来自框架的道具图缓存，没缓存的只显示名字。", "Everything the framework knows. The catalog was saved the last time you were in the shop."), 0f, 0f, 26f, true);
            Ui.Label(c, shown.Count + " / " + all.Count, LeftW - 120f, 0f, 120f, 26f, 13f, Skin.Note, TextAlignmentOptions.MidlineRight);
            var drops = new List<Action<float, float>>();
            if (sourceIds.Count > 1) drops.Add((x, w) => Ui.Dropdown(c, sources, sourceAt, x, 32f, w, 28f, i => { sourceAt = i; page = 0; Build(); }));
            if (items && typeIds.Count > 0) drops.Add((x, w) => Ui.Dropdown(c, types, typeAt, x, 32f, w, 28f, i => { typeAt = i; page = 0; Build(); }));
            drops.Add((x, w) => Ui.Dropdown(c, filters, filterAt, x, 32f, w, 28f, i => { filterAt = i; page = 0; Build(); }));
            float searchW = drops.Count > 2 ? 130f : 176f, dropW = (LeftW - searchW - drops.Count * 8f) / drops.Count;
            for (int i = 0; i < drops.Count; i++) drops[i](i * (dropW + 8f), dropW);
            // 搜索框：回车生效（打字时不重摆窗口，否则会丢焦点）
            Ui.Input(c, items ? T("搜 名字 / ID", "Search name / id") : T("搜 ID", "Search id"), search, LeftW - searchW, 32f, searchW, 28f, null)
                .onSubmit.AddListener(v => { search = v ?? ""; page = 0; Build(); });
            const float gap = 8f, cardH = 100f, thumb = 50f;
            float cardW = (LeftW - gap * (Columns - 1)) / Columns;
            for (int i = page * PerPage; i < Mathf.Min(shown.Count, (page + 1) * PerPage); i++)
            {
                Workshop.Target t = shown[i];
                int slot = i - page * PerPage, mine = Workshop.Mine(scope, t.id).Count;
                string title = items && !string.IsNullOrEmpty(t.name) ? t.name : t.id;   // 道具显示游戏里的名字
                Image card = Ui.Box(c, slot % Columns * (cardW + gap), 68f + slot / Columns * (cardH + 6f), cardW, cardH, t.id == picked ? Skin.PanelOn : Skin.Panel, Color.white, true);
                // 立绘缩略图一律按完整身份（来源:图ID）取：筛了来源就是那个来源的图，否则是现在选中的那张
                string imageKey = items ? null : source != null && perSource ? Workshop.ImageOf(t.id, source)?.key ?? t.key ?? t.id : t.key ?? t.id;
                Sprite image = items ? ItemDefs.Sprite(t.atlas, t.sprite, t.modSprite) ?? ItemDefs.Sprite(t.id) : Painter.Preview(Painter.PortraitGroup, imageKey, 4, null, showing);
                if (image != null)
                {
                    Image img = Ui.Box(card.transform, (cardW - thumb) / 2f, 6f, thumb, thumb, image, Color.white);
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                }
                else
                {
                    Image blank = Ui.Box(card.transform, (cardW - thumb) / 2f, 6f, thumb, thumb, Skin.White, Skin.Line);
                    Ui.Label(blank.transform, title.Length >= 2 ? title.Substring(0, 2).ToUpperInvariant() : title, 0f, 0f, thumb, thumb, 22f, Skin.TealDim, TextAlignmentOptions.Center, true);
                }
                Ui.Label(card.transform, title, 4f, 58f, cardW - 8f, 20f, 13f, Skin.Cream, TextAlignmentOptions.Center);
                string under = "";
                if (!items && Workshop.Current.join.TryGetValue(t.id, out string joined)) under = "→ " + joined;   // 归到了别的 root_key 名下
                if (mine > 0) under += (under.Length > 0 ? "  " : "") + "+" + mine;
                if (under.Length > 0) Ui.Label(card.transform, under, 4f, 78f, cardW - 8f, 16f, 11f, Skin.Note, TextAlignmentOptions.Center);
                Ui.Clickable(card, () => { picked = t.id; addAt = 0; rootTyping = null; Build(); });
            }
            // 翻页放在总览下面，居中；只有一页时不画，到头的那一侧不画
            if (pages > 1)
            {
                const float pagerY = 388f, pagerW = 64f, pagerMid = 110f;
                float px = (LeftW - pagerW * 2f - pagerMid) / 2f;
                if (page > 0) Ui.Button(c, "<", px, pagerY, pagerW, 32f, () => { page--; Build(); });
                Ui.Label(c, (page + 1) + " / " + pages, px + pagerW, pagerY, pagerMid, 32f, 16f, Skin.Cream, TextAlignmentOptions.Center);
                if (page < pages - 1) Ui.Button(c, ">", px + pagerW + pagerMid, pagerY, pagerW, 32f, () => { page++; Build(); });
            }
            if (shown.Count == 0) Ui.Label(c, T("没有符合的", "Nothing matches"), 0f, 200f, LeftW, 30f, 15f, Skin.Note, TextAlignmentOptions.Center);

            // 右：选中的这一个
            Workshop.Target sel = all.Find(t => t.id == picked);
            RectTransform r = Ui.Rect(c, "right", RightX, 0f, RightW, CH);
            if (sel == null) { Ui.Label(r, T("左边选一个。", "Pick one on the left."), 0f, 0f, RightW, 26f, 15f, Skin.Note); return; }
            bool named = items && !string.IsNullOrEmpty(sel.name) && sel.name != sel.id;
            Ui.Label(r, named ? sel.name : sel.id, 0f, 0f, RightW - 230f, 26f, 18f, Skin.Cream);
            Ui.Label(r, (named ? sel.id + "  ·  " : "") + (string.IsNullOrEmpty(sel.src) ? "" : T("来源 ", "from ") + SourceName(sel)), RightW - 230f, 0f, 230f, 26f, 12f, Skin.Note, TextAlignmentOptions.MidlineRight);
            Ui.Box(r, 0f, 28f, RightW, 1f, Skin.White, Skin.Line);

            const float inX = 14f, inW = RightW - 28f;
            float y = 36f;
            // 立绘：预览区只剩这张图和它的差分候选（Lune 2026-11-17）
            if (!items) { Preview(r, sel, inX, y, inW); y += 84f; }

            // 已有标签一览：自带的（只读）+ 我加的（青框，点一下摘掉）
            List<string> mineTags = Workshop.Mine(scope, sel.id);
            var have = new List<string>(sel.tags);
            foreach (string tag in mineTags) if (!have.Contains(tag)) have.Add(tag);
            Ui.Info(r, T("已有标签一览", "Tags it has"), T("灰框是游戏和框架认出来的，只读。青框是你加的，点一下摘掉。", "Grey ones are built in and read-only. Teal ones are yours; click one to remove it."), inX, y);
            float chipsH = items ? 72f : 46f;
            Ui.Chips(r, have, inX + 8f, y + 26f, inW - 8f, chipsH, name => mineTags.Contains(name) ? Skin.ChipMine : Skin.Chip, name => { Workshop.Peel(scope, sel.id, name); Touch(); }, T("没有", "None"), name => mineTags.Contains(name));
            y += 26f + chipsH + 6f;

            // 添加标签：左边的标签组只是筛选（第一项留空 = 不筛），右边是这个组里还能加的标签
            List<string> groups = Workshop.GroupIds(scope);
            var groupNames = new List<string> { T("标签组", "Tag group") };
            foreach (string g in groups) groupNames.Add(Workshop.GroupLabel(g));
            groupAt = Mathf.Clamp(groupAt, 0, groups.Count);
            List<string> can = Workshop.TagsIn(scope, groupAt > 0 ? groups[groupAt - 1] : null);
            can.RemoveAll(have.Contains);
            var canNames = new List<string> { T("可选标签", "Tag") };
            canNames.AddRange(can);
            addAt = Mathf.Clamp(addAt, 0, can.Count);
            float half = (inW - 4f - 72f - 16f) / 2f;
            Ui.Info(r, T("添加标签", "Add a tag"), T("先用左边的标签组缩小范围（留空 = 全部），再在右边选标签，点“添加”。自带的标签也可以加给别的目标。新标签在“标签”页建。", "Narrow down with the group on the left (blank = all), choose a tag on the right, then Add. Create new tags on the Tags page."), inX, y);
            Ui.Dropdown(r, groupNames, groupAt, inX + 4f, y + 28f, half, 28f, i => { groupAt = i; addAt = 0; Build(); });
            Ui.Dropdown(r, canNames, addAt, inX + 4f + half + 8f, y + 28f, half, 28f, i => { addAt = i; Build(); });
            Ui.Button(r, T("添加", "Add"), RightW - 14f - 72f, y + 28f, 72f, 28f, () =>
            {
                if (addAt <= 0 || addAt > can.Count) { Say(T("先选一个标签。", "Choose a tag first."), true); Build(); return; }
                Workshop.Stick(scope, sel.id, can[addAt - 1]);
                addAt = 0;
                Touch();
            }, false, 15f);
            y += 66f;

            string key = Workshop.Key(scope, sel.id);
            if (items)
            {
                // 手动选图：只有道具有（Lune 2026-11-17）
                Ui.Info(r, T("手动选图", "Manual pick"), T("禁止后，这件道具在游戏里的“选图”窗口只能看不能选，贴图只由规则切换。", "When denied, players cannot pick this item's look by hand; only rules switch it."), inX, y, 28f);
                Ui.Dropdown(r, new List<string> { T("允许", "Allowed"), T("禁止", "Denied") }, Workshop.Current.noManual.Contains(key) ? 1 : 0, RightW - 14f - 160f, y, 160f, 28f, i =>
                {
                    Workshop.Current.noManual.Remove(key);
                    if (i == 1) Workshop.Current.noManual.Add(key);
                    Touch();
                });
                return;
            }

            // 限制条件：预设 + 各 mod 登记的具名条件
            List<string> triggers = Workshop.Triggers();
            Workshop.Current.limit.TryGetValue(key, out string limit);
            if (!string.IsNullOrEmpty(limit) && !triggers.Contains(limit)) triggers.Add(limit);   // 登记它的 mod 这次没开：照样显示，不丢
            var triggerNames = new List<string> { T("可用触发条件（不限制）", "Trigger condition (none)") };
            foreach (string t in triggers) triggerNames.Add(Workshop.TriggerLabel(t));
            Ui.Info(r, T("限制条件", "Restriction"), T("归进了 root_key 组的：条件成立时自动替换主体立绘（道具上直接生效；来客要勾“自动动态适配”）。root_key 自己：勾了自动动态适配时，条件不成立就显示游戏原图。第一项 = 不限制。", "For a group member: replaces the root_key portrait while this holds (items always; visitors only with Auto dynamic fit). For a root_key itself with Auto dynamic fit: shows the game original while it does not hold."), inX, y);
            Ui.Dropdown(r, triggerNames, string.IsNullOrEmpty(limit) ? 0 : triggers.IndexOf(limit) + 1, inX + 120f, y - 2f, inW - 120f, 28f, i =>
            {
                if (i <= 0) Workshop.Current.limit.Remove(key); else Workshop.Current.limit[key] = triggers[i - 1];
                Touch();
            });
            y += 36f;

            // root_key 组：命名过于自由的立绘归到某张立绘名下
            Workshop.Current.join.TryGetValue(sel.id, out string root);
            Ui.Label(r, T("将这个立绘添加进", "Add this portrait to"), inX, y, 150f, 28f, 15f, Skin.Text);
            TMP_InputField box = Ui.Input(r, root ?? "root_key", rootTyping ?? root ?? "", inX + 150f, y, 170f, 28f, null);
            Ui.Label(r, T("组", "group"), inX + 326f, y, 30f, 28f, 15f, Skin.Text);
            Ui.Button(r, root != null && (rootTyping ?? root) == root ? T("移出", "Remove") : T("添加", "Add"), RightW - 14f - 72f, y, 72f, 28f, () =>
            {
                string typed = (rootTyping ?? root ?? "").Trim();
                if (root != null && typed == root) { Workshop.Current.join.Remove(sel.id); rootTyping = null; Touch(); return; }
                string match = RootMatch(typed, sel.id, out _);
                if (match == null) { Say(typed.Length == 0 ? T("先输入一个 root_key。", "Type a root_key first.") : T("找不到“", "No portrait \"") + typed + T("”这张立绘，换个写法试试。", "\"."), true); Build(); return; }
                Workshop.Current.join[sel.id] = match;
                rootTyping = null;
                Touch();
            }, false, 15f);
            TextMeshProUGUI hint = Ui.Label(r, RootHint(rootTyping ?? root ?? "", sel.id, root), inX + 150f, y + 30f, inW - 150f, 18f, 12f, Skin.Note);
            box.onValueChanged.AddListener(v => { rootTyping = v; hint.text = RootHint(v, sel.id, root); });   // 边打边匹配，只改提示行，不重摆（不丢焦点）
            y += 54f;

            // 自动动态适配
            bool auto = Workshop.Current.auto.Contains(key);
            Ui.Info(r, T("自动动态适配", "Auto dynamic fit"), T("不改游戏怎么抽来客（游戏照旧按派系抽到一个立绘键），只决定这个键显示哪张。勾上：每次来客取图时按限制条件实时判断要不要替换。不勾：照“来源设置”选定的那张显示，不会自己变。同一张图有多个来源时，宣称了 TopPriority 的来源优先（只让位给玩家自己的选择）。", "Does not change how the game draws visitors; only which image the drawn key shows. Checked: re-checked on every visitor display. Unchecked: shows the image chosen in source settings. Among sources, a TopPriority source wins unless the player chose otherwise."), RightW - 250f, Mathf.Max(y, 380f), 26f);
            Image check = Ui.Box(r, RightW - 40f, Mathf.Max(y, 380f) + 1f, 24f, 24f, auto ? Skin.White : Skin.Chip, auto ? Skin.Teal : Color.white, true);
            Ui.Clickable(check, () => { if (!Workshop.Current.auto.Remove(key)) Workshop.Current.auto.Add(key); Touch(); });
        }

        private static string rootTyping;   // root_key 输入框里正打着的字（没点“添加”之前）

        // 预览：同一个 root_key 在各来源里的本体（不同来源自动并入同一组）+ 现在选中来源的差分候选（含归进来的立绘）。
        //   缩略图来自道具图缓存，没缓存的只显示标签名。黄底 = 现在来客用的那张。
        private static void Preview(RectTransform r, Workshop.Target sel, float x, float y, float w)
        {
            var cells = new List<KeyValuePair<string, string>>();   // (预览用的身份, 标签)
            List<Workshop.Target> images = Workshop.Catalog.images ?? new List<Workshop.Target>();
            foreach (Workshop.Target t in images) if (t.id == sel.id && t.key != null) cells.Add(new KeyValuePair<string, string>(t.key, SourceName(t)));
            if (cells.Count == 0) cells.Add(new KeyValuePair<string, string>(sel.id, T("本体", "Base")));
            if (sel.variants != null) foreach (string v in sel.variants) cells.Add(new KeyValuePair<string, string>(sel.id + "#" + v, Labels.Of(v)));
            foreach (KeyValuePair<string, string> kv in Workshop.Current.join)
                if (kv.Value == sel.id && (sel.variants == null || Array.IndexOf(sel.variants, PortraitLibrary.Normal(kv.Key)) < 0)) cells.Add(new KeyValuePair<string, string>(kv.Key, "→ " + kv.Key));
            const float cell = 64f, thumb = 56f;
            int fits = Mathf.Max(1, (int)(w / (cell + 4f)));
            for (int i = 0; i < Mathf.Min(cells.Count, fits); i++)
            {
                string id = cells[i].Key;
                int hash = id.IndexOf('#');
                Sprite image = hash < 0 ? Painter.Preview(Painter.PortraitGroup, id, 4, null, showing) : Painter.Preview(Painter.PortraitGroup, id.Substring(0, hash), 4, id.Substring(hash + 1), showing);
                bool current = ImageKey.SourceOf(id) == sel.src || (i == 0 && !ImageKey.Full(id));
                float cx = x + i * (cell + 4f);
                Image back = Ui.Box(r, cx, y, cell, thumb + 4f, current ? Skin.PanelOn : Skin.Panel, Color.white);
                if (image != null)
                {
                    Image img = Ui.Box(back.transform, (cell - thumb) / 2f, 2f, thumb, thumb, image, Color.white);
                    img.type = Image.Type.Simple;
                    img.preserveAspect = true;
                }
                Ui.Label(r, cells[i].Value, cx, y + thumb + 6f, cell, 16f, 11f, current ? Skin.Cream : Skin.Text, TextAlignmentOptions.Center);
            }
            if (cells.Count > fits) Ui.Label(r, "+" + (cells.Count - fits), x + w - 40f, y + 20f, 40f, 20f, 13f, Skin.Note, TextAlignmentOptions.MidlineRight);
        }

        // root_key 自动匹配：完全一样的优先；否则开头一样的、再否则包含的，只有一个时就是它
        private static string RootMatch(string typed, string self, out List<string> candidates)
        {
            candidates = new List<string>();
            typed = (typed ?? "").Trim();
            if (typed.Length == 0) return null;
            string lower = typed.ToLowerInvariant();
            var starts = new List<string>();
            foreach (Workshop.Target t in Workshop.List(Workshop.Portrait))
            {
                if (t.id == self) continue;
                string id = t.id.ToLowerInvariant();
                if (id == lower) return t.id;
                if (id.StartsWith(lower, StringComparison.Ordinal)) starts.Add(t.id);
                else if (id.IndexOf(lower, StringComparison.Ordinal) >= 0) candidates.Add(t.id);
            }
            starts.Sort(string.CompareOrdinal);
            candidates.Sort(string.CompareOrdinal);
            candidates.InsertRange(0, starts);
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static string RootHint(string typed, string self, string current)
        {
            if (current != null && typed == current) return T("已在 ", "In ") + current + T(" 组里", "");
            if ((typed ?? "").Trim().Length == 0) return T("输入立绘 ID，自动匹配", "Type a portrait id");
            string match = RootMatch(typed, self, out List<string> list);
            if (match != null) return "→ " + match;
            if (list.Count == 0) return T("搜索不到这张立绘", "No such portrait");
            return T("多个匹配：", "Matches: ") + string.Join(" / ", list.GetRange(0, Mathf.Min(3, list.Count))) + (list.Count > 3 ? " …" : "");
        }

        // ── 标签：标签一览 + 标签组管理 + 标签管理（版式照 Lune 2026-11-17 的图例） ──

        private const float ListW = 390f, FormX = 440f;

        private static string tagSearch = "";
        private static readonly HashSet<string> picks = new HashSet<string>();   // 多选中的标签（框选 / Ctrl + 点）
        private static readonly List<KeyValuePair<string, RectTransform>> chipRects = new List<KeyValuePair<string, RectTransform>>();   // 这次摆出来的、能拖的标签
        private static readonly HashSet<string> folded = new HashSet<string>();   // 收起来的标签组（"类别|组"；未分组的组名是空）
        private static Dictionary<string, List<string>> byGroup = new Dictionary<string, List<string>>();
        private static ScrollRect tagScroll;
        private static float tagScrollAt = 1f;

        // ClaudeNote: 0.8.5 标签一览改成“标签集”（Lune 2026-11-17 的图例）：每个标签组一块——组名一个框，下面是组里的标签一排排小框，
        //   下级组缩进着嵌在里面。标签和整个组都可以拖：拖进哪个组的那一块就归到哪个组（拖到“未分组”= 拿出来 / 回到顶层）。
        //   写死的顶层（派系 / 性别 / 体型，道具的三个组）不能拖。点一下你建的标签 / 组 = 选中（名字填进右边输入框）。
        private static void Tags(RectTransform c)
        {
            bool items = scope == Workshop.Item;
            Ui.Info(c, T("标签一览", "All tags"), T("每个标签组一块。你建的标签 / 标签组、扩展与 DLC 登记的标签可以拖进别的组（拖到“未分组”= 拿出来）；默认标签（暗色字）和写死的顶层组不能拖。多选：空白处按住拖出一个框，或按住 Ctrl 逐个点；拖其中一个，整批一起走。", "One block per group. Your own tags and groups, and tags registered by extensions, can be dragged into another group; default tags (dim) and the fixed top groups cannot. Multi-select: drag a box from empty space, or Ctrl-click; dragging one moves them all."), 8f, 0f, 26f, true);
            Ui.Input(c, "TagSearch", tagSearch, 124f, 0f, ListW - 124f - 118f, 26f, null).onSubmit.AddListener(v => { tagSearch = (v ?? "").Trim().ToLowerInvariant(); Build(); });
            Ui.Dropdown(c, new List<string> { T("按群组", "By group"), T("按名字", "By name") }, viewAt, ListW - 110f, 0f, 110f, 26f, i => { viewAt = i; Build(); });
            Ui.Box(c, 0f, 34f, ListW, CH - 34f, Skin.Chip, Color.white);
            RectTransform list = Ui.Scroll(c, 2f, 36f, ListW - 4f, CH - 38f, 0f, true);
            chipRects.Clear();
            Image ground = list.gameObject.AddComponent<Image>();   // 透明底：空白处按住拖 = 框选
            ground.color = Skin.Clear;
            if (viewAt == 0) list.gameObject.AddComponent<Marquee>().Set(Boxed);   // 0.8.9 拖动与框选只在“按群组”时有（按名字排时没有组可拖进去）
            else picks.Clear();
            float inner = ListW - 4f - 8f - 12f, y = 6f;
            byGroup = Workshop.TagsByGroup(scope);   // 这次重摆用的归类（各组只查这张表）
            if (viewAt == 0)
            {
                var seen = new HashSet<string>();
                foreach (string g in Workshop.DefaultGroups(scope)) { float x = (Workshop.Level(scope, g) - 1) * 28f; y += Zone(list, g, 6f + x, y, inner - x, seen) + 10f; }
                foreach (string g in Workshop.Names(scope, true)) if (Workshop.Upstream(scope, g) == null) y += Zone(list, g, 6f, y, inner, seen) + 10f;
                foreach (string g in Workshop.Names(scope, true)) if (!seen.Contains(g)) y += Zone(list, g, 6f, y, inner, seen) + 10f;   // 上下级绕成圈的（旧数据）：也列出来，不藏
                y += Zone(list, "", 6f, y, inner, seen);
            }
            else y += Flow(list, Shown(Workshop.AllTags(scope)), 6f, y, inner);
            list.sizeDelta = new Vector2(list.sizeDelta.x, Mathf.Max(CH - 38f, y + 8f));
            tagScroll = list.parent.GetComponent<ScrollRect>();   // 重摆之后回到刚才滚到的位置（否则每点一下都跳回顶上）
            if (tagScroll != null) { Canvas.ForceUpdateCanvases(); tagScroll.verticalNormalizedPosition = tagScrollAt; }

            RectTransform r = Ui.Rect(c, "form", FormX, 0f, CW - FormX, CH);
            List<string> groups = Workshop.GroupIds(scope);
            List<string> labels = groups.ConvertAll(Workshop.GroupLabel);

            // 标签组管理
            Ui.Info(r, T("标签组管理", "Tag groups"), T("标签组 = 标签的归类。默认：派系、性别在最顶层，体型在第二层。“绑定”把输入框里这个组挂到选中的上游标签组下面（第一项 = 顶层）。", "A group sorts tags. Bind puts the group named in the box under the chosen upstream group (first entry = top level)."), 0f, 20f, 26f, true);
            if (items) Ui.Label(r, T("道具固定三个组：默认分类 / 标识性标签 / 扩展标签。", "Items have three fixed groups: Category / Markers / Extended."), 4f, 54f, CW - FormX, 26f, 14f, Skin.Note);
            else
            {
                var ups = new List<string> { T("上游标签组", "Upstream group") };
                ups.AddRange(labels);
                upAt = Mathf.Clamp(upAt, 0, groups.Count);
                Ui.Input(r, T("标签组名", "Group name"), newGroup, 4f, 54f, 220f, 30f, v => newGroup = v);
                Ui.Button(r, T("新增", "New"), 244f, 54f, 64f, 30f, () => Add(newGroup, true, upAt > 0 ? groups[upAt - 1] : ""), false, 15f);
                Ui.Button(r, T("删除", "Delete"), 316f, 54f, 64f, 30f, () => Remove(newGroup, true), false, 15f);
                Ui.Dropdown(r, ups, upAt, 4f, 100f, 350f, 30f, i => { upAt = i; Build(); });
                Ui.Button(r, T("绑定", "Bind"), 374f, 100f, 64f, 30f, () => BindTo(newGroup, true, upAt > 0 ? groups[upAt - 1] : ""), false, 15f);
            }

            // 标签管理
            Ui.Info(r, T("标签管理", "Tags"), T("“新增”建一个标签并归到选中的标签组；“绑定”把输入框里这个标签改归到选中的标签组。名字用小写字母、数字、下划线，2 到 24 个字。贴到目标上在“目标”页。", "New creates a tag in the chosen group; Bind moves the tag named in the box to the chosen group. Names: a-z, 0-9, _, 2 to 24 characters."), 0f, 180f, 26f, true);
            Ui.Input(r, T("标签名", "Tag name"), newTag, 4f, 214f, 220f, 30f, v => newTag = v);
            Ui.Button(r, T("删除", "Delete"), 316f, 214f, 64f, 30f, () => Remove(newTag, false), false, 15f);
            if (items)
            {
                Ui.Button(r, T("新增", "New"), 244f, 214f, 64f, 30f, () => Add(newTag, false, ItemTags.Groups[ItemTags.Extend]), false, 15f);
                Ui.Label(r, T("新标签都归在“扩展标签”。", "New tags go into Extended."), 4f, 260f, CW - FormX, 26f, 14f, Skin.Note);
                return;
            }
            var binds = new List<string> { T("可选标签组", "Tag group") };
            binds.AddRange(labels);
            bindAt = Mathf.Clamp(bindAt, 0, groups.Count);
            Ui.Button(r, T("新增", "New"), 244f, 214f, 64f, 30f, () => Add(newTag, false, bindAt > 0 ? groups[bindAt - 1] : ""), false, 15f);
            Ui.Dropdown(r, binds, bindAt, 4f, 260f, 350f, 30f, i => { bindAt = i; Build(); });
            Ui.Button(r, T("绑定", "Bind"), 374f, 260f, 64f, 30f, () => BindTo(newTag, false, bindAt > 0 ? groups[bindAt - 1] : ""), false, 15f);
        }

        // 点一览里我建的一行：名字填进对应的输入框，下拉跳到它现在的上游 / 所属组
        private static void Select(string name, bool group)
        {
            List<string> groups = Workshop.GroupIds(scope);
            if (group) { newGroup = name; upAt = groups.IndexOf(Workshop.Upstream(scope, name) ?? "") + 1; }
            else { newTag = name; bindAt = groups.IndexOf(Workshop.GroupOfTag(scope, name) ?? "") + 1; }
            said = "";
            Build();
        }

        private static void Add(string typed, bool group, string under)
        {
            string name = (typed ?? "").Trim().ToLowerInvariant();
            string problem = Workshop.NameProblem(name);
            if (problem != null) { Say(problem, true); Build(); return; }
            Workshop.Create(name, scope, group).under = under;
            if (group) newGroup = name; else newTag = name;
            Touch();
        }

        // 删掉输入框里这个名字的标签 / 标签组（只能删你建的）
        private static void Remove(string typed, bool group)
        {
            string name = (typed ?? "").Trim().ToLowerInvariant();
            if (!Workshop.Current.tags.TryGetValue(name, out Workshop.Tag tag) || tag.scope != scope || tag.group != group)
            {
                Say(T("只能删你建的：先在左边点一下它。", "Only your own can be deleted: click it on the left first."), true);
                Build();
                return;
            }
            Workshop.Delete(name);
            if (group) newGroup = ""; else newTag = "";
            Touch();
        }

        private static void BindTo(string typed, bool group, string under)
        {
            string name = (typed ?? "").Trim().ToLowerInvariant();
            if (!Workshop.Current.tags.TryGetValue(name, out Workshop.Tag tag) || tag.scope != scope || tag.group != group)
            {
                Say(group ? T("没有这个标签组：先“新增”，或在左边点一个你建的组。", "No such group: create it first, or click one of yours on the left.")
                          : T("没有这个标签：先“新增”，或在左边点一个你建的标签。", "No such tag: create it first, or click one of yours on the left."), true);
                Build();
                return;
            }
            if (!Workshop.Bind(name, under)) { Say(T("不能挂到自己的下级下面。", "A group cannot go under one of its own children."), true); Build(); return; }
            Touch();
        }

        private static List<string> Shown(List<string> tags)
        {
            if (tagSearch.Length > 0) tags.RemoveAll(t => t.IndexOf(tagSearch, StringComparison.Ordinal) < 0);
            return tags;
        }

        // 一个标签组的一块：组名框 + 组里的标签 + 嵌在里面的下级组。group 为空 = “未分组”。返回占的高度。
        private static float Zone(RectTransform parent, string group, float x, float y, float w, HashSet<string> seen)
        {
            if (!seen.Add(group)) return -10f;   // 已经画过（抵掉调用方加的间距）
            RectTransform zone = Ui.Rect(parent, "zone", x, y, w, 10f);
            Image back = zone.gameObject.AddComponent<Image>();   // 透明底：整块都能接住拖过来的东西
            back.color = Skin.Clear;
            back.raycastTarget = true;
            zone.gameObject.AddComponent<DropZone>().group = group;

            bool mine = group.Length > 0 && Workshop.Current.tags.TryGetValue(group, out Workshop.Tag g) && g.group;
            string label = group.Length == 0 ? T("未分组", "Ungrouped") : Workshop.GroupLabel(group) + (mine ? "  " + Workshop.Pool(group).Count : "");
            // 组名前面的小箭头：点一下收起 / 展开这一组（Lune 2026-11-17：组多了方便处理）
            string foldKey = Workshop.Key(scope, group);
            bool closed = folded.Contains(foldKey);
            Action toggle = () => { if (!folded.Remove(foldKey)) folded.Add(foldKey); Build(); };
            Image fold = Ui.Box(zone, 0f, 3f, 20f, 20f, Skin.Chip, Color.white, true);
            Ui.Label(fold.transform, closed ? ">" : "v", 0f, 0f, 20f, 19f, 13f, Skin.Teal, TextAlignmentOptions.Center, true);
            Ui.Clickable(fold, toggle);
            float headW = Mathf.Ceil(Ui.MeasureBody(label, 14f)) + 22f;
            Image head = Ui.Box(zone, 24f, 0f, headW, 26f, group.Length == 0 ? Skin.Chip : mine && group == newGroup ? Skin.PanelOn : Skin.ChipGroup, Color.white, mine);
            Ui.Label(head.transform, label, 10f, 0f, headW - 12f, 26f, 14f, group.Length == 0 ? Skin.Note : Skin.Pick);
            // ClaudeNote: 0.9.2 点组名本身也收起 / 展开（Lune：点击群组没收起来）。你建的组点组名是“选中”（要拿它改绑定、删除），收起用前面的箭头。
            if (mine) head.gameObject.AddComponent<DragChip>().Set(() => Select(group, true), viewAt == 0 ? (Action<string>)(to => Dropped(group, true, to)) : null);
            else Ui.Clickable(head, toggle);

            float h = 32f;
            if (closed)   // 收起：只留组名一行（右边写里面有几个）；下级组也算画过了，免得被当成没画的又列到最外面
            {
                int inside = byGroup.TryGetValue(group, out List<string> hidden) ? hidden.Count : 0;
                Ui.Label(zone, "… " + inside, 24f + headW + 8f, 0f, 80f, 26f, 12f, Skin.Note);
                if (group.Length > 0) MarkSeen(group, seen);
                zone.sizeDelta = new Vector2(w, 28f);
                return 28f;
            }
            List<string> tags = byGroup.TryGetValue(group, out List<string> own) ? new List<string>(own) : new List<string>();
            h += Flow(zone, Shown(tags), 20f, h, w - 20f);
            if (group.Length > 0)
                foreach (string child in Workshop.Names(scope, true))
                    if (Workshop.Upstream(scope, child) == group) h += Zone(zone, child, 28f, h, w - 28f, seen) + 6f;
            zone.sizeDelta = new Vector2(w, h);
            return h;
        }

        private static void MarkSeen(string group, HashSet<string> seen)
        {
            foreach (string child in Workshop.Names(scope, true))
                if (Workshop.Upstream(scope, child) == group && seen.Add(child)) MarkSeen(child, seen);
        }

        // 一排排标签小框，放不下就折行。返回占的高度。
        private static float Flow(RectTransform parent, List<string> tags, float x, float y, float w)
        {
            const float chipH = 24f, gap = 6f;
            float cx = 0f, cy = 0f;
            foreach (string tag in tags)
            {
                string name = tag;
                bool mine = Workshop.Current.tags.TryGetValue(name, out Workshop.Tag t) && t.scope == scope && !t.group;
                float cw = Mathf.Ceil(Ui.MeasureBody(name, 13f)) + 18f;
                if (cx > 0f && cx + cw > w) { cx = 0f; cy += chipH + gap; }
                bool fixedTag = Workshop.Fixed(scope, name), picked = picks.Contains(name) || (mine && name == newTag);
                Image chip = Ui.Box(parent, x + cx, y + cy, cw, chipH, picked ? Skin.PanelOn : mine ? Skin.ChipMine : Skin.Chip, Color.white, !fixedTag);
                Ui.Label(chip.transform, name, 9f, 0f, cw - 12f, chipH, 13f, mine ? Skin.Teal : fixedTag ? Skin.TealDim : Skin.Text);
                if (!fixedTag)   // 默认标签不能拖也不能选
                {
                    chipRects.Add(new KeyValuePair<string, RectTransform>(name, (RectTransform)chip.transform));
                    chip.gameObject.AddComponent<DragChip>().Set(() => Clicked(name, mine), viewAt == 0 ? (Action<string>)(to => Dropped(name, false, to)) : null,
                        () => picks.Contains(name) && picks.Count > 1 ? "  +" + (picks.Count - 1) : null);
                }
                cx += cw + gap;
            }
            return tags.Count == 0 ? 0f : cy + chipH + gap;
        }

        // 点一个能拖的标签：按住 Ctrl = 加进 / 移出多选；否则只选它（你建的同时填进右边输入框）
        private static void Clicked(string name, bool mine)
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) { if (!picks.Remove(name)) picks.Add(name); Build(); return; }
            picks.Clear();
            picks.Add(name);
            if (mine) Select(name, false); else Build();
        }

        // 框选：框碰到的能拖的标签全部选中（按住 Ctrl = 在原来的基础上加）
        private static void Boxed(Rect area)
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) picks.Clear();
            var corners = new Vector3[4];
            foreach (KeyValuePair<string, RectTransform> chip in chipRects)
            {
                if (chip.Value == null) continue;
                chip.Value.GetWorldCorners(corners);   // 整屏画布：世界坐标就是屏幕坐标
                if (area.Overlaps(Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y))) picks.Add(chip.Key);
            }
            Build();
        }

        // 拖完松手：name（标签或标签组）放进 to 这个组（空 = 未分组 / 顶层）
        private static void Dropped(string name, bool group, string to)
        {
            if (group)
            {
                if (to == name || (Workshop.Upstream(scope, name) ?? "") == to) return;
                if (scope == Workshop.Item) return;
                if (!Workshop.Bind(name, to)) { Say(T("不能挂到自己的下级下面。", "A group cannot go under one of its own children."), true); Build(); return; }
            }
            else
            {
                // 拖的是多选里的一个：整批一起走
                var moving = picks.Contains(name) ? new List<string>(picks) : new List<string> { name };
                moving.RemoveAll(t => Workshop.Fixed(scope, t) || (Workshop.GroupOfTag(scope, t) ?? "") == to);
                if (moving.Count == 0) return;
                foreach (string t in moving) Workshop.Place(scope, t, to);
                picks.Clear();
            }
            Touch();
        }

        // ── 抽选：标签组的条件 ──

        private static void Pools(RectTransform c)
        {
            // 0.8.1 “必须带 / 不能带”是标签自己的条件（符合的目标自动带上这个标签）；标签组的池子 = 组里各标签的合集，在“标签”页看数量
            List<string> groups = Workshop.Names(scope, false);
            if (groups.Count == 0) { Ui.Label(c, T("还没有自建的标签，去“标签”页新增一个。", "No tags of your own yet. Create one on the Tags page."), 0f, 150f, CW, 40f, 18f, Skin.Note, TextAlignmentOptions.Center); return; }
            if (poolPicked == null || !groups.Contains(poolPicked)) poolPicked = groups[0];
            Workshop.Tag g = Workshop.Current.tags[poolPicked];
            List<string> conds = Workshop.BuiltIn(scope);
            foreach (string name in Workshop.Names(scope, false)) if (name != poolPicked) conds.Add(name);
            condAt = Mathf.Clamp(condAt, 0, Mathf.Max(0, conds.Count - 1));
            List<Workshop.Target> pool = Workshop.Pool(poolPicked);

            Ui.Info(c, T("标签", "Tag"), T("别的 mod 按标签名向框架要图时，从这个池子里抽。只列当前类别里你建的标签。", "Other mods draw from this pool by tag name."), 0f, 0f, 28f);
            Ui.Dropdown(c, groups, groups.IndexOf(poolPicked), LeftW - 320f, 0f, 320f, 28f, i => { poolPicked = groups[i]; Build(); });
            Ui.Info(c, T("必须带", "Must have"), T("列出的标签全部带齐才进池子。点一下移除。", "All of these are required. Click one to remove it."), 0f, 44f);
            Ui.Chips(c, new List<string>(g.with), 0f, 72f, LeftW, 84f, _ => Skin.ChipMine, name => { g.with.Remove(name); Touch(); }, T("无", "None"));
            Ui.Info(c, T("不能带", "Must not have"), T("带了其中任何一个就排除。点一下移除。", "Any one of these excludes the target. Click one to remove it."), 0f, 164f);
            Ui.Chips(c, new List<string>(g.without), 0f, 192f, LeftW, 84f, _ => Skin.ChipNo, name => { g.without.Remove(name); Touch(); }, T("无", "None"));
            Ui.Dropdown(c, conds, condAt, 0f, 290f, LeftW - 210f, 28f, i => { condAt = i; Build(); });
            Ui.Button(c, T("必须带", "Must have"), LeftW - 200f, 290f, 96f, 28f, () => AddCondition(g.with, conds), false, 15f);
            Ui.Button(c, T("不能带", "Must not"), LeftW - 96f, 290f, 96f, 28f, () => AddCondition(g.without, conds), false, 15f);

            RectTransform r = Ui.Rect(c, "right", RightX, 0f, RightW, CH);
            Ui.Info(r, T("现在抽得到", "In the pool now"), T("按左边的条件、对照目录实时算。黄框 = 在“目标”页点名贴进来的，不受条件影响。", "Computed from the catalog. Yellow = tagged directly, always in."), 0f, 0f, 30f, true);
            Ui.Label(r, pool.Count.ToString(), RightW - 120f, -4f, 120f, 36f, 32f, Skin.Pick, TextAlignmentOptions.MidlineRight, true);
            Ui.Box(r, 0f, 32f, RightW, 1f, Skin.White, Skin.Line);
            string group = poolPicked;
            Ui.Chips(r, pool.ConvertAll(t => t.id), 0f, 42f, RightW, 330f, id => Workshop.Mine(scope, id).Contains(group) ? Skin.ChipGroup : Skin.Chip, null, T("空", "Empty"));
            string warn = g.with.Exists(w => g.without.Contains(w)) ? T("同一个标签既必须带又不能带。", "A tag is both required and excluded.") : pool.Count == 0 ? T("池子是空的，要图的 mod 会拿不到。", "The pool is empty.") : "";
            if (warn.Length > 0) Ui.Label(r, warn, 0f, 384f, RightW, 24f, 14f, Skin.Warn);
        }

        private static void AddCondition(List<string> into, List<string> conds)
        {
            if (conds.Count == 0) return;
            string name = conds[Mathf.Clamp(condAt, 0, conds.Count - 1)];
            if (into.Contains(name)) return;
            into.Add(name);
            Touch();
        }

        // ── 导出：注册表格预览 ──

        private static void Out(RectTransform c)
        {
            Ui.Info(c, T("注册文件", "Registration file"), T("立绘扩展和道具扩展导出在同一份文件里。点“导出”写到下面这个位置，下次读档自动读入；放进任何 mod 的文件夹（文件名 TextureExtend.csv）也能用，格式相同。", "Portraits and items go into one file. Export writes it to the path shown; it is read on the next load, and the same file also works inside any mod folder."), 0f, 0f, 26f, true);
            Ui.Label(c, "ModData/" + Painter.ModId + "/" + Workshop.CsvFile, 220f, 0f, CW - 220f, 26f, 12f, Skin.Note, TextAlignmentOptions.MidlineRight);
            string csv = Workshop.Csv();
            int lines = csv.Split('\n').Length;
            Ui.Box(c, 0f, 34f, CW, CH - 34f, Skin.Chip, Color.white);
            RectTransform body = Ui.Scroll(c, 2f, 36f, CW - 4f, CH - 38f, lines * 20f + 16f);
            TextMeshProUGUI text = Ui.Label(body, csv, 12f, 8f, CW - 28f, lines * 20f, 13f, Skin.Cream, TextAlignmentOptions.TopLeft);
            text.overflowMode = TextOverflowModes.Overflow;
        }
    }
}
