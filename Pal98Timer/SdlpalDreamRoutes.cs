using System;
namespace Pal98Timer
{
    internal static class SdlpalDreamRoutes
    {
        // Original 梦幻22.cs route, audited against all three SSS/DATA variants.
        // Coordinates are viewport coordinates; battle teams replace XP/cash heuristics.
        private static SdlpalSplitRule Position(string id, string name, int scene, int x, int y, int radius) =>
            new SdlpalSplitRule { Id=id, Name=name, Kind="position", Scene=scene, X=x, Y=y, Radius=radius };
        private static SdlpalSplitRule Battle(string id, string name, int team, int enemy, params int[] scenes) =>
            new SdlpalSplitRule { Id=id, Name=name, Kind="battle", Team=team, Enemy=enemy, Scenes=scenes };
        internal static SdlpalSplitRule[] ForProfile(string profile)
        {
            if (SdlpalClassicRoutes.Supports(profile)) return SdlpalClassicRoutes.ForProfile(profile);
            if (SdlpalExpandedRoutes.Supports(profile)) return SdlpalExpandedRoutes.ForProfile(profile);
            if (profile != "dream22-puppet100-v1" && profile != "dream222-normal-v1" && profile != "dream222-hard-v1" && profile != "dreamspace39x-hard-v1")
                throw new ArgumentException("Unknown Dream profile");
            var caiyi = Battle("dream-caiyi", "过彩依", 39, 468, 108); caiyi.Kind = "battle-end";
            return new[] {
                Position("dream-ship", "上船", 5, 1076, 1082, 5),
                Position("dream-snake-cave", "出蛇洞", 48, 304, 1560, 3),
                Battle("dream-zhixiu", "过智修", 35, 524, 58),
                Battle("dream-ghost-general", "过鬼将军", 26, 472, 65, 66),
                Battle("dream-red-ghost", "过赤鬼王", 27, 473, 59),
                Position("dream-yangzhou-in", "进扬州", 82, 288, 1072, 5),
                Position("dream-yangzhou-out", "出扬州", 105, 64, 960, 3),
                Battle("dream-ghost-mother", "过鬼母", 36, 500, 103), caiyi,
                Battle("dream-sword-elder", "过剑老头", 163, 494, 147),
                Battle("dream-mingwang", "过明王", 188, 519, 145, 153),
                new SdlpalSplitRule { Id="dream-tower", Name="拆塔", Kind="music", Music=23 },
                Battle("dream-phoenix", "过凤凰", 203, 464, 185),
                Battle("dream-wood", "过木道人", 221, 474, 181),
                Battle("dream-fire", "过火麒麟", 224, 463, 200),
                new SdlpalSplitRule { Id="dream-past", Name="过十年前", Kind="item", Item=0x109 },
                Battle("dream-seven-poisons", "过七毒", 189, 533, 224),
                Battle("dream-blood-dragon", "过血角青龙", 386, 572, 295),
                Battle("dream-five-dragons", "过五神龙", 387, 541, 213),
                Battle("dream-baiyue-bridge", "过桥头拜月", 313, 546, 281),
                new SdlpalSplitRule { Id="dream-ending", Name="通关", Kind="completion", Team=389, Scene=297, Enemy=576 }
            };
        }
    }
}
