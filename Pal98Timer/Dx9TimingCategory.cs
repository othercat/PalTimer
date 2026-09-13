using HFrame.ENT;
using System;
using System.IO;

namespace Pal98Timer
{
    internal static class Dx9TimingCategory
    {
        internal const string ClassicCore = "PAL98DX9";
        internal const string FastCore = "PAL98DX9_800";
        internal const string ClassicDisplayName = "仙剑98柔情DX9-1.2秒";
        internal const string FastDisplayName = "仙剑98柔情DX9-0.8秒";

        internal static string RuntimeError(int expected, int? actual)
        {
            if (expected == 0 || actual == expected) return "";
            string selected = expected == 800 ? FastDisplayName : ClassicDisplayName;
            if (!actual.HasValue)
                return selected + "：黑屏模式待确认，计时暂停；请使用 PALDLL v1.63 或更新版本。";
            return selected + "：游戏实际为 " + (actual == 800 ? "0.8" : "1.2") +
                " 秒，计时暂停。请切换计时器配置，或修改游戏黑屏设置后重启游戏。";
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
            if (expected == 800)
                mismatch |= source != FastCore || selected != 800 || actual != 800;
            else if (expected == 1200)
                mismatch |= (selected != 0 && selected != 1200) ||
                    (actual != 0 && actual != 1200) || legacyFast;
            if (mismatch)
                throw new InvalidDataException("接力成绩与当前计时器配置不一致，未导入游戏存档。1.2 秒和 0.8 秒须使用各自的配置；旧版无分类成绩只兼容原 DX9 1.2 秒配置。");
        }
    }
}
