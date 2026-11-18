// ClaudeNote: 0.6.0 立绘大小自适配：量各来源立绘的“内容占比”，给显示方案（Plan）提供每个来源的缩放。算法见 Core/FitMath.cs。
//   怎么量：道具图烘焙时本来就要读像素、找不透明外框（Raster.Bounds），顺手把 外框高 ÷ 画布高 记下来；
//           烘焙没覆盖到的来源（没被选中的那些），后台每个来源补量几张（Baker 分帧做，量够 9 张就停）。
//   结果存 ModData/Lune.TextureExtend/Fit.json（键带来源版本号，包更新后自动重量）。运行时显示只读一个数，没有额外开销。
//   谁的缩放听谁的：pack 行写了 scale 的听作者；别的 mod 自己在显示的（Plan 让出）根本不经过这里；其余自动。
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace TextureExtend
{
    public static class Fit
    {
        private const string File = "Fit.json";
        private static Dictionary<string, Dictionary<string, float>> samples;   // 来源@版本 → 立绘 ID → 内容占比
        private static readonly Dictionary<string, float> scaleCache = new Dictionary<string, float>();
        internal static bool Dirty { get; private set; }
        /// <summary>自动适配开关（配置项 autoFit）。关掉 = 只按画布对齐原版。</summary>
        internal static bool Enabled = true;

        private static Dictionary<string, Dictionary<string, float>> All
        {
            get
            {
                if (samples != null) return samples;
                samples = new Dictionary<string, Dictionary<string, float>>();
                try
                {
                    string text = ModHelper.ReadModDataFileText(Painter.ModId, File);
                    if (!string.IsNullOrEmpty(text)) samples = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, float>>>(text) ?? samples;
                }
                catch (Exception ex) { Entry.Warn("fit data not read: " + ex.Message); }
                return samples;
            }
        }

        private static string KeyOf(string source)
        {
            if (source == PortraitLibrary.GameSource) return source + "@" + Application.version;
            LoadedMod mod = ModLoader.Mods?.Find(m => m?.Manifest != null && m.Manifest.ID == source);
            return source + "@" + (mod?.Manifest?.ModVersion ?? "");
        }

        /// <summary>这个来源现在的显示缩放（相对“画布对齐原版”）。作者在 pack 行写了 scale 的优先。</summary>
        public static float ScaleOf(string source)
        {
            if (string.IsNullOrEmpty(source) || source == PortraitLibrary.GameSource) return 1f;
            float? authored = Packs.AuthoredScale(source);
            if (authored.HasValue) return authored.Value;
            if (!Enabled) return 1f;
            if (scaleCache.TryGetValue(source, out float have)) return have;
            float s = FitMath.Scale(RatioOf(PortraitLibrary.GameSource), RatioOf(source));
            scaleCache[source] = s;
            return s;
        }

        /// <summary>来源的内容占比（中位数）；样本不够返回 0。</summary>
        public static float RatioOf(string source)
        {
            if (!All.TryGetValue(KeyOf(source), out Dictionary<string, float> set) || set.Count < FitMath.MinSamples) return 0f;
            return FitMath.Median(new List<float>(set.Values));
        }

        internal static int SamplesOf(string source) { return All.TryGetValue(KeyOf(source), out Dictionary<string, float> set) ? set.Count : 0; }

        /// <summary>记一张本体图的内容占比。结论变了（来源的缩放变了）就让显示用的图重做。</summary>
        internal static void Record(string source, string portraitId, float ratio)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(portraitId) || ratio <= 0.05f || ratio > 1f) return;
            string key = KeyOf(source);
            if (!All.TryGetValue(key, out Dictionary<string, float> set)) All[key] = set = new Dictionary<string, float>();
            if (set.TryGetValue(portraitId, out float old) && Mathf.Abs(old - ratio) < 0.005f) return;
            set[portraitId] = ratio;
            Dirty = true;
            unsettled = true;
        }

        private static bool unsettled;

        /// <summary>新样本带来的结论一次生效（后台每秒、每遍扫描结束时调）：有来源的缩放变了，才让显示用的图重做。
        /// 0.6.1 自检：之前每记一张样本就可能清一次显示图，第一遍扫描里会连清好几次；现在攒着，一秒最多一次。</summary>
        internal static void Settle()
        {
            if (!unsettled) return;
            unsettled = false;
            var before = new Dictionary<string, float>(scaleCache);
            scaleCache.Clear();
            foreach (KeyValuePair<string, float> kv in before)
                if (Mathf.Abs(ScaleOf(kv.Key) - kv.Value) > 0.005f) { Display.Clear(); return; }
        }

        /// <summary>后台补量：还没量够的（来源, 立绘）。每个来源最多补到 WantSamples 张，按立绘 ID 均匀抽。</summary>
        internal static List<KeyValuePair<string, string>> Pending()
        {
            var bySource = new Dictionary<string, List<string>>();
            foreach (Portrait p in PortraitLibrary.All)
                foreach (Candidate c in p.candidates)
                {
                    if (!Sources.Active(c.source)) continue;   // 0.8.0 没激活的候选素材不上来客，不为它读盘量身材
                    if (!bySource.TryGetValue(c.source, out List<string> ids)) bySource[c.source] = ids = new List<string>();
                    ids.Add(p.Field);
                }
            var todo = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, List<string>> kv in bySource)
            {
                All.TryGetValue(KeyOf(kv.Key), out Dictionary<string, float> set);
                int have = set?.Count ?? 0, need = Mathf.Min(FitMath.WantSamples, kv.Value.Count) - have;
                if (need <= 0) continue;
                kv.Value.Sort(string.CompareOrdinal);
                int step = Mathf.Max(1, kv.Value.Count / FitMath.WantSamples);
                for (int i = 0; i < kv.Value.Count && need > 0; i += step)
                {
                    if ((set != null && set.ContainsKey(kv.Value[i])) || unreadable.Contains(kv.Key + "|" + kv.Value[i])) continue;
                    todo.Add(new KeyValuePair<string, string>(kv.Key, kv.Value[i]));
                    need--;
                }
            }
            return todo;
        }

        /// <summary>量一张（读像素找外框）。返回是否量到。</summary>
        internal static bool Measure(string source, string portraitId)
        {
            Portrait p = PortraitLibrary.Get(portraitId);
            Candidate c = p?.CandidateOf(source);
            if (c == null) return false;
            Sprite s = c.provider != null ? c.provider() : Originals.Get(portraitId);
            if (s == null || !SourceReader.Read(s, out Color32[] pixels, out int w, out int h, out RectInt rect) || !Record(source, portraitId, pixels, w, rect))
            {
                unreadable.Add(source + "|" + portraitId);   // 读不出来的本次运行不再试（否则每遍扫描都白读一次文件）
                return false;
            }
            return true;
        }

        /// <summary>烘焙时顺手记：已经读好的像素直接量。</summary>
        internal static bool Record(string source, string portraitId, Color32[] pixels, int texW, RectInt rect)
        {
            if (!Raster.Bounds(pixels, texW, rect, out RectInt box) || rect.height <= 0) return false;
            Record(source, portraitId, (float)box.height / rect.height);
            return true;
        }

        private static readonly HashSet<string> unreadable = new HashSet<string>();

        internal static void Save()
        {
            if (!Dirty || samples == null) return;
            Dirty = false;
            // 来源换了版本（或那个 mod 不在了）的旧样本不再有用：存盘前清掉，文件不会越攒越大
            //   只按“来源@版本”判断，不看立绘库（不在店面时立绘库是空的，不能据此删）
            foreach (string key in new List<string>(samples.Keys))
            {
                int at = key.LastIndexOf('@');
                if (at <= 0 || KeyOf(key.Substring(0, at)) != key) samples.Remove(key);
            }
            try { ModHelper.WriteModDataFileText(Painter.ModId, File, JsonConvert.SerializeObject(samples)); }
            catch (Exception ex) { Entry.Warn("fit data not saved: " + ex.Message); }
        }

        /// <summary>全部重量（调试面板“重建缓存”时一并做）。</summary>
        internal static void Forget()
        {
            samples = new Dictionary<string, Dictionary<string, float>>();
            scaleCache.Clear();
            unreadable.Clear();
            unsettled = false;
            Dirty = true;
            Display.Clear();
        }

        internal static string Report()
        {
            var b = new System.Text.StringBuilder("fit: reference (game) ratio ");
            float reference = RatioOf(PortraitLibrary.GameSource);
            b.Append(reference > 0f ? reference.ToString("0.00") : FitMath.FallbackReference.ToString("0.00") + " (fallback, not measured yet)");
            foreach (string source in Sources.All())
            {
                if (source == PortraitLibrary.GameSource) continue;
                float r = RatioOf(source);
                float? authored = Packs.AuthoredScale(source);
                b.Append("\n  ").Append(Sources.Label(source)).Append(" [").Append(source).Append("]: ratio ")
                 .Append(r > 0f ? r.ToString("0.00") : "? (" + SamplesOf(source) + " sample(s))")
                 .Append(" → scale ").Append(ScaleOf(source).ToString("0.00")).Append(authored.HasValue ? " (set by the pack)" : !Enabled ? " (auto fit off)" : "");
            }
            return b.ToString();
        }
    }
}
