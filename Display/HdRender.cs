// ClaudeNote: 第 5 步——高清立绘的渲染路径（思路与 BroodMeme 的 UI/HdView.cs 相同，框架里通用化）。
//   为什么“怎么加载都糊”：店面摄像机挂着 PixelPerfectCamera，参考分辨率 960×540 且开着 upscaleRT——整个店面先画进
//   960×540 的低分辨率画布，再点采样放大到屏幕。原版像素图这样正合适；高清立绘却先被压到一两百像素高再放大回去。
//   这和贴图怎么读进内存无关（Unity 本身确实是“图多大就按多大采样、随相机缩放”，问题出在这台相机先画进了小画布）。
//   做法：柜台上显示的是框架给的高清立绘时，关掉这台相机的 upscaleRT，店面直接按屏幕分辨率渲染；立绘离开就恢复。
//   · 原版的排序、遮挡、灯光、后处理、描边全照旧，不另开相机、不改图层、不用自定义着色器；
//   · 原版像素图是点采样，PixelPerfectCamera 也只在整数倍分辨率时启用，直接按屏幕分辨率画与“先小后放大”看起来相同；
//   · 像素风立绘包（pack 行 style=pixel）不触发，走原版路径；
//   · 非整数倍分辨率时原版自己就停用了 PixelPerfectCamera，本来就是屏幕分辨率，这里什么都不做。
//   只在状态变化时动手；每帧只做几次引用比较。配置项 hdRender 可关（关掉 = 完全原版路径，用来对比）。
//   0.6.0：这里是唯一的仲裁者。扩展 mod 自己建的高清 Sprite 用 HdRender.Mark 登记即可（BroodMeme 已改为调这里）；
//   别的 mod 把柜台那张关掉、自己在界面层上画时（显示方案里的 Yield），这里不动店面。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace TextureExtend
{
    public static class HdRender
    {
        private static readonly HashSet<Sprite> marked = new HashSet<Sprite>();
        private static GameObject host;
        private static PixelPerfectCamera held;
        internal static bool Enabled = true;
        private static bool broken;   // 这项功能出过错、已关掉

        /// <summary>登记一张走高清路径的立绘（框架给的高清显示图自动登记；扩展 mod 自己建的 Sprite 也可以登记）。</summary>
        public static void Mark(Sprite s) { if (s != null) marked.Add(s); }
        public static void Unmark(Sprite s) { if (!ReferenceEquals(s, null)) marked.Remove(s); }
        public static bool IsHd(Sprite s) { return s != null && marked.Contains(s); }
        /// <summary>现在店面是不是被本框架改成按屏幕分辨率渲染。</summary>
        public static bool Active => held != null;

        internal static void Install()
        {
            Ports.Read("EmporiumEntry.pixelPerfectCamera", "高清立绘渲染", () => EmporiumEntry.Instance == null ? null : EmporiumEntry.Instance.pixelPerfectCamera == null ? "camera not found" : null);
            Ports.Read("StoreClientMono.image", "柜台来客显示 / 高清立绘渲染", () => StoreClientMono.Instance == null ? null : StoreClientMono.Instance.image == null ? "renderer not found" : null);
            if (host != null) return;
            host = new GameObject("TextureExtend_HdRender");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Watcher>();
        }

        internal static void Uninstall()
        {
            Restore();
            if (host != null) Object.Destroy(host);
            host = null;
            // ClaudeNote: 0.6.1 自检：不整表清空——别的 mod（BroodMeme）登记的高清立绘只在它们建图时登记一次，
            //   清掉后本框架再启用它们就不走高清路径了。只清已经销毁的。
            marked.RemoveWhere(s => s == null);
            broken = false;
        }

        private static bool Wanted()
        {
            if (!Enabled || !Painter.Running || Painter.Retired) return false;
            StoreClientMono mono = StoreClientMono.Instance;
            if (mono == null || !mono.isActiveAndEnabled || mono.image == null || !mono.image.enabled) return false;
            if (mono.image.forceRenderingOff) return false;   // ClaudeNote: 0.6.0 别的 mod 把这张关掉、自己在界面层上画（Yield）：店面不用改
            return IsHd(mono.image.sprite);
        }

        private static void Tick()
        {
            bool want = Wanted();
            if (!want) { if (held != null) Restore(); return; }
            PixelPerfectCamera ppc = EmporiumEntry.Instance != null ? EmporiumEntry.Instance.pixelPerfectCamera : null;
            if (ppc == null || !ppc.upscaleRT) return;            // 本来就是屏幕分辨率（或别的 mod 已经关了）
            held = ppc;
            ppc.upscaleRT = false;
            RefreshOutliner();
        }

        private static void Restore()
        {
            PixelPerfectCamera ppc = held;
            held = null;
            if (ppc == null) return;
            ppc.upscaleRT = true;
            RefreshOutliner();
        }

        // 描边组件按摄像机画布大小建缓冲，画布变了要重开一次（原版切换 PixelPerfectCamera 时也这么做）
        private static void RefreshOutliner()
        {
            Behaviour outliner = EmporiumEntry.Instance != null ? EmporiumEntry.Instance.outliner : null;
            if (outliner == null || !outliner.enabled) return;
            outliner.enabled = false;
            outliner.enabled = true;
        }

        private sealed class Watcher : MonoBehaviour
        {
            // 每帧：几次引用比较，全部先判空，正常情况下不会抛异常。
            // ClaudeNote: 0.6.3 真抛了只有两种可能：游戏更新后用到的成员没了、或这里有 bug——哪一种下一帧重试都不会好。
            //   所以不数“连错几帧”：出错就把这项功能关掉（店面渲染恢复原版），原因记进 Health，自检里看得到；重新启用本 mod 时再试。
            private void LateUpdate()
            {
                // ClaudeNote: 0.6.4 自检：自动诊断原来和高清渲染包在同一个 try 里，诊断出错会把高清渲染一起关掉。分开：各关各的
                if (Probe.Auto)
                {
                    try { Probe.Tick(); }
                    catch (System.Exception ex) { Probe.Auto = false; Health.Report("auto probe (switched off)", ex); }
                }
                if (broken) return;
                try { Tick(); }
                catch (System.Exception ex)
                {
                    broken = true;
                    Health.Report("HD render (switched off)", ex);
                    try { Restore(); } catch (System.Exception) { }
                }
            }
        }
    }
}
