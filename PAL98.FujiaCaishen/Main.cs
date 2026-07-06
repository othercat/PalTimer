using System;
using System.Collections.Generic;
using System.Text;
using TimerPluginBase;
namespace PAL98.FujiaCaishen
{
    public class Main : TimerPlugin
    {
        public override EPluginPosition GetPosition()
        {
            return EPluginPosition.BR;
        }
        private int money=0;
        private int ic = 0;
        private bool hasYuhangArtifacts = false;
        private bool isYuhangArtifactsFrozen = false;
        public override string GetResult()
        {
            return "神器:" + (hasYuhangArtifacts ? "已收集" : "未收集") + " 钱" + money + " 道具" + ic + "　　";
        }

        private Dictionary<int, PlayerObject> Players = new Dictionary<int, PlayerObject>();
        public override void OnLoad()
        {
            ResetYuhangArtifacts();
            Players.Add(PlayerObject.LiXY, new PlayerObject());
            Players.Add(PlayerObject.ZhaoLE, new PlayerObject());
            Players.Add(PlayerObject.LinYR, new PlayerObject());
            Players.Add(PlayerObject.WuH, new PlayerObject());
            Players.Add(PlayerObject.AN, new PlayerObject());
            Players.Add(PlayerObject.GaiLJ, new PlayerObject());
        }

        public override void OnUnload()
        {
        }

        public override void OnEvent(string name, object data)
        {
            if (name == "Start" || name == "InitCheckPoints")
            {
                ResetYuhangArtifacts();
            }
        }


        private const int BaseAddrPTR = 0x428000;
        private const int MoneyOffset = 0x2B4;
        private const int XOffset = 0x262;
        private const int YOffset = 0x264;
        private const int AreaOffset = 0x26A;
        private const int ItemSlotOffsetPTR = 0x768;
        private const int MemberCountOffset = 0x266;
        private const int AttrHeadOffsetPTR = 0x7a8;
        private const int TeamOffsetPTR = 0x4b8;
        private const short ZijinDan = 0x111;
        private const short EarthBall = 0x10B;
        private const short LiushenDan = 0x11E;
        private const short ClothBag = 0x10F;
        private short[] EUItems = new short[] { 0x104, 0x107, 0x108, 0x109, 0x10A, 0x10B };
        private short[] YuhangArtifactItems = new short[] { ZijinDan, EarthBall, LiushenDan, ClothBag };
        public override void Flush(IntPtr handle, int PID, int BaseAddr32, long BaseAddr64)
        {

            int BaseAddr = Readm<int>(handle, BaseAddrPTR);
            money = Readm<int>(handle, BaseAddr + MoneyOffset);
            short x = Readm<short>(handle, BaseAddr + XOffset);
            short y = Readm<short>(handle, BaseAddr + YOffset);
            short area = Readm<short>(handle, BaseAddr + AreaOffset);
            ///item
            int ItemSlotAddr= Readm<int>(handle, BaseAddr + ItemSlotOffsetPTR);
            Dictionary<short, short> Items = new Dictionary<short, short>();
            int currentaddr = ItemSlotAddr;
            for (int i = 0; i < 251; ++i, currentaddr += 0x6)
            {
                short id = Readm<short>(handle, currentaddr);
                if (id <= 0) continue;

                short counti = Readm<short>(handle, currentaddr + 0x2);
                if (counti <= 0) continue;

                if (Items.ContainsKey(id))
                {
                    Items[id] = counti;
                }
                else
                {
                    Items.Add(id, counti);
                }
            }
            ///equip
            int AttrHeadAddr = Readm<int>(handle, BaseAddr + AttrHeadOffsetPTR);
            foreach (var kv in Players)
            {
                kv.Value.Flush(handle, AttrHeadAddr, kv.Key);
            }
            short MemberCount = Readm<short>(handle, BaseAddr + MemberCountOffset);
            MemberCount++;//count 要加1
            int TeamAddr = Readm<int>(handle, BaseAddr + TeamOffsetPTR);
            List<PlayerObject> TeamMembers = new List<PlayerObject>();
            for (int i = 0; i < MemberCount; ++i)
            {
                short id = Readm<short>(handle, TeamAddr + 10 * i);
                PlayerObject player;
                if (Players.TryGetValue(id, out player))
                {
                    TeamMembers.Add(player);
                }
            }
            if (!isYuhangArtifactsFrozen)
            {
                UpdateYuhangArtifacts(Items, TeamMembers);
                if (IsBeforeBoatPoint(area, x, y))
                {
                    isYuhangArtifactsFrozen = true;
                }
            }
            int count = 0;
            if (TeamMembers != null && Items != null)
            {
                count = Items.Count;
                foreach (var mem in TeamMembers)
                {
                    foreach (short id in EUItems)
                    {
                        if (mem.Equip_Ball == id && !Items.ContainsKey(id))
                        {
                            count++;
                        }
                    }
                }
            }
            ic = count;
        }

        private void UpdateYuhangArtifacts(Dictionary<short, short> items, List<PlayerObject> teamMembers)
        {
            foreach (short id in YuhangArtifactItems)
            {
                if (items.ContainsKey(id))
                {
                    hasYuhangArtifacts = true;
                    return;
                }
            }

            foreach (var mem in teamMembers)
            {
                foreach (short id in YuhangArtifactItems)
                {
                    if (mem.Equip_Ball == id)
                    {
                        hasYuhangArtifacts = true;
                        return;
                    }
                }
            }
        }

        private void ResetYuhangArtifacts()
        {
            hasYuhangArtifacts = false;
            isYuhangArtifactsFrozen = false;
        }

        private bool IsBeforeBoatPoint(short area, short x, short y)
        {
            return area == 6
                && x >= 1072 - 16 * 2
                && x <= 1072 + 16 * 2
                && y >= 1080 - 8 * 2
                && y <= 1080 + 8 * 2;
        }


        public class PlayerObject
        {
            public const int LiXY = 0;
            public const int ZhaoLE = 1;
            public const int LinYR = 2;
            public const int WuH = 3;
            public const int AN = 4;
            public const int GaiLJ = 5;
            public int Who;
            
            public short Equip_Ball;
            
            public PlayerObject()
            {
            }
            public PlayerObject(IntPtr handle, int AttrHeadAddr, int Who)
            {
                this.Flush(handle, AttrHeadAddr, Who);
            }
            private const int SkillOffset = 0x180;
            private const int CombineSkillOffset = 780;
            private const int LevelOffset = 72;
            private const int ExpInfoOffset = 944;
            public void Flush(IntPtr handle, int AttrHeadAddr, int Who)
            {
                this.Who = Who;
                int whooff = Who * 2;
                int curaddr = AttrHeadAddr + LevelOffset + whooff;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                curaddr += 12;
                Equip_Ball = Readm<short>(handle, curaddr);
            }
        }
    }
}
