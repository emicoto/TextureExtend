// ClaudeNote: 右键菜单最下面挂一个自己画的“选图”小按钮（Lune 2026-11-16 定：不碰原版私有字段，不改原版菜单的判断）。
//   挂载点：ItemContextHandler 的几个事件方法，全部是 Postfix，原版逻辑原样执行完再做我们的事。
//     · 右键按下（OnEventPress）后：原版菜单开着、鼠标下的物品本框架有图可选 → 在原版菜单正下方显示我们的小窗口，宽度跟原版菜单一致。
//     · 松开（OnEventRelease）：点中的是我们的按钮 → 关菜单、打开选图窗口。
//     · 每帧（OnEventUpdate，只在小窗口开着时干活）：鼠标悬停换高亮色。
//     · 菜单收起（OnEventReset）：小窗口一起收。
//   原版菜单与它的位置只通过公开成员和场景里的物体名（ContextMenuNode）找；我们的物体也标成 ContextMenuNode，
//   这样鼠标停在小按钮上时原版不会以为移出了菜单而把它关掉。
//   另：原版外观循环（ToggleAppearanceHelper：储物舱颜色）记下几种图，选图窗口里直接列出来。
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TextureExtend
{
    internal static class PickButton
    {
        private const string MenuName = "ContextMenuNode", OwnName = "TextureExtendPickNode", Tag = "ContextMenuNode";
        private static PixelWindow window;
        private static RichTextElement button;
        private static GameItem item;
        private static bool hovered;

        internal static bool Visible => item != null && Alive();

        // ClaudeNote: 0.4.1 换场景（读档）时我们的小窗口物体会被 Unity 销毁，但静态引用还在；
        //   再对它 Hide() 会在原版输入系统里抛空引用，把整个点击处理打断（Lune 2026-11-16 实测：大部分点击失效）。
        //   所以每次用之前先确认物体还活着，死了就丢掉引用、下次重建。
        private static bool Alive()
        {
            if (window == null) return false;
            if (window.handler != null && window.handler.gameObject != null) return true;
            window = null;
            button = null;
            return false;
        }
        internal static bool Is(RichTextElement element) { return element != null && element == button; }

        private static string Label => Entry.Chinese ? "选图" : "Pick image";

        /// <summary>原版菜单刚打开：要不要、在哪里显示我们的按钮。</summary>
        internal static void ShowFor(ItemContextHandler handler)
        {
            Hide();
            Transform menu = Menu();
            if (menu == null || !menu.gameObject.activeInHierarchy) return;
            GameItem target = RenderHandler.RaycastElement<GameItem>(Input.mousePosition);
            if (target == null || !Picker.HasOptions(target)) return;
            if (!Build()) return;
            item = target;
            hovered = false;
            Paint();
            window.Show(Input.mousePosition, handler.contextTranslation);
            FollowMenu((RectTransform)menu);
        }

        internal static void Hide()
        {
            item = null;
            try { if (Alive()) window.Hide(); }
            catch (Exception) { window = null; button = null; }
        }

        /// <summary>停用：收起并销毁我们的小窗口物体。</summary>
        internal static void Shutdown()
        {
            Hide();
            try { if (Alive()) UnityEngine.Object.Destroy(window.handler.gameObject); }
            catch (Exception) { }
            window = null;
            button = null;
            menuCache = null;
        }

        /// <summary>每帧（只在显示时）：悬停高亮。</summary>
        internal static void Hover(ItemContextHandler handler)
        {
            bool now = Is(handler.FindRichTextNode(Input.mousePosition, handler.overlayCanvas));
            if (now == hovered) return;
            hovered = now;
            Paint();
        }

        /// <summary>松开鼠标：点中了我们的按钮就关菜单、开选图。</summary>
        internal static void Click(ItemContextHandler handler)
        {
            if (!Is(handler.FindRichTextNode(Input.mousePosition, handler.overlayCanvas))) return;
            GameItem target = item;
            Hide();
            Transform menu = Menu();
            if (menu != null) menu.gameObject.SetActive(false);   // 原版下一帧发现鼠标不在菜单上，会自己收尾
            Picker.Open(target);
        }

        private static Transform menuCache;

        // 原版菜单的物体（名字 ContextMenuNode）。先找 overlay 层的直接子物体；显示时可能被挪进停靠容器，找不到再往下搜一遍，找到就记住。
        private static Transform Menu()
        {
            if (menuCache != null && menuCache.name == MenuName) return menuCache;
            TreeNodeRender root = RenderHandler.current != null ? RenderHandler.current.overlayRoot : null;
            if (root == null) return null;
            menuCache = root.transform.Find(MenuName);
            if (menuCache != null) return menuCache;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == MenuName) return menuCache = t;
            return null;
        }

        // 照原版菜单的建法：PixelWindow + 一行 RichTextElement，挂在同一个 overlay 层
        private static bool Build()
        {
            if (Alive()) return true;
            if (RenderHandler.current == null || RenderHandler.current.overlayRoot == null) return false;
            window = new PixelWindow();
            window.forceRaycast = true;
            window.handler.SetParent(RenderHandler.current.overlayRoot);
            window.xPadding = 2;
            window.yPadding = 2;
            window.handler.gameObject.name = OwnName;
            button = new RichTextElement();
            button.handler.gameObject.name = OwnName + "Button";
            window.Attach(button);
            Paint();
            window.Validate();
            foreach (Transform t in window.handler.GetComponentsInChildren<Transform>(true)) t.gameObject.tag = Tag;
            window.Hide();
            return true;
        }

        private static void Paint()
        {
            button?.SetText(Label, 10000, hovered ? RenderHandler.ColorPalette.VeryLightGray : RenderHandler.ColorPalette.LightGray);
        }

        // 宽度与原版菜单一致，贴在它正下方（用世界坐标对齐角点，不依赖两边的锚点设置）
        private static void FollowMenu(RectTransform menu)
        {
            RectTransform own = window.rectTransform;
            float unitsPerPixel = window.widthPixels > 0 ? own.rect.width / window.widthPixels : 0f;
            if (unitsPerPixel > 0f)
            {
                int menuPixels = Mathf.RoundToInt(menu.rect.width / unitsPerPixel);
                button.SetFixedSize(Mathf.Max(8, menuPixels - window.xPadding * 2), -1);
                window.Validate();
                foreach (Transform t in window.handler.GetComponentsInChildren<Transform>(true)) t.gameObject.tag = Tag;
            }
            var menuCorners = new Vector3[4];
            var ownCorners = new Vector3[4];
            menu.GetWorldCorners(menuCorners);   // 0 左下，1 左上，2 右上，3 右下
            own.GetWorldCorners(ownCorners);
            own.position += menuCorners[0] - ownCorners[1];
        }
    }

    // ClaudeNote: 0.6.0 补丁体（经 Ports 逐个安装，见 PickButton.Install）。原版每次收起菜单都会走 Reset：绝不能往外抛异常。
    internal static class PickButtonHooks
    {
        internal static void Install()
        {
            const string feature = "右键菜单的“选图”按钮";
            Ports.Patch("ItemContextHandler.OnEventPress", feature, () => AccessTools.Method(typeof(ItemContextHandler), "OnEventPress"), postfix: new HarmonyMethod(typeof(PickButtonHooks), nameof(Press)));
            Ports.Patch("ItemContextHandler.OnEventRelease", feature, () => AccessTools.Method(typeof(ItemContextHandler), "OnEventRelease"), postfix: new HarmonyMethod(typeof(PickButtonHooks), nameof(Release)));
            Ports.Patch("ItemContextHandler.OnEventUpdate", feature, () => AccessTools.Method(typeof(ItemContextHandler), "OnEventUpdate"), postfix: new HarmonyMethod(typeof(PickButtonHooks), nameof(Update)));
            Ports.Patch("ItemContextHandler.OnEventReset", feature, () => AccessTools.Method(typeof(ItemContextHandler), "OnEventReset"), postfix: new HarmonyMethod(typeof(PickButtonHooks), nameof(Reset)));
            // 记下原版外观循环的几种图（物品每次生成都会调一次，按物品 ID 覆盖）
            Ports.Patch("ToggleAppearanceHelper.MakeSpriteCyclable", "原版外观循环道具的手动选图",
                () => AccessTools.Method(typeof(ToggleAppearanceHelper), nameof(ToggleAppearanceHelper.MakeSpriteCyclable)), postfix: new HarmonyMethod(typeof(PickButtonHooks), nameof(NativeLooks)));
        }

        private static void Press(ItemContextHandler __instance, ISet<KeyCode> keysPressed)
        {
            try { if (keysPressed != null && keysPressed.Contains(__instance.triggerKey)) PickButton.ShowFor(__instance); }
            catch (Exception ex) { Entry.Warn("pick button (show) failed: " + ex.Message); PickButton.Hide(); }
        }

        private static void Release(ItemContextHandler __instance, ISet<KeyCode> keysReleased)
        {
            if (!PickButton.Visible || keysReleased == null || !keysReleased.Contains(__instance.key)) return;
            try { PickButton.Click(__instance); }
            catch (Exception ex) { Entry.Warn("pick button (click) failed: " + ex.Message); PickButton.Hide(); }
        }

        private static void Update(ItemContextHandler __instance)
        {
            if (!PickButton.Visible) return;
            try { PickButton.Hover(__instance); }
            catch (Exception) { try { PickButton.Hide(); } catch (Exception) { } }
        }

        private static void Reset()
        {
            try { PickButton.Hide(); }
            catch (Exception) { }
        }

        private static void NativeLooks(GameItem item, string atlasPath, string[] spriteVariants)
        {
            try { PaintRules.AddNativeLook(item?.identifier, atlasPath, spriteVariants); }
            catch (Exception ex) { Entry.Warn("native looks not recorded: " + ex.Message); }
        }
    }
}
