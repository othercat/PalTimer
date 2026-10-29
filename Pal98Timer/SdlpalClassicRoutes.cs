using System;
namespace Pal98Timer
{
    // Refs #47 / othercat/sdlpal-lostedit#156. Original 21 PAL98 checkpoints,
    // mapped against each complete DOS/v6 SSS/DATA resource set. Late DOS
    // scenes are reordered and must not be obtained by subtracting one.
    internal static class SdlpalClassicRoutes
    {
        internal static bool Supports(string profile) => profile == "pal-dos-v1" || profile == "pal98-classic-v6-v1";
        private static SdlpalSplitRule Position(string id, string name, int scene, int x, int y, int radius) =>
            new SdlpalSplitRule { Id = "classic-" + id, Name = name, Kind = "position", Scene = scene, X = x, Y = y, Radius = radius };
        private static SdlpalSplitRule Battle(string id, string name, int scene, int team, int enemy, string kind = "battle") =>
            new SdlpalSplitRule { Id = "classic-" + id, Name = name, Kind = kind, Scene = scene, Team = team, Enemy = enemy };
        internal static SdlpalSplitRule[] ForProfile(string profile)
        {
            if (!Supports(profile)) throw new ArgumentException("Unknown classic profile");
            bool dos = profile == "pal-dos-v1";
            int shift = dos ? 0 : 1;
            return new[] {
                Position("stone", "见石碑", 18 + shift, 1696, 384, 1),
                new SdlpalSplitRule { Id = "classic-training", Name = "学功夫", Kind = "music", Music = 86 },
                Position("ship", "上船", 5 + shift, 1072, 1080, 2),
                Position("lin-out", "出林家堡", 39 + shift, 1456, 872, 5),
                Position("snake-out", "出隐龙窟", 48 + shift, 304, 1560, 5),
                Position("zombies", "生化危机", 61 + shift, 1152, 1264, 2),
                Battle("ghost-general", "过鬼将军", 65 + shift, 26, 472),
                Battle("red-ghost", "过赤鬼王", 59 + shift, 27, 473),
                Position("yangzhou-in", "进扬州", 79 + shift, 256, 1344, 5),
                Position("yangzhou-out", "出扬州", 105 + shift, 64, 960, 5),
                Position("cave-out", "出麻烦洞", 106 + shift, 1520, 408, 5),
                Position("capital", "进京城", 100 + shift, 272, 216, 2),
                Battle("caiyi", "过彩依", 108 + shift, 39, 468, "battle-end"),
                new SdlpalSplitRule { Id = "classic-tower-in", Name = "进锁妖塔", Kind = "any-location", Alternatives = new[] {
                    new SdlpalLocationRule { Scene = 163 + shift, X = 1024, Y = 992, Radius = 4 },
                    new SdlpalLocationRule { Scene = 164 + shift },
                    new SdlpalLocationRule { Scene = 146 + shift, X = 1024, Y = 448, Radius = 2 } } },
                Position("sword", "剑柱", 145 + shift, 304, 1048, 3),
                Battle("tower-out", "拆塔", 144 + shift, 308, 542),
                Battle("phoenix", "过凤凰", 185 + shift, 203, 464),
                Position("past", "进十年前", dos ? 227 : 247, 1408, 1584, 5),
                new SdlpalSplitRule { Id = "classic-pearl", Name = "水灵珠", Kind = "water-pearl", Scene = dos ? 247 : 267, ReturnScene = dos ? 203 : 204 },
                Position("rain", "祈雨", dos ? 258 : 228, 992, 928, 0),
                Battle("ending", "通关", dos ? 281 : 282, 313, 546, "completion")
            };
        }
    }
}
