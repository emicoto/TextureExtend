// ClaudeNote: 第 2 步——检查点与动作触发的临时状态（pulse）。
//   检查点：框架统一重算、或临时状态到期的时间点。内置 load（读档）、wake（起床）、open（开门）、close（关门）、sleep（入睡）、visit（来客换人）；
//     mod 可以 Checkpoint.Register("brood.feed") 自建，再在合适的时候 Checkpoint.Fire("brood.feed")。
//   临时状态：Painter.Pulse(modId, target, id).On("water").Tag("wet").Until("sleep") —— 有 mod 调 Painter.Fire("water", item) 时，
//     这件物品切到 wet，直到 sleep 检查点到来。记在物品标签 PEF_PULSE 里（随存档走）。
//   状态判定四层：临时 > 锁定（玩家亲手选的）> 规则 > 本体。临时状态到期后，锁定的物品回到玩家选的那个状态。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    /// <summary>一条临时状态定义。用 Painter.Pulse(...) 创建，链式补全；缺 On / Tag 不生效。</summary>
    public sealed class PulseDef
    {
        internal string owner, id, action, tag, until = Checkpoint.Sleep;
        internal PaintTarget target;
        internal PaintRule rule;

        /// <summary>哪个动作触发（Painter.Fire 的第一个参数）。</summary>
        public PulseDef On(string actionName) { action = (actionName ?? "").Trim().ToLowerInvariant(); return this; }
        /// <summary>切到哪个状态。</summary>
        public PulseDef Tag(string stateTag) { tag = PortraitLibrary.Normal(stateTag); Pulses.Bind(this); return this; }
        /// <summary>到哪个检查点结束（默认 sleep）。</summary>
        public PulseDef Until(string checkpoint) { until = Checkpoint.Normal(checkpoint) ?? Checkpoint.Sleep; return this; }

        internal bool Complete => !string.IsNullOrEmpty(action) && !string.IsNullOrEmpty(tag);
    }

    public static class Checkpoint
    {
        public const string Load = "load", Wake = "wake", Open = "open", Close = "close", Sleep = "sleep", Visit = "visit";
        private static readonly HashSet<string> known = new HashSet<string> { Load, Wake, Open, Close, Sleep, Visit };

        /// <summary>每次检查点到来时（临时状态清完之后）。参数是检查点名。</summary>
        public static event Action<string> OnFired;

        /// <summary>自建一个检查点（名字建议带 mod 前缀：brood.feed）。</summary>
        public static void Register(string name) { name = Normal(name); if (name != null) known.Add(name); }

        public static bool Exists(string name) { name = Normal(name); return name != null && known.Contains(name); }

        /// <summary>检查点到来：结束到这一点为止的临时状态，并重算店里物品（内置检查点的重算由框架自己做）。</summary>
        // ClaudeNote: 0.6.2 替换掉 0.6.1 的“嵌套超过 4 层就停”。
        //   检查点是“一个时刻”：到这个时刻，该到期的临时状态到期、物品重算、告知监听方。
        //   监听方在回调里再触发别的检查点是正当用法（例：入睡时顺带触发自己的 brood.feed）——排队，一个处理完再处理下一个，不套着跑。
        //   监听方在回调里触发“正在处理的这同一个检查点”（或 A 触发 B、B 又触发 A）：同一时刻到两次没有任何新含义
        //   （该到期的已经到期，重算结果不变），所以这一串处理里每个检查点只处理一次，重复的直接并掉。
        //   检查点的名字是有限的，每个最多处理一次，所以一串处理必然结束——不需要数层数。
        public static void Fire(string name)
        {
            name = Normal(name);
            if (name == null) return;
            if (!known.Contains(name)) { Entry.Warn("checkpoint \"" + name + "\" fired but never registered"); known.Add(name); }
            if (!arrived.Add(name)) return;     // 这一串里已经到过（或正排着）：同一时刻，并掉
            queue.Enqueue(name);
            if (running) return;                // 正在处理别的检查点：排队
            running = true;
            Notices.Begin();                    // 这一串检查点引起的状态变化，全部处理完再统一告知
            try
            {
                while (queue.Count > 0) Arrive(queue.Dequeue());
            }
            finally
            {
                queue.Clear();
                arrived.Clear();
                running = false;
                Notices.End();
            }
        }

        private static readonly Queue<string> queue = new Queue<string>();
        private static readonly HashSet<string> arrived = new HashSet<string>();
        private static bool running;

        private static void Arrive(string name)
        {
            try
            {
                Pulses.Expire(name);
                if (name != Load && name != Wake && name != Open && name != Close && name != Visit && name != Sleep) Painter.RefreshManaged();   // 这几个框架本来就会重算（来客换人只重算立绘道具；入睡在结算后整店过一遍）
            }
            catch (Exception ex) { Entry.Warn("checkpoint \"" + name + "\" failed: " + ex.Message); }
            Delegate[] listeners = OnFired?.GetInvocationList();
            if (listeners == null) return;
            foreach (Delegate d in listeners)
            {
                try { ((Action<string>)d)(name); }
                catch (Exception ex) { Entry.Warn("checkpoint listener failed (" + name + "): " + ex.Message); }
            }
        }

        internal static string Normal(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            return n.Length == 0 || n.IndexOfAny(new[] { ';', '>', '|' }) >= 0 ? null : n;
        }

        // 内置检查点：要排在统一重算之前订阅（Entry 里先装它），重算时临时状态已经清掉
        internal static void Install()
        {
            ModHook.OnGameLoadedLate += FireLoad;
            ModHook.OnWakingUpLate += FireWake;
            ModHook.OnShutterOpenedLate += FireOpen;
            ModHook.OnShutterClosedLate += FireClose;
            ModHook.OnGoingSleepEarly += FireSleep;
        }

        internal static void Uninstall()
        {
            ModHook.OnGameLoadedLate -= FireLoad;
            ModHook.OnWakingUpLate -= FireWake;
            ModHook.OnShutterOpenedLate -= FireOpen;
            ModHook.OnShutterClosedLate -= FireClose;
            ModHook.OnGoingSleepEarly -= FireSleep;
        }

        private static void FireLoad() { Fire(Load); }
        private static void FireWake() { Fire(Wake); }
        private static void FireOpen() { Fire(Open); }
        private static void FireClose() { Fire(Close); }
        private static void FireSleep() { Fire(Sleep); }
    }

    internal static class Pulses
    {
        /// <summary>物品身上正在进行的临时状态：状态>到期检查点;状态>到期检查点</summary>
        // ClaudeNote: 1.0.0 存档标签前缀由 TEXEXT_ 改为 PEF_（发布前定名，不做旧存档迁移）。
        internal const string ItemTag = "PEF_PULSE";
        /// <summary>临时状态的规则权重：压过所有普通规则。</summary>
        internal const int Weight = 1000000;

        private static readonly List<PulseDef> defs = new List<PulseDef>();

        internal static PulseDef Add(string owner, PaintTarget target, string id)
        {
            var d = new PulseDef { owner = owner ?? "", target = target, id = id ?? "" };
            defs.Add(d);
            return d;
        }

        // 每条定义挂一条最高权重的规则：物品身上有这个临时状态就成立
        internal static void Bind(PulseDef d)
        {
            if (d.rule != null || string.IsNullOrEmpty(d.tag)) return;
            string tag = d.tag;
            d.rule = Painter.For(d.owner, d.target, d.id).Tag(tag).When(item => Has(item, tag)).Weight(Weight);
        }

        internal static void Remove(string owner) { defs.RemoveAll(d => d.owner == owner); }   // 规则由 PaintRules.Remove 一起撤

        internal static int Count => defs.Count;

        /// <summary>动作发生：这件物品上所有对得上的临时状态开始。返回开始了几个。</summary>
        internal static int Fire(string action, GameItem item)
        {
            action = (action ?? "").Trim().ToLowerInvariant();
            if (item == null || action.Length == 0) return 0;
            string portrait = Painter.PortraitOf(item);
            int n = 0;
            foreach (PulseDef d in defs)
            {
                if (!d.Complete || d.action != action) continue;
                string id = d.target == PaintTarget.Portrait ? portrait : item.identifier;
                if (id == null || !d.rule.Covers(id) || !d.rule.Applies(item)) continue;
                Start(item, d.tag, d.until);
                n++;
            }
            if (n > 0) Painter.Refresh(item);
            return n;
        }

        internal static bool Has(GameItem item, string tag)
        {
            if (item == null || !item.IsTag(ItemTag)) return false;
            bool has = false;
            foreach (KeyValuePair<string, string> e in Read(item))
            {
                waiting.Add(e.Value);   // 重算时路过：顺手记下这件物品在等哪个检查点
                if (e.Key == tag) has = true;
            }
            return has;
        }

        internal static bool Any(GameItem item) { return item != null && item.IsTag(ItemTag) && Read(item).Count > 0; }

        /// <summary>到这个检查点为止的临时状态全部结束，受影响的物品立刻重算。</summary>
        internal static void Expire(string checkpoint)
        {
            if (!PlayerStore.HasInstance) return;
            // ClaudeNote: 0.6.3 来客每换一次人就会到这里（visit 检查点），不能每次都扫全店。
            //   0.6.1 是“没有任何定义以这个检查点为终点就跳过”——但该不该扫取决于物品身上挂着什么，不取决于定义名单：
            //   登记定义的 mod 撤了（定义没了），物品上的临时状态还在，那样就永远不到期。
            //   现在记的是事实本身：waiting = 有物品在等的检查点。读档那一次全扫并重建，之后开始（Start）和重算路过（Has）时补记。
            bool load = checkpoint == Checkpoint.Load;
            if (!load && !waiting.Contains(checkpoint)) return;
            if (load) waiting.Clear();
            waiting.Remove(checkpoint);
            foreach (GameItem item in Items.All())
            {
                if (item == null || !item.IsTag(ItemTag)) continue;
                List<KeyValuePair<string, string>> list = Read(item);
                int before = list.Count;
                list.RemoveAll(e => e.Value == checkpoint);
                foreach (KeyValuePair<string, string> e in list) waiting.Add(e.Value);
                if (list.Count == before) continue;
                Write(item, list);
                Painter.Refresh(item);
            }
        }

        private static readonly HashSet<string> waiting = new HashSet<string>();

        private static void Start(GameItem item, string tag, string until)
        {
            List<KeyValuePair<string, string>> list = Read(item);
            list.RemoveAll(e => e.Key == tag);
            list.Add(new KeyValuePair<string, string>(tag, until));
            Write(item, list);
            waiting.Add(until);
        }

        private static List<KeyValuePair<string, string>> Read(GameItem item)
        {
            var list = new List<KeyValuePair<string, string>>();
            string text = item.GetTagReadonly(ItemTag)?.GetString() ?? "";
            foreach (string part in text.Split(';'))
            {
                int gt = part.IndexOf('>');
                if (gt > 0) list.Add(new KeyValuePair<string, string>(part.Substring(0, gt), part.Substring(gt + 1)));
            }
            return list;
        }

        private static void Write(GameItem item, List<KeyValuePair<string, string>> list)
        {
            if (list.Count == 0) { item.ModifyTag(ItemTag, s => s.Disable()); return; }
            var parts = new List<string>();
            foreach (KeyValuePair<string, string> e in list) parts.Add(e.Key + ">" + e.Value);
            string text = string.Join(";", parts);
            item.ModifyTag(ItemTag, s => { s.Enable(); s.SetString(text); });
        }
    }
}
