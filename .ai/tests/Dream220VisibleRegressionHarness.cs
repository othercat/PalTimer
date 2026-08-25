using Pal98Timer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class Dream220VisibleRegressionHarness
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                throw new InvalidOperationException("Expected repository root and temporary directory arguments.");
            }

            TestProfileValidation(args[0], args[1]);
            TestDerivedProfileValidation(args[0], Path.Combine(args[1], "derived"));
            TestHunqianProfileValidation(args[0], Path.Combine(args[1], "hunqian"));
            Assert(Dream220VisibleProfile.EventObjectBytes == 171808, "Dream event byte count changed");
            Assert(Dream220VisibleProfile.ExpectedSaveLength == 185872, "Dream save length changed");
            Assert(Hunqian167Profile.EventObjectBytes == 170624, "Hunqian event byte count changed");
            Assert(Hunqian167Profile.ExpectedSaveLength == 184688, "Hunqian save length changed");
            TestRoutePredicates();
            Console.WriteLine("PASS dream220-visible public profile and route predicates");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void TestHunqianProfileValidation(string repositoryRoot, string temporaryRoot)
    {
        string fixtureRoot = Path.Combine(repositoryRoot, ".ai", "fixtures", "hunqian167-easy-profile");
        Hunqian167ProfileIdentity easy = Hunqian167Profile.Find("pal98.hunqian167.easy", "1.0.2");
        Hunqian167ProfileIdentity hard = Hunqian167Profile.Find("pal98.hunqian167.hard", "1.0.2");
        Hunqian167ProfileIdentity nonhuman = Hunqian167Profile.Find("pal98.hunqian167.nightmare", "1.0.3");
        Assert(easy != null && easy.Edition == Hunqian167Edition.Easy, "Hunqian Easy identity missing");
        Assert(hard != null && hard.Edition == Hunqian167Edition.Hard, "Hunqian Hard identity missing");
        Assert(nonhuman != null && nonhuman.Edition == Hunqian167Edition.Nonhuman, "Hunqian Nonhuman identity missing");
        Assert(Hunqian167Profile.Find("pal98.hunqian167.nightmare", "1.0.2") == null, "superseded Nonhuman version accepted");

        string profileRoot = Path.Combine(temporaryRoot, "palmod", "Profiles");
        string staging = Path.Combine(profileRoot, easy.ProfileId, easy.ProfileVersion, "manifest");
        Directory.CreateDirectory(staging);
        CopyUtf8WithoutTrailingNewline(Path.Combine(fixtureRoot, "current.json"), Path.Combine(profileRoot, "current.json"));
        CopyUtf8WithoutTrailingNewline(Path.Combine(fixtureRoot, "game-profile.json"), Path.Combine(staging, "game-profile.json"));

        Hunqian167ProfileIdentity selected;
        string error;
        Assert(Hunqian167Profile.TryValidateGameDirectory(temporaryRoot, out selected, out error), "Hunqian Easy profile rejected: " + error);
        Assert(selected != null && selected.Edition == Hunqian167Edition.Easy, "Hunqian Easy edition not returned");

        string pointerPath = Path.Combine(profileRoot, "current.json");
        string pointer = File.ReadAllText(pointerPath, Encoding.UTF8);
        File.WriteAllText(pointerPath, pointer.Replace("pal98.hunqian167.easy", "pal98.hunqian167.nightmare"), new UTF8Encoding(false));
        Assert(!Hunqian167Profile.TryValidateGameDirectory(temporaryRoot, out selected, out error), "cross-edition pointer accepted");
    }

    private static void TestProfileValidation(string repositoryRoot, string temporaryRoot)
    {
        string fixtureRoot = Path.Combine(repositoryRoot, ".ai", "fixtures", "dream220-visible-profile");
        string profileRoot = Path.Combine(temporaryRoot, "palmod", "Profiles");
        string staging = Path.Combine(profileRoot, Dream220VisibleProfile.ProfileId, Dream220VisibleProfile.ProfileVersion);
        string manifest = Path.Combine(staging, "manifest");
        Directory.CreateDirectory(manifest);

        CopyUtf8WithoutTrailingNewline(Path.Combine(fixtureRoot, "current.json"), Path.Combine(profileRoot, "current.json"));
        CopyUtf8WithoutTrailingNewline(Path.Combine(fixtureRoot, "game-profile.json"), Path.Combine(manifest, "game-profile.json"));

        string error;
        Assert(Dream220VisibleProfile.TryValidateGameDirectory(temporaryRoot, out error), "canonical profile rejected: " + error);

        string pointerPath = Path.Combine(profileRoot, "current.json");
        string canonicalPointer = File.ReadAllText(pointerPath, Encoding.UTF8);
        File.WriteAllText(pointerPath, canonicalPointer.Replace("\"1.0.18\"", "\"1.0.17\""), new UTF8Encoding(false));
        Assert(!Dream220VisibleProfile.TryValidateGameDirectory(temporaryRoot, out error), "wrong pointer version accepted");
        File.WriteAllText(pointerPath, canonicalPointer, new UTF8Encoding(false));

        string descriptorPath = Path.Combine(manifest, "game-profile.json");
        string canonicalDescriptor = File.ReadAllText(descriptorPath, Encoding.UTF8);
        File.WriteAllText(descriptorPath, canonicalDescriptor.Replace("梦幻2.2显血版", "Dream220"), new UTF8Encoding(false));
        Assert(!Dream220VisibleProfile.TryValidateGameDirectory(temporaryRoot, out error), "tampered descriptor accepted");
        File.WriteAllText(descriptorPath, canonicalDescriptor, new UTF8Encoding(false));

        File.Delete(pointerPath);
        Assert(!Dream220VisibleProfile.TryValidateGameDirectory(temporaryRoot, out error), "missing pointer accepted");
    }

    private static void TestDerivedProfileValidation(string repositoryRoot, string temporaryRoot)
    {
        string fixtureRoot = Path.Combine(repositoryRoot, ".ai", "fixtures", "dream220-visible-derived-profile");
        Dream220VisibleProfileIdentity identity = Dream220VisibleProfile.Find(
            Dream220VisibleProfile.DerivedProfileId,
            Dream220VisibleProfile.ProfileVersion);
        Assert(identity != null, "supported Dream DrawCard identity missing");
        Assert(identity.DisplayName == Dream220VisibleProfile.DerivedDisplayName,
            "supported Dream DrawCard display name changed");
        Assert(Dream220VisibleProfile.Find(
            "pal98.dream220.compat.drawcard.16E143813DF5",
            Dream220VisibleProfile.ProfileVersion) == null,
            "unknown Dream DrawCard identity accepted");

        string profileRoot = Path.Combine(temporaryRoot, "palmod", "Profiles");
        string staging = Path.Combine(profileRoot, identity.ProfileId, identity.ProfileVersion, "manifest");
        Directory.CreateDirectory(staging);
        CopyUtf8WithoutTrailingNewline(
            Path.Combine(fixtureRoot, "current.json"),
            Path.Combine(profileRoot, "current.json"));
        CopyUtf8WithoutTrailingNewline(
            Path.Combine(fixtureRoot, "game-profile.json"),
            Path.Combine(staging, "game-profile.json"));

        Dream220VisibleProfileIdentity selected;
        string error;
        Assert(Dream220VisibleProfile.TryValidateGameDirectory(
            temporaryRoot, out selected, out error), "supported Dream DrawCard profile rejected: " + error);
        Assert(selected != null && selected.ProfileId == identity.ProfileId,
            "supported Dream DrawCard identity not returned");

        string pointerPath = Path.Combine(profileRoot, "current.json");
        string pointer = File.ReadAllText(pointerPath, Encoding.UTF8);
        File.WriteAllText(
            pointerPath,
            pointer.Replace(identity.ProfileId, "pal98.dream220.compat.drawcard.000000000000"),
            new UTF8Encoding(false));
        Assert(!Dream220VisibleProfile.TryValidateGameDirectory(
            temporaryRoot, out selected, out error), "unapproved Dream DrawCard package accepted");
    }

    private static void TestRoutePredicates()
    {
        Assert(Dream220VisibleRoute.IsPositionAround(5, 1076, 1082, 5, 1076, 1082, 5), "position center rejected");
        Assert(Dream220VisibleRoute.IsPositionAround(5, 1156, 1122, 5, 1076, 1082, 5), "position boundary rejected");
        Assert(!Dream220VisibleRoute.IsPositionAround(6, 1076, 1082, 5, 1076, 1082, 5), "wrong scene accepted");

        List<Dream220VisibleEnemyState> boss = new List<Dream220VisibleEnemyState>
        {
            new Dream220VisibleEnemyState(524, 0)
        };
        Assert(Dream220VisibleRoute.IsBattleDefeated(58, boss, new short[] { 58 }, 524), "single boss defeat rejected");
        Assert(!Dream220VisibleRoute.IsBattleDefeated(59, boss, new short[] { 58 }, 524), "wrong battle scene accepted");

        List<Dream220VisibleEnemyState> dragons = new List<Dream220VisibleEnemyState>
        {
            new Dream220VisibleEnemyState(541, 0),
            new Dream220VisibleEnemyState(542, 0),
            new Dream220VisibleEnemyState(543, 0),
            new Dream220VisibleEnemyState(544, 0),
            new Dream220VisibleEnemyState(545, 0)
        };
        Assert(Dream220VisibleRoute.IsBattleDefeated(213, dragons, new short[] { 213 }, 541, 542, 543, 544, 545), "five-dragon defeat rejected");

        dragons[4] = new Dream220VisibleEnemyState(545, 1);
        Assert(!Dream220VisibleRoute.IsBattleDefeated(213, dragons, new short[] { 213 }, 541, 542, 543, 544, 545), "living dragon accepted");
        dragons.RemoveAt(4);
        Assert(!Dream220VisibleRoute.IsBattleDefeated(213, dragons, new short[] { 213 }, 541, 542, 543, 544, 545), "missing dragon accepted");

        boss.Add(new Dream220VisibleEnemyState(1, 1));
        Assert(!Dream220VisibleRoute.IsBattleDefeated(58, boss, new short[] { 58 }, 524), "living non-boss enemy accepted");

        List<Dream220VisibleEnemyState> hunqianBoss = new List<Dream220VisibleEnemyState>
        {
            new Dream220VisibleEnemyState(546, 1)
        };
        Assert(Hunqian167Route.IsBattlePresent(187, hunqianBoss, new short[] { 187, 226, 231 }, 546), "Hunqian ending battle not detected");
        hunqianBoss[0] = new Dream220VisibleEnemyState(546, 0);
        Assert(Hunqian167Route.IsBattleDefeated(187, hunqianBoss, new short[] { 187, 226, 231 }, 546), "Hunqian ending defeat not detected");
        Assert(Hunqian167Route.IsPositionAround(184, 480, 544, 184, 480, 544, 3), "Hunqian tree entrance rejected");
    }

    private static void CopyUtf8WithoutTrailingNewline(string source, string destination)
    {
        string value = File.ReadAllText(source, Encoding.UTF8).TrimEnd('\r', '\n');
        File.WriteAllText(destination, value, new UTF8Encoding(false));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
