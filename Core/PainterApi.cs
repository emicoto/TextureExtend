// ClaudeNote: 第 2–4 步的对外接口（总纲第七节）。和表格一一对应；只做转发，逻辑在各自的文件里。
using System;
using System.Collections.Generic;
using UnityEngine;
using ProbablyExtendFramework.TagSystem;

namespace ProbablyExtendFramework
{
    public static partial class Painter
    {
        // ── 第 2 步：条件、临时状态、检查点 ──

        /// <summary>具名条件，表格里用 cond:名字 引用。名字建议带 mod 前缀（brood.isSick）。</summary>
        public static void Condition(string modId, string name, Func<GameItem, bool> condition) { Conditions.Add(modId, name, condition); }

        /// <summary>编译一条条件表达式（与表格 when 列同一套写法），给代码里想复用表达式的 mod 用。写错了抛 FormatException。</summary>
        public static Func<GameItem, bool> Expression(string text) { return When.Compile(text); }

        /// <summary>动作触发的临时状态：Pulse(modId, PaintTarget.StaticItem, "plant_pot").On("water").Tag("wet").Until("sleep")。</summary>
        public static PulseDef Pulse(string modId, PaintTarget target, string id) { return Pulses.Add(modId, target, id); }

        /// <summary>动作发生在这件物品上（浇水、喂食…）：对得上的临时状态开始，物品立刻重算。返回开始了几个。</summary>
        public static int Fire(string action, GameItem item) { return Pulses.Fire(action, item); }

        // ── 第 3 步：标签层 ──

        /// <summary>标签层登记：Tag(modId, PaintTarget.Portrait, "mutant_cat").Under("lower").Group("mutant").With("female").Without("aug")。</summary>
        public static TagDef Tag(string modId, PaintTarget target, string id) { return Regist.Tag(modId, target, id); }

        /// <summary>取池子：带齐这些标签的立绘 ID / 道具 ID（@组名 与 组名 同义）。</summary>
        public static List<string> Pool(PaintTarget target, params string[] tags) { return Regist.Pool(target, tags); }

        // ── 状态变化通知、直接取图 ──

        /// <summary>物品换了状态之后（参数：物品、原状态、新状态；本体为空字符串）。挂演出、音效、日志用。
        /// 事后告知：一批操作（整店重算等）做完才统一发，每件物品只发净变化。想影响状态请登记规则或临时状态，不要在这里手动改回去。</summary>
        public static event Action<GameItem, string, string> OnStateChanged;

        // 换图的地方只记账（Notices），由它在这批操作做完后统一告知：见 Core/Notices.cs
        internal static void Changed(GameItem item, string from, string to) { Notices.Record(item, from, to); }

        internal static bool HasStateListeners => OnStateChanged != null;

        // 逐个监听方告知：一个出错不连累其他
        internal static void TellStateChanged(GameItem item, string from, string to)
        {
            Delegate[] listeners = OnStateChanged?.GetInvocationList();
            if (listeners == null) return;
            foreach (Delegate d in listeners)
            {
                try { ((Action<GameItem, string, string>)d)(item, from, to); }
                catch (Exception ex) { Entry.Warn("OnStateChanged listener failed: " + ex.Message); }
            }
        }

        /// <summary>直接取一张立绘某个状态的整张图（给对话框、全息电话等自己画界面的功能）。尺寸与轴心对齐游戏里同字段的原图。拿不到返回 null。</summary>
        public static Sprite Resolve(string portraitId, string state = null) { return Display.Get(portraitId, state); }

        /// <summary>按用途取图：全身 / 腰上 / 胸上。腰上、胸上有注册方专门给的图就用那张（PortraitLibrary.RegisterFrame 或表格 frame 列），没有就从全身图上部裁出。</summary>
        public static Sprite Resolve(string portraitId, string state, Display.Usage usage) { return Display.Get(portraitId, state, null, usage); }

        /// <summary>换缓存的存放处（伴生 mod 用：它有“文件”权限，把成品图缓存放进自己的文件夹）。null = 回到 ModData。见 Display/Cache.cs。</summary>
        public static void UseCacheStore(ICacheStore store) { Cache.UseStore(store); NeedScan = true; }

        /// <summary>交一个“读没启用的 mod 的 DLL 里的图片”的读取器（伴生 mod 用，见 ExternalImages 的 ext 身份串）。null = 撤掉。</summary>
        public static void UseImageReader(Func<string, string, byte[]> reader) { ExternalImages.Reader = reader; }

        // ── 0.6.0 接触面自检、显示方案、自适配（诊断与扩展用） ──

        /// <summary>框架对游戏的每个补丁 / 读数接口的状态（游戏更新后先看这个）。</summary>
        public static string SurfaceReport() { return Ports.Report(); }

        /// <summary>立绘表的键现在怎么显示：用哪个来源、谁来画、多大、走哪条渲染路。</summary>
        public static Plan PlanOf(string key) { return Plan.ForKey(key); }
    }
}
