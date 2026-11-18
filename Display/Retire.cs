// ClaudeNote: 0.6.2 卸载前清理（Lune 2026-11-16：让玩家停用前到调试面板执行一下，再扒掉模组）。
//   为什么需要：框架在存档里的物品身上留了东西——画上去的图指向本 mod 的贴图、切过状态的静态道具换成了差分图、
//   还有一批 PEF_* 标签。直接删掉 mod，这些物品的图就找不到了（缺图 / 空白）。
//   清理做什么：
//     · 接管来的道具（别的 mod 的立绘道具，如 lewd_meme 少女）→ 还原成接管前的原图与占格
//     · 经框架画图的道具（别的 mod 调 Painter.Paint 的，如 BroodMeme 宿主）→ 还原成第一次画之前的图与占格（物品标签 textureOG）
//     · 切过状态的静态道具 → 换回本体图
//     · 锁定、临时状态、lewd_meme 情绪档这些标签 → 去掉
//     · 排队和柜台上的来客若用着框架的显示键 → 换回游戏立绘表里的键；柜台上那张图换回立绘表里的
//     · 店面渲染、对话框位置 → 恢复原版；本局“用到的图”清单清空
//   清理之后框架进入“已清理”状态：这一局不再自动换图（否则下一次开门 / 来客又会接管回去）。读别的档、或重新读档会恢复正常工作。
//   之后请玩家：存档（过夜）→ 退出游戏 → 在 Mods 菜单停用或删除本 mod。
//   不删的东西：ModData/Lune.TextureExtend 文件夹（缓存图、来源设置、旗标）——不影响存档，想清干净手动删这个文件夹。
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProbablyExtendFramework
{
    public static partial class Painter
    {
        /// <summary>这一局已经做过卸载前清理：框架不再自动换图。读档时复位。</summary>
        public static bool Retired { get; internal set; }

        internal static string Look(GameItem item)
        {
            return (item.useSpriteFromMod ? "1" : "0") + "|" + item.spriteAtlasPath + "|" + item.spritePath + "|" + (item.shape?.width ?? 0) + "|" + (item.shape?.height ?? 0);
        }

        /// <summary>按 Look 记下的样子还原图与占格。记录不完整返回 false。</summary>
        internal static bool Restyle(GameItem item, string look)
        {
            string[] p = (look ?? "").Split('|');
            if (p.Length < 3) return false;
            item.useSpriteFromMod = p[0] == "1";
            item.SetSprite(p[1], p[2]);
            if (p.Length >= 5 && int.TryParse(p[3], out int w) && int.TryParse(p[4], out int h) && w > 0 && h > 0)
                item.SetShape(new GridShapeBuilder(w, h).SetDataFill(w, h, 1).Build());
            return true;
        }

        /// <summary>卸载前清理。返回给玩家看的结果说明。要在店面里（读了档）执行。</summary>
        public static string Retire()
        {
            bool zh = Entry.Chinese;
            if (!PlayerStore.HasInstance) return zh ? "请先读档进到店面里再执行（清理的是存档里的物品）。" : "Load a save first: the clean-up works on the items in the save.";
            int released = 0, restored = 0, kept = 0, statics = 0, tags = 0, visitors = 0;
            Retired = true;   // 先置上：下面还原物品的过程中不会再被规则接管回去
            Notices.Reset();
            foreach (GameItem item in Items.All())
            {
                if (item == null) continue;
                try
                {
                    if (item.IsTag(PaintRules.BaseTag) && PaintRules.SetStaticState(item, "", null, null)) statics++;
                    if (PaintRules.IsAdopted(item)) { PaintRules.Release(item); released++; }
                    else if (item.IsTag(SourceTag))
                    {
                        if (item.IsTag(OgTag) && Restyle(item, item.GetTagReadonly(OgTag)?.GetString()))
                        {
                            foreach (string tag in new[] { SourceTag, InfoTag, OgTag }) item.ModifyTag(tag, s => s.Disable());
                            restored++;
                        }
                        else kept++;
                    }
                    foreach (string tag in new[] { PaintRules.LockTag, PaintRules.AutoTag, Pulses.ItemTag, LewdMemeCompat.MoodTag })
                        if (item.IsTag(tag)) { item.ModifyTag(tag, s => s.Disable()); tags++; }
                }
                catch (Exception ex) { OneBadItem(item, ex); }
            }
            Notices.Reset();
            try { visitors = RetireVisitors(); }
            catch (Exception ex) { Entry.Warn("visitors not reset: " + ex.Message); }
            try { ModHelper.SetModData(ModId, UsedKey, ""); } catch (Exception) { }
            used.Clear();
            DialogPlacement.Shutdown();
            Display.Shutdown();      // 撤掉立绘表里的显示键、放掉显示用的图；HdRender 见 Retired 自己恢复店面渲染
            SourceReader.Release();

            var b = new StringBuilder(zh ? "卸载前清理完成：" : "Clean-up before uninstall done:");
            b.Append(zh ? "\n  还原接管的道具 " : "\n  adopted items released ").Append(released)
             .Append(zh ? " · 还原经框架画图的道具 " : " · painted items restored ").Append(restored)
             .Append(zh ? " · 静态道具换回本体 " : " · static items back to base ").Append(statics)
             .Append(zh ? " · 去掉状态标签 " : " · state tags removed ").Append(tags)
             .Append(zh ? " · 来客换回原键 " : " · visitors reset ").Append(visitors);
            if (kept > 0)
                b.Append(zh ? "\n  有 " + kept + " 件道具是旧版本画的，没有原图记录，还原不了：卸载后它们由所属的 mod 显示占位图（重装本框架可接回）。"
                            : "\n  " + kept + " item(s) painted by an older version have no record of their original image and were left as they are.");
            b.Append(zh ? "\n  接下来：存档（过夜）→ 退出游戏 → 在 Mods 菜单停用或删除本 mod。这一局里框架不会再自动换图。"
                        : "\n  Next: save (sleep) → quit → disable or remove the mod. The framework stays idle for the rest of this session.")
             .Append(zh ? "\n  缓存与设置在 ModData/" + ModId + " 文件夹，不影响存档，想清干净可手动删除。" : "\n  Cache and settings live in ModData/" + ModId + "; delete the folder by hand if you want.");
            string text = b.ToString();
            Entry.Info(text);
            return text;
        }

        // 排队的来客、柜台上的来客：框架的显示键换回游戏立绘表里的键；柜台上的图换回立绘表里的那张
        private static int RetireVisitors()
        {
            int n = 0;
            Dictionary<string, Sprite> dict = SpriteDict.Instance != null ? SpriteDict.Instance.spriteDictionary : null;
            if (dict == null) return 0;
            var clients = new List<StoreClient>();
            if (PlayerStore.Instance.storeClientManager?.clientStack != null) clients.AddRange(PlayerStore.Instance.storeClientManager.clientStack);
            StoreClient current = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
            if (current != null) clients.Add(current);
            foreach (StoreClient c in clients)
            {
                if (c == null || string.IsNullOrEmpty(c.spriteName) || !c.spriteName.StartsWith(Display.KeyPrefix, StringComparison.Ordinal)) continue;
                string portrait = Display.PortraitOfKey(c.spriteName, out _);
                if (portrait == null || !dict.ContainsKey(portrait)) continue;   // 原版没有这张（立绘包新增的）：留着，原版找不到键时会换成随机来客图
                c.spriteName = portrait;
                n++;
            }
            SpriteRenderer image = StoreClientMono.Instance != null ? StoreClientMono.Instance.image : null;
            string key = current?.GetSprite();
            if (image != null && key != null && dict.TryGetValue(key, out Sprite table) && table != null && image.sprite != table) image.sprite = table;
            return n;
        }
    }
}
