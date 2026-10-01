using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using Pal98Timer;
using TimerPluginBase;

namespace PAL98.FujiaCaishen
{
    public sealed class Main : TimerPlugin
    {
        public string Result;
        public int Reads;
        public override void OnLoad() { }
        public override void OnUnload() { }
        public override EPluginPosition GetPosition() { return EPluginPosition.BR; }
        public override string GetResult() { ++Reads; return Result; }
        public override void Flush(IntPtr handle, int pid, int base32, long base64)
        { throw new Exception("Display must never sample game memory."); }
    }
}

internal sealed class OtherDisplayPlugin : TimerPlugin
{
    public string Result;
    public override void OnLoad() { }
    public override void OnUnload() { }
    public override EPluginPosition GetPosition() { return EPluginPosition.BR; }
    public override string GetResult() { return Result; }
}

internal static class PluginDisplayBehavior
{
    private static readonly FieldInfo Plugins = typeof(TimerCore).GetField("Plugins", BindingFlags.NonPublic | BindingFlags.Instance);
    private static int checks;

    private static void Equal(string expected, string actual, string description)
    {
        if (expected != actual) throw new Exception(description + ": expected [" + expected + "] got [" + actual + "]");
        ++checks;
    }

    private static TimerCore Core(string className, TimerPlugin plugin)
    {
        var core = (TimerCore)FormatterServices.GetUninitializedObject(typeof(TimerCore).Assembly.GetType("Pal98Timer." + className, true));
        var plugins = new Dictionary<TimerPlugin.EPluginPosition, TimerPlugin>();
        if (plugin != null) plugins.Add(TimerPlugin.EPluginPosition.BR, plugin);
        Plugins.SetValue(core, plugins);
        return core;
    }

    private static string Hash(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    private static void Set(TimerPlugin plugin, string field, object value)
    {
        FieldInfo member = plugin.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (member == null) throw new Exception("Signed package fixture field missing: " + field);
        member.SetValue(plugin, value);
    }

    private static int Main(string[] args)
    {
        string[][] vectors = {
            new[] { "神器:未收集 钱12345 道具68　　", "钱12345 道具68　　" },
            new[] { "神器:已收集 钱2147483647 道具251　　", "钱2147483647 道具251　　" },
            new[] { "神器：已收集 錢1000000 道具68", "錢1000000 道具68" },
            new[] { "四大神器：未收集 钱：0  道具：0", "钱：0  道具：0" },
            new[] { "四大神器:已收集 錢：2147483647  道具：251", "錢：2147483647  道具：251" },
            new[] { "　神器： 未收集\t钱1 道具2", "钱1 道具2" },
            new[] { "钱123 道具4　　", "钱123 道具4　　" },
            new[] { "錢123 道具4", "錢123 道具4" },
            new[] { "", "" }, new string[] { null, null },
            new[] { "神器:未知 钱5 道具6", "神器:未知 钱5 道具6" },
            new[] { "神器:已收集", "神器:已收集" },
            new[] { "神器:未收集 状态", "神器:未收集 状态" },
            new[] { "神器已收集 钱5 道具6", "神器已收集 钱5 道具6" }
        };
        foreach (string name in new[] { "仙剑98柔情", "仙剑98柔情DX9", "仙剑98柔情不欢乐模式", "Pal98Dx9Automatic" })
        {
            var fixture = new PAL98.FujiaCaishen.Main();
            TimerCore core = Core(name, fixture);
            foreach (var vector in vectors)
            {
                fixture.Result = vector[0];
                int previousReads = fixture.Reads;
                Equal(vector[1], core.GetPluginResult(TimerPlugin.EPluginPosition.BR), name + " common display entry");
                if (fixture.Reads != previousReads + 1) throw new Exception("Display sampled a plugin more than once.");
                ++checks;
                Equal(vector[0], fixture.Result, "Cached plugin data is unchanged");
            }
            Equal(null, core.GetPluginResult(TimerPlugin.EPluginPosition.BL), "Absent position remains absent");
            var other = new OtherDisplayPlugin { Result = "神器:未收集 钱123 道具4" };
            Equal(other.Result, Core(name, other).GetPluginResult(TimerPlugin.EPluginPosition.BR), "Other plugin remains untouched");
            Equal(null, Core(name, null).GetPluginResult(TimerPlugin.EPluginPosition.BR), "Disabled/absent plugin remains absent");
        }

        string originalHash = Hash(args[0]);
        var package = new TimerPluginPackageInfo(args[0]);
        if (!package.IsOK || !package.Enable || package.Version != TimerPlugin.Version.ToString() || package.ClassName != "PAL98.FujiaCaishen")
            throw new Exception("Expected the unchanged, enabled, signed Fujia package.");
        var signed = (TimerPlugin)Activator.CreateInstance(Assembly.Load(package.Data).GetType(package.ClassName + ".Main", true));
        FieldInfo artifacts = signed.GetType().GetField("hasYuhangArtifacts", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (int money in new[] { 0, 999999, 1000000, int.MaxValue })
        foreach (bool collected in new[] { false, true })
        {
            Set(signed, "money", money);
            Set(signed, "ic", 251);
            if (artifacts != null) artifacts.SetValue(signed, collected);
            string raw = signed.GetResult();
            int moneyIndex = raw.IndexOf('钱');
            if (moneyIndex < 0) moneyIndex = raw.IndexOf('錢');
            if (moneyIndex < 0 || !raw.Contains(money.ToString()) || !raw.Contains("251"))
                throw new Exception("Unexpected signed plugin output: " + raw);
            string actual = Core("仙剑98柔情DX9", signed).GetPluginResult(TimerPlugin.EPluginPosition.BR);
            Equal(raw.Substring(moneyIndex), actual, "Real signed package display");
            Equal(raw, signed.GetResult(), "Real package state remains unchanged");
            if (actual.Contains("神器")) throw new Exception("Artifact state remains in real display.");
            ++checks;
        }
        Equal(originalHash, Hash(args[0]), "TPG bytes are unchanged");
        Console.WriteLine("PASS " + checks + " plugin display checks; PAL98/DX9/Unhappy/Automatic common entry, simplified/traditional prefixes, collected/uncollected, no prefix, malformed/other outputs, large cash, one cached result read, no Flush; real signed TPG unchanged.");
        Console.WriteLine("TPG SHA-256=" + originalHash + "; version=" + package.Version + "; signature valid=" + package.IsOK + "; artifact field=" + (artifacts != null));
        return 0;
    }
}
