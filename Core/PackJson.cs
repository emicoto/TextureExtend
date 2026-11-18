// ClaudeNote: 1.0.0 rc9 json 注册文件 TextureExtend.json（Lune 2026-11-18）：csv 管简单的静态变化与批量贴标签，json 给图包做完整登记。
//   结构：
//     { "pack": 图包 ID, "name": 显示名, "priority": 数字 或 "top", "root": 贴图路径的前缀, "style": "hd"/"pixel", "scale": 包的显示大小, "dialog": 对话框位置,
//       "tagGroups": [ { "id", "under", "scope": "portrait"/"item" } ],
//       "tags":      [ { "id", "group", "scope" } ],
//       "portraits": [ { "id": 原版立绘键 或 自定义 NPC 的 ID, "file" 或 "sprite", "scale", "tags": [], "pool", "dialog",
//                        "frames": { "waist": 文件, "bust": 文件 }, "variants": [ { "name", "file" 或 "sprite", "tags": [] } ] } ],
//       "items":     [ { "id": 道具 ID, "tags": [], "manual": false, "variants": [ { "name", "file" 或 "sprite", "tags": [] } ] } ],
//       "images":    [ { "id", "path", "variant" } ]   ← 旧写法，照认 }
//   图在哪：file = 相对 root 的文件路径（框架用到时读；没启用的包也读得到）；写成 res:资源名 = 打包在这个 mod 自己 DLL 里的图；
//     sprite = 游戏已经载入的贴图键（启用中的 mod 的 Textures / Textures/Items 里的）。
//   portraits 的 id 是原版立绘键 → 替换那张；是新名字 → 自定义 NPC：写了 pool 进那个来客池，没写进自定义 NPC 池 custom。
//   做法：把 json 翻成同样的表格行交给 CsvLoader —— 立绘、差分、标签、来客池、对话框、取景图、包声明，表格那边都已经有，这里不再写第二套登记逻辑。
//   谁读：启用中的 mod（资源载入时，和 csv 同一时机，两份都读）；架子上没启用的包（上架时，PackShelf.Declare）；代码交来的（Painter.RegisterPack）。
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ProbablyExtendFramework
{
    internal static class PackJson
    {
        internal const string File = "TextureExtend.json";
        private static readonly string[] Header = { "kind", "target", "id", "tag", "sprite", "tags", "group", "under", "manual", "name", "priority", "scale", "style", "dialog", "frame" };

        /// <summary>有没有完整登记的内容（portraits / items / tags…）。只有 name / priority / images 的旧写法返回 false。</summary>
        internal static bool HasBody(JObject root)
        {
            return root != null && (root["portraits"] is JArray || root["items"] is JArray || root["tags"] is JArray || root["tagGroups"] is JArray);
        }

        /// <summary>读一份 json，登记在 owner 名下；modId = 图片在哪个 mod 的文件夹 / DLL 里。返回登记成功的行数。</summary>
        internal static int Apply(string owner, string modId, string text, string label = File)
        {
            JObject root;
            try { root = JObject.Parse(text ?? "{}"); }
            catch (Exception ex) { Entry.Warn(owner + "/" + label + " is not valid json: " + ex.Message); return 0; }
            string csv = ToCsv(root, modId, out List<string> problems);
            foreach (string p in problems) Entry.Warn(owner + "/" + label + ": " + p);
            return CsvLoader.Text(owner, modId, csv, label);
        }

        // ── json → 表格行（纯转换，离线测试 verification_match 直接测） ──

        internal static string ToCsv(JObject root, string modId, out List<string> problems)
        {
            problems = new List<string>();
            var b = new StringBuilder(string.Join(",", Header)).Append('\n');
            string rootPath = ((string)root["root"] ?? "").Replace('\\', '/');
            if (rootPath.Length > 0 && !rootPath.EndsWith("/", StringComparison.Ordinal)) rootPath += "/";

            string pack = (string)root["pack"];
            if (pack != null || root["name"] != null || root["priority"] != null || root["scale"] != null || root["style"] != null || root["dialog"] != null)
                Row(b, "kind", "pack", "id", pack ?? modId, "name", (string)root["name"], "priority", Text(root["priority"]), "scale", Text(root["scale"]), "style", (string)root["style"], "dialog", (string)root["dialog"]);

            foreach (JToken g in Array(root["tagGroups"]))
            {
                string id = (string)g["id"];
                if (string.IsNullOrEmpty(id)) { problems.Add("tagGroups: an entry has no id"); continue; }
                Row(b, "kind", "tag", "target", Scope(g), "tag", "", "group", id, "under", (string)g["under"]);
            }
            foreach (JToken t in Array(root["tags"]))
            {
                string id = (string)t["id"];
                if (string.IsNullOrEmpty(id)) { problems.Add("tags: an entry has no id"); continue; }
                Row(b, "kind", "tag", "target", Scope(t), "group", id, "under", (string)t["group"]);
            }

            foreach (JToken p in Array(root["portraits"]))
            {
                string id = (string)p["id"];
                string image = Image(p, rootPath, modId);
                if (string.IsNullOrEmpty(id)) { problems.Add("portraits: an entry has no id"); continue; }
                Row(b, "kind", "portrait", "id", id, "sprite", image ?? "", "tags", Words(p["tags"]), "scale", Text(p["scale"]), "dialog", (string)p["dialog"]);
                if ((string)p["pool"] is string pool && pool.Length > 0) Row(b, "kind", "pool", "id", id, "group", pool);
                if (p["frames"] is JObject frames)
                    foreach (JProperty f in frames.Properties())
                    {
                        string frame = Image(f.Value, rootPath, modId);
                        if (frame != null) Row(b, "kind", "portrait", "id", id, "sprite", frame, "frame", f.Name);
                    }
                foreach (JToken v in Array(p["variants"]))
                {
                    string name = (string)v["name"], vimage = Image(v, rootPath, modId);
                    if (string.IsNullOrEmpty(name) || vimage == null) { problems.Add("portraits/" + id + ": a variant needs name and file (or sprite)"); continue; }
                    Row(b, "kind", "state", "target", "portrait", "id", id, "tag", name, "sprite", vimage, "tags", Words(v["tags"]));
                    if (v["frames"] is JObject vframes)
                        foreach (JProperty f in vframes.Properties())
                        {
                            string frame = Image(f.Value, rootPath, modId);
                            if (frame != null) Row(b, "kind", "state", "target", "portrait", "id", id, "tag", name, "sprite", frame, "frame", f.Name);
                        }
                }
            }

            foreach (JToken it in Array(root["items"]))
            {
                string id = (string)it["id"];
                if (string.IsNullOrEmpty(id)) { problems.Add("items: an entry has no id"); continue; }
                foreach (string tag in Words(it["tags"]).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    Row(b, "kind", "tag", "target", "item", "id", id, "group", tag);
                if (it["manual"] != null && it["manual"].Type == JTokenType.Boolean && !(bool)it["manual"])
                    Row(b, "kind", "tag", "target", "item", "id", id, "manual", "no");
                foreach (JToken v in Array(it["variants"]))
                {
                    string name = (string)v["name"], vimage = Image(v, rootPath, modId);
                    if (string.IsNullOrEmpty(name) || vimage == null) { problems.Add("items/" + id + ": a variant needs name and file (or sprite)"); continue; }
                    Row(b, "kind", "state", "target", "item", "id", id, "tag", name, "sprite", vimage, "tags", Words(v["tags"]));
                }
            }

            foreach (JToken i in Array(root["images"]))   // 旧写法
            {
                string id = (string)i["id"], path = (string)i["path"];
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(path)) continue;
                string variant = (string)i["variant"];
                string image = modId + ":" + path.Replace('\\', '/');
                if (string.IsNullOrEmpty(variant)) Row(b, "kind", "portrait", "id", id, "sprite", image);
                else Row(b, "kind", "state", "target", "portrait", "id", id, "tag", variant, "sprite", image);
            }
            return b.ToString();
        }

        // 一张图怎么写：file（相对 root 的路径 → 模组ID:路径；res: 开头 = DLL 资源）或 sprite（已载入的贴图键）。一个字符串也当 file。
        private static string Image(JToken t, string rootPath, string modId)
        {
            string file = t == null ? null : t.Type == JTokenType.String ? (string)t : (string)t["file"];
            if (!string.IsNullOrEmpty(file))
            {
                if (file.StartsWith(CsvLoader.ResPrefix, StringComparison.Ordinal)) return file;
                return modId + ":" + rootPath + file.Replace('\\', '/').TrimStart('/');
            }
            string sprite = t != null && t.Type == JTokenType.Object ? (string)t["sprite"] : null;
            return string.IsNullOrEmpty(sprite) ? null : sprite;
        }

        private static string Scope(JToken t) { return string.Equals((string)t["scope"], "item", StringComparison.OrdinalIgnoreCase) ? "item" : "portrait"; }
        private static IEnumerable<JToken> Array(JToken t) { return t as JArray ?? new JArray(); }
        private static string Text(JToken t) { return t == null || t.Type == JTokenType.Null ? "" : t.ToString(); }

        private static string Words(JToken t)
        {
            if (t == null) return "";
            if (t.Type == JTokenType.String) return ((string)t).Trim();
            var list = new List<string>();
            foreach (JToken w in Array(t)) { string s = ((string)w ?? "").Trim(); if (s.Length > 0) list.Add(s); }
            return string.Join(" ", list);
        }

        // 按表头顺序写一行；值里有逗号 / 引号的加引号
        private static void Row(StringBuilder b, params string[] pairs)
        {
            var cells = new string[Header.Length];
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                int at = System.Array.IndexOf(Header, pairs[i]);
                if (at >= 0) cells[at] = pairs[i + 1] ?? "";
            }
            for (int i = 0; i < cells.Length; i++)
            {
                string c = cells[i] ?? "";
                if (c.IndexOfAny(new[] { ',', '"', '\n' }) >= 0) c = "\"" + c.Replace("\"", "\"\"") + "\"";
                if (i > 0) b.Append(',');
                b.Append(c);
            }
            b.Append('\n');
        }
    }

    public static partial class Painter
    {
        /// <summary>代码里交一份 json 注册（和在 mod 文件夹里放 TextureExtend.json 效果一样；适合内容是程序生成的 mod）。
        /// 登记在 modId 名下，UnregisterAll(modId) 撤销。file 路径相对 modId 的文件夹。返回登记成功的行数。</summary>
        public static int RegisterPack(string modId, string json) { return PackJson.Apply(modId, modId, json, "RegisterPack"); }

        private static readonly List<Action> readyListeners = new List<Action>();
        internal static bool ReadyFired { get; private set; }

        /// <summary>框架准备好了（可以登记了）。订阅时已经准备好的话当场调一次，不用管加载顺序。</summary>
        public static event Action OnReady
        {
            add
            {
                if (value == null) return;
                readyListeners.Add(value);
                if (ReadyFired) CallReady(value);
            }
            remove { readyListeners.Remove(value); }
        }

        internal static void ResetReady() { ReadyFired = false; }

        internal static void FireReady()
        {
            ReadyFired = true;
            foreach (Action a in new List<Action>(readyListeners)) CallReady(a);
        }

        private static void CallReady(Action a)
        {
            try { a(); }
            catch (Exception ex) { Entry.Warn("OnReady listener failed: " + ex.Message); }
        }
    }
}
