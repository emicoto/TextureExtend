// ClaudeNote: 对话框位置补丁（Lune 2026-11-16）。不自制对话组件，只在原版对话框显示来客台词时挪一下位置：
//   有的立绘包画面构图比较随意，原版位置会挡住眼睛嘴巴，所以让立绘包 / 立绘自己选对话框放哪。
//   四档（0.9.3，Lune 2026-11-17 照截图上的红框重定）：
//     vanilla  原版位置（立绘右侧、胸口高度）
//     high     高：头顶右上方；名字牌上下翻转、挂在气泡下面（气泡在上、名字牌在它左下）
//     low      低：柜台高度、立绘左侧（气泡右端贴近立绘中线）；名字牌在气泡上面、靠右端
//     mid      中：柜台高度、立绘正下方（气泡从立绘左侧起）；名字牌在气泡上面、靠左端
//   原来的 top 改名 mid（表格 / 配置里写 top 仍然认）。中文也认：高 / 低 / 中 / 原版。
//   谁说了算（前面的优先）：
//     1. 事件方临时指定：DialogPlacement.Use("mid")，到这段对话结束为止（猜拳之类自己挑位置）
//     2. 玩家在配置里强制（dialogPlacement，默认 auto = 跟着立绘走）
//     3. 立绘：表格 portrait 行的 dialog 列 → 这张立绘当前来源的 pack 行的 dialog 列
//     4. 原版位置
//   打字机效果、打字速度、气泡缩放都照原版设置，不碰。射击模式（原版自己挪气泡）期间不动。
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ProbablyExtendFramework
{
    public static class DialogPlacement
    {
        public const string Vanilla = "vanilla", High = "high", Low = "low", Mid = "mid";
        // 位移一律按“原版位置到射击模式位置”的距离 span 的倍数写（不同分辨率、不同缩放下比例一致）。
        // 每行 = { 往上几个 span, 气泡哪一端对齐（-1 左端 / +1 右端）, 那一端相对原版左端往右几个 span }。数值是照 Lune 截图上的红框量的。
        private static readonly Dictionary<string, float[]> spots = new Dictionary<string, float[]>
        {
            { High, new[] { 1.05f, -1f, 0.38f } },
            { Low, new[] { -0.35f, 1f, -0.39f } },   // 0.9.6 Lune：再往右约 20 像素
            { Mid, new[] { -0.65f, -1f, -0.84f } },
        };

        private static readonly Dictionary<string, KeyValuePair<string, string>> perPortrait = new Dictionary<string, KeyValuePair<string, string>>();   // 立绘 → (登记者, 位置)
        private static string forced;          // 事件方临时指定
        internal static string PlayerSetting = "auto";
        // ClaudeNote: 0.6.3 原来自己记两个标记：shooting（靠补丁跟着游戏的进 / 出射击模式翻转）、moved（挪没挪过）。
        //   0.6.1 发现 shooting 会跨存档卡死，我当时的处理是“读档时把标记清零”——治标。成因是：这是游戏状态的一份副本，
        //   靠两个补丁同步，漏掉一次就永远对不上。需求只是“游戏自己在摆气泡的时候别动”，游戏有现成的公开状态可读
        //   （StoreUIManager.isShootingMode / isMagnifyingMode），气泡挪没挪过看它现在的位置就知道。
        //   所以两个标记都删了，补丁也少一个（不再挂 SetShootingMode）。
        private static DialogUIManager forcedFor;    // forced 是对哪个对话框实例说的（换了场景就不作数）
        private const float VanillaShootY = 54f;     // 原版 DialogUIManager.shootingModeY
        private static DialogUIManager baseOf;       // 记下原位时对应的对话框实例（换场景会新建）
        private static float baseX, baseY;
        private static Vector3 baseFlip;             // 气泡底图原来的缩放（low 档把它左右镜像，尖角朝向立绘）

        /// <summary>规范化位置名；认不出返回 null。也认中文：上方 / 中 / 下 / 原版。</summary>
        public static string Normal(string text)
        {
            // ClaudeNote: 0.9.5 配置选项是“high 高”这种双语写法：只认第一个词
            text = (text ?? "").Trim().ToLowerInvariant();
            int space = text.IndexOf(' ');
            switch (space > 0 ? text.Substring(0, space) : text)
            {
                case "vanilla": case "原版": case "default": return Vanilla;
                case "high": case "高": case "上": case "上方": return High;
                case "low": case "低": case "下": case "下方": return Low;
                case "mid": case "middle": case "中": case "中间": case "top": return Mid;   // top 是 0.9.3 之前的名字
                default: return null;
            }
        }

        /// <summary>事件方用：这段对话的来客台词放在这个位置，对话结束自动恢复。传 null 立刻取消。</summary>
        public static void Use(string placement)
        {
            forced = Normal(placement);
            forcedFor = DialogUIManager.Instance;
            Place();
        }

        /// <summary>给一张立绘指定对话框位置（表格 portrait 行的 dialog 列走这里）。</summary>
        public static void SetFor(string owner, string portraitId, string placement)
        {
            placement = Normal(placement);
            if (string.IsNullOrEmpty(portraitId)) return;
            if (placement == null) perPortrait.Remove(portraitId);
            else perPortrait[portraitId] = new KeyValuePair<string, string>(owner ?? "", placement);
        }

        internal static void Remove(string owner)
        {
            foreach (string key in new List<string>(perPortrait.Keys)) if (perPortrait[key].Key == owner) perPortrait.Remove(key);
        }

        /// <summary>现在该用的位置。</summary>
        public static string Current()
        {
            if (forced != null && forcedFor == DialogUIManager.Instance) return forced;
            string player = Normal(PlayerSetting);
            if (player != null) return player;
            string portrait = CounterPortrait();
            if (portrait == null) return Vanilla;
            if (perPortrait.TryGetValue(portrait, out KeyValuePair<string, string> mine)) return mine.Value;
            string source = PortraitLibrary.Get(portrait)?.Source;
            return (source != null ? Packs.DialogOf(source) : null) ?? Vanilla;
        }

        // 柜台上来客的立绘 ID：texext_立绘[_状态] 的显示键还原成立绘 ID
        private static string CounterPortrait()
        {
            try
            {
                StoreClient c = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() : null;
                return Display.PortraitOfKey(c?.GetSprite(), out _);
            }
            catch (Exception) { return null; }
        }

        internal static void Place()
        {
            try
            {
                DialogUIManager ui = DialogUIManager.Instance;
                if (ui == null || ui.clientBubble == null || ui.clientNameBubble == null || GameIsPlacing() || !PortsOk() || Painter.Retired) return;
                string where = Current();
                // ClaudeNote: 2026-11-16 不再用 Traverse 读原版私有字段（游戏的 mod 扫描会判成“反射访问内部实现”，要求声明权限）。
                //   原版在 Start 里把气泡的初始高度记作 defaultModeY；我们第一次碰到这个对话框实例时它还在原位，直接记下来。
                //   拍照模式高度是原版写死的 54。
                if (!(ui.clientBubble.transform is RectTransform bubble) || !(ui.clientNameBubble.transform is RectTransform name)) return;
                Transform back = ui.clientBubble.backgroundImage != null ? ui.clientBubble.backgroundImage.transform : null;
                if (baseOf != ui)
                {
                    baseOf = ui; baseX = bubble.localPosition.x; baseY = bubble.localPosition.y;
                    baseFlip = back != null ? back.localScale : Vector3.one;
                }
                if (where == Vanilla && !Moved(ui) && shaped.Count == 0) return;   // 本来就在原位
                float span = Mathf.Abs(VanillaShootY - baseY);
                if (span < 1f) span = 100f;
                // 先回原样（轴心、翻转、位置），让名字牌按原版的相对位置跟上（ApplyBubbleScale），再按档位挪
                RestoreShape();
                bubble.localPosition = new Vector3(baseX, baseY, bubble.localPosition.z);
                bool mirror = false;
                float edge = 0f;
                if (spots.TryGetValue(where, out float[] spot))
                {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(bubble);
                    float width = bubble.rect.width * bubble.localScale.x;
                    float left0 = baseX - bubble.pivot.x * width;                         // 原版位置时气泡的左端
                    edge = left0 + spot[2] * span;                                        // 目标：对齐的那一端放在这里
                    float left = spot[1] > 0f ? edge - width : edge;
                    bubble.localPosition = new Vector3(left + bubble.pivot.x * width, baseY + spot[0] * span, bubble.localPosition.z);
                    mirror = spot[1] > 0f;
                }
                ui.ApplyBubbleScale();   // 名字牌按原版的相对位置（气泡上面、靠左端）跟上
                if (spot != null)
                {
                    // ClaudeNote: 0.9.6 Lune：“高和低的对话框与名牌锚点得调整下；中正常，估计是锚点跟原版一致”——正是这样。
                    //   气泡和名字牌的宽度是台词 / 名字排完版之后才定的（原版 DialogBubbleWidth 在 LateUpdate 里量），而这里在台词刚交给打字机时就摆位置，
                    //   量到的是上一句的宽度：按右端对齐的 low、要量气泡高度的 high 都会摆歪，只有和原版同一端对齐的 mid 不受影响。
                    //   改法不是“等排完版再摆一次”，而是把对齐的那一端设成轴心：之后宽度、行数怎么变，那一端都不动。
                    float nameLeft = name.localPosition.x - name.pivot.x * name.rect.width * name.localScale.x;
                    float bubbleBottom = bubble.localPosition.y - bubble.pivot.y * bubble.rect.height * bubble.localScale.y;
                    float bubbleTop = bubbleBottom + bubble.rect.height * bubble.localScale.y;
                    float nameHeight = name.rect.height * name.localScale.y;
                    float gap = name.localPosition.y - name.pivot.y * nameHeight - bubbleTop;     // 原版：名字牌下沿到气泡上沿（负数 = 压着）
                    PinX(bubble, mirror ? 1f : 0f, edge);
                    if (where == High)
                    {
                        // 气泡下沿钉住（台词多行时往上长），名字牌上下翻过来挂在气泡下面：原来贴着气泡的那条边还是贴着气泡
                        PinY(bubble, 0f, bubbleBottom);
                        PinX(name, 0f, nameLeft);
                        bool whole = FlipName(ui, name);
                        float top = bubbleBottom - gap;
                        name.localPosition = new Vector3(name.localPosition.x, whole ? top - name.pivot.y * nameHeight : top - (1f - name.pivot.y) * nameHeight, name.localPosition.z);
                    }
                    else if (where == Low) PinX(name, 1f, edge);   // 名字牌右端 = 气泡右端（靠近立绘的那一端）
                    else PinX(name, 0f, nameLeft);
                }
                // low 档气泡在立绘左边：底图左右镜像，尖角朝右。只在“文字不是底图的子物体”时做（否则字也会反）
                if (back != null && back != bubble && (ui.clientBubble.textfield == null || !ui.clientBubble.textfield.transform.IsChildOf(back)))
                    back.localScale = new Vector3(mirror ? -baseFlip.x : baseFlip.x, baseFlip.y, baseFlip.z);
            }
            catch (Exception) { }   // 绝不让异常进游戏的对话流程
        }

        // ---- 轴心与翻转：改过的东西逐件记下原样，下次摆位置之前先全部还原 ----
        private struct Shape { internal RectTransform rect; internal Vector2 pivot; internal Vector3 scale, position; }
        private static readonly List<Shape> shaped = new List<Shape>();

        private static void Keep(RectTransform r)
        {
            foreach (Shape k in shaped) if (k.rect == r) return;
            shaped.Add(new Shape { rect = r, pivot = r.pivot, scale = r.localScale, position = r.localPosition });
        }

        private static void RestoreShape()
        {
            for (int i = shaped.Count - 1; i >= 0; i--)
            {
                Shape k = shaped[i];
                if (k.rect == null) continue;
                k.rect.pivot = k.pivot; k.rect.localScale = k.scale; k.rect.localPosition = k.position;
            }
            shaped.Clear();
        }

        // 把这一端设成轴心并放到指定位置：之后宽 / 高怎么变，这一端不动
        private static void PinX(RectTransform r, float pivotX, float x)
        {
            Keep(r);
            r.pivot = new Vector2(pivotX, r.pivot.y);
            r.localPosition = new Vector3(x, r.localPosition.y, r.localPosition.z);
        }

        private static void PinY(RectTransform r, float pivotY, float y)
        {
            Keep(r);
            r.pivot = new Vector2(r.pivot.x, pivotY);
            r.localPosition = new Vector3(r.localPosition.x, y, r.localPosition.z);
        }

        // 在父物体的框里上下镜像一个子物体的位置（flip = 连图形一起翻；不翻 = 只换位置，字保持正的）
        private static void MirrorY(RectTransform child, RectTransform parent, bool flip)
        {
            Keep(child);
            float h = child.rect.height * Mathf.Abs(child.localScale.y);
            float center = child.localPosition.y + (0.5f - child.pivot.y) * h * Mathf.Sign(child.localScale.y);
            float mirrored = 2f * (0.5f - parent.pivot.y) * parent.rect.height - center;
            Vector3 sc = child.localScale;
            if (flip) sc.y = -sc.y;
            child.localScale = sc;
            child.localPosition = new Vector3(child.localPosition.x, mirrored - (0.5f - child.pivot.y) * h * Mathf.Sign(sc.y), child.localPosition.z);
        }

        // 原地上下翻一个物体的图形（占的位置不变）。
        // ClaudeNote: 0.9.13 Lune 的截图：高档的名字掉到牌子下面去了。原来的做法是“翻转 + 挪位置补偿轴心”，
        //   但名字牌里的字是自动排版摆的：排版每次重排都按轴心把它放回去，我挪的那一下就没了；字的轴心在上沿，翻转后整行就甩到了牌子外面。
        //   改成先把轴心移到正中（占的位置不变），再翻：绕正中翻不需要补偿位置，排版怎么重排它都在原地。
        private static void FlipInPlace(RectTransform r)
        {
            Keep(r);
            float shift = (0.5f - r.pivot.y) * r.rect.height * r.localScale.y;
            r.pivot = new Vector2(r.pivot.x, 0.5f);
            r.localPosition = new Vector3(r.localPosition.x, r.localPosition.y + shift, r.localPosition.z);
            Vector3 sc = r.localScale;
            r.localScale = new Vector3(sc.x, -sc.y, sc.z);
        }

        // ClaudeNote: 0.9.6 名字牌上下翻转（Lune：“high 那个得上下翻转下那个名牌”，0.9.3 只挪了位置没翻图）。
        //   底图翻过来，字不翻、但在牌子里的位置跟着上下镜像（牌子的留白是上下不对称的）。
        //   底图就在名字牌本体上：整个牌子翻（返回 true，调用方按翻转后的上沿算位置），里面的字各自原地翻回来；
        //   底图是子物体：只翻它，其余子物体（字）换到镜像位置。
        private static bool FlipName(DialogUIManager ui, RectTransform name)
        {
            RectTransform image = ui.clientNameBubbleImage != null ? ui.clientNameBubbleImage.transform as RectTransform : null;
            if (image == null || image == name)
            {
                Keep(name);
                Vector3 sc = name.localScale;
                name.localScale = new Vector3(sc.x, -Mathf.Abs(sc.y), sc.z);
                foreach (TMPro.TMP_Text t in name.GetComponentsInChildren<TMPro.TMP_Text>(true)) FlipInPlace(t.rectTransform);
                return true;
            }
            foreach (Transform child in name)
                if (child is RectTransform r) MirrorY(r, name, r == image);
            return false;
        }

        // ClaudeNote: 0.6.0 补丁经 Ports 逐个安装；三个里任何一个装不上，位置补丁整体不动原版（Place 里检查）
        private const string Feature = "来客对话框位置";
        private static readonly string[] portNames = { "DialogUIManager.DisplayClientText", "DialogUIManager.SetNormalMode", "DialogUIManager.EndConversation" };

        internal static void Install()
        {
            Ports.Patch(portNames[0], Feature, () => AccessTools.Method(typeof(DialogUIManager), nameof(DialogUIManager.DisplayClientText)), postfix: new HarmonyMethod(typeof(DialogPlacement), nameof(AfterDisplay)));
            Ports.Patch(portNames[1], Feature, () => AccessTools.Method(typeof(DialogUIManager), nameof(DialogUIManager.SetNormalMode)), postfix: new HarmonyMethod(typeof(DialogPlacement), nameof(AfterNormal)));
            Ports.Patch(portNames[2], Feature, () => AccessTools.Method(typeof(DialogUIManager), nameof(DialogUIManager.EndConversation)), postfix: new HarmonyMethod(typeof(DialogPlacement), nameof(AfterEnd)));
            Ports.Read("DialogUIManager.clientBubble", Feature, () => DialogUIManager.Instance == null ? null : DialogUIManager.Instance.clientBubble == null || DialogUIManager.Instance.clientNameBubble == null ? "bubble objects missing" : null);
        }

        // 游戏自己正在摆气泡（射击 / 放大镜模式）：读游戏的公开状态
        private static bool GameIsPlacing()
        {
            StoreUIManager store = StoreUIManager.Instance;
            return store != null && (store.isShootingMode || store.isMagnifyingMode);
        }

        // 气泡现在不在原位（= 我们挪过）
        private static bool Moved(DialogUIManager ui)
        {
            Vector3 at = ui.clientBubble.transform.localPosition;
            return baseOf == ui && (Mathf.Abs(at.y - baseY) > 0.01f || Mathf.Abs(at.x - baseX) > 0.01f);
        }

        /// <summary>停用：把气泡放回原位（要在撤补丁之前调）。</summary>
        internal static void Shutdown()
        {
            try
            {
                DialogUIManager ui = DialogUIManager.Instance;
                if (ui != null && ui.clientBubble != null && !GameIsPlacing() && Moved(ui))
                {
                    Transform bubble = ui.clientBubble.transform;
                    RestoreShape();
                    bubble.localPosition = new Vector3(baseX, baseY, bubble.localPosition.z);
                    if (ui.clientBubble.backgroundImage != null && ui.clientBubble.backgroundImage.transform != bubble) ui.clientBubble.backgroundImage.transform.localScale = baseFlip;
                    ui.ApplyBubbleScale();
                }
            }
            catch (Exception) { }
            forced = null;
            forcedFor = null;
        }

        private static bool PortsOk()
        {
            foreach (string n in portNames) if (!Ports.Ok(n)) return false;
            return true;
        }

        private static void AfterDisplay() { Place(); }
        private static void AfterNormal() { Place(); }   // 游戏刚把气泡放回原位：按当前设置重新摆
        private static void AfterEnd() { forced = null; forcedFor = null; }
    }
}
