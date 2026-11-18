// ClaudeNote: 0.7.2 工坊窗口的零件（Unity 自带的 UI 组件：Image / Button / ScrollRect / TMP 文字与输入框）。
//   全部按“左上角为原点、像素坐标”摆（参考分辨率 1280×720，画布随屏幕等比缩放），不用自动排版：设计稿是固定版式，直接照着摆最不容易走样。
//   零件：Box 色块 / Label 文字 / Button 按钮 / Input 输入框 / Dropdown 下拉 / Scroll 滚动区 / Chips 一排小标签 / Info 带问号的设置项名。
//   下拉和悬浮说明各只有一个实例，挂在窗口根上（Popup、Tip）。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TextureExtend.WorkshopUi
{
    internal static class Ui
    {
        internal static RectTransform Root;   // 窗口根（下拉、悬浮说明挂在它上面）

        internal static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        internal static Image Box(Transform parent, float x, float y, float w, float h, Sprite sprite, Color color, bool blocks = false)
        {
            Image img = Rect(parent, "box", x, y, w, h).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            img.raycastTarget = blocks;
            return img;
        }

        internal static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool display = false, bool wrap = false)
        {
            // ClaudeNote: 0.8.8 单行文字的框太矮时整行不显示（Lune：“标签一览”“标签组管理”这几个标题一直是空的）。
            //   成因：单行文字用的是“放不下就加省略号”，而这个判断连高度也算——中文正文字体的行高约是字号的 1.45 倍，
            //     18 号字要 26.1 高，我给的框正好 26，被判成“放不下”，整行连省略号一起省掉了。15 号字 24 高的那些够，所以只丢了标题。
            //   改法：垂直居中的单行文字，框的高度不够一行半时上下各让出一点（文字位置不变）——不是逐个去调那几个标题的数字。
            bool middle = align == TextAlignmentOptions.MidlineLeft || align == TextAlignmentOptions.MidlineRight || align == TextAlignmentOptions.Midline
                || align == TextAlignmentOptions.Center || align == TextAlignmentOptions.Left || align == TextAlignmentOptions.Right;
            if (!wrap && middle && h < size * 1.7f) { float need = size * 1.7f; y -= (need - h) / 2f; h = need; }
            TextMeshProUGUI t = Rect(parent, "label", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = display ? Skin.Display : Skin.Body;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            t.richText = false;
            t.raycastTarget = false;
            t.text = text ?? "";
            return t;
        }

        /// <summary>切角双线按钮。on = 选中（黄线黄字）。</summary>
        internal static Button Button(Transform parent, string text, float x, float y, float w, float h, Action click, bool on = false, float size = 18f)
        {
            Image img = Box(parent, x, y, w, h, on ? Skin.ButtonOn : Skin.Button, Color.white, true);
            Label(img.transform, text, 0f, 0f, w, h, size, on ? Skin.Pick : Skin.Teal, TextAlignmentOptions.Center, true);
            return Clickable(img, click);
        }

        /// <summary>让一个色块可点（悬浮略亮、按下略暗）。</summary>
        internal static Button Clickable(Image img, Action click)
        {
            img.raycastTarget = true;
            Button b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock colors = b.colors;
            colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);   // 平时略压暗一点，悬浮时回到原色（Unity 的按钮着色只能变暗不能提亮）
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.fadeDuration = 0.05f;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (click != null) b.onClick.AddListener(() => { try { click(); } catch (Exception ex) { Health.Report("workshop ui", ex); } });
            return b;
        }

        /// <summary>输入框（深底、青色细线）。changed 每敲一个字调一次——只记下值，不要在里面重排窗口（会丢焦点）。</summary>
        internal static TMP_InputField Input(Transform parent, string placeholder, string value, float x, float y, float w, float h, Action<string> changed)
        {
            Image img = Box(parent, x, y, w, h, Skin.Chip, Color.white, true);
            RectTransform area = Rect(img.transform, "area", 8f, 2f, w - 16f, h - 4f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI hint = Label(area, placeholder, 0f, 0f, w - 16f, h - 4f, 15f, new Color(0.44f, 0.42f, 0.49f, 1f));
            TextMeshProUGUI text = Label(area, "", 0f, 0f, w - 16f, h - 4f, 15f, Skin.Cream);
            TMP_InputField input = img.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = Skin.Pick;
            input.customCaretColor = true;
            input.selectionColor = new Color(Skin.Teal.r, Skin.Teal.g, Skin.Teal.b, 0.4f);
            input.text = value ?? "";
            if (changed != null) input.onValueChanged.AddListener(v => changed(v));
            return input;
        }

        /// <summary>下拉框（青底深字，和原版选项窗口一样）。点开后在窗口根上弹一列选项。</summary>
        internal static void Dropdown(Transform parent, IList<string> options, int at, float x, float y, float w, float h, Action<int> picked)
        {
            at = Mathf.Clamp(at, 0, Mathf.Max(0, options.Count - 1));
            Image img = Box(parent, x, y, w, h, Skin.White, Skin.Teal, true);
            Label(img.transform, options.Count > 0 ? options[at] : "", 8f, 0f, w - 34f, h, 15f, Skin.Ink);
            Box(img.transform, w - h, 0f, h, h, Skin.White, Skin.Ink);
            Label(img.transform, "v", w - h, 0f, h, h, 14f, Skin.Teal, TextAlignmentOptions.Center, true);
            Clickable(img, () => Popup.Open((RectTransform)img.transform, options, at, picked));
        }

        /// <summary>滚动区（鼠标滚轮 / 拖动）。返回内容容器；contentHeight = 内容总高。</summary>
        internal static RectTransform Scroll(Transform parent, float x, float y, float w, float h, float contentHeight, bool bar = false)
        {
            Image view = Box(parent, x, y, w, h, Skin.White, Skin.Clear, true);   // 透明但接收滚轮
            view.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect(view.transform, "content", 0f, 0f, w, Mathf.Max(h, contentHeight));
            ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)view.transform;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            if (bar)   // 右侧一条滚动条（内容不超出时自动藏起来）
            {
                Image track = Box(view.transform, w - 8f, 0f, 8f, h, Skin.White, Skin.Line, true);
                RectTransform area = Rect(track.transform, "area", 0f, 0f, 8f, h);
                Image handle = Box(area, 0f, 0f, 8f, 20f, Skin.White, Skin.Teal, true);
                var hr = (RectTransform)handle.transform;
                hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.pivot = new Vector2(0.5f, 0.5f); hr.offsetMin = hr.offsetMax = Vector2.zero;
                Scrollbar sb = track.gameObject.AddComponent<Scrollbar>();
                sb.handleRect = hr;
                sb.targetGraphic = handle;
                sb.direction = Scrollbar.Direction.BottomToTop;
                sb.navigation = new Navigation { mode = Navigation.Mode.None };
                scroll.verticalScrollbar = sb;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            return content;
        }

        /// <summary>设置项的名字 + 圆圈问号。鼠标停在问号上显示 tip。返回占用的宽度。</summary>
        internal static float Info(Transform parent, string label, string tip, float x, float y, float h = 24f, bool header = false)
        {
            float width = 0f;
            if (!string.IsNullOrEmpty(label))
            {
                TextMeshProUGUI t = Label(parent, label, x, y, 600f, h, header ? 18f : 15f, header ? Skin.Cream : Skin.Text);
                width = Mathf.Ceil(t.GetPreferredValues(label).x) + 6f;
                ((RectTransform)t.transform).sizeDelta = new Vector2(width, h);
            }
            Image dot = Box(parent, x + width, y + (h - 16f) / 2f, 16f, 16f, Skin.Disc, Skin.Text, true);
            Label(dot.transform, "?", 0f, 0f, 16f, 16f, 12f, Skin.Ink, TextAlignmentOptions.Center);
            dot.gameObject.AddComponent<Hover>().Set(tip, dot);
            return width + 16f;
        }

        /// <summary>一排小标签，放不下就折行；超出高度的不画，末尾写“+N”。remove 不为空 = 可以点掉（removable 给了就只有它说行的那些能点掉）。</summary>
        internal static void Chips(Transform parent, IList<string> names, float x, float y, float w, float h, Func<string, Sprite> frame, Action<string> remove, string none, Func<string, bool> removable = null)
        {
            Action<string> removeAll = remove;
            if (names.Count == 0) { Label(parent, none, x, y, w, 24f, 14f, Skin.Note); return; }
            float cx = 0f, cy = 0f;
            const float RowH = 26f, Gap = 6f;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                remove = removeAll != null && (removable == null || removable(name)) ? removeAll : null;
                float cw = Mathf.Ceil(MeasureBody(name, 13f)) + (remove != null ? 34f : 18f);
                if (cx > 0f && cx + cw > w) { cx = 0f; cy += RowH + Gap; }
                if (cy + RowH > h)   // 放不下了：在最后一行的右端写还剩几个
                {
                    Image more = Box(parent, x + w - 48f, y + Mathf.Max(0f, cy - RowH - Gap), 48f, RowH, Skin.White, Skin.Ink);
                    Label(more.transform, "+" + (names.Count - i), 0f, 0f, 48f, RowH, 13f, Skin.Note, TextAlignmentOptions.Center);
                    return;
                }
                Sprite s = frame != null ? frame(name) : Skin.Chip;
                Image chip = Box(parent, x + cx, y + cy, cw, RowH, s, Color.white, remove != null);
                Color color = s == Skin.ChipMine ? Skin.Teal : s == Skin.ChipGroup ? Skin.Pick : s == Skin.ChipNo ? Skin.Warn : Skin.Text;
                Label(chip.transform, name, 9f, 0f, cw - (remove != null ? 28f : 12f), RowH, 13f, color);
                if (remove != null)
                {
                    Label(chip.transform, "x", cw - 20f, 0f, 14f, RowH, 13f, color, TextAlignmentOptions.Center);
                    Clickable(chip, () => removeAll(name));
                }
                cx += cw + Gap;
            }
        }

        private static TextMeshProUGUI ruler;

        // 量一段正文字的宽度（借一个不显示的文字组件）
        internal static float MeasureBody(string text, float size)
        {
            if (ruler == null)
            {
                ruler = Label(Root, "", 0f, 0f, 10f, 10f, size, Skin.Clear);
                ruler.gameObject.name = "ruler";   // 文字是空的、颜色透明：不会画出任何东西
            }
            ruler.fontSize = size;
            return ruler.GetPreferredValues(text ?? "").x;
        }
    }

    /// <summary>可以把东西拖进来的区域（标签一览里的一个标签组）。</summary>
    internal sealed class DropZone : MonoBehaviour { internal string group; }

    /// <summary>框选：在空白处按住拖出一个框，松手时把框的范围（屏幕坐标）交出去。挂在滚动区的内容上。</summary>
    internal sealed class Marquee : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private Action<Rect> done;
        private RectTransform box;
        private Vector2 from;

        internal void Set(Action<Rect> onDone) { done = onDone; }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Ui.Root == null) return;
            from = e.pressPosition;
            Image img = Ui.Box(Ui.Root, 0f, 0f, 1f, 1f, Skin.White, new Color(Skin.Teal.r, Skin.Teal.g, Skin.Teal.b, 0.25f));
            box = (RectTransform)img.transform;
            box.pivot = Vector2.zero;
            Place(e.position);
        }

        public void OnDrag(PointerEventData e) { if (box != null) Place(e.position); }

        public void OnEndDrag(PointerEventData e)
        {
            if (box == null) return;
            Destroy(box.gameObject);
            box = null;
            Rect area = Area(e.position);
            try { done?.Invoke(area); } catch (Exception ex) { Health.Report("workshop ui", ex); }
        }

        private void OnDisable() { if (box != null) Destroy(box.gameObject); box = null; }

        private Rect Area(Vector2 to) { return Rect.MinMaxRect(Mathf.Min(from.x, to.x), Mathf.Min(from.y, to.y), Mathf.Max(from.x, to.x), Mathf.Max(from.y, to.y)); }

        private void Place(Vector2 to)
        {
            Rect a = Area(to);
            float scale = box.lossyScale.x > 0.001f ? box.lossyScale.x : 1f;
            box.position = new Vector3(a.xMin, a.yMin, 0f);   // 整屏画布：世界坐标就是屏幕坐标
            box.sizeDelta = new Vector2(a.width / scale, a.height / scale);
        }
    }

    /// <summary>可以点、可以拖走的小标签。拖起来时跟着鼠标走的是它的一个影子；松手时看鼠标底下是哪个 DropZone。</summary>
    internal sealed class DragChip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private Action click;
        private Action<string> drop;
        private Func<string> badge;
        private GameObject ghost;

        /// <summary>click 可空（不可选中）；drop 可空（不可拖）。drop 收到的是松手处的标签组。badge：拖起来时影子上追加的字（多选时的“+N”）。</summary>
        internal void Set(Action onClick, Action<string> onDrop, Func<string> onBadge = null) { click = onClick; drop = onDrop; badge = onBadge; }

        public void OnPointerClick(PointerEventData e) { if (!e.dragging) Safe(click); }

        public void OnBeginDrag(PointerEventData e)
        {
            if (drop == null || Ui.Root == null) return;
            ghost = Instantiate(gameObject, Ui.Root, false);
            DragChip copy = ghost.GetComponent<DragChip>();
            if (copy != null) Destroy(copy);
            foreach (Graphic g in ghost.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;   // 影子不能挡住鼠标底下的区域
            ghost.AddComponent<CanvasGroup>().alpha = 0.85f;
            string extra = badge?.Invoke();
            TMP_Text text = string.IsNullOrEmpty(extra) ? null : ghost.GetComponentInChildren<TMP_Text>(true);
            if (text != null) { text.overflowMode = TextOverflowModes.Overflow; text.text += extra; }
            ghost.transform.position = e.position;
        }

        public void OnDrag(PointerEventData e) { if (ghost != null) ghost.transform.position = e.position; }

        public void OnEndDrag(PointerEventData e)
        {
            if (ghost == null) return;
            Destroy(ghost);
            ghost = null;
            var hits = new List<RaycastResult>();
            if (EventSystem.current != null) EventSystem.current.RaycastAll(e, hits);
            foreach (RaycastResult hit in hits)
            {
                DropZone zone = hit.gameObject != null ? hit.gameObject.GetComponentInParent<DropZone>() : null;
                if (zone == null) continue;
                string to = zone.group;
                Safe(() => drop(to));
                return;
            }
        }

        private void OnDisable() { if (ghost != null) Destroy(ghost); ghost = null; }

        private static void Safe(Action a)
        {
            if (a == null) return;
            try { a(); } catch (Exception ex) { Health.Report("workshop ui", ex); }
        }
    }

    /// <summary>鼠标停在上面时显示说明（挂在圆圈问号上）。</summary>
    internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private string tip;
        private Image dot;

        internal void Set(string text, Image image) { tip = text; dot = image; }
        public void OnPointerEnter(PointerEventData e) { if (dot != null) dot.color = Skin.Pick; Tip.Show(tip, (RectTransform)transform); }
        public void OnPointerExit(PointerEventData e) { if (dot != null) dot.color = Skin.Text; Tip.Hide(); }
        private void OnDisable() { Tip.Hide(); }
    }

    /// <summary>悬浮说明（整个窗口只有一个）。</summary>
    internal static class Tip
    {
        private const float Width = 320f;
        private static GameObject box;

        internal static void Show(string text, RectTransform anchor)
        {
            Hide();
            if (string.IsNullOrEmpty(text) || Ui.Root == null) return;
            Vector2 at = Popup.LocalTopLeft(anchor);
            RectTransform r = Ui.Rect(Ui.Root, "tip", 0f, 0f, Width, 10f);
            box = r.gameObject;
            Image bg = box.AddComponent<Image>();
            bg.sprite = Skin.ChipMine; bg.type = Image.Type.Sliced; bg.color = Color.white; bg.raycastTarget = false;
            TextMeshProUGUI t = Ui.Label(r, text, 10f, 8f, Width - 20f, 10f, 14f, Skin.Text, TextAlignmentOptions.TopLeft, false, true);
            t.overflowMode = TextOverflowModes.Overflow;
            float h = Mathf.Ceil(t.GetPreferredValues(text, Width - 20f, 0f).y) + 16f;
            ((RectTransform)t.transform).sizeDelta = new Vector2(Width - 20f, h - 16f);
            r.sizeDelta = new Vector2(Width, h);
            // 默认在问号右下；右边或下边放不下就翻到另一侧
            Vector2 size = Ui.Root.rect.size;
            float x = at.x + 22f, y = at.y + 22f;
            if (x + Width > size.x - 8f) x = at.x - Width - 6f;
            if (y + h > size.y - 8f) y = at.y - h - 6f;
            r.anchoredPosition = new Vector2(Mathf.Max(8f, x), -Mathf.Max(8f, y));
        }

        internal static void Hide()
        {
            if (box != null) UnityEngine.Object.Destroy(box);
            box = null;
        }
    }

    /// <summary>下拉框点开后的那一列选项（整个窗口只有一个）。点选项或点别处都会关。</summary>
    internal static class Popup
    {
        private const float RowH = 28f, MaxH = 280f;
        private static GameObject layer;

        /// <summary>零件左上角在窗口根里的位置（左上为原点、y 朝下）。</summary>
        internal static Vector2 LocalTopLeft(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);   // 1 = 左上
            Vector3 local = Ui.Root.InverseTransformPoint(corners[1]);
            Rect root = Ui.Root.rect;
            return new Vector2(local.x - root.xMin, root.yMax - local.y);
        }

        internal static void Open(RectTransform anchor, IList<string> options, int at, Action<int> picked)
        {
            Close();
            if (Ui.Root == null || options.Count == 0) return;
            Vector2 size = Ui.Root.rect.size, top = LocalTopLeft(anchor);
            float w = anchor.rect.width, h = Mathf.Min(MaxH, options.Count * RowH + 4f);
            float y = top.y + anchor.rect.height;
            if (y + h > size.y - 6f) y = Mathf.Max(6f, top.y - h);   // 下面放不下就往上开
            Image catcher = Ui.Box(Ui.Root, 0f, 0f, size.x, size.y, Skin.White, Skin.Clear, true);   // 点别处 = 关
            layer = catcher.gameObject;
            layer.name = "popup";
            Ui.Clickable(catcher, Close).transition = Selectable.Transition.None;
            Ui.Box(layer.transform, top.x, y, w, h, Skin.ChipMine, Color.white, true);
            RectTransform list = Ui.Scroll(layer.transform, top.x + 2f, y + 2f, w - 4f, h - 4f, options.Count * RowH);
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                Image row = Ui.Box(list, 0f, i * RowH, w - 4f, RowH, Skin.White, i == at ? Skin.Line : Skin.InkDeep, true);
                Ui.Label(row.transform, options[i], 8f, 0f, w - 20f, RowH, 14f, i == at ? Skin.Pick : Skin.Text);
                Ui.Clickable(row, () => { Close(); picked?.Invoke(index); });
            }
            if (at * RowH > h) list.anchoredPosition = new Vector2(0f, Mathf.Min(options.Count * RowH - (h - 4f), at * RowH));   // 滚到当前选中的那一行
        }

        internal static void Close()
        {
            if (layer != null) UnityEngine.Object.Destroy(layer);
            layer = null;
        }
    }
}
