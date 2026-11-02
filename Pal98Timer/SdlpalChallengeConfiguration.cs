using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pal98.Challenges;
using Pal98.Storage;
using Wire = Pal98.Challenges.ChallengeRelayContract;

namespace Pal98Timer
{
    // r102 does not publish challenge slots in its bridge: freeze file evidence,
    // never claim these are verified memory values.
    internal sealed class SdlpalChallengeConfiguration
    {
        private readonly object gate = new object();
        private bool reading;
        private long nextRead;
        private string process, fingerprint;
        private volatile Dictionary<string, string> facts = new Dictionary<string, string>(StringComparer.Ordinal);
        private volatile string error;
        internal Dictionary<string, string> Facts => facts;
        internal string Error => error;
        private void SelectProcess(string value)
        {
            if (process == value) return;
            process = value; nextRead = 0; fingerprint = null;
            facts = new Dictionary<string, string>(StringComparer.Ordinal); error = "正在读取完整挑战配置。";
        }
        internal void Observe(string root, int processId, ulong processCreated, string profile)
        {
            // No file or JSON work on the 70 ms timing loop.
            lock (gate) {
                string requested = processId + ":" + processCreated;
                SelectProcess(requested);
                if (reading || DateTime.UtcNow.Ticks < nextRead) return;
                reading = true; nextRead = DateTime.UtcNow.Ticks + TimeSpan.TicksPerSecond;
                Task.Run(() => {
                    try { ReadFilesCore(root, processCreated, profile, requested); }
                    finally {lock (gate) reading = false;}
                });
            }
        }
        internal KeyValuePair<string, string>[] SnapshotFacts() => facts.ToArray();
        internal void ReadFiles(string root, int processId, ulong processCreated, string profile)
        {
            string requested = processId + ":" + processCreated;
            lock (gate) SelectProcess(requested);
            ReadFilesCore(root, processCreated, profile, requested);
        }
        private void ReadFilesCore(string root, ulong processCreated, string profile, string requested)
        {
            try {
                string content = profile.EndsWith("-v1", StringComparison.Ordinal) ? profile.Substring(0, profile.Length - 3) : profile;
                Dictionary<string, object> selection = null, activation = null;
                string selectionPath = UserDataStore.ReadGamePath(root, "palmod/challenge-selection.v1.json");
                if (File.Exists(selectionPath)) {
                    selection = Wire.Object(OriginalChallengeIdentity.ReadFile(root, "palmod/challenge-selection.v1.json", 1048576));
                    if (Wire.Boolean(selection, "active")) {
                        activation = Wire.Map(Wire.Field(selection, "activation"));
                        Wire.Activation(activation, Wire.String(activation, "device_id"), Wire.String(activation, "content_id"));
                        if (content != Wire.String(activation, "content_id")) throw new InvalidDataException("在线挑战内容与当前改版不一致。");
                    }
                }
                string iniPath = UserDataStore.ReadGamePath(root, "challenge-rules.ini");
                string ini = File.Exists(iniPath) ? OriginalChallengeIdentity.ReadFile(root, "challenge-rules.ini", 65536) : "[Challenge]\nSchemaVersion=1\nHero=li-xiaoyao\n";
                var values = EffectiveChallengeConfiguration.Parse(ini, false);
                bool inheritedSteal = !values.ContainsKey("Steal99");
                string legacy = inheritedSteal ? LegacySteal99(root) : "0";
                foreach (string slot in EffectiveChallengeConfiguration.SdlSlots) {
                    if (!values.ContainsKey(slot)) values[slot] = slot == "Steal99" ? legacy :
                        (slot == "AutoCollect" || slot == "SpiritShop") && content != "pal-dos" && content != "pal98-classic-v6" ? "1" : "0";
                    if (!values.ContainsKey("Priority" + slot)) values["Priority" + slot] = slot == "AutoCollect" ? "999" : "0";
                }
                if (!values.ContainsKey("Hero")) values["Hero"] = "li-xiaoyao";
                if (!EffectiveChallengeConfiguration.CompleteSdl(values)) throw new InvalidDataException("改版完整配置缺项或无效。");
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                EffectiveChallengeConfiguration.AddFacts(result, EffectiveChallengeConfiguration.Serialize(values));
                if (activation != null) {
                    EffectiveChallengeConfiguration.RequireSource(Wire.String(activation, "ini"), EffectiveChallengeConfiguration.Serialize(values));
                    if (selection.TryGetValue("effective_ini", out object expected) && expected != null && !Equals(expected, ini))
                        throw new InvalidDataException("已保存的完整配置与实际文件不一致。");
                    var item = Wire.Map(activation["challenge"]); var rules = Wire.Map(item["rules"]);
                    if (rules.TryGetValue("sha256", out object hash) && hash is string) {
                        result["config.source.id"] = Wire.String(item, "id");
                        result["config.source.revision"] = Wire.Integer(item, "revision").ToString(System.Globalization.CultureInfo.InvariantCulture);
                        result["config.source.content_id"] = content; result["config.source.rules_sha256"] = (string)hash;
                    }
                }
                // Cover and author/display text never change gameplay identity.
                string signature = RankingConfiguration.Digest(result);
                bool changedAfterStart = processCreated != 0 && File.Exists(iniPath) && (ulong)File.GetLastWriteTimeUtc(iniPath).ToFileTimeUtc() > processCreated;
                if (inheritedSteal && processCreated != 0) {
                    string configPath = UserDataStore.ReadGamePath(root, "config.ini");
                    changedAfterStart |= File.Exists(configPath) && (ulong)File.GetLastWriteTimeUtc(configPath).ToFileTimeUtc() > processCreated;
                }
                lock (gate) {
                    if (process != requested) return;
                    if (fingerprint != null) {
                        if (fingerprint != signature) error = "运行期间配置已改变，保留启动时完整配置；请重启游戏。";
                        return;
                    }
                    facts = result; fingerprint = signature;
                    error = changedAfterStart ? "挑战配置文件晚于游戏启动；保留完整文件证据，请重启游戏确认生效。" : null;
                }
            } catch (Exception failure) when (failure is IOException || failure is InvalidDataException || failure is ArgumentException || failure is UnauthorizedAccessException) {
                lock (gate) if (process == requested) error = "读取完整挑战配置失败：" + failure.Message;
            }
        }
        private static string LegacySteal99(string root)
        {
            string path = UserDataStore.ReadGamePath(root, "config.ini");
            if (!File.Exists(path)) return "0";
            if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("本地配置超过容量。");
            bool gameplay = false;
            using (var reader = new StreamReader(path, Encoding.UTF8, true)) {
                string raw;
                while ((raw = reader.ReadLine()) != null) {
                    string line = raw.Trim();
                    if (line.StartsWith("[")) {gameplay = line.Equals("[Gameplay]", StringComparison.OrdinalIgnoreCase); continue;}
                    int equal = line.IndexOf('=');
                    if (!gameplay || equal < 1 || !line.Substring(0, equal).Trim().Equals("Steal99", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = line.Substring(equal + 1).Trim().Trim('"', '\'').Split(';', '#')[0].Trim();
                    return value == "1" ? "1" : "0";
                }
            }
            return "0";
        }
    }
}

