// ClaudeNote: 表格注册（TextureExtend.csv）。能填表就不写代码；每种行都有对应的代码 API。
//   自动加载：任何已启用 mod 的文件夹里放一份 TextureExtend.csv，资源载入后框架自动读（进店面时再补读一遍没读到的）。
//   手动加载：Painter.LoadCsv(modId, "相对路径.csv")。框架自带的 DLC 示范表也走这里（按配置开关加载）。
//   表头必须有，认列名不认顺序；表中间可以再写一行表头（第一格是 kind），之后的行换用新表头。列：kind,target,id,tag,sprite,suffixes,prefix,tags,manual,word,name,priority,when,weight,on,until,group,under,with,without,dialog
//   kind（行类型）：
//     item      id + sprite + suffixes   道具：本体贴图键 + 后缀序列（red|blue）→ 找 本体键_red… 登记为可切换的状态
//     portrait  id + sprite [+ tags]     立绘：登记一张立绘（新 ID，或接管游戏字段）；tags 空格分开
//     portrait  id + tags（无 sprite）   只给已有立绘追加标签
//     state     target + id + tag + sprite  给道具 / 立绘挂一个状态图
//     series    target + id + prefix     一整个系列：prefix=Clara_story → Clara_story 为本体，Clara_story_ep1… 为状态 ep1…
//     pack      id + name [+ priority]   声明这个 mod 是一个贴图包 / 立绘包（玩家按 name 选来源，第 4 步启用）
//     faction   id + word [+ name]       新派系：word=Mutant → femaleMutant3 识别为派系 id
//     rule      target + id + tag + when [+ weight] [+ sprite]   条件切换（when 的写法见 When.cs）；道具可用 sprite 指定这个状态的图
//     pulse     target + id + tag + on [+ until]                动作触发的临时状态：on=water，until=sleep（默认 sleep）
//     tag       target + group [+ id] [+ under] [+ with] [+ without]   标签层：id 进组（* = 符合约束的全部）；只写 group + under = 声明上下级
//     join      id + group [+ when]      root_key 组（0.8.2）：id 这张立绘归到 group（root_key）名下，成为它的差分候选；写了 when = 道具上成立时自动切到它
//     auto      id [+ when]              自动动态适配（0.8.3）：来客取图时实时替换（成员：成立时显示成它；root_key 自己：不成立时显示游戏原图）
//     pack 行的 priority 可写 top（或 top 10）= 宣称 TopPriority：同一张立绘有多个来源时优先用它，只让位给玩家自己的选择
//     dynamic   id                       动态贴图道具 [DynamicTexture]（0.8.0）：id = 道具 ID。这种道具的图是生成时才定的，选图窗口直接给它整个图库
//   id 可以写 @组名（标签层里的一组，或立绘的标签 / 道具的游戏分类）。with / without 空格分开。
//   pack 行可选 scale（描绘大小：1x = 画布与原版同高，2x = 两倍，或小数；不写 = 框架按内容占比自动适配，见 Core/FitMath.cs）、style（hd = 高清渲染路径，pixel = 像素风：点采样、走原版路径）。
//   pack / portrait 行可选 dialog：这个包 / 这张立绘说话时对话框放哪（high / low / mid / vanilla，也可写 高 / 低 / 中 / 原版；见 DialogPlacement.cs）。
//   portrait / state 行可选 frame（0.6.4）：waist（腰上）/ bust（胸上）——这一行的 sprite 是这张立绘（这个状态）的专用取景图，
//           不写 frame = 全身图。没交专用图的用途照旧从全身图上部裁。例：portrait,,clara,,clara_bust,…,frame=bust
//   target：item / portrait。manual=no：这个目标禁止手动切换（选图窗口能看不能选）。
//   sprite：本 mod 贴图表里的键（Textures/Items 或 Textures 根目录的文件名，不带扩展名）；
//           或 模组ID:相对路径.png —— 另一个 mod 文件夹里的图片（仅立绘，用到时才读）。
//   # 开头是注释；字段里有逗号时用双引号包起来；坏行跳过并报行号。
using System;
using System.Collections.Generic;
using UnityEngine;
using ProbablyExtendFramework.TagSystem;

namespace ProbablyExtendFramework
{
    internal static class CsvLoader
    {
        internal const string AutoFile = "TextureExtend.csv";
        private static readonly HashSet<string> autoLoaded = new HashSet<string>();
        private static readonly string[] Columns = { "kind", "target", "id", "tag", "sprite", "suffixes", "prefix", "tags", "manual", "word", "name", "priority",
            "when", "weight", "on", "until", "group", "under", "with", "without", "dialog", "scale", "style", "frame" };

        /// <summary>这个 mod 文件夹里有 TextureExtend.csv 就读（每个 mod 每次启用只读一回）。</summary>
        internal static void Auto(LoadedMod mod)
        {
            string id = mod?.Manifest?.ID;
            if (id == null || id == Painter.ModId || !mod.IsEnabled || !autoLoaded.Add(id)) return;
            try { if (ModHelper.ModFileExists(id, AutoFile)) File(id, AutoFile, id); }
            catch (Exception ex) { Entry.Warn(id + "/" + AutoFile + " not loaded: " + ex.Message); }
        }

        internal static void Reset() { autoLoaded.Clear(); }

        /// <summary>读 fileMod 文件夹里的表，登记在 owner 名下（DLC 示范表：文件在框架里，owner 是 DLC 的来源名）。</summary>
        internal static int File(string fileMod, string relativePath, string owner)
        {
            string text = ModHelper.ReadModFileText(fileMod, relativePath);
            if (text == null) { Entry.Warn(fileMod + "/" + relativePath + " not found"); return 0; }
            int count = Text(owner, fileMod, text, relativePath);
            Entry.Info(fileMod + "/" + relativePath + ": " + count + " row(s) registered as " + owner);
            return count;
        }

        /// <summary>返回登记成功的行数。owner = 登记归属（撤销时用）；spriteMod = 不带“模组ID:”的贴图键去哪个 mod 找。</summary>
        internal static int Text(string owner, string spriteMod, string text, string label = "csv")
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(text)) return 0;
            int[] col = null;
            int count = 0, lineNo = 0;
            foreach (string raw in text.Split('\n'))
            {
                lineNo++;
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line[0] == '#') continue;
                List<string> cells = Split(line);
                // ClaudeNote: 0.5.0 表中间可以再写一行表头（第一格是 kind），之后的行按新表头读——不同种类的行可以各用各的列
                if (col == null || (cells.Count > 0 && string.Equals(cells[0], "kind", StringComparison.OrdinalIgnoreCase)))
                {
                    col = new int[Columns.Length];
                    for (int c = 0; c < Columns.Length; c++) col[c] = cells.FindIndex(h => string.Equals(h, Columns[c], StringComparison.OrdinalIgnoreCase));
                    if (col[0] < 0) { Entry.Warn(owner + "/" + label + ": header needs a kind column"); return 0; }
                    continue;
                }
                var row = new Row(cells, col);
                bool ok;
                try { ok = Apply(owner, spriteMod, row); }
                catch (Exception ex) { ok = false; Entry.Warn(owner + "/" + label + " line " + lineNo + ": " + ex.Message); }
                if (ok) count++;
                else Entry.Warn(owner + "/" + label + " line " + lineNo + " skipped: " + line);
            }
            return count;
        }

        private sealed class Row
        {
            private readonly List<string> cells;
            private readonly int[] col;
            internal Row(List<string> cells, int[] col) { this.cells = cells; this.col = col; }
            internal string this[string name]
            {
                get
                {
                    int i = Array.IndexOf(Columns, name), at = i >= 0 ? col[i] : -1;
                    return at >= 0 && at < cells.Count ? cells[at] : "";
                }
            }
        }

        private static bool Apply(string owner, string spriteMod, Row r)
        {
            string kind = r["kind"].ToLowerInvariant(), id = r["id"];
            bool noManual = string.Equals(r["manual"], "no", StringComparison.OrdinalIgnoreCase);
            switch (kind)
            {
                case "item":
                {
                    string baseKey = r["sprite"];
                    string[] suffixes = Words(r["suffixes"], '|');
                    if (id.Length == 0 || baseKey.Length == 0 || suffixes.Length == 0) return false;
                    foreach (string s in suffixes) PaintRules.AddItemVariant(owner, id, s, spriteMod, baseKey + "_" + s);
                    if (noManual) PaintRules.DenyManual(owner, PaintTarget.StaticItem, id);
                    return true;
                }
                case "portrait":
                {
                    if (id.Length == 0) return false;
                    string sprite = r["sprite"];
                    string[] tags = Words(r["tags"], ' ');
                    string dialog = DialogPlacement.Normal(r["dialog"]);
                    if (!FrameColumn(r["frame"], out Display.Usage frame)) return false;
                    if (frame != Display.Usage.FullBody) return sprite.Length > 0 && RegisterFrame(owner, spriteMod, id, null, sprite, frame);
                    if (sprite.Length > 0) RegisterPortrait(owner, spriteMod, id, null, sprite, tags);
                    else if (tags.Length > 0) PortraitLibrary.AddTags(owner, id, tags);
                    else if (dialog == null) return false;
                    if (dialog != null) DialogPlacement.SetFor(owner, id, dialog);
                    if (noManual) PaintRules.DenyManual(owner, PaintTarget.Portrait, id);
                    return true;
                }
                case "state":
                {
                    string tag = r["tag"], sprite = r["sprite"];
                    if (id.Length == 0 || tag.Length == 0 || sprite.Length == 0 || !Target(r["target"], out PaintTarget target)) return false;
                    if (!FrameColumn(r["frame"], out Display.Usage frame)) return false;
                    if (frame != Display.Usage.FullBody) return target == PaintTarget.Portrait && RegisterFrame(owner, spriteMod, id, tag, sprite, frame);
                    if (target == PaintTarget.Portrait) RegisterPortrait(owner, spriteMod, id, tag, sprite, null);
                    else if (!IsExternal(sprite)) PaintRules.AddItemVariant(owner, id, tag, spriteMod, sprite);
                    else return false;   // 道具的图只能是 mod 自己贴图表里的
                    if (noManual) PaintRules.DenyManual(owner, target, id);
                    return true;
                }
                case "series":
                {
                    string prefix = r["prefix"];
                    if (id.Length == 0 || prefix.Length == 0 || !Target(r["target"], out PaintTarget target)) return false;
                    int n = Series(owner, spriteMod, target, id, prefix);
                    if (noManual) PaintRules.DenyManual(owner, target, id);
                    return n > 0;
                }
                case "pack":
                {
                    if (id.Length == 0) return false;
                    int priority = Packs.ParsePriority(r["priority"], out bool top);
                    Packs.Declare(owner, id, r["name"].Length > 0 ? r["name"] : id, priority, r["dialog"], top);
                    Packs.Style(owner, id, r["scale"], r["style"]);
                    return true;
                }
                case "rule":
                {
                    string tag = r["tag"], when = r["when"];
                    if (id.Length == 0 || tag.Length == 0 || when.Length == 0 || !Target(r["target"], out PaintTarget target)) return false;
                    Func<GameItem, bool> cond = When.Compile(when);   // 写错了在这里报行号
                    PaintRule rule = PaintRules.Add(owner, target, id).Tag(tag).When(cond);
                    if (int.TryParse(r["weight"], out int weight)) rule.Weight(weight);
                    string sprite = r["sprite"];
                    if (sprite.Length > 0 && target == PaintTarget.StaticItem && !IsExternal(sprite)) rule.Sprite(spriteMod, sprite);
                    if (noManual) PaintRules.DenyManual(owner, target, id);
                    return true;
                }
                case "pulse":
                {
                    string tag = r["tag"], on = r["on"];
                    if (id.Length == 0 || tag.Length == 0 || on.Length == 0 || !Target(r["target"], out PaintTarget target)) return false;
                    PulseDef d = Pulses.Add(owner, target, id).On(on);
                    if (r["until"].Length > 0) d.Until(r["until"]);
                    d.Tag(tag);
                    if (noManual) PaintRules.DenyManual(owner, target, id);
                    return true;
                }
                case "tag":
                {
                    string group = r["group"];
                    if (!Target(r["target"], out PaintTarget target)) return false;
                    // ClaudeNote: 0.7.0 tag 行也认 manual=no（工坊导出用：只禁手动选图、不登记贴图的目标没有别的行可写）。只对点名的 id 生效
                    bool deny = noManual && id.Length > 0 && id != "*";
                    if (deny) PaintRules.DenyManual(owner, target, id);
                    if (group.Length == 0) return deny;
                    TagDef d = Regist.Tag(owner, target, id).Group(group);
                    if (r["under"].Length > 0) d.Under(r["under"]);
                    if (r["with"].Length > 0) d.With(Words(r["with"], ' '));
                    if (r["without"].Length > 0) d.Without(Words(r["without"], ' '));
                    return true;
                }
                case "join":   // 0.8.2 root_key 组：id = 这张立绘，group = root_key，when = 限制条件（可空）
                {
                    string root = r["group"], when = r["when"];
                    if (id.Length == 0 || root.Length == 0) return false;
                    return RootJoin.Add(owner, id, root, when.Length > 0 ? When.Compile(when) : null);
                }
                case "pool":   // 0.9.12 立绘池：id = 立绘，group = 池名（原版的池如 femaleLower，或自己起的名字）
                {
                    if (id.Length == 0 || r["group"].Length == 0) return false;
                    PortraitPools.Assign(owner, id, r["group"]);
                    return true;
                }
                case "auto":   // 0.8.3 自动动态适配：id = 立绘，when = 限制条件（可空）
                {
                    if (id.Length == 0) return false;
                    RootJoin.SetAuto(owner, id, r["when"].Length > 0 ? When.Compile(r["when"]) : null);
                    return true;
                }
                case "dynamic":   // 0.8.0 动态贴图道具 [DynamicTexture]：id = 道具 ID
                {
                    if (id.Length == 0) return false;
                    PaintRules.AddDynamic(owner, id, null);
                    return true;
                }
                case "faction":
                {
                    string word = r["word"];
                    if (id.Length == 0 || word.Length == 0) return false;
                    Regist.Faction(word, id);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>系列：在 spriteMod 的贴图键里找 prefix（本体）与 prefix_xxx（状态 xxx）。返回登记了几张。代码 API Painter.Series 也走这里。</summary>
        internal static int Series(string owner, string spriteMod, PaintTarget target, string id, string prefix)
        {
            LoadedMod mod = Find(spriteMod);
            if (mod == null) return 0;
            var keys = new List<string>();
            if (mod.Sprites != null) keys.AddRange(mod.Sprites.Keys);
            if (target == PaintTarget.Portrait && mod.Textures != null) keys.AddRange(mod.Textures.Keys);
            int n = 0;
            foreach (string key in keys)
            {
                string tag;
                if (key == prefix) tag = null;
                else if (key.StartsWith(prefix + "_", StringComparison.Ordinal) && key.Length > prefix.Length + 1) tag = key.Substring(prefix.Length + 1).ToLowerInvariant();
                else continue;
                if (target == PaintTarget.Portrait) { RegisterPortrait(owner, spriteMod, id, tag, key, null); n++; }
                else if (tag != null) { PaintRules.AddItemVariant(owner, id, tag, spriteMod, key); n++; }
            }
            return n;
        }

        private static void RegisterPortrait(string owner, string spriteMod, string id, string variant, string sprite, string[] tags)
        {
            if (IsExternal(sprite))
            {
                int colon = sprite.IndexOf(':');
                PortraitLibrary.RegisterFile(owner, id, sprite.Substring(0, colon), sprite.Substring(colon + 1), variant, tags ?? Array.Empty<string>());
                return;
            }
            string key = sprite;
            Func<Sprite> load = () => SpriteOf(spriteMod, key);
            if (string.IsNullOrEmpty(variant)) PortraitLibrary.Register(owner, id, load, tags ?? Array.Empty<string>());
            else PortraitLibrary.RegisterVariant(owner, id, variant, load);
        }

        // frame 列：空 = 全身；写了但认不出 = 坏行（不当成全身图登记，免得一张胸像顶掉本体）
        private static bool FrameColumn(string text, out Display.Usage frame)
        {
            frame = PortraitLibrary.FrameOf(text);
            return text.Length == 0 || frame != Display.Usage.FullBody;
        }

        private static bool RegisterFrame(string owner, string spriteMod, string id, string variant, string sprite, Display.Usage frame)
        {
            if (IsExternal(sprite))
            {
                int colon = sprite.IndexOf(':');
                PortraitLibrary.RegisterFrameFile(owner, id, frame, sprite.Substring(0, colon), sprite.Substring(colon + 1), variant);
                return true;
            }
            string key = sprite;
            PortraitLibrary.RegisterFrame(owner, id, frame, () => SpriteOf(spriteMod, key), variant);
            return true;
        }

        // 模组ID:路径——冒号前不能是单个字母（排除 E:\ 这种绝对路径），冒号后要像文件
        private static bool IsExternal(string sprite)
        {
            int colon = sprite.IndexOf(':');
            return colon > 1 && colon < sprite.Length - 1 && sprite.IndexOf('.', colon) > colon;
        }

        private static bool Target(string text, out PaintTarget target)
        {
            switch ((text ?? "").ToLowerInvariant())
            {
                case "item": target = PaintTarget.StaticItem; return true;
                case "portrait": target = PaintTarget.Portrait; return true;
                default: target = PaintTarget.StaticItem; return false;
            }
        }

        private static LoadedMod Find(string modId) { return ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == modId); }

        private static Sprite SpriteOf(string modId, string key)
        {
            LoadedMod mod = Find(modId);
            if (mod == null || !mod.IsEnabled) return null;
            if (mod.Sprites != null && mod.Sprites.TryGetValue(key, out Sprite s) && s != null) return s;
            return mod.Textures != null && mod.Textures.TryGetValue(key, out Texture2D tex) ? PortraitLibrary.SpriteOf(tex) : null;
        }

        private static string[] Words(string text, char sep)
        {
            var list = new List<string>();
            foreach (string w in (text ?? "").Split(sep)) { string t = w.Trim(); if (t.Length > 0) list.Add(t); }
            return list.ToArray();
        }

        // 一行拆成格：逗号分隔，双引号包住的格里可以有逗号，"" 表示一个引号。
        private static List<string> Split(string line)
        {
            var cells = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') cell.Append(c);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString().Trim()); cell.Length = 0; }
                else cell.Append(c);
            }
            cells.Add(cell.ToString().Trim());
            return cells;
        }
    }

    /// <summary>声明过的贴图包 / 立绘包（来源分发在第 4 步启用；现在先记下来、在状态里显示）。</summary>
    public static class Packs
    {
        public sealed class Pack
        {
            public string Owner { get; internal set; }
            public string Id { get; internal set; }
            public string Name { get; internal set; }
            public int Priority { get; internal set; }
            /// <summary>宣称 TopPriority（0.8.3）：同一张立绘（同 root_key、同差分）有多个来源时优先用它，只让位给玩家自己的选择。</summary>
            public bool Top { get; internal set; }
            /// <summary>作者指定的描绘大小（相对“画布对齐原版”）。0 = 没指定，由框架按内容占比自动适配。</summary>
            public float Scale { get; internal set; }
            /// <summary>像素风：点采样、不走高清渲染路径。</summary>
            public bool Pixel { get; internal set; }
            /// <summary>这个包的立绘说话时对话框放哪（DialogPlacement）。空 = 不指定。</summary>
            public string Dialog { get; internal set; }
        }

        private static readonly Dictionary<string, Pack> packs = new Dictionary<string, Pack>();

        public static ICollection<Pack> All => packs.Values;

        public static void Declare(string owner, string id, string name, int priority, string dialog = null, bool top = false)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(id)) return;
            packs[owner + "|" + id] = new Pack { Owner = owner, Id = id, Name = name ?? id, Priority = priority, Top = top, Dialog = DialogPlacement.Normal(dialog) };
        }

        /// <summary>priority 列：数字，或 top（= TopPriority）。top 也可以和数字一起写（top 10），数字用来在几个都宣称 top 的来源之间排先后。</summary>
        public static int ParsePriority(string text, out bool top)
        {
            top = false;
            int number = 0;
            foreach (string word in (text ?? "").Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(word, "top", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "toppriority", StringComparison.OrdinalIgnoreCase)) top = true;
                else int.TryParse(word, out number);
            }
            return number;
        }

        /// <summary>这个来源宣称了 TopPriority。</summary>
        public static bool TopOf(string owner)
        {
            foreach (Pack p in packs.Values) if (p.Owner == owner && p.Top) return true;
            return false;
        }

        /// <summary>来源（登记者 mod ID）声明的最高优先级；没声明 pack 的算 0。</summary>
        public static int PriorityOf(string owner)
        {
            int best = 0;
            bool any = false;
            foreach (Pack p in packs.Values) if (p.Owner == owner && (!any || p.Priority > best)) { best = p.Priority; any = true; }
            return best;
        }

        /// <summary>来源声明的包名；没声明返回 null。</summary>
        public static string NameOf(string owner)
        {
            foreach (Pack p in packs.Values) if (p.Owner == owner) return p.Name;
            return null;
        }

        /// <summary>pack 行的 scale / style 列。scale：1x、2x 或数字（0.25–4）；style：hd / pixel。</summary>
        public static void Style(string owner, string id, string scale, string style)
        {
            if (!packs.TryGetValue(owner + "|" + id, out Pack p)) return;
            string sc = (scale ?? "").Trim().ToLowerInvariant().TrimEnd('x');
            if (sc.Length > 0 && float.TryParse(sc, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                p.Scale = Mathf.Clamp(f, 0.25f, 4f);
            p.Pixel = string.Equals((style ?? "").Trim(), "pixel", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>作者在 pack 行写的描绘大小；没写返回 null（由 Fit 自动适配）。</summary>
        public static float? AuthoredScale(string owner)
        {
            foreach (Pack p in packs.Values) if (p.Owner == owner && p.Scale > 0f) return p.Scale;
            return null;
        }

        /// <summary>来源是不是像素风包。</summary>
        public static bool PixelOf(string owner)
        {
            foreach (Pack p in packs.Values) if (p.Owner == owner && p.Pixel) return true;
            return false;
        }

        /// <summary>来源声明的对话框位置；没声明返回 null。</summary>
        public static string DialogOf(string owner)
        {
            foreach (Pack p in packs.Values) if (p.Owner == owner && p.Dialog != null) return p.Dialog;
            return null;
        }

        public static void Remove(string owner)
        {
            foreach (string key in new List<string>(packs.Keys)) if (packs[key].Owner == owner) packs.Remove(key);
        }
    }
}
