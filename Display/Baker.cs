// ClaudeNote: 后台预处理：把各来源组里还没进缓存（或来源变了）的图逐张画好写盘。
//   “后台”= 摊在多帧里：每帧最多干 FrameBudget 秒就让出去，不开线程（贴图回读、编码只能在主线程，单张也只要一两毫秒）。
//   触发：有新的来源组、或原版立绘表换了实例（进店面、换场景）。没活时每秒只醒一次，看一眼标记。
//   协程挂在本 mod 自己的组件上，停用时随组件一起销毁。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyExtendFramework
{
    internal sealed class Baker : MonoBehaviour
    {
        private const float FrameBudget = 0.003f;
        // ClaudeNote: 0.3.0 单张就超出预算的（外部立绘包的高清原图，解码一张约 20~40 毫秒）画完歇一会儿，免得连续卡帧
        private const float HeavyRest = 0.1f;
        private static GameObject host;
        private SpriteDict seenDict;
        private int seenStamp;
        private WaitForSecondsRealtime rest;
        private WaitForSecondsRealtime measureRest;
        private object seenClient;   // ClaudeNote: 当前来客（只比对是不是同一个，不读内容）

        internal static bool Busy { get; private set; }

        internal static void Launch()
        {
            if (host != null) return;
            host = new GameObject("TextureExtend_Baker");
            DontDestroyOnLoad(host);
            host.AddComponent<Baker>();
        }

        internal static void Shutdown()
        {
            if (host != null) Destroy(host);
            host = null;
            Busy = false;
            PrepareScreen.Hide();
        }

        /// <summary>下一次扫描做集中预处理（读档后、入睡时置上）。</summary>
        internal static bool Burst;

        /// <summary>读档后 / 入睡时调：安排一次集中预处理。</summary>
        internal static void RequestBurst() { Burst = true; Painter.NeedScan = true; }

        // ClaudeNote: 1.0.0 rc9 读档后的那次：活多时显示“正在准备贴图”画面（UI/PrepareScreen.cs）。入睡那次不显示（有过夜淡出）
        private static bool loadBurst;
        internal static void RequestLoadBurst() { loadBurst = true; RequestBurst(); }

        /// <summary>开 / 关贴图扩展工坊的键（设置里可改，见 Entry.DefineConfig）。</summary>
        internal static KeyCode Hotkey = KeyCode.PageUp;

        private void Start() { StartCoroutine(Loop()); }

        // ClaudeNote: 0.9.8 PageUp 开 / 关贴图扩展工坊（Lune 2026-11-18：游戏内也要能打开）。每帧一次按键查询，别的不做。
        private void Update()
        {
            if (Hotkey == KeyCode.None || !Input.GetKeyDown(Hotkey) || !Painter.Running || Painter.Retired || KeyBindHelper.SuppressHotkeys) return;   // 游戏正在识别按键（改键）时不响应
            GameObject focus = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            if (focus != null && focus.GetComponent<TMPro.TMP_InputField>() != null) return;   // 正在输入框里打字：PageUp 是翻页键，不抢
            WorkshopWindow.Toggle();
        }

        private IEnumerator Loop()
        {
            var idle = new WaitForSecondsRealtime(1f);
            rest = new WaitForSecondsRealtime(HeavyRest);
            measureRest = new WaitForSecondsRealtime(0.5f);
            // ClaudeNote: 0.6.1 自检加固：协程里有一步抛出异常，Unity 会把整个协程停掉、而且不会再启动——
            //   后台扫描、来客检查点、每秒放像素全部跟着停，Busy 还会一直卡在 true。所以每一步单独兜住，循环永远往下走；
            //   出的错记进 Health（同一种错只写一次日志，自检里看得到次数）。
            //   组件随 mod 停用销毁时协程自然结束（Shutdown），这是它唯一的出口。
            while (true)
            {
                bool scan = false;
                try
                {
                    SpriteDict dict = SpriteDict.Instance;
                    if (!ReferenceEquals(dict, seenDict))
                    {
                        seenDict = dict;
                        Painter.NeedScan = true;
                        if (dict == null) Painter.Unsettle();   // 离开店面：下一局读完存档之前，来源都不算定下来（见 Painter.Settled）
                    }
                    // ClaudeNote: 0.6.3 别的立绘 mod 可能在任何时候把表里的图换成自己的（表还是同一张）。0.6.1 是“读档 1.5 秒后重认一遍”——
                    //   靠猜时间。现在直接看表的内容变没变，变了才重认，什么时候换都接得住。
                    int stamp = Originals.TableStamp();
                    if (stamp != seenStamp) { seenStamp = stamp; PortraitLibrary.Invalidate(); Painter.NeedScan = true; }
                    scan = Painter.NeedScan && Painter.VanillaReady && Painter.Settled;   // 存档读完、各 mod 换完表之后才扫
                }
                catch (System.Exception ex) { Health.Report("background: watch table", ex); }
                if (scan) yield return Scan();
                Step("workshop entry", WorkshopEntry.Tick);   // 标题界面的工坊按钮：进场景时已经装过，这里只是兜底（装过就是一次判空）
                Step("watch client", WatchClient);
                Step("release pixels", SourceReader.Release);   // 零散的 Paint / SetVariant 读过的整张来源像素（高清图集可达几十 MB）最多留一秒
                Step("fit settle", Fit.Settle);                 // 这一秒里新量到的内容占比：缩放结论变了才让显示图重做（攒着一次做）
                Step("save index", SaveIndexIfDirty);
                yield return idle;
            }
        }

        // ClaudeNote: 1.0.0 索引有改动时最多每 IndexEvery 秒存一次（原来是每秒）：零散地现画了几张图时，不必每秒把整份索引（上千条）重写一遍。
        //   扫描结束、入睡时照旧各存一次，所以最多丢这十几秒里画的几张（下次用到重画）。
        private const float IndexEvery = 15f;
        private static float indexSavedAt;

        private static void SaveIndexIfDirty()
        {
            if (!Cache.Dirty || Time.realtimeSinceStartup - indexSavedAt < IndexEvery) return;
            indexSavedAt = Time.realtimeSinceStartup;
            Cache.SaveIndex();
        }

        private static void Step(string what, System.Action action)
        {
            float t = Health.Now;
            try { action(); }
            catch (System.Exception ex) { Health.Report("background: " + what, ex); }
            Health.Took("background: " + what, t);   // 1.0.0 后台每一步都计时：超过一帧的会记进“slow steps”
        }


        // ClaudeNote: 来客到来 / 离开 = 当前来客换了人。游戏没给这个事件，这里每秒比一次引用；换了才去重算立绘衍生道具。
        private void WatchClient()
        {
            object now = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance : null;
            if (ReferenceEquals(now, seenClient)) return;
            seenClient = now;
            Display.Sweep();   // 柜台换了人：上一位用着、当时不能销毁的旧显示图现在可以放了
            Counter.ClientChanged(now != null);
            float t = Health.Now;
            Checkpoint.Fire(Checkpoint.Visit);
            Painter.RefreshPortraitItems();
            Health.Took("visitor changed: recompute portrait items", t);
        }

        private IEnumerator Scan()
        {
            Painter.NeedScan = false;
            Busy = true;
            // ClaudeNote: 补读各 mod 的 TextureExtend.csv（资源载入那一刻没读到的，进店面时在这里补上；读过的不会重读）
            Step("read tables", () => { if (ModLoader.Mods != null) foreach (LoadedMod mod in new List<LoadedMod>(ModLoader.Mods)) CsvLoader.Auto(mod); });
            Step("read-only packs", PackShelf.Apply);   // ClaudeNote: 0.6.0 玩家开着的只读立绘包（未启用的 mod）在这里登记：这时原版立绘表已经就绪
            var ids = new List<PaintId>();
            Step("list sources", () => ids = Painter.AllIds());
            // ClaudeNote: 1.0.0 集中预处理（Lune 2026-11-18）：读档后、入睡时各一次，把“每个立绘键现在选中的那张（激活来源的、逐张点名的候选）+ 存档里道具正用着的”
            //   里还没画过的外部图片一口气画完——Painter.AllIds 列的正是这些。这一遍一帧一张不歇（首次可能十几秒，之后有缓存就是空跑）。
            //   其余时候触发的扫描（换来源、开关包…）不画外部图片，用到才画。
            bool burst = Burst, screen = burst && loadBurst;
            Burst = false;
            loadBurst = false;
            int done = 0;
            float burstStart = Time.realtimeSinceStartup;
            int baked = 0;
            bool whole = ids.Count > 0;   // 这一遍有没有完整走完（中途离开店面 = 没有）
            float frameStart = Time.realtimeSinceStartup;
            foreach (PaintId id in ids)
            {
                if (!Painter.VanillaReady) { whole = false; break; }   // 扫到一半离开了店面：来源表没了，剩下的等下次进店面（seenDict 变了会重新置 NeedScan）
                bool worked = false;
                try { worked = Painter.Prebake(id, burst); }
                catch (System.Exception ex) { Health.Report("prebake", id.Text + ": " + ex.GetType().Name + ": " + ex.Message); }
                if (worked) baked++;
                done++;
                if (screen && !PrepareScreen.Showing && Time.realtimeSinceStartup - burstStart > PrepareScreen.ShowAfter && done < ids.Count) PrepareScreen.Show();
                float spent = Time.realtimeSinceStartup - frameStart;
                if (spent < (PrepareScreen.Showing ? PrepareScreen.FrameBudget : FrameBudget)) continue;
                PrepareScreen.Progress(done, ids.Count);
                if (spent > FrameBudget * 5f) { Step("release pixels", SourceReader.Release); yield return burst ? null : rest; }   // 集中预处理：一帧一张，不歇
                else yield return null;
                frameStart = Time.realtimeSinceStartup;
            }
            PrepareScreen.Hide();
            // ClaudeNote: 0.6.0 立绘大小自适配的补量：每个来源还没量够的补几张（同样分帧，量够就不再做）
            var jobs = new List<KeyValuePair<string, string>>();
            Step("fit pending", () => jobs = Fit.Pending());
            foreach (KeyValuePair<string, string> job in jobs)
            {
                if (!Painter.VanillaReady) break;   // 扫到一半离开了店面（回主菜单）：剩下的下次再量
                try { Fit.Measure(job.Key, job.Value); }
                catch (System.Exception ex) { Health.Report("fit measure", job.Key + "/" + job.Value + ": " + ex.GetType().Name + ": " + ex.Message); }
                float spent = Time.realtimeSinceStartup - frameStart;
                if (spent < FrameBudget) continue;
                Step("release pixels", SourceReader.Release);
                // 1.0.0 量一张外部图片要读盘解码（几十毫秒）：这种的每张之间歇半秒，不连着卡。每个来源最多量 9 张，量过的存着，以后不再量
                yield return spent > FrameBudget * 5f ? measureRest : null;
                frameStart = Time.realtimeSinceStartup;
            }
            Step("release pixels", SourceReader.Release);
            Step("fit settle", Fit.Settle);
            Step("fit save", Fit.Save);
            Step("save index", Cache.SaveIndex);
            Step("after scan", Painter.AfterScan);
            // 完整过了一遍、期间来源没再变：清掉缓存里已经认不得的图
            if (whole && Painter.VanillaReady && !Painter.NeedScan)
                Step("cache prune", () => { if (Painter.TryKnownKeys(out HashSet<string> known)) { Cache.Prune(known); if (Cache.Dirty) Cache.SaveIndex(); } });
            // ClaudeNote: 0.7.0 工坊的目录快照（标题界面拿不到立绘表，靠这份）。完整扫完才存；道具逐个记、同样分帧
            if (whole && Painter.VanillaReady)
            {
                var itemIds = new List<string>();
                Step("workshop catalog", () => { Workshop.SnapshotBegin(); itemIds = Workshop.SnapshotItemIds(); });
                foreach (string itemId in itemIds)
                {
                    string one = itemId;
                    Step("workshop catalog", () => Workshop.SnapshotItem(one));
                    if (Time.realtimeSinceStartup - frameStart < FrameBudget) continue;
                    yield return null;
                    frameStart = Time.realtimeSinceStartup;
                }
                Step("workshop catalog", Workshop.SnapshotEnd);
                // ClaudeNote: 1.0.0 原来这里还有一遍“把各来源的图都补画进缓存”（0.8.6 为标题界面的工坊缩略图加的）：工坊现在自己读原图，
                //   这一遍只剩下卡顿，去掉了。没被选中的来源的图，选图窗口翻到时现画。
            }
            Busy = false;
            if (burst && baked > 0) Entry.Info("preprocess after load / at sleep: " + baked + " image(s) in " + (Time.realtimeSinceStartup - burstStart).ToString("0.0") + " s");
            if (baked > 0) Entry.Info("cache: baked " + baked + " of " + ids.Count + " source image(s)");
        }
    }
}
