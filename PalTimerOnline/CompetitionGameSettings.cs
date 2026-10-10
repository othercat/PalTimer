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
        public bool use_custom_competition { get; set; }
        public string custom_competition_id { get; set; }
    }
    internal static class CompetitionGameSettings
    {
        internal const string PathInGame = "palmod/common-tools.v1.json";
        internal static CompetitionSettings Parse(string json, bool locked = false)
        {
            var settings = new CompetitionSettings();
            if (json == null) return settings;
            var envelope = CompetitionProtocol.Json().Deserialize<CompetitionGameEnvelope>(json);
            if (envelope == null || envelope.schema != "PAL98.ToolLaunchSettings.v1") throw new InvalidDataException("游戏的工具配置格式不受支持。");
            if (envelope.competition_upload == null) return settings;
            var upload = envelope.competition_upload;
            if ((upload.schema != "PAL98.CompetitionUploadSettings.v1" && upload.schema != "PAL98.TimerOnlineSettings.v1") || upload.xiaorou == null) throw new InvalidDataException("服务器上传配置格式不受支持。");
            settings.Enabled = upload.xiaorou.enabled; settings.Server = upload.xiaorou.server;
            settings.CustomCompetitionId = locked ? (upload.schema == "PAL98.CompetitionUploadSettings.v1"
                ? upload.xiaorou.event_id : upload.xiaorou.use_custom_competition ? upload.xiaorou.custom_competition_id : null) : null;
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
            if (locked.State == TournamentLockReadState.Invalid)
                throw new InvalidDataException("游戏配置锁未能验证，联机保持关闭：" + locked.Diagnostic);
            // A legacy lock lacking this signed setting cannot grant upload by
            // placing a different live file next to it. Do not rewrite old locks.
            if (locked.State == TournamentLockReadState.Locked) return Parse(locked.CommonToolsSnapshot, true);
            string path = Pal98.Storage.UserDataStore.ReadGamePath(root, PathInGame);
            return Parse(Pal98.Storage.UserDataStore.FileExists(path) ? CompetitionStorage.ReadBounded(path, 1024 * 1024) : null);
        }
    }
}
