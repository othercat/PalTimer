using HFrame.ENT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Pal98Timer
{
    [TimerCoreDisplayName(Hunqian167Profile.CoreDisplayName)]
    public sealed class Hunqian167 : 仙剑98柔情DX9
    {
        private Hunqian167ProfileIdentity selectedProfile;

        public Hunqian167(GForm form) : base(form)
        {
            CoreName = "PAL98DX9HUNQIAN";
        }

        protected override void InitCheckPoints()
        {
            LoadBest();
            _CurrentStep = -1;
            Data = new HObj();
            CheckPoints = new List<CheckPoint>();

            // No inherited Classic/Dream default times: this is an independent Hunqian line.
            AddPosition("上船", 5, 1076, 1082, 5);
            AddPosition("出蛇洞", 48, 304, 1560, 3);
            AddBattle("过智修", new short[] { 58 }, 524);
            AddBattle("过鬼将军", new short[] { 65, 66 }, 472);
            AddBattle("过赤鬼王", new short[] { 59 }, 473);
            AddPosition("进扬州", 82, 288, 1072, 5);
            AddPosition("出扬州", 105, 64, 960, 3);
            AddBattle("过鬼母", new short[] { 103 }, 500);
            AddBattle("过彩依", new short[] { 108 }, 468);
            AddBattle("过剑老头", new short[] { 147 }, 494);
            AddBattle("过明王", new short[] { 145, 153 }, 519);
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("拆塔", TimeSpan.Zero))
            {
                Check = delegate () { return GameObj.AreaBGM == 23; }
            });
            AddBattle("过凤凰", new short[] { 185 }, 464);
            AddBattle("过木道人", new short[] { 181 }, 474);
            AddBattle("过火麒麟", new short[] { 200 }, 463);
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("过十年前", TimeSpan.Zero))
            {
                Check = delegate () { return GameObj.GetItemCount(0x109) > 0; }
            });
            AddBattle("过黑凤凰", new short[] { 224 }, 533);
            AddBattle("过五神龙", new short[] { 213 }, 541, 542, 543, 544, 545);
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("结局条件", TimeSpan.Zero))
            {
                Check = CheckEndingCondition
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("结局入口", TimeSpan.Zero))
            {
                Check = CheckEndingEntry
            });
            AddBattle("通关", new short[] { 187, 226, 231 }, 546);
        }

        protected override bool TryValidateAttachedGameProfile(Process process, out string error)
        {
            selectedProfile = null;
            try
            {
                string gameDirectory = Path.GetDirectoryName(process.MainModule.FileName);
                Hunqian167ProfileIdentity identity;
                string validationError;
                if (!Hunqian167Profile.TryValidateGameDirectory(gameDirectory, out identity, out validationError))
                {
                    error = Hunqian167Profile.CoreDisplayName + "计时内核拒绝连接：" + validationError;
                    return false;
                }
                selectedProfile = identity;
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                selectedProfile = null;
                error = Hunqian167Profile.CoreDisplayName + "计时内核无法确认游戏 profile：" + ex.Message;
                return false;
            }
        }

        public override string GetGameVersion()
        {
            string inherited = base.GetGameVersion();
            if (!string.IsNullOrEmpty(TournamentDisplayName))
            {
                return TournamentDisplayName;
            }
            if (string.Equals(inherited, "等待游戏运行", StringComparison.Ordinal))
            {
                return inherited;
            }
            string edition = selectedProfile == null ? "profile 待确认" : selectedProfile.DisplayName;
            return Hunqian167Profile.CoreDisplayName + " / " + edition;
        }

        public override string GetMoreInfo()
        {
            string edition = selectedProfile == null ? "" : selectedProfile.DisplayName + "  ";
            return edition + base.GetMoreInfo();
        }

        protected override void FillMoreTimerData(HObj exdata)
        {
            base.FillMoreTimerData(exdata);
            exdata["PublicProfileSchema"] = Hunqian167Profile.PublicSchema;
            exdata["ProfileId"] = selectedProfile == null ? "" : selectedProfile.ProfileId;
            exdata["ProfileVersion"] = selectedProfile == null ? "" : selectedProfile.ProfileVersion;
            exdata["ProfileDisplayName"] = selectedProfile == null ? "" : selectedProfile.DisplayName;
            exdata["ProfileCredits"] = Hunqian167Profile.OrderedCredits;
            exdata["RouteEvidence"] = "resource-aligned; PAL98 runtime acceptance pending";
        }

        protected override byte[] CaptureRelaySaveBuffer()
        {
            byte[] save = SaveObject.GetSaveBuffer(PalHandle, Hunqian167Profile.EventObjectBytes);
            if (save == null || save.Length != Hunqian167Profile.ExpectedSaveLength)
            {
                throw new InvalidDataException(
                    "魂牵 1.67 接力存档长度必须为 " + Hunqian167Profile.ExpectedSaveLength + " 字节。");
            }
            return save;
        }

        private bool CheckEndingCondition()
        {
            if (selectedProfile == null)
            {
                return false;
            }
            if (selectedProfile.Edition == Hunqian167Edition.Nonhuman)
            {
                return GameObj.GetItemCount(0x11F) > 0;
            }
            return Hunqian167Route.IsPositionAround(GameObj.Area, GameObj.X, GameObj.Y, 184, 480, 544, 3);
        }

        private bool CheckEndingEntry()
        {
            if (selectedProfile == null)
            {
                return false;
            }
            if (selectedProfile.Edition == Hunqian167Edition.Nonhuman)
            {
                return Hunqian167Route.IsBattlePresent(GameObj.Area, SnapshotEnemies(), new short[] { 187, 226, 231 }, 546);
            }
            return Hunqian167Route.IsPositionAround(GameObj.Area, GameObj.X, GameObj.Y, 187, 1296, 984, 3);
        }

        private void AddPosition(string name, int area, int x, int y, int radius)
        {
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest(name, TimeSpan.Zero))
            {
                Check = delegate ()
                {
                    return Hunqian167Route.IsPositionAround(GameObj.Area, GameObj.X, GameObj.Y, area, x, y, radius);
                }
            });
        }

        private void AddBattle(string name, short[] acceptedAreas, params short[] requiredEnemyIds)
        {
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest(name, TimeSpan.Zero))
            {
                Check = delegate ()
                {
                    return Hunqian167Route.IsBattleDefeated(GameObj.Area, SnapshotEnemies(), acceptedAreas, requiredEnemyIds);
                }
            });
        }

        private List<Dream220VisibleEnemyState> SnapshotEnemies()
        {
            List<Dream220VisibleEnemyState> result = new List<Dream220VisibleEnemyState>();
            foreach (EnemyObject enemy in GameObj.Enemies)
            {
                result.Add(new Dream220VisibleEnemyState(enemy.ID, enemy.Blood));
            }
            return result;
        }
    }
}
