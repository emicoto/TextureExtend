// ClaudeNote: 0.9.10 资源名 → 立绘键的对照表：名字上看不出、只写在那个包自己代码里的对应关系。
//   游戏里有 18 对“贴图名相同、却是两张不同原图”的立绘（下层 / 上层 / 黑市的前三张，各有一张改造人版：femaleBM1 与 augBMFemale1 的贴图都叫 femaleBM_0）。
//   白蛇娘化给每对各画了一张，文件名是“贴图名-编号”（femaleBM_0-1700 / femaleBM_0-1624），哪张给谁写在它的代码里（DuplicateFiles / AugFiles）。
//   它启用时由它自己显示，用不到这张表；它不启用、只当来源时（Lune 2026-11-18 的兼容方案），框架得自己知道哪张给谁——照抄它 2.9.14 的对应关系。
//   白蛇以后自带 TextureExtend 登记表的话，这张表就用不上了。别的包有同类情况：在这里加行。
using System;
using System.Collections.Generic;

namespace ProbablyExtendFramework
{
    internal static class ResourceAliases
    {
        private static readonly Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "femaleBM_0-1700", "femaleBM1" }, { "femaleBM_0-1624", "augBMFemale1" }, { "femaleBM_1-978", "femaleBM2" }, { "femaleBM_1-1657", "augBMFemale2" },
            { "femaleBM_2-1072", "femaleBM3" }, { "femaleBM_2-1026", "augBMFemale3" },
            { "femaleLower_0-1087", "femaleLower1" }, { "femaleLower_0-1715", "augLLFemale1" }, { "femaleLower_1-1071", "femaleLower2" }, { "femaleLower_1-972", "augLLFemale2" },
            { "femaleLower_2-988", "femaleLower3" }, { "femaleLower_2-989", "augLLFemale3" },
            { "femaleUpper_0-963", "femaleUpper1" }, { "femaleUpper_0-1676", "augULFemale1" }, { "femaleUpper_1-997", "femaleUpper2" }, { "femaleUpper_1-1006", "augULFemale2" },
            { "femaleUpper_2-1627", "femaleUpper3" }, { "femaleUpper_2-1020", "augULFemale3" },
            { "maleBM_0-1608", "maleBM1" }, { "maleBM_0-1618", "augBMMale1" }, { "maleBM_1-1643", "maleBM2" }, { "maleBM_1-1007", "augBMMale2" },
            { "maleBM_2-979", "maleBM3" }, { "maleBM_2-1034", "augBMMale3" },
            { "maleLower_0-1682", "maleLower1" }, { "maleLower_0-982", "augLLMale1" }, { "maleLower_1-1645", "maleLower2" }, { "maleLower_1-1664", "augLLMale2" },
            { "maleLower_2-951", "maleLower3" }, { "maleLower_2-1059", "augLLMale3" },
            { "maleUpper_0-1077", "maleUpper1" }, { "maleUpper_0-981", "augULMale1" }, { "maleUpper_1-958", "maleUpper2" }, { "maleUpper_1-1580", "augULMale2" },
            { "maleUpper_2-1707", "maleUpper3" }, { "maleUpper_2-1733", "augULMale3" },
        };

        internal static string Of(string stem) { return stem != null && table.TryGetValue(stem, out string key) ? key : null; }
    }
}
