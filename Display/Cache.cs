// ClaudeNote: 磁盘缓存：ItemCache/<来源组>/<键>@<格数>.png，另有一份 index.json。
//   index.json 记每张图的来源指纹与采样方式；指纹对不上（别的 mod 换了立绘、算法版本变了）就重做。索引改动先记在内存里，由 Baker 空闲时写盘。
//   放在哪（0.9.1，Lune 2026-11-17：缓存最好放 mod 文件夹里，ModData 只留影响存档的）：
//     游戏给 mod 的写文件接口只有 ModData 这一处；往 mod 文件夹里写要“文件”权限，框架本体是零权限，自己写不了。
//     所以缓存的读写抽成一个存放处（ICacheStore）：默认 = ModData（只用 ModHelper.*ModDataFile*）；
//     伴生 mod（有“文件”权限）启用时把存放处换成它自己文件夹里的 Cache/（Painter.UseCacheStore）。
//     换了存放处 = 换一份索引，那边没有的图重新画；原来那边的文件不动。
//   缓存只是加速用的成品图，丢了能重画。影响存档的东西（物品标签、本局用到的图清单）不在这里。
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ProbablyExtendFramework
{
    /// <summary>缓存的存放处。路径都是相对路径（ItemCache/…，用 / 分隔）。读不到返回 null。</summary>
    public interface ICacheStore
    {
        string ReadText(string path);
        byte[] ReadBytes(string path);
        void WriteText(string path, string text);
        void WriteBytes(string path, byte[] bytes);
        bool Delete(string path);
        /// <summary>现有的全部文件（相对路径）。</summary>
        System.Collections.Generic.List<string> List();
        /// <summary>给人看的位置说明（日志用）。</summary>
        string Where { get; }
    }

    // 默认存放处：存档目录下 ModData/<本 mod>/
    internal sealed class ModDataStore : ICacheStore
    {
        public string ReadText(string path) { return ModHelper.ReadModDataFileText(Painter.ModId, path); }
        public byte[] ReadBytes(string path) { return ModHelper.ReadModDataFileBytes(Painter.ModId, path); }
        public void WriteText(string path, string text) { ModHelper.WriteModDataFileText(Painter.ModId, path, text); }
        public void WriteBytes(string path, byte[] bytes) { ModHelper.WriteModDataFileBytes(Painter.ModId, path, bytes); }
        public bool Delete(string path) { return ModHelper.DeleteModDataFile(Painter.ModId, path); }
        public System.Collections.Generic.List<string> List() { return ModHelper.ListModDataFiles(Painter.ModId); }
        public string Where => "ModData/" + Painter.ModId;
    }

    internal static class Cache
    {
        private static readonly ICacheStore modData = new ModDataStore();
        private static ICacheStore store = modData;

        /// <summary>换存放处（null = 回到 ModData）。先把手上的索引写回原处，再改用新处的索引。</summary>
        internal static void UseStore(ICacheStore next)
        {
            next = next ?? modData;
            if (ReferenceEquals(next, store)) return;
            SaveIndex();
            store = next;
            entries = null;
            Dirty = false;
            LoadIndex();
            Entry.Info("cache now lives in " + store.Where + " · " + Count + " cached image(s)");
        }

        internal const string Folder = "ItemCache";
        private const string IndexFile = Folder + "/index.json";
        private const int AlgoVersion = 1;       // 取景 / 重采样改了就加 1，旧缓存整体作废

        private static JObject entries;
        internal static bool Dirty { get; private set; }
        internal static int Count => entries?.Count ?? 0;

        internal static void LoadIndex()
        {
            entries = new JObject();
            try
            {
                string text = store.ReadText(IndexFile);
                if (string.IsNullOrEmpty(text)) return;
                JObject root = JObject.Parse(text);
                if ((int?)root["algo"] == AlgoVersion && root["entries"] is JObject saved) entries = saved;
            }
            catch (Exception ex) { Entry.Warn("cache index unreadable, rebuilding: " + ex.Message); }
        }

        internal static void SaveIndex()
        {
            if (!Dirty || entries == null) return;
            Dirty = false;
            try
            {
                var root = new JObject { ["algo"] = AlgoVersion, ["entries"] = entries };
                store.WriteText(IndexFile, root.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex) { Entry.Warn("cache index not saved: " + ex.Message); }
        }

        /// <summary>缓存里有这张图，且（给了指纹时）指纹一致。</summary>
        internal static bool Has(PaintId id, string signature)
        {
            if (entries == null) LoadIndex();
            if (!(entries[id.Text] is JObject e)) return false;
            return signature == null || (string)e["sig"] == signature;
        }

        /// <summary>从缓存读出贴图（保持可读：游戏按透明度判断点没点中物品，要读像素，见 Painter.Bake）。文件不在或坏了返回 null，并从索引里拿掉。</summary>
        internal static Texture2D Load(PaintId id)
        {
            if (entries == null) LoadIndex();
            if (!(entries[id.Text] is JObject e)) return null;
            try
            {
                byte[] bytes = store.ReadBytes(id.File);
                if (bytes != null && bytes.Length > 0)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes, false) && tex.width == tex.height && tex.width >= 8)
                        return Painter.Finish(tex, (bool?)e["point"] ?? true);
                    UnityEngine.Object.Destroy(tex);
                }
            }
            catch (Exception ex) { Entry.Warn("cache read failed (" + id.Text + "): " + ex.Message); }
            entries.Remove(id.Text);
            Dirty = true;
            return null;
        }

        /// <summary>写入缓存。贴图此时必须还可读。</summary>
        internal static void Save(PaintId id, Texture2D readable, string signature, bool point)
        {
            if (entries == null) LoadIndex();
            try
            {
                store.WriteBytes(id.File, readable.EncodeToPNG());
                bool kept = entries[id.Text] is JObject old && (bool?)old["kept"] == true;
                var e = new JObject { ["sig"] = signature ?? "", ["point"] = point };
                if (kept) e["kept"] = true;
                entries[id.Text] = e;
                Dirty = true;
            }
            catch (Exception ex) { Entry.Warn("cache write failed (" + id.Text + "): " + ex.Message); }
        }

        // ClaudeNote: 0.6.4 自检：缓存文件夹是全部存档共用的，而“本局存档正在用的图”只知道当前这一局。
        //   0.6.3 的清理只保当前这局在用的 → 另一局存档里、来源已经撤掉的物品，它的图会在这一局被清掉（那边读档就缺图）。
        //   所以图一旦被画到物品上，就在索引里记 kept：清理永远不碰（哪一局画的都一样）。只有手动“重建缓存”会清掉它。
        internal static void Keep(PaintId id)
        {
            if (entries == null) LoadIndex();
            if (!(entries[id.Text] is JObject e) || (bool?)e["kept"] == true) return;
            e["kept"] = true;
            Dirty = true;
        }

        internal static void Forget()
        {
            entries = new JObject();
            Dirty = true;
        }

        // ClaudeNote: 0.6.3 缓存的清理。需求：缓存文件夹不能只进不出（玩家撤掉一个两百张图的立绘包，会留下几十 MB 再也用不到的图）。
        //   0.6.1 的做法是“手动重建缓存时把文件夹清空”——只有玩家想起来去点才生效，而且把还在用的也一起删了。
        //   现在：后台完整扫完一遍之后，对照“现在还认得的图”（Painter.TryKnownKeys），索引里认不得的条目拿掉，
        //   磁盘上索引里没有的图删掉。画到过物品上的图（kept，见 Keep）永远留着（来源撤了物品也不会丢图）。
        //   差分、不同占格的成品跟着本体走：本体还认得就留。
        //   只经存放处（ICacheStore）读写，只碰 ItemCache/ 下面的 png。
        internal static void Prune(System.Collections.Generic.HashSet<string> known)
        {
            if (entries == null || known == null) return;
            var gone = new System.Collections.Generic.List<string>();
            var wanted = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in entries)
            {
                PaintId id = PaintId.Parse(kv.Key);
                if (id.Valid && ((kv.Value is JObject e && (bool?)e["kept"] == true) || known.Contains(id.Group + "\n" + id.Key))) wanted.Add(id.File);
                else gone.Add(kv.Key);
            }
            foreach (string key in gone) entries.Remove(key);
            if (gone.Count > 0) Dirty = true;
            int n = 0;
            foreach (string path in store.List() ?? new System.Collections.Generic.List<string>())
                if (path.StartsWith(Folder + "/", StringComparison.Ordinal) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    && !wanted.Contains(path) && store.Delete(path)) n++;
            if (gone.Count > 0 || n > 0) Entry.Info("cache: dropped " + gone.Count + " entr(ies) no longer known, deleted " + n + " file(s)");
        }
    }
}
