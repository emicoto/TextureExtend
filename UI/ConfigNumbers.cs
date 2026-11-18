// ClaudeNote: 1.0.0 设置窗口里滑杆旁边显示数字（Lune：“这个滑杆你漏了数字的处理了”）。
//   游戏的 mod 设置窗口给带范围的数值项画一根滑杆，但不显示当前是多少。窗口是游戏建的，这里只在它建好之后（ModConfigWindow.Open 的后置）
//   找到本框架的那几项，把数字写进那一行的名字里（“立绘默认大小（%）：100”），拖动时跟着变。不加任何新的界面零件。
//   要显示数字的项填在 keys 里。
using System;
using HarmonyLib;
using UnityEngine;

namespace ProbablyExtendFramework
{
    internal static class ConfigNumbers
    {
        private static readonly string[] keys = { "portraitScale" };

        internal static void Install()
        {
            Ports.Patch("ModConfigWindow.Open", "设置窗口的滑杆旁显示数字", () => AccessTools.Method(typeof(ModConfigWindow), nameof(ModConfigWindow.Open), new[] { typeof(LoadedMod) }),
                postfix: new HarmonyMethod(typeof(ConfigNumbers), nameof(Postfix)));
        }

        private static void Postfix(LoadedMod mod, CustomUIWindow __result)
        {
            try
            {
                if (__result == null || mod?.Manifest == null || mod.Manifest.ID != Painter.ModId) return;
                foreach (string key in keys)
                {
                    CustomUIElement slider = __result.Get(key), label = __result.Get("label_" + key);
                    if (slider == null || label == null || slider.slider == null) continue;
                    string text = label.GetText() ?? "";
                    int icon = text.IndexOf(" <link=", StringComparison.Ordinal);   // 名字后面跟着提示图标：数字插在图标前面
                    string head = icon >= 0 ? text.Substring(0, icon) : text, tail = icon >= 0 ? text.Substring(icon) : "";
                    Action<float> show = v => label.SetText(head + (Entry.Chinese ? "：" : ": ") + Mathf.RoundToInt(v) + tail);
                    slider.slider.onValueChanged.AddListener(v => show(v));
                    show(slider.GetFloat());
                    // “恢复默认”是不带通知地改滑杆的：点它之后补一次
                    CustomUIElement reset = __result.Get("reset");
                    if (reset != null && reset.button != null) reset.button.onClick.AddListener(() => show(slider.GetFloat()));
                }
            }
            catch (Exception ex) { Health.Report("config numbers", ex); }
        }
    }
}
