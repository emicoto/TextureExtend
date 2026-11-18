// ClaudeNote: 0.4.2 框架要过一遍的物品：店里各个窗口的物品（含不是玩家拥有的），再顺着每件物品的内容窗口往里找（箱子、
//   lewd_meme 的容器等装在别的物品里的物品，原版 FindAllItem 找不到它们——Lune 2026-11-16 实测：容器里的少女道具没自动生效）。
//   只在统一重算点（读档、起床、开关门、过夜、来客换人）调，不逐帧。
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    internal static class Items
    {
        // ClaudeNote: 0.6.1 自检：原来用静态表攒结果，调用完不清——整店的物品会被它一直拖着（读别的档后上一局的物品也放不掉）。改成局部变量。

        /// <summary>店里的全部物品（含装在别的物品里的）。每次返回新列表，调用方遍历时改物品不受影响。</summary>
        internal static List<GameItem> All()
        {
            var buffer = new List<GameItem>();
            if (!PlayerStore.HasInstance) return buffer;
            var seen = new HashSet<GameItem>();
            var queue = new Queue<GameItem>(PlayerStore.Instance.FindAllItem(false));
            // ClaudeNote: 0.4.3 再加上所有开着的窗口里的物品（别的 mod 自建的窗口，例如 lewd_meme 的界面——Lune 实测：左上角那件没生效）
            if (WindowsHandler.current != null && WindowsHandler.current.visibleWindows != null)
                foreach (PixelWindow window in WindowsHandler.current.visibleWindows.ToArray())
                    if (window != null) foreach (GameItem inner in GraphUtils.FindAllChildrenType<GameItem>(window)) queue.Enqueue(inner);
            while (queue.Count > 0)
            {
                GameItem item = queue.Dequeue();
                if (item == null || !seen.Add(item)) continue;
                buffer.Add(item);
                if (item.contentWindow == null) continue;
                foreach (GameItem inner in GraphUtils.FindAllChildrenType<GameItem>(item.contentWindow)) queue.Enqueue(inner);
            }
            return buffer;
        }
    }
}
