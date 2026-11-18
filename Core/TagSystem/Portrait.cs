// ClaudeNote: 0.7.1 TagSystem.PortraitTags——立绘的内置基础标签库 + 字段名解析（Lune 2026-11-17 起草的结构；原 Core/PortraitTags.cs 搬到这里）。
//   立绘字段 → 标签。纯文字解析，不碰 Unity，定义全在顶部的表里，改标签只改表。
//   依据：原版 SpriteDict 的 221 个字段（2026-11-15 的 sprite_dict_dump.json）。
//   泛用来客：male|female + 类别 + 编号（femaleLower9）；改造体：aug + LL|BM|UL + Male|Female + 编号；通缉犯：wanted + 编号；
//   其余（商人、退休角色、具名角色、别的 mod 追加的非编号字段）一律算 named。
//   标签全部小写。派系：scav upper lower rev bm sec；职业（上下层通用，不带派系）：eng med farmer chef merc sci miner。
//   secOff / secCorpo 并入 sec，原细分留作副标签 secoff / seccorpo（Lune 2026-11-15）。
using System;
using System.Collections.Generic;

namespace TextureExtend.TagSystem
{
    public static class PortraitTags
    {
        public const string Male = "male", Female = "female", Aug = "aug", Wanted = "wanted", Named = "named", GenericTag = "generic";

        // ── 内置基础标签库：每行 = { 字段里的词, 标签, 副标签… } ──

        /// <summary>性别词。Female 排在 Male 前面：按词尾比对时 “…Female” 也以 “Male” 结尾。</summary>
        public static readonly List<string[]> Generic = new List<string[]>
        {
            new[] { "Female", Female },
            new[] { "Male", Male },
        };

        /// <summary>派系 / 职业词 → 标签。长的排前面（SecOff 要先于 Sec 匹配）。mod 可用 AddFaction 追加（按长度重排）。</summary>
        public static readonly List<string[]> Classes = new List<string[]>
        {
            new[] { "SecCorpo", "sec", "seccorpo" },
            new[] { "SecOff", "sec", "secoff" },
            new[] { "Farmer", "farmer" },
            new[] { "Miner", "miner" },   // ClaudeNote: 0.6.0 原版矿工不分性别（miner1…），立绘包里有 femaleMiner / maleMiner 池
            new[] { "Upper", "upper" },
            new[] { "Lower", "lower" },
            new[] { "Scav", "scav" },
            new[] { "Chef", "chef" },
            new[] { "Merc", "merc" },
            new[] { "Rev", "rev" },
            new[] { "Sec", "sec" },
            new[] { "Eng", "eng" },
            new[] { "Med", "med" },
            new[] { "Sci", "sci" },
            new[] { "BM", "bm" },
        };

        /// <summary>情绪标签（Lune 2026-11-17 内置）：每行 = { 标签, 中文显示名 }。归默认标签组“情绪”；显示名由 Labels 读这张表。</summary>
        public static readonly List<string[]> Emotions = new List<string[]>
        {
           new[] { "happy", "开心" },
           new[] { "calm", "平静" },
           new[] { "unhappy", "不开心"},
           new[] {"angry", "愤怒"},
           new[] { "delighted", "欢喜" },
           new[] { "hostile", "敌对" },
           new[] { "pleased", "好货" },
           new[] { "thinking", "思考" },
           new[] { "farewell", "告别" },
           new[] { "love", "心动" },
        };

        // ── 默认标签组（0.8.1，Lune 2026-11-17）：派系、性别互为最顶层；体型是次级顶层 ──
        //   每行 = { 组 ID, 层级 }。组只是“这些标签归哪一类”，不是贴在立绘上的标签。
        //   派系：Classes 表里的全部（派系 + 上下层通用的职业——它们在字段名里占同一个位置）。性别：Generic 表。体型：改造体 aug。
        //   情绪：Emotions 表（第 3 层）。wanted / named / generic 不归任何组。
        public const string FactionGroup = "faction", GenderGroup = "gender", BodyGroup = "body", EmotionGroup = "emotion";
        public static readonly List<string[]> Groups = new List<string[]>
        {
            new[] { FactionGroup, "1" },
            new[] { GenderGroup, "1" },
            new[] { BodyGroup, "2" },
            new[] { EmotionGroup, "3"},
        };

        /// <summary>内置标签归哪个默认组；不归任何组返回 null。</summary>
        public static string GroupOf(string tag)
        {
            if (tag == Aug) return BodyGroup;
            foreach (string[] g in Generic) if (g[1] == tag) return GenderGroup;
            foreach (string[] c in Classes) for (int i = 1; i < c.Length; i++) if (c[i] == tag) return FactionGroup;
            foreach (string[] e in Emotions) if (e[0] == tag) return EmotionGroup;   // ClaudeNote: 0.9.0 自检：比的是标签（第 0 列），不是中文显示名（第 1 列）
            return null;
        }

        /// <summary>内置基础标签库里的全部标签（性别、派系 / 职业、改造体、通缉、具名…），去重。工坊的筛选与条件下拉用。</summary>
        public static List<string> Library()
        {
            var list = new List<string>();
            void Add(string t) { if (!list.Contains(t)) list.Add(t); }
            foreach (string[] g in Generic) Add(g[1]);
            foreach (string[] c in Classes) for (int i = 1; i < c.Length; i++) Add(c[i]);
            foreach (string[] a in AugAreas) Add(a[1]);
            foreach (string t in new[] { Aug, Wanted, Named, GenericTag }) Add(t);
            foreach (string[] e in Emotions) Add(e[0]);
            return list;
        }

        /// <summary>登记一个新派系词：word=Mutant → 字段 femaleMutant3 识别为 mutant 派系。已有的词不覆盖。</summary>
        public static bool AddFaction(string word, string tag)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(tag) || !char.IsLetter(word[0])) return false;
            foreach (string[] c in Classes) if (string.Equals(c[0], word, StringComparison.OrdinalIgnoreCase)) return false;
            string w = char.ToUpperInvariant(word[0]) + word.Substring(1);
            Classes.Add(new[] { w, tag.ToLowerInvariant() });
            Classes.Sort((a, b) => b[0].Length.CompareTo(a[0].Length));
            return true;
        }

        // ── 字段结构识别（Lune 2026-11-16）：性别 + 派系或职业 + 编号 + 任意个 _后缀；改造体 aug + 区域 + 性别 + 编号 + 后缀 ──

        /// <summary>键里有没有一段按原版字段结构命名的立绘。有就给出规范字段（femaleLower15）与差分（后缀串，小写；没有为空）。
        /// 这一段必须在键的开头或前面是非字母数字（_ . / -…），且一直延续到键的末尾。</summary>
        public static bool TryStructure(string key, out string field, out string variant)
        {
            field = null; variant = "";
            if (string.IsNullOrEmpty(key)) return false;
            for (int start = 0; start < key.Length; start++)
            {
                if (start > 0 && char.IsLetterOrDigit(key[start - 1])) continue;
                if (ParseAt(key, start, out field, out variant)) return true;
            }
            return false;
        }

        private static bool ParseAt(string key, int start, out string field, out string variant)
        {
            field = null; variant = "";
            int i = start;
            string head = null;
            foreach (string[] g in Generic) if (Take(key, ref i, g[0])) { head = g[1]; break; }
            if (head == null) return Take(key, ref i, "aug") && ParseAug(key, i, out field, out variant);
            string word = null;
            foreach (string[] c in Classes) if (Take(key, ref i, c[0])) { word = c[0]; break; }
            if (word == null) return false;
            int numStart = i;
            while (i < key.Length && char.IsDigit(key[i])) i++;
            if (i == numStart || i - numStart > 6) return false;
            if (!Suffix(key, i, out variant)) return false;
            field = head + word + key.Substring(numStart, i - numStart);
            return true;
        }

        private static bool ParseAug(string key, int i, out string field, out string variant)
        {
            field = null; variant = "";
            string area = null;
            foreach (string[] a in AugAreas) if (Take(key, ref i, a[0])) { area = a[0]; break; }
            if (area == null) return false;
            string gender = null;
            foreach (string[] g in Generic) if (Take(key, ref i, g[0])) { gender = g[0]; break; }
            if (gender == null) return false;
            int numStart = i;
            while (i < key.Length && char.IsDigit(key[i])) i++;
            if (i == numStart || i - numStart > 6) return false;
            if (!Suffix(key, i, out variant)) return false;
            field = "aug" + area + gender + key.Substring(numStart, i - numStart);
            return true;
        }

        // 不分大小写地吃掉一个词
        private static bool Take(string key, ref int i, string word)
        {
            if (i + word.Length > key.Length || string.Compare(key, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            i += word.Length;
            return true;
        }

        // 余下必须是空，或若干段 _字母数字；差分名 = 去掉开头下划线后的整串（小写）
        private static bool Suffix(string key, int i, out string variant)
        {
            variant = "";
            if (i == key.Length) return true;
            if (key[i] != '_') return false;
            for (int j = i + 1; j < key.Length; j++)
                if (!char.IsLetterOrDigit(key[j]) && key[j] != '_') return false;
            variant = key.Substring(i + 1).Trim('_').ToLowerInvariant();
            return variant.Length > 0;
        }

        /// <summary>改造体的区域缩写 → 派系。</summary>
        public static readonly string[][] AugAreas = { new[] { "LL", "lower" }, new[] { "BM", "bm" }, new[] { "UL", "upper" } };

        /// <summary>具名字段里能看出归属的词（按下划线拆开后逐段比对）。</summary>
        public static readonly string[][] NamedHints =
        {
            new[] { "sec", "sec" }, new[] { "rev", "rev" }, new[] { "scav", "scav" }, new[] { "miner", "miner" },
        };

        /// <summary>名字开头是不是一个立绘池（性别 + 派系或职业，如 femaleSec、augULFemale）。是就返回池名，否则 null。
        /// 例：“femaleSec-保安女”“femaleMiner_psrl_f01” → femaleSec / femaleMiner。池名 + 编号就是这个池里的立绘 ID。</summary>
        public static string PoolOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int n = 0;
            while (n < name.Length && char.IsLetter(name[n]) && name[n] < 128) n++;
            if (n == 0 || (n < name.Length && char.IsDigit(name[n]))) return null;   // 后面紧跟数字的是具体立绘键，不是池
            string word = name.Substring(0, n);
            return TryStructure(word + "1", out string field, out string variant) && field == word + "1" && variant.Length == 0 ? word : null;
        }

        /// <summary>这个字段的全部标签。认不出规律的字段只有 named。</summary>
        public static string[] Of(string field)
        {
            var tags = new List<string>(4);
            if (string.IsNullOrEmpty(field)) return tags.ToArray();
            string body = TrimNumber(field, out bool numbered);

            if (numbered && TryGeneric(body, tags)) { tags.Add(GenericTag); return tags.ToArray(); }
            if (numbered && TryAug(body, tags)) { tags.Add(Aug); return tags.ToArray(); }
            if (numbered && body == "wanted") { tags.Add(Wanted); return tags.ToArray(); }

            tags.Add(Named);
            foreach (string part in body.Split('_'))
                foreach (string[] hint in NamedHints)
                    if (string.Equals(part, hint[0], StringComparison.OrdinalIgnoreCase) && !tags.Contains(hint[1])) tags.Add(hint[1]);
            return tags.ToArray();
        }

        /// <summary>字段是不是“前缀 + 编号”这一路的（femaleLower9 之于 femaleLower）。</summary>
        public static bool IsNumberedUnder(string field, string prefix)
        {
            if (field == null || prefix == null || field.Length <= prefix.Length || !field.StartsWith(prefix, StringComparison.Ordinal)) return false;
            for (int i = prefix.Length; i < field.Length; i++) if (!char.IsDigit(field[i])) return false;
            return true;
        }

        /// <summary>字段末尾的编号；没有编号返回 0。</summary>
        public static int NumberOf(string field)
        {
            if (string.IsNullOrEmpty(field)) return 0;
            int start = field.Length;
            while (start > 0 && char.IsDigit(field[start - 1])) start--;
            return start < field.Length && field.Length - start <= 6 ? int.Parse(field.Substring(start)) : 0;
        }

        private static string TrimNumber(string field, out bool numbered)
        {
            int end = field.Length;
            while (end > 0 && char.IsDigit(field[end - 1])) end--;
            numbered = end < field.Length && end > 0;
            return field.Substring(0, end);
        }

        private static bool TryGeneric(string body, List<string> tags)
        {
            string gender = null, rest = null;
            foreach (string[] g in Generic)
                if (body.StartsWith(g[0], StringComparison.OrdinalIgnoreCase)) { gender = g[1]; rest = body.Substring(g[0].Length); break; }
            if (gender == null) return false;
            foreach (string[] c in Classes)
            {
                if (rest != c[0]) continue;
                tags.Add(gender);
                for (int i = 1; i < c.Length; i++) tags.Add(c[i]);
                return true;
            }
            return false;
        }

        private static bool TryAug(string body, List<string> tags)
        {
            if (!body.StartsWith("aug", StringComparison.Ordinal)) return false;
            string rest = body.Substring(3), gender = null;
            foreach (string[] g in Generic)
                if (rest.EndsWith(g[0], StringComparison.Ordinal)) { gender = g[1]; rest = rest.Substring(0, rest.Length - g[0].Length); break; }
            if (gender == null) return false;
            foreach (string[] a in AugAreas)
            {
                if (rest != a[0]) continue;
                tags.Add(gender);
                tags.Add(a[1]);
                return true;
            }
            return false;
        }
    }
}
