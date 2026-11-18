// ClaudeNote: 0.7.0 标题界面的“贴图扩展工坊”按钮（Lune 2026-11-17：入口放标题界面，排在“改装件”前面）。
//   做法：克隆原版的改装件按钮（MainMenuUIController.moddingButton，公开字段）——底图、字体、悬浮效果都和原版按钮一致，
//         换掉文字和点击事件，插在它前一位。不打补丁：进标题场景时（sceneLoaded）装一次，后台每秒再看一眼作兜底，装过就不重复。
//   排版：按钮的父物体带自动排版（LayoutGroup）时，插进去就自动排好；不带时手动把改装件和它下面的按钮各往下挪一格。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProbablyExtendFramework
{
    internal static class WorkshopEntry
    {
        private const string ButtonName = "texext_workshop_button";
        private static GameObject button;
        private static bool hooked;

        internal static void Install()
        {
            if (!hooked) { SceneManager.sceneLoaded += OnScene; hooked = true; }
            Tick();
        }

        internal static void Uninstall()
        {
            if (hooked) { SceneManager.sceneLoaded -= OnScene; hooked = false; }
            if (button != null) UnityEngine.Object.Destroy(button);   // 自动排版会把后面的按钮收回原位；手动挪过的随场景重载复原
            button = null;
        }

        private static void OnScene(Scene scene, LoadSceneMode mode) { Tick(); }

        /// <summary>在标题界面且还没装按钮就装上。别处调用是空操作。</summary>
        internal static void Tick()
        {
            if (button != null || !Painter.Running) return;
            MainMenuUIController menu = MainMenuUIController.Instance;
            if (menu == null || menu.moddingButton == null) return;
            try { Add(menu.moddingButton); }
            catch (Exception ex) { Health.Report("workshop title button", ex); }
        }

        private static void Add(GameObject source)
        {
            Transform parent = source.transform.parent;
            Transform old = parent != null ? parent.Find(ButtonName) : null;
            if (old != null) { button = old.gameObject; return; }

            GameObject clone = UnityEngine.Object.Instantiate(source, parent);
            clone.name = ButtonName;
            // 原版按钮的文字由本地化组件写入，克隆出来的要让它闭嘴，否则会被写回“改装件”。
            // ClaudeNote: 0.8.4 不能直接删 LocalizeStringEvent（Lune 的日志：TMPLocalizer 依赖它，删不掉）——之前这一步其实一直失败。
            //   改成断开它的输出（代码里加的监听全撤、编辑器里连的监听关掉）并停用它；TMPLocalizer 留着，它负责按语言换字体（这正是要的）。
            foreach (LocalizeStringEvent loc in clone.GetComponentsInChildren<LocalizeStringEvent>(true))
            {
                loc.OnUpdateString.RemoveAllListeners();
                for (int i = 0; i < loc.OnUpdateString.GetPersistentEventCount(); i++) loc.OnUpdateString.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                loc.enabled = false;
            }
            TMP_Text label = clone.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = Entry.Chinese ? "贴图扩展工坊" : "Texture Workshop";
            Button b = clone.GetComponentInChildren<Button>(true);
            if (b == null) { UnityEngine.Object.Destroy(clone); Health.Report("workshop title button", "the mods button has no Button component"); return; }
            b.onClick = new Button.ButtonClickedEvent();   // 整个换掉：编辑器里绑的“打开改装件面板”也在原来那份里
            b.onClick.AddListener(Open);

            int index = source.transform.GetSiblingIndex();
            if (parent.GetComponent<LayoutGroup>() == null) MakeRoom(source.transform as RectTransform, clone.transform as RectTransform, index);
            clone.transform.SetSiblingIndex(index);
            clone.SetActive(true);
            button = clone;
        }

        // 没有自动排版：新按钮占改装件的位置，改装件和排在它下面的同列按钮各往下挪一格。
        // 一格 = 改装件与它上面那个按钮的间距；只挪“同一列、带按钮、在它下方”的，不碰同一父物体下别的东西。
        private static void MakeRoom(RectTransform source, RectTransform clone, int index)
        {
            if (source == null || clone == null) return;
            Transform parent = source.parent;
            Vector2 at = source.anchoredPosition;
            float step = 0f;
            for (int i = index - 1; i >= 0 && step == 0f; i--)
                if (parent.GetChild(i) is RectTransform above && SameColumn(above, at) && above.anchoredPosition.y > at.y) step = above.anchoredPosition.y - at.y;
            if (step <= 0f) step = source.rect.height * 1.2f;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (!(parent.GetChild(i) is RectTransform r) || r == clone || !SameColumn(r, at) || r.anchoredPosition.y > at.y + 0.5f) continue;
                r.anchoredPosition = new Vector2(r.anchoredPosition.x, r.anchoredPosition.y - step);
            }
            clone.anchoredPosition = at;
        }

        private static bool SameColumn(RectTransform r, Vector2 at)
        {
            return r.gameObject.activeSelf && Mathf.Abs(r.anchoredPosition.x - at.x) < 1f && r.GetComponentInChildren<Button>(true) != null;
        }

        private static void Open()
        {
            try
            {
                MainMenuUIController.Instance?.OnAnyButtonClick();   // 原版的按钮音效
                WorkshopWindow.Open();
            }
            catch (Exception ex) { Health.Report("workshop open", ex); }
        }
    }
}
