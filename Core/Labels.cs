// ClaudeNote: 状态标签在选图窗口里显示的名字。内置常见的情绪 / 时间类标签，其余原样显示。mod 可用 Labels.Set 追加。
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    public static class Labels
    {
        // ClaudeNote: 0.9.0 情绪的中文名只留一份：读 TagSystem.PortraitTags.Emotions（Lune 内置的那张表），这里只补不属于情绪的几个。
        private static readonly Dictionary<string, string> zh = Build();

        private static Dictionary<string, string> Build()
        {
            var d = new Dictionary<string, string> { ["sick"] = "生病", ["poisoned"] = "中毒", ["wet"] = "湿润", ["night"] = "夜晚", ["day"] = "白天" };
            foreach (string[] e in TagSystem.PortraitTags.Emotions) d[e[0]] = e[1];
            return d;
        }

        /// <summary>显示名；没登记过的原样返回。</summary>
        public static string Of(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return tag;
            return Entry.Chinese && zh.TryGetValue(tag, out string name) ? name : tag;
        }

        /// <summary>给一个状态标签起中文显示名。</summary>
        public static void Set(string tag, string chinese)
        {
            string t = PortraitLibrary.Normal(tag);
            if (t.Length > 0 && !string.IsNullOrEmpty(chinese)) zh[t] = chinese;
        }
    }
}
