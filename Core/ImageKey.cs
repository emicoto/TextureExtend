// ClaudeNote: 0.8.0 贴图身份 = 来源 + 图 ID（Lune 2026-11-18：“立绘图的 ID 是来源 + ID：source:imageID”）。
//   写法：来源:图ID        例 game:femaleLower9、Dudi.HDPortraits:femaleLower9
//         来源:图ID~编号   同一个来源里同名（同 ID）的第 2、3… 张，编号从 1 起自动分
//   只写图 ID（femaleLower9）= “自动”：由框架按来源优先级挑一个来源（PortraitLibrary.Pick）。
//   框架内部（物品标签、缓存文件、成品图）一律用完整写法，所以两个来源的同名图各有各的缓存，谁也不覆盖谁。
using System;

namespace TextureExtend
{
    public static class ImageKey
    {
        public const char Sep = ':', Alt = '~';

        public static string Make(string source, string id, int number = 0)
        {
            return (string.IsNullOrEmpty(source) ? "" : source + Sep) + id + (number > 0 ? Alt + number.ToString() : "");
        }

        /// <summary>拆开。只写了图 ID 时 source 为 null（= 自动）。</summary>
        public static void Parse(string key, out string source, out string id, out int number)
        {
            source = null; number = 0; id = key ?? "";
            int sep = id.IndexOf(Sep);
            if (sep > 0) { source = id.Substring(0, sep); id = id.Substring(sep + 1); }
            int alt = id.LastIndexOf(Alt);
            if (alt > 0 && int.TryParse(id.Substring(alt + 1), out int n) && n > 0) { number = n; id = id.Substring(0, alt); }
        }

        /// <summary>图 ID（去掉来源与编号）。游戏里匹配、规则、标签都按它。</summary>
        public static string IdOf(string key) { Parse(key, out _, out string id, out _); return id; }

        /// <summary>来源；只写了图 ID 时返回 null。</summary>
        public static string SourceOf(string key) { Parse(key, out string source, out _, out _); return source; }

        /// <summary>写明了来源。</summary>
        public static bool Full(string key) { return key != null && key.IndexOf(Sep) > 0; }
    }
}
