// ClaudeNote: TextureExtend（贴图扩展框架，原 ItemPainter）入口。只接生命周期，不含绘制逻辑。
//   资源载入完成 → 读缓存索引、启动后台预处理；读档 → 先载入本局用到的图（物品还原之前）→ 读档完成后兜底接图；
//   入睡 → 收拢“用到的图”并写索引。调试按钮在 TextureExtend 调试面板（F9 的 Modding 面板里有入口）。
using System;

namespace ProbablyExtendFramework
{
    public sealed class Entry : IMod
    {
        private static ModLog log;
        internal static string Version { get; private set; } = "?";
        private bool enabled;
        private static ConfigEntry<string> imageSize, dlcDeepSpace, lewdMood, visitorPortraits, visitorPools, dialogPlacement, hdRender, workshopKey, beforeUninstall;
        private static ConfigEntry<bool> autoSize;
        private static ConfigEntry<int> portraitScale;
        // ClaudeNote: 内置 DLC 示范注册表：深空娘化（Lune 2026-11-16 定：默认关闭，玩家在配置里手动开启才加载）
        private static bool assetsReady;
        private static bool localeReady;   // 可以问当前语言了（本 mod 资源载入事件起）

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            Version = manifest.ModVersion;
        }

        public void OnEnable()
        {
            if (enabled) return;
            enabled = true;
            DefineConfig();
            Painter.Start();
            DayNight.Install();   // ClaudeNote: 0.4.2 要排在统一重算之前订阅
            Checkpoint.Install(); // ClaudeNote: 0.5.0 同上：临时状态先到期，统一重算时已经清掉
            HdRender.Install();
            // ClaudeNote: 0.6.0 游戏接触面：各模块登记自己的补丁 / 读数接口，然后逐个安装（装不上的只停那一项功能）
            PlanPort.Install();
            PortraitPools.Install();
            Counter.Install();
            ConfigNumbers.Install();
            DialogPlacement.Install();
            PickButtonHooks.Install();
            Ports.InstallAll();
            WorkshopEntry.Install();   // ClaudeNote: 0.7.0 标题界面的“贴图扩展工坊”按钮（不打补丁）
            ModHook.OnModAssetsLoaded += OnAssetsLoaded;
            ModHook.OnGameLoadedNormal += OnSessionStart;   // 要排在 Preload 之前
            ModHook.OnGameLoadedNormal += Painter.Preload;
            ModHook.OnGameLoadedLate += Painter.Settle;       // ClaudeNote: 0.7.2 存档读完 = 来源定下来了（要排在 RestoreAll 之前）
            ModHook.OnWakingUpLate += Painter.Settle;         //   起床、开门也算：新开一局不走读档事件时的保底
            ModHook.OnShutterOpenedLate += Painter.Settle;
            ModHook.OnGameLoadedLate += Painter.RestoreAll;
            ModHook.OnGoingSleepEarly += OnSleep;
            // ClaudeNote: 动态差分的统一重算点（不逐帧）：读档后、起床后、开门后、关门后。来客到来 / 离开由 Baker 每秒看一眼当前来客有没有换人。
            // ClaudeNote: 1.0.0 rc6 检测点（Lune 2026-11-18 定）：道具 = 读档、开店、关店、入睡结算（起床那次挪到这里：黑屏里做，起床就是结果）。
            //   整店（含容器套容器）只在读档和入睡结算时走一遍，顺手记下“受管清单”；开店、关店、起床（临时状态到期）只过清单。
            //   都挂在“晚”那一档：别的 mod 在同一时刻、“早”档里写的存档位，这里读得到。
            ModHook.OnGameLoadedLate += OnLoaded;
            ModHook.OnGoingSleepLate += OnSleepLate;
            ModHook.OnWakingUpLate += OnWake;
            ModHook.OnShutterOpenedLate += OnOpen;
            ModHook.OnShutterClosedLate += OnClose;
            ModHook.OnGameLoadedLate += RecheckAfterLoad;
            ModHook.OnGameLoadedLate += Baker.RequestLoadBurst;   // 1.0.0 集中预处理：读档后一次（rc9：活多时显示等待画面）、入睡时一次
            ModHook.OnGoingSleepEarly += Baker.RequestBurst;
            Baker.Launch();
        }

        public void OnDisable()
        {
            if (!enabled) return;
            enabled = false;
            ModHook.OnModAssetsLoaded -= OnAssetsLoaded;
            Painter.ResetReady();
            ModHook.OnGameLoadedNormal -= OnSessionStart;
            ModHook.OnGameLoadedNormal -= Painter.Preload;
            ModHook.OnGameLoadedLate -= Painter.Settle;
            ModHook.OnWakingUpLate -= Painter.Settle;
            ModHook.OnShutterOpenedLate -= Painter.Settle;
            ModHook.OnGameLoadedLate -= Painter.RestoreAll;
            ModHook.OnGoingSleepEarly -= OnSleep;
            ModHook.OnGameLoadedLate -= OnLoaded;
            ModHook.OnGoingSleepLate -= OnSleepLate;
            ModHook.OnWakingUpLate -= OnWake;
            ModHook.OnShutterOpenedLate -= OnOpen;
            ModHook.OnShutterClosedLate -= OnClose;
            Counter.Reset();
            ModHook.OnGameLoadedLate -= RecheckAfterLoad;
            ModHook.OnGameLoadedLate -= Baker.RequestLoadBurst;
            ModHook.OnGoingSleepEarly -= Baker.RequestBurst;
            // ClaudeNote: 0.6.1 自检：停用时把开着的窗口、挪过的对话框、调试开关一并收掉
            Notices.Reset();
            Picker.Close();
            SourceWindow.Close();
            WorkshopWindow.Close();
            ClickShield.ReleaseAll();   // 0.7.3 兜底：被临时关掉的射线接收器全部打开
            WorkshopEntry.Uninstall();
            Workshop.Shutdown();
            PickButton.Shutdown();
            DialogPlacement.Shutdown();   // 在撤补丁之前
            Probe.Auto = false;
            LewdMemeCompat.Apply(false);
            DayNight.Uninstall();
            Checkpoint.Uninstall();
            HdRender.Uninstall();
            Ports.UninstallAll();
            PackShelf.Reset();
            BundledTables.Reset();
            Fit.Save();
            Display.Shutdown();
            DevPanel.Clear();   // ClaudeNote: 2026-11-16 调试项收进自己的面板，F9 只挂一个入口
            Health.Clear();
            Baker.Shutdown();
            CsvLoader.Reset();
            assetsReady = false;
            Painter.Stop();
        }

        // 设置：道具图大小。128 = 4×4 格的道具在 1080p / 2K 屏幕上占的像素，256 = 4K。只影响高清来源；原版像素立绘始终按原像素。
        private static void DefineConfig()
        {
            ModConfig cfg = ModHelper.GetConfig(Painter.ModId);
            if (cfg == null) return;
            if (imageSize == null)
            {
                imageSize = cfg.Define("imageSize", "128", "Display",
                    () => Chinese ? "道具图大小（4×4 格）" : "Item image size (4x4 cells)",
                    new[] { "128", "256" },
                    () => Chinese
                        ? "动态生成的道具图最大边长。128 适合 1080p / 2K 屏幕，256 适合 4K（缓存文件更大）。不会放大来源图：原版像素立绘始终按原像素保存。改动后缓存在后台重做。"
                        : "Largest side of generated item images. 128 suits 1080p / 1440p, 256 suits 4K (bigger cache files). Sources are never upscaled. The cache is rebuilt in the background after a change.");
                imageSize.OnChanged += value => Painter.SetPixelsPerCell(value == "256" ? 64 : 32);
            }
            Painter.SetPixelsPerCell(imageSize.Value == "256" ? 64 : 32);
            if (dlcDeepSpace == null)
            {
                // ClaudeNote: 0.4.2（Lune 2026-11-16）改为自动：启动时检测到深空娘化已启用就加载，不用玩家手动开；只留“关”作为退路
                dlcDeepSpace = cfg.Define("dlcDeepSpace", "auto", "DLC",
                    () => Chinese ? "示范注册表：深空娘化" : "Demo registry: DeepSpace portraits",
                    new[] { "auto", "off" },
                    () => Chinese
                        ? "自动：「深空娘化 · 人物美化内容包」启用着时，按框架附带的注册表读取它的立绘与情绪差分（图片仍在它自己的文件夹里，用到时才读）。关：不读。改动在下次进游戏时生效。没启用那个包、只想读它的图：到贴图扩展工坊的“来源一览”里激活它。"
                        : "Auto: when the DeepSpace portrait pack is enabled, register its portraits and emotion states from the bundled table. Takes effect on next launch.");
                // 不在游戏中途切换（中途重登记上千行、重建立绘库，Lune 实测会卡住点击）；下次启动生效
            }
            if (lewdMood == null)
            {
                lewdMood = cfg.Define("lewdMemeMood", "on", "Compat",
                    () => Chinese ? "lewd_meme 好感立绘" : "lewd_meme affection portraits",
                    new[] { "on", "off" },
                    () => Chinese
                        ? "装了 lewd_meme 时生效：少女道具改由本框架画高清图；每晚过夜处理后按好感换情绪立绘（≤−60 愤怒、≤−20 不开心、≥20 开心、≥60 欢喜）。情绪图要由立绘包提供，没有时显示高清本体。关掉后还原 lewd_meme 原图。"
                        : "With lewd_meme: after the nightly processing, maiden items switch to emotion portraits by affection.");
                lewdMood.OnChanged += v => { if (assetsReady) LewdMemeCompat.Apply(v == "on"); };
            }
            if (visitorPortraits == null)
            {
                // ClaudeNote: 0.5.0 第 4 步 来源分发在游戏里生效（SpriteDict.GetSprite 前缀）
                visitorPortraits = cfg.Define("visitorPortraits", "on", "Display",
                    () => Chinese ? "来客等处按选中的来源显示立绘" : "Show the chosen portrait source in game",
                    new[] { "on", "off" },
                    () => Chinese
                        ? "开：来客、对话、全息电话等走游戏立绘表的地方，显示你在“来源设置”里选的立绘（尺寸与位置对齐原图）。关：这些地方一律显示游戏表里的原图，只有道具图跟着来源走。"
                        : "On: visitors and other places that read the game's portrait table show the source you picked. Off: they show the table's own art.");
                visitorPortraits.OnChanged += v => PlanPort.Enabled = v != "off";
            }
            PlanPort.Enabled = visitorPortraits.Value != "off";
            if (visitorPools == null)
            {
                // ClaudeNote: 0.9.12 新立绘加入来客立绘池（Core/PortraitPools.cs）。默认开（Lune 2026-11-18）
                visitorPools = cfg.Define("visitorPools", "on", "Display",
                    () => Chinese ? "新立绘加入来客立绘池" : "Add new portraits to visitor pools",
                    new[] { "on", "off" },
                    () => Chinese
                        ? "开：激活的立绘包里多出来的随机路人立绘（原版没有的新编号、“池名_任意”命名的、带 generic 标签的）会和原版的一起被抽到。只追加，原有的立绘怎么抽不变。关：只抽原版有的。要“来客等处按选中的来源显示立绘”开着才生效。"
                        : "On: extra random-visitor portraits from active packs can be drawn along with the game's own. Off: only the game's own keys. Needs the option above to be on.");
                visitorPools.OnChanged += v => PortraitPools.Enabled = v != "off";
            }
            PortraitPools.Enabled = visitorPools.Value != "off";
            if (dialogPlacement == null)
            {
                // ClaudeNote: 0.9.5 选项是定义时就要给死的字符串，而定义发生在加载器 Awake 里——0.9.3 在这里问了当前语言，
                //   等于在本地化系统初始化之前同步取 SelectedLocale，把游戏的本地化表弄坏了（标题界面全部取字失败、按钮点不动）。
                //   选项改成不看语言的双语写法；DialogPlacement.Normal 只认第一个词，旧存的 top / high / 高 都照认。
                dialogPlacement = cfg.Define("dialogPlacement", "auto 自动", "Display",
                    () => Chinese ? "来客对话框位置" : "Visitor dialogue box position",
                    new[] { "auto 自动", "vanilla 原版", "high 高", "low 低", "mid 中" },
                    () => Chinese
                        ? "自动：跟着立绘包 / 立绘的设置走（有的立绘构图会被原版位置挡住脸）。原版：原版位置。高：头顶右上方，名字牌在气泡下面。低：柜台高度、立绘左侧。中：柜台高度、立绘正下方。事件自己指定位置时以事件为准。"
                        : "auto: follow the portrait pack. vanilla / high (above the head, name plate below) / low (counter height, left of the visitor) / mid (counter height, under the visitor): force one position for every visitor.");
                dialogPlacement.OnChanged += v => { DialogPlacement.PlayerSetting = v; DialogPlacement.Place(); };
            }
            DialogPlacement.PlayerSetting = dialogPlacement.Value;
            if (hdRender == null)
            {
                hdRender = cfg.Define("hdRender", "on", "Display",
                    () => Chinese ? "高清立绘渲染" : "HD portrait rendering",
                    new[] { "on", "off" },
                    () => Chinese
                        ? "开：柜台上是高清立绘时，店面改按屏幕分辨率渲染（原版先画进 960×540 的小画布再放大，高清图会被压糊）。原版像素图看起来不变。关：完全原版路径（用来对比）。"
                        : "On: while an HD portrait is on the counter, the shop renders at screen resolution instead of the 960x540 pixel canvas. Off: vanilla path.");
                hdRender.OnChanged += v => HdRender.Enabled = v != "off";
            }
            HdRender.Enabled = hdRender.Value != "off";
            if (autoSize == null)
            {
                // ClaudeNote: 1.0.0 立绘大小两项（Lune 2026-11-18；替掉原来的 autoFit）：
                //   自动大小 开 → 按自动算的结果（算法见 Core/FitMath.cs）；关 → 用下面滑杆的值。两项都只是“默认”：作者给包登记了显示大小的，按作者写的。
                autoSize = cfg.Define("portraitAutoSize", true, "Display",
                    () => Chinese ? "立绘自动大小" : "Auto portrait size",
                    () => Chinese
                        ? "开：各立绘包留白不同，框架量出每个包里人物实际占画布的比例，把人调到和原版差不多高（最多缩小 25%、放大 10%，底边贴齐）。开着时下面的“立绘默认大小”不起作用。关：所有立绘包都按“立绘默认大小”的百分比显示。作者给包登记了显示大小的，两种情况下都按作者写的。"
                        : "On: each portrait pack is scaled so its characters stand about as tall as vanilla ones; the size slider below is ignored. Off: every pack uses the slider. Packs whose author set a size keep it either way.");
                autoSize.OnChanged += v => { Fit.Enabled = v; Fit.Changed(); };
                portraitScale = cfg.Define("portraitScale", 100, "Display",
                    () => Chinese ? "立绘默认大小（%）" : "Default portrait size (%)", 50, 150,
                    () => Chinese
                        ? "只在“立绘自动大小”关着时起作用：所有立绘包的图按这个百分比显示（100 = 画布和原版一样大，底边贴齐）。游戏原图不受影响；作者给包登记了显示大小的按作者写的。"
                        : "Only used while auto portrait size is off: every portrait pack is shown at this percentage (100 = same canvas as vanilla, bottom aligned). Vanilla art and packs with an author-set size are not affected.");
                portraitScale.OnChanged += v => { Fit.Manual = v / 100f; Fit.Changed(); };
            }
            Fit.Enabled = autoSize.Value;
            Fit.Manual = portraitScale.Value / 100f;
            if (workshopKey == null)
            {
                // ClaudeNote: 1.0.0 工坊热键可改（Lune 2026-11-18）。按键识别用游戏给 mod 的正式入口：游戏“选项”的按键设置里多一行，和原版改键同一套界面
                //   （mod 自己的设置窗口没有按键识别控件）。键存在这里；这一项在 mod 设置里显示成一个文本框，也可以直接写键名。
                workshopKey = cfg.Define("workshopKey", "PageUp", "Workshop",
                    () => Chinese ? "贴图扩展工坊的热键" : "Workshop hotkey", null,
                    () => Chinese
                        ? "开 / 关贴图扩展工坊的键。推荐去游戏“选项”的按键设置里改（本框架那一栏，点一下再按想用的键）；这里显示的是键名，也可以直接写（Unity 的键名，如 PageUp、F6、Home；写 None = 不用热键）。"
                        : "Key that opens the workshop. Change it in the game's Options > key bindings (this framework's section); the key name shown here can also be typed (Unity key names; None = no hotkey).");
                workshopKey.OnChanged += v => Baker.Hotkey = ParseKey(v);
                ModHelper.AddGameOptionSection(Painter.ModId, "keys", () => Chinese ? "大概是游戏扩展框架" : "Probably Extend Framework");
                ModHelper.AddGameOptionKeyBind(Painter.ModId, "keys", "workshop", () => Chinese ? "打开贴图扩展工坊" : "Open the texture workshop",
                    () => Baker.Hotkey,
                    key => { Baker.Hotkey = key; workshopKey.Set(key.ToString()); cfg.Save(); },
                    () => Chinese ? "标题界面和游戏里都能用。" : "Works on the title screen and in game.");
            }
            Baker.Hotkey = ParseKey(workshopKey.Value);
            if (beforeUninstall == null)
            {
                // ClaudeNote: 0.6.2 卸载前清理的第二个入口：调试面板要在游戏目录放 dev.txt 才打得开，普通玩家从设置里也能执行
                beforeUninstall = cfg.Define("beforeUninstall", "no", "Uninstall",
                    () => Chinese ? "卸载前清理" : "Clean up before uninstall",
                    new[] { "no", "run" },
                    () => Chinese
                        ? "停用或删除本 mod 之前，在店面里把这项改成 run 并应用：存档里被本框架换过图的物品会还原成不依赖本框架的样子，之后存档（过夜）、退出、再停用。执行后本项自动回到 no；这一局里框架不再自动换图。调试面板里也有同一个按钮。"
                        : "Before disabling or removing this mod, set this to run while in the shop: items in the save are restored so they no longer depend on the framework. Then save (sleep), quit, and disable.");
                beforeUninstall.OnChanged += v =>
                {
                    if (v != "run") return;
                    try { Painter.Retire(); }
                    catch (Exception ex) { Warn("clean-up before uninstall failed: " + ex.Message); }
                    beforeUninstall.Set("no");
                };
            }
        }

        // ClaudeNote: 读档接图（Lune 2026-11-16 定）：读档完成时常规跑一次，1.5 秒后再跑一次——
        //   别的 mod（lewd_meme 补注入少女立绘等）可能在读档收尾时又把图换回去，第二次兜底接上
        private static void RecheckAfterLoad()
        {
            try
            {
                ModHelper.Delay(Painter.ModId, 1.5f, () =>
                {
                    // （这 1.5 秒里 mod 被停用的话，游戏会把本 mod 的延迟回调一起停掉：ModLoader 停用时调 StopBackgroundWork）
                    try { Painter.RestoreAll(); Painter.RefreshAll(); }
                    catch (Exception ex) { Warn("recheck after load failed: " + ex.Message); }
                });
            }
            catch (Exception ex) { Warn("recheck after load not scheduled: " + ex.Message); }
        }

        private static UnityEngine.KeyCode ParseKey(string name)
        {
            return Enum.TryParse((name ?? "").Trim(), true, out UnityEngine.KeyCode key) ? key : UnityEngine.KeyCode.PageUp;
        }

        internal static bool Chinese
        {
            get
            {
                // ClaudeNote: 0.9.5 本 mod 的资源载入事件之前（加载器还在 Awake）不许碰本地化：见 DefineConfig 里 dialogPlacement 的说明
                if (!localeReady) return false;
                try { return ModHelper.GetCurrentLocale()?.Identifier.Code?.StartsWith("zh") ?? false; }
                catch (Exception) { return false; }
            }
        }

        private static void OnAssetsLoaded(LoadedMod mod)
        {
            // ClaudeNote: 表格注册——别的 mod 文件夹里的 TextureExtend.csv。本 mod 自己载入时把先于它载入的 mod 补读一遍。
            if (mod?.Manifest == null) return;
            if (mod.Manifest.ID != Painter.ModId)
            {
                CsvLoader.Auto(mod);
                if (BundledTables.Has(mod.Manifest.ID) && assetsReady) BundledTables.ApplyEnabled();   // 附带登记表的包比本框架晚载入时
                PortraitLibrary.Invalidate();   // 新载入的 mod 可能带着按字段结构命名的立绘：识别要重做
                Painter.NeedScan = true;
                return;
            }
            // ClaudeNote: 0.4.1 不在这里补读别的 mod 的表：那时它们的贴图可能还没载入（系列登记会找不到图）。
            //   每个 mod 自己的资源载入事件会读它的表；漏掉的由后台进店面时补读。
            assetsReady = true;
            localeReady = true;
            BundledTables.AutoForEnabled = dlcDeepSpace == null || dlcDeepSpace.Value != "off";
            BundledTables.ApplyEnabled();
            LewdMemeCompat.Apply(lewdMood == null || lewdMood.Value == "on");
            Workshop.Apply();   // ClaudeNote: 0.7.0 工坊导出的注册表格（ModData 里）当作一个来源读进来
            Cache.LoadIndex();
            RegisterDebug();
            Painter.FireReady();   // 1.0.0 rc9 告诉订阅了 Painter.OnReady 的 mod：可以登记了
            Info("ready · " + Cache.Count + " cached image(s)");
        }

        // ClaudeNote: 2026-11-16 调试项全部收进 TextureExtend 调试面板（DevPanel），原版 F9 只挂“打开调试面板”一个按钮。
        //   先撤掉框架自己的旧登记，资源重载时不会重复。
        private static void RegisterDebug()
        {
            string page = DevPanel.FramePage;
            DevPanel.RemoveOwner(Painter.ModId);
            DevPanel.Add(Painter.ModId, page, Chinese ? "状态报告" : "Status", () => Info(Painter.Report()));
            // ClaudeNote: 0.6.2 停用 / 删除本 mod 之前执行：把存档里的物品还原成不依赖本框架的样子（Display/Retire.cs）
            DevPanel.Add(Painter.ModId, page, Chinese ? "【卸载前清理】停用本 mod 之前先点这个" : "[Before uninstall] run this first", () => Painter.Retire());
            DevPanel.Add(Painter.ModId, page, Chinese ? "重建缓存" : "Rebuild cache", Painter.Rebuild);
            // 自检——把立绘库全表（字段、标签、来源、尺寸）写到 ModData/Lune.TextureExtend/PortraitLibrary.json
            DevPanel.Add(Painter.ModId, page, Chinese ? "导出立绘库全表" : "Dump portrait library", DumpLibrary);
            // ClaudeNote: 0.6.0 游戏更新后先点这个：每个补丁 / 读数接口一行
            DevPanel.Add(Painter.ModId, page, Chinese ? "接触面自检（游戏更新后先看这个）" : "Game surface self-check", () => Info(Ports.Report()));
            DevPanel.Add(Painter.ModId, page, Chinese ? "立绘大小适配：各来源的占比与缩放" : "Auto-fit: ratio and scale per source", () => Info(Fit.Report()));
            DevPanel.Add(Painter.ModId, page, Chinese ? "显示探针（写进日志）" : "Display probe (to log)", Probe.Run);
            // ClaudeNote: 0.5.1 开着时柜台上的图每换一次就写一行诊断（糊 / 清楚的原因）
            DevPanel.Add(Painter.ModId, page, Chinese ? "自动诊断：每位来客写一行（开 / 关）" : "Auto probe per visitor (toggle)", () =>
            {
                Probe.Auto = !Probe.Auto;
                Info("auto probe " + (Probe.Auto ? "on" : "off"));
            });
            DevPanel.Add(Painter.ModId, page, Chinese ? "立绘来源设置" : "Portrait sources", SourceWindow.Open);
            DevPanel.Add(Painter.ModId, page, Chinese ? "立绘池：各池追加了几张新立绘" : "Portrait pools: new portraits per pool", () => Info(PortraitPools.Report()));
            DevPanel.Add(Painter.ModId, page, Chinese ? "对话框位置：轮换预览" : "Dialogue box: cycle preview", CycleDialog);
            DevPanel.Add(Painter.ModId, page, Chinese ? "lewd_meme 好感立绘：立即判定（不等过夜）" : "lewd_meme affection: judge now", () =>
            {
                if (LewdMemeCompat.Active) LewdMemeCompat.Overnight();
                else Info("lewd_meme compat is off (lewd_meme not enabled, or turned off in settings)");
            });
            DevPanel.RegisterF9();
        }

        private static void DumpLibrary()
        {
            try
            {
                ModHelper.WriteModDataFileText(Painter.ModId, "PortraitLibrary.json", PortraitLibrary.Dump());
                Info(PortraitLibrary.Summary() + " · written to ModData/" + Painter.ModId + "/PortraitLibrary.json");
            }
            catch (Exception ex) { Warn("portrait library dump failed: " + ex.Message); }
        }

        private static readonly string[] dialogCycle = { DialogPlacement.Vanilla, DialogPlacement.High, DialogPlacement.Low, DialogPlacement.Mid };
        private static int dialogAt;

        // 当前这段对话里轮换四档位置看效果（对话结束自动恢复）
        private static void CycleDialog()
        {
            dialogAt = (dialogAt + 1) % dialogCycle.Length;
            DialogPlacement.Use(dialogCycle[dialogAt]);
            Info("dialogue box → " + dialogCycle[dialogAt] + " (until this conversation ends)");
        }

        // 读档开始：只属于上一局的东西放掉（选图窗口抓着的物品、对话框位置状态）
        private static void OnSessionStart()
        {
            Picker.Close();
            PickButton.Hide();
            Notices.Reset();
            Painter.Unsettle();        // ClaudeNote: 0.7.2 开始读档：读完之前来源不算定下来（见 Painter.Settled）
            Painter.Retired = false;   // 新读的档：照常工作（卸载前清理只管做清理的那一局）
        }

        private static void Guard(string what, Action run)
        {
            try { run(); }
            catch (Exception ex) { Health.Report(what, ex); }
        }

        private static void OnLoaded()
        {
            Guard("checkpoint load", () =>
            {
                Wants.Reload();
                Wants.TimeFromStore();
                Counter.Reset();
                LocalImages.Scan();   // 每次读档重扫本地图片（只列文件、比记号）
                ItemArt.Discover();   // 启用中的 mod 贴图表里按“道具ID_差分名”命名的图：自动进道具贴图组
                Painter.RefreshAll();
            });
        }

        private static void OnSleepLate()
        {
            Guard("checkpoint sleep", () =>
            {
                Wants.Expire(Wants.UntilSleep);
                Wants.Reload();
                Wants.SetTime(Wants.WakeUp);
                Painter.RefreshAll();
            });
        }

        private static void OnWake() { Guard("checkpoint wake", () => { Wants.Reload(); Wants.SetTime(Wants.WakeUp); Painter.RefreshManaged(); }); }
        private static void OnOpen() { Guard("checkpoint open", () => { Wants.Reload(); Wants.SetTime(Wants.OpenShop); Painter.RefreshManaged(); }); }
        private static void OnClose() { Guard("checkpoint close", () => { Wants.Reload(); Wants.SetTime(Wants.EndShop); Painter.RefreshManaged(); }); }

        private static void OnSleep()
        {
            Fit.Save();
            Painter.PruneUsed();
            Cache.SaveIndex();
        }

        internal static void Info(string message) { log?.Log(message); DevPanel.Capture(message); }
        internal static void Warn(string message) { log?.Warning(message); DevPanel.Capture("! " + message); }
    }
}
