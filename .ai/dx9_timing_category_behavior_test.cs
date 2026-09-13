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
    static object Mode(int ms, int speed)
    {
        var type = typeof(TimerCore).Assembly.GetType("Pal98Timer.RuntimeTimingMode", true);
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { ms, speed }, null);
    }
    static void Confirm(TimerCore core, int ms, int speed)
    { typeof(仙剑98柔情DX9).GetField("lastConfirmedTimingMode", PrivateInstance).SetValue(core, Mode(ms, speed)); }
    static string RuntimeError(int expected, int? actual, int speed = 10, int actualSpeed = 10)
    { return (string)Policy.GetMethod("RuntimeError", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { expected, speed, actual.HasValue ? Mode(actual.Value, actualSpeed) : null }); }
    static HObj Record(TimerCore core, int mode)
    {
        Confirm(core, mode, core.CoreName == "PAL98DX9_800_SPEED" ? 8 : 10);
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
        var speed = new Pal98Dx9Fast800Speed(null);
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
        Init(speed);
        Assert(speed.CoreName == "PAL98DX9_800_SPEED" && speed.CheckPoints.All(p => p.Best == TimeSpan.Zero), "Speed must start with an independent empty line");
        speed.CreateBestReferenceIfMissing();
        Assert(File.Exists("bestPAL98DX9_800_SPEED.txt"), "Offline reference creation failed");
        Assert(!(new HObj(File.ReadAllText("bestPAL98DX9_800_SPEED.txt"))).GetValue<bool>("TimingRulesVerified"), "Reference became verified score");
        Assert(!File.Exists("bestPAL98DX9.txt"), "Fast triggered legacy migration");
        Assert(fast.CheckPoints.All(p => p.Best == TimeSpan.Zero), "Fast inherited 1.2s reference times");
        Init(slow);
        Assert(slow.CheckPoints[0].Best == TimeSpan.FromSeconds(367), "Legacy slow best not preserved");
        Assert(slow.CheckPoints.Select(p => p.Name).SequenceEqual(fast.CheckPoints.Select(p => p.Name)), "Route predicates diverged");
        string slowBefore = File.ReadAllText("bestPAL98DX9.txt");
        fast.CheckPoints[0].Current = TimeSpan.FromSeconds(321);
        Confirm(fast, 800, 10);
        fast.SaveBest();
        var fastReopened = new Pal98Dx9Fast800(null); Init(fastReopened);
        Assert(fastReopened.CheckPoints[0].Best == TimeSpan.FromSeconds(321), "Fast best roundtrip failed");
        Assert(File.ReadAllText("bestPAL98DX9.txt") == slowBefore && File.ReadAllText("bestPAL98.txt") == legacy, "Legacy scores changed");

        var slowRecord = Record(slow, 1200);
        var fastRecord = Record(fast, 800);
        fastRecord["Current"] = "00:05:21.000";
        var speedRecord = Record(speed, 800);
        var all = new[] { Tuple.Create<TimerCore, int, HObj>(slow, 1200, slowRecord), Tuple.Create<TimerCore, int, HObj>(fast, 800, fastRecord), Tuple.Create<TimerCore, int, HObj>(speed, 800, speedRecord) };
        foreach (var target in all)
            foreach (var source in all)
                if (target.Item1.CoreName == source.Item1.CoreName) Validate(target.Item1.CoreName, target.Item2, source.Item3);
                else Rejected(() => Validate(target.Item1.CoreName, target.Item2, source.Item3));
        foreach (var entry in all)
        foreach (string invalidField in new[] { "TimingRulesVerified", "ReferenceTimeline", "TimingValidationError" })
        {
            var invalid = new HObj(entry.Item3.ToJson());
            if (invalidField == "TimingValidationError") invalid[invalidField] = "unconfirmed runtime";
            else invalid[invalidField] = invalidField == "ReferenceTimeline";
            File.WriteAllText("1.RPG", "invalid-score-sentinel");
            using (var file = File.Create("invalid.bin"))
                new BinaryFormatter().Serialize(file, new SRPGobj { TimerStr = invalid.ToJson(), RPG = new byte[] { 1 } });
            Rejected(() => typeof(仙剑98柔情DX9).GetMethod("LoadGame", PrivateInstance).Invoke(entry.Item1, new object[] { "invalid.bin", "1.RPG" }));
            Assert(File.ReadAllText("1.RPG") == "invalid-score-sentinel", "Invalid score overwrote save");
        }
        Assert(speedRecord.GetValue<string>("DX9Version").EndsWith("-0.8秒&快走速"), "Speed suffix missing");
        Assert(speedRecord.GetValue<string>("LeaderboardCategory") == "PC NewPatch Classic-0.8s+Speed", "English category missing");
        Assert(slowRecord.GetValue<string>("DX9Version").EndsWith("-1.2秒"), "Traditional suffix missing");
        Validate(slow.CoreName, 1200, slowRecord);
        Validate(fast.CoreName, 800, fastRecord);
        Rejected(() => Validate(slow.CoreName, 1200, fastRecord));
        Rejected(() => Validate(fast.CoreName, 800, slowRecord));
        var legacyRecord = new HObj(slowRecord.ToJson());
        legacyRecord.Remove("TimerCore"); legacyRecord.Remove("TimingModeMs"); legacyRecord.Remove("PaletteFadeModeMs");
        foreach (var field in new[] { "TimingRulesVersion", "MapSpeedTicks", "TimingMapSpeedTicks", "TimingRulesVerified", "LeaderboardCategory" }) legacyRecord.Remove(field);
        Validate(slow.CoreName, 1200, legacyRecord);
        Rejected(() => Validate(fast.CoreName, 800, legacyRecord));
        Rejected(() => Validate(speed.CoreName, 800, legacyRecord));
        var oldFast = new HObj(fastRecord.ToJson());
        foreach (var field in new[] { "TimingRulesVersion", "MapSpeedTicks", "TimingMapSpeedTicks", "TimingRulesVerified" }) oldFast.Remove(field);
        Validate(fast.CoreName, 800, oldFast);
        Rejected(() => Validate(speed.CoreName, 800, oldFast));
        var conflict = new HObj(fastRecord.ToJson()); conflict["PaletteFadeModeMs"] = 1200;
        Rejected(() => Validate(fast.CoreName, 800, conflict));
        conflict = new HObj(slowRecord.ToJson()); conflict["DX9Version"] = "1.63-0.8s";
        Rejected(() => Validate(slow.CoreName, 1200, conflict));

        // Actual restore entry points, including an existing target RPG sentinel.
        slow.SetTimerFromString(legacyRecord.ToJson());
        Assert(slow.GetScoreValidationError().Contains("仅供参考"), "Legacy score became verified");
        fastReopened.SetTimerFromString(fastRecord.ToJson());
        string before = fastReopened.GetRStr();
        Rejected(() => fastReopened.SetTimerFromString(slowRecord.ToJson()));
        Assert(fastReopened.CheckPoints[0].Current == TimeSpan.FromSeconds(321), "Rejected import mutated timer");
        foreach (var target in all)
        foreach (var source in all.Where(item => item.Item1.CoreName != target.Item1.CoreName))
        {
            var timesBefore = target.Item1.CheckPoints.Select(p => p.Current).ToArray();
            long runBefore = target.Item1.ScoreRunSequence;
            Rejected(() => ((仙剑98柔情DX9)target.Item1).SetTimerFromString(source.Item3.ToJson()));
            Assert(target.Item1.CheckPoints.Select(p => p.Current).SequenceEqual(timesBefore) &&
                target.Item1.ScoreRunSequence == runBefore, "Rejected import mutated timer state");
            File.WriteAllText("1.RPG", "game-save-sentinel");
            using (var file = File.Create("cross.bin"))
                new BinaryFormatter().Serialize(file, new SRPGobj { TimerStr = source.Item3.ToJson(), RPG = new byte[] { 9, 8, 7 } });
            Rejected(() => typeof(仙剑98柔情DX9).GetMethod("LoadGame", PrivateInstance).Invoke(target.Item1, new object[] { "cross.bin", "1.RPG" }));
            Assert(File.ReadAllText("1.RPG") == "game-save-sentinel", "Rejected relay overwrote game save");
        }
        var relay = typeof(仙剑98柔情DX9).GetProperty("RelayFileName", PrivateInstance);
        Assert((string)relay.GetValue(slow) == "SRPG.bin", "Legacy relay filename changed");
        Assert((string)relay.GetValue(fast) == "SRPG.PAL98DX9_800.bin", "Fast relay shares filename");
        Assert((string)relay.GetValue(speed) == "SRPG.PAL98DX9_800_SPEED.bin", "Speed relay shares filename");
        foreach (int expected in new[] { 800, 1200 })
        {
            Assert(RuntimeError(expected, expected) == "", "Matching runtime blocked");
            Assert(RuntimeError(expected, expected == 800 ? 1200 : 800).Contains("计时暂停"), "Mismatch not blocked");
            Assert(RuntimeError(expected, null).Contains("待确认"), "Unknown runtime treated as known");
        }
        Assert(RuntimeError(0, null) == "", "Other profile cores changed");
        Assert(RuntimeError(800, 800, 8, 10).Contains("暂停"), "Speed mismatch not blocked");
        Assert(RuntimeError(800, 800, 10, 8).Contains("暂停"), "Speed accepted as normal 800ms");
        Confirm(speed, 800, 8);
        speed.CheckPoints[0].Current = TimeSpan.FromSeconds(299);
        speed.SaveBest();
        var speedReopened = new Pal98Dx9Fast800Speed(null); Init(speedReopened);
        Assert(speedReopened.CheckPoints[0].Best == TimeSpan.FromSeconds(299), "Speed best roundtrip failed");
        Assert(File.ReadAllText("bestPAL98DX9.txt") == slowBefore, "Speed changed traditional best");
        var unknown = new Pal98Dx9Fast800Speed(null); Init(unknown);
        bool blocked = false;
        try { unknown.SaveBest(); } catch (InvalidOperationException) { blocked = true; }
        Assert(blocked, "Public SaveBest bypassed timing validation");
        Confirm(unknown, 800, 8);
        typeof(仙剑98柔情DX9).GetField("PalProcess", PrivateInstance).SetValue(unknown, System.Diagnostics.Process.GetCurrentProcess());
        Assert(unknown.GetScoreValidationError().Contains("待确认"), "Unknown attached process inherited old mode");
        typeof(仙剑98柔情DX9).GetField("PalProcess", PrivateInstance).SetValue(unknown, null);
        Assert(unknown.GetScoreValidationError().Contains("待确认"), "Closing unknown process restored old mode");
        blocked = false;
        try { unknown.ForCloudLiteData(); } catch (InvalidOperationException) { blocked = true; }
        Assert(blocked, "Unknown then exit enabled cloud data");
        Confirm(fastReopened, 800, 8);
        Assert(fastReopened.GetScoreValidationError().Contains("重置"), "Observed class change after imported run was not latched");
        Confirm(fastReopened, 800, 10);
        Assert(fastReopened.GetScoreValidationError().Contains("重置"), "Returning to original class erased invalidation");
        fastReopened.Reset();
        Assert(!fastReopened.GetScoreValidationError().Contains("重置"), "Reset did not clear invalidation");
        Console.WriteLine("PASS: three independent categories, legacy preservation, best save/reopen, all six cross-import/relay paths before writes, speed/unknown gates and reset latch");
        return 0;
    }
}
