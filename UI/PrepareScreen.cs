// ClaudeNote: 1.0.0 rc9 读档时的“正在准备贴图”画面（Lune 2026-11-18：最重的那一次是读档后的集中预处理，别人电脑不一定跑得动，
//   给游戏加一个贴图缓存中的临时画面让人等一等；入睡那次靠过夜的淡出就够）。
//   游戏读档没有可以拖住的加载画面（PlayerStore.LoadGame 读完直接进店），所以框架自己画一层：
//     整屏半透明遮罩 + 一行进度（第几张 / 共几张），挡住点击（遮罩自己接射线 + ClickShield 关掉别的画布的射线），
//     遮罩在的时候后台每帧多画几张（不用再顾及操作手感），画完自动收起。
//   什么时候出现：只在读档后的那次集中预处理，而且已经干了一小会儿（ShowAfter）还有活——缓存都在时一闪而过，不出现。
//   不依赖工坊的皮肤（标题界面才准备），只用 Unity 自带的 UI 与 TextMeshPro 的默认字体。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyExtendFramework
{
    internal static class PrepareScreen
    {
        internal const float ShowAfter = 0.4f;     // 干了这么久还没完才出现
        internal const float FrameBudget = 0.05f;  // 出现后每帧最多干这么久（约 20 帧 / 秒，进度条还在动）
        private static GameObject canvas;
        private static TextMeshProUGUI line;
        private static Image bar;

        internal static bool Showing => canvas != null;

        internal static void Show()
        {
            if (canvas != null) return;
            try
            {
                canvas = new GameObject("PEF_PrepareScreen", typeof(RectTransform));
                Canvas c = canvas.AddComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = 6000;
                CanvasScaler scaler = canvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.matchWidthOrHeight = 0.5f;
                canvas.AddComponent<GraphicRaycaster>();
                var full = (RectTransform)canvas.transform;
                Image shade = canvas.AddComponent<Image>();
                shade.color = new Color(0.06f, 0.05f, 0.09f, 0.82f);
                shade.raycastTarget = true;
                RectTransform track = Rect(full, "track", 0f, -18f, 520f, 10f);
                track.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
                RectTransform fill = Rect(track, "fill", 0f, 0f, 0f, 10f);
                fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
                bar = fill.gameObject.AddComponent<Image>();
                bar.color = new Color(0.55f, 0.75f, 0.73f, 1f);
                line = Rect(full, "text", 0f, 22f, 900f, 40f).gameObject.AddComponent<TextMeshProUGUI>();
                line.alignment = TextAlignmentOptions.Center;
                line.fontSize = 22f;
                line.color = new Color(0.95f, 0.92f, 0.85f, 1f);
                line.raycastTarget = false;
                ClickShield.Guard(full);
            }
            catch (System.Exception ex) { Health.Report("prepare screen", ex); Hide(); }
        }

        internal static void Progress(int done, int total)
        {
            if (canvas == null) return;
            if (line != null) line.text = (Entry.Chinese ? "大概是游戏扩展框架：正在准备贴图  " : "Probably Extend Framework: preparing images  ") + done + " / " + total;
            if (bar != null) bar.rectTransform.sizeDelta = new Vector2(520f * Mathf.Clamp01(total > 0 ? done / (float)total : 1f), 0f);
        }

        internal static void Hide()
        {
            if (canvas != null) Object.Destroy(canvas);
            canvas = null; line = null; bar = null;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }
    }
}
