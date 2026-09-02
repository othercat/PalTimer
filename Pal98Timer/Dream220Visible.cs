using HFrame.ENT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Pal98Timer
{
    [TimerCoreDisplayName(Dream220VisibleProfile.CoreDisplayName)]
    public sealed class Dream220Visible : 仙剑98柔情DX9
    {
        private Dream220VisibleProfileIdentity selectedProfile = Dream220VisibleProfile.Canonical;

        public Dream220Visible(GForm form) : base(form)
        {
            CoreName = "DREAM220VISIBLE";
        }

        protected override void InitCheckPoints()
        {
            LoadBest();
            _CurrentStep = -1;
            Data = new HObj();
            CheckPoints = new List<CheckPoint>();

            AddPosition("上船", new TimeSpan(0, 14, 30), 5, 1076, 1082, 5);
            AddPosition("出蛇洞", new TimeSpan(0, 29, 0), 48, 304, 1560, 3);
            AddBattle("过智修", new TimeSpan(0, 43, 30), new short[] { 58 }, 524);
            AddBattle("过鬼将军", new TimeSpan(0, 58, 0), new short[] { 65, 66 }, 472);
            AddBattle("过赤鬼王", new TimeSpan(1, 12, 30), new short[] { 59 }, 473);
            AddPosition("进扬州", new TimeSpan(1, 27, 0), 82, 288, 1072, 5);
            AddPosition("出扬州", new TimeSpan(1, 41, 30), 105, 64, 960, 3);
            AddBattle("过鬼母", new TimeSpan(1, 55, 0), new short[] { 103 }, 500);
            AddBattle("过彩依", new TimeSpan(2, 9, 30), new short[] { 108 }, 468);
            AddBattle("过剑老头", new TimeSpan(2, 24, 0), new short[] { 147 }, 494);
            AddBattle("过明王", new TimeSpan(2, 38, 30), new short[] { 145, 153 }, 519);
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("拆塔", new TimeSpan(2, 53, 0)))
            {
                Check = delegate () { return GameObj.AreaBGM == 23; }
            });
            AddBattle("过凤凰", new TimeSpan(3, 7, 30), new short[] { 185 }, 464);
            AddBattle("过木道人", new TimeSpan(3, 22, 0), new short[] { 181 }, 474);
            AddBattle("过火麒麟", new TimeSpan(3, 36, 30), new short[] { 200 }, 463);
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("过十年前", new TimeSpan(3, 51, 0)))
            {
                Check = delegate () { return GameObj.GetItemCount(0x109) > 0; }
            });
            AddBattle("过七毒", new TimeSpan(4, 5, 30), new short[] { 224 }, 533);
            AddBattle("过血角青龙", new TimeSpan(4, 20, 0), new short[] { 295 }, 572);
            AddBattle("过五神龙", new TimeSpan(4, 34, 30), new short[] { 213 }, 541, 542, 543, 544, 545);
            AddBattle("过桥头拜月", new TimeSpan(4, 49, 0), new short[] { 281 }, 546);
            AddBattle("通关", new TimeSpan(5, 3, 30), new short[] { 297 }, 576);
        }

        protected override bool TryValidateAttachedGameProfile(Process process, out string error)
        {
            try
            {
                string executable = process.MainModule.FileName;
                string gameDirectory = Path.GetDirectoryName(executable);
                string validationError;
                Dream220VisibleProfileIdentity validatedProfile;
                if (!Dream220VisibleProfile.TryValidateGameDirectory(
                        gameDirectory,
                        out validatedProfile,
                        out validationError))
                {
                    error = Dream220VisibleProfile.DisplayName + "计时内核拒绝连接：" + validationError;
                    return false;
                }
                selectedProfile = validatedProfile;
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = Dream220VisibleProfile.DisplayName + "计时内核无法确认游戏 profile：" + ex.Message;
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
            return string.Equals(inherited, "等待游戏运行", StringComparison.Ordinal)
                ? inherited
                : Dream220VisibleProfile.CoreDisplayName + " / " + inherited;
        }

        public override string GetMoreInfo()
        {
            return selectedProfile.DisplayName + "  " + base.GetMoreInfo();
        }

        protected override void FillMoreTimerData(HObj exdata)
        {
            base.FillMoreTimerData(exdata);
            exdata["PublicProfileSchema"] = Dream220VisibleProfile.PublicSchema;
            exdata["ProfileId"] = selectedProfile.ProfileId;
            exdata["ProfileVersion"] = selectedProfile.ProfileVersion;
            exdata["ProfileDisplayName"] = selectedProfile.DisplayName;
            exdata["ProfileCredits"] = Dream220VisibleProfile.OrderedCredits;
            exdata["RouteEvidence"] = "resource-aligned; PAL98 runtime acceptance pending";
        }

        protected override byte[] CaptureRelaySaveBuffer()
        {
            byte[] save = SaveObject.GetSaveBuffer(PalHandle, Dream220VisibleProfile.EventObjectBytes);
            if (save == null || save.Length != Dream220VisibleProfile.ExpectedSaveLength)
            {
                throw new InvalidDataException(
                    Dream220VisibleProfile.DisplayName + "接力存档长度必须为 " +
                    Dream220VisibleProfile.ExpectedSaveLength + " 字节。");
            }
            return save;
        }

        private void AddPosition(string name, TimeSpan best, int area, int x, int y, int radius)
        {
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest(name, best))
            {
                Check = delegate ()
                {
                    return Dream220VisibleRoute.IsPositionAround(GameObj.Area, GameObj.X, GameObj.Y, area, x, y, radius);
                }
            });
        }

        private void AddBattle(string name, TimeSpan best, short[] acceptedAreas, params short[] requiredEnemyIds)
        {
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest(name, best))
            {
                Check = delegate ()
                {
                    return Dream220VisibleRoute.IsBattleDefeated(
                        GameObj.Area,
                        SnapshotEnemies(),
                        acceptedAreas,
                        requiredEnemyIds);
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
