using HFrame.ENT;
using Pal98Timer;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]

internal static class Dx9TimingCategoryBehaviorTest
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type Policy = typeof(TimerCore).Assembly.GetType("Pal98Timer.Dx9TimingCategory", true);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Init(TimerCore core) { core.GetType().GetMethod("InitCheckPoints", PrivateInstance).Invoke(core, null); }
    static void Rejected(Action action)
    {
        try { action(); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { return; }
        catch (InvalidDataException) { return; }
        throw new Exception("Cross-category input was accepted");
    }
    static void Validate(string core, int mode, HObj json)
    { Policy.GetMethod("ValidateImport", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { core, mode, json }); }
    static string RuntimeError(int expected, int? actual)
    { return (string)Policy.GetMethod("RuntimeError", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { expected, actual }); }
    static HObj Record(TimerCore core, int mode)
    {
        HObj record = new HObj(core.GetRStr());
        record["PaletteFadeModeMs"] = mode;
        return record;
    }
    [STAThread]
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); Console.Error.WriteLine(ex.StackTrace); if (ex.InnerException != null) { Console.Error.WriteLine(ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message); Console.Error.WriteLine(ex.InnerException.StackTrace); } return 1; }
    }
    static int Run(string[] args)
    {
        Directory.SetCurrentDirectory(args[0]);
        foreach (var encoding in new System.Text.Encoding[] { new System.Text.UTF8Encoding(false), new System.Text.UTF8Encoding(true), System.Text.Encoding.Unicode, System.Text.Encoding.BigEndianUnicode, System.Text.Encoding.Default })
        {
            string value = "仙剑98柔情DX9 仙劍98柔情DX9";
            File.WriteAllText("encoding-probe.txt", value, encoding);
            Assert(File.ReadAllText("encoding-probe.txt", TimerCore.GetFileEncodeType("encoding-probe.txt")) == value, "Simplified/traditional text decoding failed");
        }
        var slow = new 仙剑98柔情DX9(null);
        var fast = new Pal98Dx9Fast800(null);
        Assert(slow.CoreName == "PAL98DX9" && fast.CoreName == "PAL98DX9_800", "Stable storage identities");
        Assert(TimerCore.GetAllCores().Contains("Pal98Dx9Fast800"), "Fast selection missing");
        Assert(TimerCore.GetCoreIns("仙剑98柔情DX9", null).CoreName == slow.CoreName, "Legacy LastCore broken");
        Assert(TimerCore.GetCoreIns("Pal98Dx9Fast800", null).CoreName == fast.CoreName, "Fast LastCore broken");
        Assert(TimerCore.GetCoreDisplayName("仙剑98柔情DX9").EndsWith("1.2秒"), "Slow menu label");
        Assert(TimerCore.GetCoreDisplayName("Pal98Dx9Fast800").EndsWith("0.8秒"), "Fast menu label");

        // Only a legacy line exists. Starting fast must not create/copy the slow line.
        File.WriteAllText("bestPAL98.txt", "{\"CheckPoints\":[{\"name\":\"见石碑\",\"des\":\"legacy\",\"time\":\"00:06:07.000\"}]}", System.Text.Encoding.UTF8);
        string legacy = File.ReadAllText("bestPAL98.txt");
        Init(fast);
        Assert(!File.Exists("bestPAL98DX9.txt"), "Fast triggered legacy migration");
        Assert(fast.CheckPoints.All(p => p.Best == TimeSpan.Zero), "Fast inherited 1.2s reference times");
        Init(slow);
        Assert(slow.CheckPoints[0].Best == TimeSpan.FromSeconds(367), "Legacy slow best not preserved");
        Assert(slow.CheckPoints.Select(p => p.Name).SequenceEqual(fast.CheckPoints.Select(p => p.Name)), "Route predicates diverged");
        string slowBefore = File.ReadAllText("bestPAL98DX9.txt");
        fast.CheckPoints[0].Current = TimeSpan.FromSeconds(321);
        fast.SaveBest();
        var fastReopened = new Pal98Dx9Fast800(null); Init(fastReopened);
        Assert(fastReopened.CheckPoints[0].Best == TimeSpan.FromSeconds(321), "Fast best roundtrip failed");
        Assert(File.ReadAllText("bestPAL98DX9.txt") == slowBefore && File.ReadAllText("bestPAL98.txt") == legacy, "Legacy scores changed");

        var slowRecord = Record(slow, 1200);
        var fastRecord = Record(fast, 800);
        Validate(slow.CoreName, 1200, slowRecord);
        Validate(fast.CoreName, 800, fastRecord);
        Rejected(() => Validate(slow.CoreName, 1200, fastRecord));
        Rejected(() => Validate(fast.CoreName, 800, slowRecord));
        var legacyRecord = new HObj(slowRecord.ToJson());
        legacyRecord.Remove("TimerCore"); legacyRecord.Remove("TimingModeMs"); legacyRecord.Remove("PaletteFadeModeMs");
        Validate(slow.CoreName, 1200, legacyRecord);
        Rejected(() => Validate(fast.CoreName, 800, legacyRecord));
        var conflict = new HObj(fastRecord.ToJson()); conflict["PaletteFadeModeMs"] = 1200;
        Rejected(() => Validate(fast.CoreName, 800, conflict));
        conflict = new HObj(slowRecord.ToJson()); conflict["DX9Version"] = "1.63-0.8s";
        Rejected(() => Validate(slow.CoreName, 1200, conflict));

        // Actual restore entry points, including an existing target RPG sentinel.
        slow.SetTimerFromString(legacyRecord.ToJson());
        fastReopened.SetTimerFromString(fastRecord.ToJson());
        string before = fastReopened.GetRStr();
        Rejected(() => fastReopened.SetTimerFromString(slowRecord.ToJson()));
        Assert(fastReopened.CheckPoints[0].Current == TimeSpan.FromSeconds(321), "Rejected import mutated timer");
        foreach (var pair in new[] { Tuple.Create<仙剑98柔情DX9, HObj>(fastReopened, slowRecord), Tuple.Create<仙剑98柔情DX9, HObj>(slow, fastRecord) })
        {
            File.WriteAllText("1.RPG", "game-save-sentinel");
            using (var file = File.Create("cross.bin"))
                new BinaryFormatter().Serialize(file, new SRPGobj { TimerStr = pair.Item2.ToJson(), RPG = new byte[] { 9, 8, 7 } });
            Rejected(() => typeof(仙剑98柔情DX9).GetMethod("LoadGame", PrivateInstance).Invoke(pair.Item1, new object[] { "cross.bin", "1.RPG" }));
            Assert(File.ReadAllText("1.RPG") == "game-save-sentinel", "Rejected relay overwrote game save");
        }
        var relay = typeof(仙剑98柔情DX9).GetProperty("RelayFileName", PrivateInstance);
        Assert((string)relay.GetValue(slow) == "SRPG.bin", "Legacy relay filename changed");
        Assert((string)relay.GetValue(fast) == "SRPG.PAL98DX9_800.bin", "Fast relay shares filename");
        foreach (int expected in new[] { 800, 1200 })
        {
            Assert(RuntimeError(expected, expected) == "", "Matching runtime blocked");
            Assert(RuntimeError(expected, expected == 800 ? 1200 : 800).Contains("计时暂停"), "Mismatch not blocked");
            Assert(RuntimeError(expected, null).Contains("待确认"), "Unknown runtime treated as known");
        }
        Assert(RuntimeError(0, null) == "", "Other profile cores changed");
        Console.WriteLine("PASS: independent core selection, legacy preservation, best save/reopen, relay names, cross-import before writes, 800/1200/unknown gates");
        return 0;
    }
}
