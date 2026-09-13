using HFrame.ENT;
using System;
using System.IO;

namespace Pal98Timer
{
    internal static class Dx9TimingCategory
    {
        internal const string ClassicCore = "PAL98DX9";
        internal const string FastCore = "PAL98DX9_800";
        internal const string SpeedCore = "PAL98DX9_800_SPEED";
        internal const string ClassicDisplayName = "仙剑98柔情DX9-1.2秒";
        internal const string FastDisplayName = "仙剑98柔情DX9-0.8秒";
        internal const string SpeedDisplayName = "仙剑98柔情DX9-0.8秒&快走速";

        internal static bool Traditional
        {
            get
            {
                string name = System.Globalization.CultureInfo.CurrentUICulture.Name;
                return name == "zh-TW" || name == "zh-HK" || name == "zh-MO" || name == "zh-Hant";
            }
        }
        internal static string Text(string simplified, string traditional)
        { return Traditional ? traditional : simplified; }

        internal static string Suffix(RuntimeTimingMode actual)
        { return (actual == null ? " " : "-") + ModeLabel(actual); }

        internal static string ModeLabel(RuntimeTimingMode actual)
        {
            if (actual == null) return Text("[时序模式未知]", "[時序模式未知]");
            return actual.FadeMilliseconds == 1200 ? "1.2秒" :
                actual.MapSpeedTicks == 9 ? "0.8秒&快走速" : "0.8秒";
        }
        internal static string LeaderboardName(string core)
        {
            if (core == ClassicCore) return "PC NewPatch Classic";
            if (core == FastCore) return "PC NewPatch Classic-0.8s";
            return core == SpeedCore ? "PC NewPatch Classic-0.8s+Speed" : "";
        }

        internal static string RuntimeError(int expected, int speed, RuntimeTimingMode actual)
        {
            if (expected == 0 || (actual != null && actual.FadeMilliseconds == expected && actual.MapSpeedTicks == speed)) return "";
            if (actual == null)
                return Text("黑屏与走速模式待确认，计时暂停；请更新本次 v1.63 完整包及配套计时器。",
                    "黑屏與走速模式待確認，計時暫停；請更新本次 v1.63 完整包及配套計時器。");
            return Text("游戏实际为", "遊戲實際為") + Suffix(actual) +
                Text("，与所选配置不一致，计时暂停。请切换配置或修改设置后重启游戏。",
                    "，與所選配置不一致，計時暫停。請切換配置或修改設定後重啟遊戲。");
        }

        // Run before any RPG/sidecar write, and at the direct timer-restore entry.
        // Legacy untagged records belong only to the existing Classic 1.2s line.
        internal static void ValidateImport(string core, int expected, HObj record)
        {
            string source = record.HasValue("TimerCore") ? record.GetValue<string>("TimerCore") : "";
            int selected = record.HasValue("TimingModeMs") ? record.GetValue<int>("TimingModeMs") : 0;
            int actual = record.HasValue("PaletteFadeModeMs") ? record.GetValue<int>("PaletteFadeModeMs") : 0;
            string version = record.HasValue("DX9Version") ? record.GetValue<string>("DX9Version") : "";
            bool legacyFast = !string.IsNullOrEmpty(version) && version.Contains("-0.8s");
            bool mismatch = !string.IsNullOrEmpty(source) && source != core;
            if (expected == 0) return;
            if (expected == 800)
                mismatch |= source != core || selected != 800 || actual != 800;
            else if (expected == 1200)
                mismatch |= (selected != 0 && selected != 1200) ||
                    (actual != 0 && actual != 1200) || legacyFast;
            int expectedSpeed = core == SpeedCore ? 9 : 10;
            bool newFormat = record.HasValue("TimingRulesVersion") || record.HasValue("MapSpeedTicks") ||
                record.HasValue("TimingMapSpeedTicks");
            if (newFormat)
            {
                if (!record.HasValue("TimingRulesVerified") || !record.GetValue<bool>("TimingRulesVerified") ||
                    (record.HasValue("ReferenceTimeline") && record.GetValue<bool>("ReferenceTimeline")) ||
                    (record.HasValue("TimingValidationError") && !string.IsNullOrEmpty(record.GetValue<string>("TimingValidationError"))))
                    throw new InvalidDataException(Text("此记录未通过时序验证或仅为参考时间线，不能恢复为有效成绩。",
                        "此記錄未通過時序驗證或僅為參考時間線，不能恢復為有效成績。"));
                mismatch |= !record.HasValue("TimingRulesVersion") || record.GetValue<int>("TimingRulesVersion") != 1 ||
                    !record.HasValue("MapSpeedTicks") || record.GetValue<int>("MapSpeedTicks") != expectedSpeed ||
                    !record.HasValue("TimingMapSpeedTicks") || record.GetValue<int>("TimingMapSpeedTicks") != expectedSpeed ||
                    source != core || selected != expected || actual != expected;
            }
            if (core == SpeedCore)
                mismatch |= !newFormat || !record.HasValue("TimingRulesVerified") || !record.GetValue<bool>("TimingRulesVerified");
            if (mismatch)
                throw new InvalidDataException(Text(
                    "成绩与当前时序分类不一致，未导入计时状态或游戏存档。三种配置必须使用各自的成绩；旧格式不能导入快走速配置。",
                    "成績與目前時序分類不一致，未匯入計時狀態或遊戲存檔。三種配置必須使用各自的成績；舊格式不能匯入快走速配置。"));
        }
    }
}
