// ClaudeNote: 0.7.2 工坊窗口的皮肤（Lune 2026-11-18：不借游戏的界面工具，用 Unity 自己的 UI 组件照设计稿画）。
//   颜色取自设计稿（项目根目录 Claude的贴图扩展工坊设计稿.html，来源是游戏标题界面与“选项 / 专精”窗口）。
//   线框不用图片素材：切角双线框、页签、圆点都是开窗时现算的小贴图（几十像素见方，九宫格拉伸），关窗时全部销毁（Release）。
//   字体用游戏自己的，按当前语言向 FontLoader 要（见 Fonts）。
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ProbablyExtendFramework.WorkshopUi
{
    internal static class Skin
    {
        internal static readonly Color Ink = Hex(0x28233a), InkDeep = Hex(0x1a1726), Teal = Hex(0x8dbfba), TealDim = Hex(0x557a7b),
            Cream = Hex(0xd9d0a7), Text = Hex(0xbdbab3), Pick = Hex(0xfff29a), Note = Hex(0xd9a05e), Warn = Hex(0xd65a5a), Line = Hex(0x3d3852),
            Shade = new Color(0f, 0f, 0f, 0.55f), Clear = new Color(0f, 0f, 0f, 0f);

        internal static TMP_FontAsset Display, Body;
        // 现算的贴图：名字 → Sprite
        internal static Sprite Window, Button, ButtonOn, Tab, Panel, PanelOn, Chip, ChipMine, ChipGroup, ChipNo, Disc, Corner, White;

        private static readonly List<Object> made = new List<Object>();

        private static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f); }

        /// <summary>开窗时调一次：找字体、算贴图。已经备好就什么都不做。</summary>
        // ClaudeNote: 0.8.5 线框按屏幕像素现算（Lune：边框显示参差不齐）。
        //   线框一直是开窗时程序现画的小贴图，不是截的图。毛病出在画法：贴图按参考分辨率（1280×720）画，1 像素的线到了 1080p 要放大 1.5 倍、
        //   2K 放大 2 倍…… 1.5 像素的线落在像素格上就一边 1 像素一边 2 像素；色块的位置落在半个像素上时也一样。
        //   现在：先算出画布的缩放 S，贴图直接按屏幕像素画（线宽 = 四舍五入到整像素，至少 1），Sprite 的每单位像素数 = 100 × S，
        //   于是贴图的 1 像素正好是屏幕的 1 像素；画布另开 pixelPerfect，色块的位置吸附到整像素。
        private static float S = 1f;
        private static int Px(float units) { return Mathf.Max(1, Mathf.RoundToInt(units * S)); }

        /// <summary>画布缩放：和 CanvasScaler（按屏幕缩放、宽高各占一半）算出来的一致。</summary>
        internal static float ScaleFor(float refW, float refH)
        {
            return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(Screen.width / refW, 2f), Mathf.Log(Screen.height / refH, 2f), 0.5f));
        }

        internal static void Prepare(float scale)
        {
            if (White != null && Mathf.Approximately(scale, S)) return;
            if (White != null) Release();
            S = scale > 0.1f ? scale : 1f;
            Fonts();

            Window = Frame(64, 16, Teal, Ink, TealDim, 2);
            Button = Frame(32, 7, Teal, Ink, TealDim, 2);
            ButtonOn = Frame(32, 7, Pick, Ink, Pick, 2);
            Tab = Frame(24, 5, Color.white, Color.white, Clear, 0);   // 白底：用的时候染成青色 / 选中的黄色
            Panel = Frame(16, 0, Line, InkDeep, Clear, 1);
            PanelOn = Frame(16, 0, Pick, InkDeep, Clear, 2);
            Chip = Frame(16, 0, TealDim, InkDeep, Clear, 1);
            ChipMine = Frame(16, 0, Teal, InkDeep, Clear, 1);
            ChipGroup = Frame(16, 0, Pick, InkDeep, Clear, 1);
            ChipNo = Frame(16, 0, Warn, InkDeep, Clear, 1);
            Disc = Shape(Px(18), (x, y, s) => (x - s / 2f + 0.5f) * (x - s / 2f + 0.5f) + (y - s / 2f + 0.5f) * (y - s / 2f + 0.5f) <= s * s / 4f);
            Corner = Shape(Px(22), (x, y, s) => x <= y);   // 标题条下沿的斜切：左上实、右下空（贴图的 y 朝上，所以是 x ≤ y）
            White = Shape(4, (x, y, s) => true);
        }

        // ClaudeNote: 0.7.5 字体（Lune 实测：0.7.2 的窗口里中文显示不出来）。
        //   成因：我当时是从标题界面的现成文字上“借”字体——正文借的是新游戏面板的说明文字，而那个面板没打开过，
        //     它身上负责“按语言换字体”的组件还没运行，手里拿的仍是西文字体，没有中文字形。
        //   游戏自己的做法：每种语言一套字体，统一由 FontLoader 按“用途 + 当前语言”给（TMPLocalizer 就是这么换的）。
        //   现在直接问 FontLoader 要：标题字 = Lubrifont 这个用途，正文字 = Noto_Sans 这个用途——中文下分别是游戏的中文标题字和中文正文字，
        //     换成日文、韩文也会跟着对。FontLoader 不在时才退回借字体的老办法。
        // ClaudeNote: 0.8.4 Lune：“字体又丢了”。0.7.5 按“用途 + 当前语言”问 FontLoader 要字体，这和游戏自己的做法一致，
        //   但它只在语言代码是游戏列出的几种时才给中文字体，其余（以及 FontLoader 还没醒的时候）给的是西文字体——没有中文字形，字就是空的。
        //   我当时只照搬了做法，没有验证拿到的字体到底能不能写中文。
        //   现在按“能不能写出要用的字”来挑：每个候选字体问一遍 HasCharacter（含它的备用字体，动态字体允许现加字形），
        //   第一个能写的就用；候选依次是：FontLoader 按用途给的 → FontLoader 的简中字体 → 场景里正在显示的文字用的字体 → TMP 默认字体。
        //   挑中了哪个写进日志（Entry.Info），下次再丢字可以直接看日志。
        private const string Probe = "贴图扩展工坊标签立绘道具";

        private static void Fonts()
        {
            var candidates = new List<TMP_FontAsset>();
            UnityEngine.Localization.Locale locale = null;
            try
            {
                locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
                FontLoader loader = FontLoader.Instance;
                if (loader != null && locale != null)
                {
                    candidates.Add(loader.GetFontByRole(FontLoader.FontRole.Lubrifont, locale));
                    candidates.Add(loader.GetFontByRole(FontLoader.FontRole.Noto_Sans, locale));
                }
                if (loader != null) { candidates.Add(loader.lubrifont); candidates.Add(loader.simplifiedChineseFont); candidates.Add(loader.westernFont); }
            }
            catch (System.Exception ex) { Health.Report("workshop fonts", ex); }
            MainMenuUIController menu = MainMenuUIController.Instance;
            TMP_Text title = menu != null && menu.moddingButton != null ? menu.moddingButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (title != null) candidates.Add(title.font);
            foreach (TMP_Text t in Object.FindObjectsOfType<TMP_Text>()) if (t != null && t.isActiveAndEnabled) candidates.Add(t.font);
            candidates.Add(TMP_Settings.defaultFontAsset);

            string probe = Entry.Chinese ? Probe : "Texture Workshop";
            Display = FirstThatWrites(candidates, 0, probe);   // 标题字：优先第一个候选（游戏的标题字），写不出再往后找
            Body = FirstThatWrites(candidates, 1, probe);
            if (Display == null) Display = Body ?? TMP_Settings.defaultFontAsset;
            if (Body == null) Body = Display;
            Entry.Info("workshop fonts: display " + (Display != null ? Display.name : "none") + " · body " + (Body != null ? Body.name : "none")
                + " · locale " + (locale != null ? locale.Identifier.Code : "none") + (Writes(Body, probe) ? "" : " · WARNING: no font found that has the needed glyphs"));
        }

        private static TMP_FontAsset FirstThatWrites(List<TMP_FontAsset> list, int start, string text)
        {
            for (int i = 0; i < list.Count; i++)
            {
                TMP_FontAsset f = list[(start + i) % list.Count];
                if (Writes(f, text)) return f;
            }
            return null;
        }

        private static bool Writes(TMP_FontAsset font, string text)
        {
            if (font == null) return false;
            try
            {
                foreach (char c in text) if (!font.HasCharacter(c, true, true)) return false;
                return true;
            }
            catch (System.Exception) { return false; }
        }

        /// <summary>关窗时调：现算的贴图全部销毁。</summary>
        internal static void Release()
        {
            foreach (Object o in made) if (o != null) Object.Destroy(o);
            made.Clear();
            Window = Button = ButtonOn = Tab = Panel = PanelOn = Chip = ChipMine = ChipGroup = ChipNo = Disc = Corner = White = null;
            Display = Body = null;
        }

        // 切角框：外线 line 粗 thick 像素；inner 不透明时在往里 5 像素处再画一圈细线（原版的双线）；cut = 切角大小
        private static Sprite Frame(int size, int cut, Color line, Color fill, Color inner, int thick)
        {
            // 参数是参考分辨率下的尺寸，这里换成屏幕像素
            int gap = Px(3), innerW = Px(1);
            cut = cut > 0 ? Px(cut) : 0;
            thick = thick > 0 ? Px(thick) : 0;
            int border = cut + thick + gap + innerW + Px(1);
            size = Mathf.Max(Px(size), border * 2 + 2);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int rx = size - 1 - x, ry = size - 1 - y;
                    float inset = Mathf.Min(Mathf.Min(x, y), Mathf.Min(rx, ry));
                    if (cut > 0)
                    {
                        float diagonal = Mathf.Min(Mathf.Min(x + y, rx + y), Mathf.Min(x + ry, rx + ry)) - cut;
                        inset = Mathf.Min(inset, diagonal / 1.414f);
                    }
                    Color c = inset < 0f ? Clear : inset < thick ? line : inner.a > 0f && inset >= thick + gap && inset < thick + gap + innerW ? inner : fill;
                    px[y * size + x] = c;
                }
            return Make(px, size, new Vector4(border, border, border, border));
        }

        private static Sprite Shape(int size, System.Func<int, int, int, bool> inside)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) px[y * size + x] = inside(x, y, size) ? Color.white : Clear;
            return Make(px, size, Vector4.zero);
        }

        private static Sprite Make(Color32[] px, int size, Vector4 border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "texext_workshop_skin" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f * S, 0u, SpriteMeshType.FullRect, border);
            made.Add(tex);
            made.Add(s);
            return s;
        }
    }
}
