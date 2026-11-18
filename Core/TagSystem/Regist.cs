// ClaudeNote: 0.7.1 TagSystem.Regist——给外部 mod 的注册入口（Lune 2026-11-17 起草的结构）。只转发，不存状态：
//   标签库在 PortraitTags / ItemTags，群组与层级在 Layer。表格（TextureExtend.csv）的 tag / faction 行和这里的方法一一对应。
//   用法（mod 里 using TextureExtend.TagSystem;）：
//     Regist.Tag(modId, PaintTarget.Portrait, "mutant_cat").Group("mutant").Under("lower").With("female").Without("aug");
//     Regist.Tag(modId, PaintTarget.StaticItem, "*").Group("streetfood").With("food");      // id 写 * = 符合条件的全部进组
//     Regist.Faction("Mutant", "mutant");                     // 立绘字段 femaleMutant3 识别为 mutant 派系
//     Regist.Library(PaintTarget.StaticItem, "CURSED");       // 往道具扩展标签库里加一个基础标签
//     Regist.Pool(PaintTarget.Portrait, "lower", "female");   // 取池子
//     Regist.Remove(modId);                                   // OnDisable 里撤掉自己登记的群组
//   注意命名空间是 TextureExtend.TagSystem：游戏自己有一个全局的 TagSystem 类，顶层命名空间不能同名。
using System.Collections.Generic;

namespace TextureExtend.TagSystem
{
    public static class Regist
    {
        /// <summary>一条群组登记：目标 id 进哪个组、组挂在谁下面、进组的条件。链式补全（见 TagDef）。id 写 "*" = 符合条件的全部；留空 = 只声明上下级。</summary>
        public static TagDef Tag(string modId, PaintTarget target, string id) { return Layer.Add(modId, target, id); }

        /// <summary>新派系词：word=Mutant → 立绘字段 femaleMutant3 识别为派系 tag。已有的词不覆盖，返回 false。</summary>
        public static bool Faction(string word, string tag)
        {
            if (!PortraitTags.AddFaction(word, tag)) return false;
            PortraitLibrary.Invalidate();
            return true;
        }

        /// <summary>往内置基础标签库里加标签（只是让它出现在库里、可以被选用；贴到具体目标上用 Tag）。
        /// 道具：加进 ItemTags 的扩展标签行。立绘的基础标签由字段名决定，新词请用 Faction。</summary>
        public static void Library(PaintTarget target, params string[] tags)
        {
            if (target == PaintTarget.StaticItem) ItemTags.Add(ItemTags.Extend, tags);
        }

        /// <summary>取池子：带齐这些标签的立绘 ID / 道具 ID（道具只在登记过贴图扩展的里找）。</summary>
        public static List<string> Pool(PaintTarget target, params string[] tags) { return Layer.Pool(target, tags); }

        /// <summary>撤掉这个 mod 登记的全部群组。Painter.UnregisterAll 里已经包含。</summary>
        public static void Remove(string modId) { Layer.Remove(modId); }
    }
}
