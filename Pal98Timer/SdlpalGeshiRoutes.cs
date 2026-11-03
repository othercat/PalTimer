// SPDX-License-Identifier: GPL-2.0-only
namespace Pal98Timer
{
    // Refs #55. All five Geshi editions share these plot milestones. Scene106
    // contains the human Buhuagu guide: 70AA -> 712D message -> 712E removes
    // magic571. This is the user's "trouble cave" entrance, not a stock name.
    internal static class SdlpalGeshiRoutes
    {
        internal static bool Supports(string profile) => profile == "geshi-easy-v1" || profile == "geshi-hard-v1" ||
            profile == "geshi-nightwolf-v1" || profile == "geshi-fanmade-v1" || profile == "geshi-fanmade-entertainment-v1";
        private static SdlpalSplitRule Transition(string id, string name, int from, int to) =>
            new SdlpalSplitRule { Id=id, Name=name, Kind="scene-transition", Scene=from, ReturnScene=to };
        private static SdlpalSplitRule Battle(string id, string name, int scene, int team, int enemy) =>
            new SdlpalSplitRule { Id=id, Name=name, Kind="battle", Scene=scene, Team=team, Enemy=enemy };
        internal static SdlpalSplitRule[] Create()
        {
            var youle = Battle("geshi-youle", "过游乐", 14, 305, 539); youle.Kind = "battle-after-loss";
            youle.Event = 223; youle.EventState = 0; // 263D, only after the second phase wins.
            var final = Battle("geshi-underworld-youtianba", "过阴间游天霸", 154, 307, 541); final.Kind = "battle-finish";
            return new[] {
                Transition("geshi-dungeon-out", "出地宫", 281, 277),
                Transition("geshi-trial-cave-in", "进试炼窟", 277, 285),
                Battle("geshi-qian-wutong", "过大理钱无通", 86, 382, 579),
                Battle("geshi-zhao-lianer", "过京城赵莲儿", 80, 383, 580),
                Battle("geshi-red-ghost", "过赤鬼王", 59, 27, 473),
                Battle("geshi-dikui", "过地魁", 156, 25, 502),
                new SdlpalSplitRule { Id="geshi-guifu-sword", Name="收归服之剑", Kind="item", Scene=65, Item=175 },
                Battle("geshi-return-yang", "回阳间（打败南盗侠李逍遥）", 168, 21, 480),
                new SdlpalSplitRule { Id="geshi-peach-escape", Name="出桃源村幻境（使用命运罗盘）", Kind="item-consumed", Scene=184, ReturnScene=64, Item=567 },
                Battle("geshi-sword-saint", "过剑圣", 227, 163, 494),
                Battle("geshi-return-reality", "回现实（打败楼澈）", 148, 26, 472),
                new SdlpalSplitRule { Id="geshi-trouble-cave-in", Name="进麻烦洞", Kind="scene", Scene=106 },
                Transition("geshi-yangzhou-in", "进扬州", 81, 84),
                new SdlpalSplitRule { Id="geshi-rescue-pinru", Name="救林品如（第二次使用命运罗盘）", Kind="item-consumed", Scene=294, ReturnScene=294, Item=583, Event=5372, EventState=0 },
                Transition("geshi-yangzhou-out", "出扬州", 84, 105),
                Battle("geshi-youtianba-living", "过阳间游天霸", 154, 309, 543),
                youle, final
            };
        }
    }
}
