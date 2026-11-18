// ClaudeNote: 0.6.0 游戏接触面。框架对游戏打的每一个 Harmony 补丁、读的每一处游戏内部，都在这里登记成一个“接口”：
//   · 补丁逐个安装：某个目标在新版游戏里改名 / 没了，只有它装不上，对应的那一项功能停用并记一条日志，其余照常。
//   · 读数接口登记一个探针：调试面板“接触面自检”里一眼看到哪一处读不到了。
//   · 这张表同时就是“游戏更新后要对一遍的清单”（离线核对工具 tools/surface 读 Claude的TextureExtend接触面清单.md）。
//   不用 [HarmonyPatch] 特性：特性补丁由加载器整批安装，一个目标失效会连累全部。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace TextureExtend
{
    internal static class Ports
    {
        private sealed class Port
        {
            internal string name, feature, status;
            internal Func<MethodBase> target;
            internal HarmonyMethod prefix, postfix;
            internal Func<string> probe;      // 读数接口：返回 null = 正常，否则是问题说明
            internal bool ok;
        }

        private static readonly List<Port> ports = new List<Port>();
        private static Harmony harmony;

        /// <summary>登记一个补丁接口。补丁体由登记方用 new HarmonyMethod(typeof(自己), nameof(方法)) 给出
        /// （类型与方法名都写成字面量：游戏的 mod 扫描把“算出来的类型 / 名字”上的反射判为需要权限）。</summary>
        internal static void Patch(string name, string feature, Func<MethodBase> target, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (ports.Exists(p => p.name == name)) return;
            ports.Add(new Port { name = name, feature = feature, target = target, prefix = prefix, postfix = postfix, status = "not installed" });
        }

        /// <summary>登记一个读数接口（只用于自检，不改变行为）。</summary>
        internal static void Read(string name, string feature, Func<string> probe)
        {
            if (ports.Exists(p => p.name == name)) return;
            ports.Add(new Port { name = name, feature = feature, probe = probe, status = "" });
        }

        internal static void InstallAll()
        {
            if (harmony == null) harmony = new Harmony(Painter.ModId);
            foreach (Port p in ports)
            {
                if (p.target == null || p.ok) continue;
                try
                {
                    MethodBase m = p.target();
                    if (m == null) throw new MissingMethodException("target not found");
                    harmony.Patch(m, p.prefix, p.postfix);
                    p.ok = true;
                    p.status = "ok";
                }
                catch (Exception ex)
                {
                    p.ok = false;
                    p.status = ex.GetType().Name + ": " + ex.Message;
                    Entry.Warn("port " + p.name + " not installed, feature off: " + p.feature + " (" + p.status + ")");
                }
            }
        }

        internal static void UninstallAll()
        {
            try { harmony?.UnpatchAll(Painter.ModId); }
            catch (Exception ex) { Entry.Warn("unpatch failed: " + ex.Message); }
            foreach (Port p in ports) if (p.target != null) { p.ok = false; p.status = "not installed"; }
        }

        /// <summary>这个补丁接口装上了没有（功能据此决定自己开不开）。</summary>
        internal static bool Ok(string name)
        {
            Port p = ports.Find(x => x.name == name);
            return p != null && p.ok;
        }

        /// <summary>接触面自检：每个接口一行。</summary>
        internal static string Report()
        {
            var b = new StringBuilder("game surface:");
            int bad = 0;
            foreach (Port p in ports)
            {
                string state;
                if (p.target != null) state = p.ok ? "ok" : p.status;
                else
                {
                    try { state = p.probe() ?? "ok"; }
                    catch (Exception ex) { state = ex.GetType().Name + ": " + ex.Message; }
                }
                if (state != "ok") bad++;
                b.Append("\n  ").Append(state == "ok" ? "√ " : "× ").Append(p.target != null ? "patch " : "read  ").Append(p.name)
                 .Append("  [").Append(p.feature).Append("]").Append(state == "ok" ? "" : "  → " + state);
            }
            b.Append("\n  ").Append(bad == 0 ? "all " + ports.Count + " ok" : bad + " of " + ports.Count + " need attention");
            b.Append("\n").Append(Health.Summary());
            return b.ToString();
        }
    }
}
