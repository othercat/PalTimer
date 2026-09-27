using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pal98Timer
{
    internal sealed class CompetitionGameEnvelope
    {
        public string schema { get; set; }
        public CompetitionUploadConfiguration competition_upload { get; set; }
    }
    internal sealed class CompetitionUploadConfiguration
    {
        public string schema { get; set; }
        public CompetitionServerConfiguration xiaorou { get; set; }
    }
    internal sealed class CompetitionServerConfiguration
    {
        public bool enabled { get; set; }
        public string server { get; set; }
        public string event_id { get; set; }
        public string ruleset_id { get; set; }
    }
    internal static class CompetitionGameSettings
    {
        internal const string PathInGame = "palmod/common-tools.v1.json";
        internal static CompetitionSettings Parse(string json)
        {
            var settings = new CompetitionSettings();
            if (json == null) return settings;
            var envelope = CompetitionProtocol.Json().Deserialize<CompetitionGameEnvelope>(json);
            if (envelope == null || envelope.schema != "PAL98.ToolLaunchSettings.v1") throw new InvalidDataException("游戏的工具配置格式不受支持。");
            if (envelope.competition_upload == null) return settings;
            var upload = envelope.competition_upload;
            if (upload.schema != "PAL98.CompetitionUploadSettings.v1" || upload.xiaorou == null) throw new InvalidDataException("服务器上传配置格式不受支持。");
            settings.Enabled = upload.xiaorou.enabled; settings.Server = upload.xiaorou.server;
            settings.Event = upload.xiaorou.event_id; settings.Ruleset = upload.xiaorou.ruleset_id;
            string error = settings.Validate();
            if (error.Length != 0) throw new InvalidDataException(error);
            return settings;
        }
        internal static CompetitionSettings Load(Process process)
        {
            if (process == null) return new CompetitionSettings();
            // Only the selected PAL process owns this directory. Never search
            // the timer's own directory or accept a previous machine preference.
            long created = process.StartTime.ToUniversalTime().ToFileTimeUtc();
            string path = process.MainModule.FileName;
            if (!Path.GetFileName(path).Equals("PAL.exe", StringComparison.OrdinalIgnoreCase) || process.HasExited)
                throw new InvalidDataException("当前目标不是运行中的 PAL.exe。");
            var result = LoadDirectory(Path.GetDirectoryName(path));
            if (process.HasExited || created != process.StartTime.ToUniversalTime().ToFileTimeUtc())
                throw new InvalidDataException("读取配置期间游戏已退出。");
            return result;
        }
        internal static CompetitionSettings LoadDirectory(string root)
        {
            var locked = TournamentLockInfoReader.Load(root);
            if (locked.State == TournamentLockReadState.Invalid) throw new InvalidDataException("游戏配置锁未能验证，比赛联机保持关闭。");
            // A legacy lock lacking this signed setting cannot grant upload by
            // placing a different live file next to it. Do not rewrite old locks.
            if (locked.State == TournamentLockReadState.Locked) return Parse(locked.CommonToolsSnapshot);
            string path = Path.Combine(root, PathInGame);
            return Parse(File.Exists(path) ? CompetitionStorage.ReadBounded(path, 1024 * 1024) : null);
        }
    }
}
