using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Wire = Pal98.Challenges.ChallengeRelayContract;

namespace Pal98Timer
{
    // Transport only. Each ConfigTool remains the interpreter of its game's
    // cover format, asset validation and atomic configuration transaction.
    internal static class ChallengeCoverRefresh
    {
        internal const string Protocol = "PAL.ChallengeCoverRefresh.v1";
        internal static Dictionary<string, object> ReadSelection(string root, string localAppData = null)
        {
            string path = Pal98.Storage.UserDataStore.ReadGamePath(root, "palmod/challenge-selection.v1.json", localAppData);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("Challenge selection exceeds capacity.");
            byte[] bytes = File.ReadAllBytes(path); if (bytes.Length > 1048576) throw new InvalidDataException("Challenge selection exceeds capacity.");
            var selection = Wire.Object(new System.Text.UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
            if (!Wire.Boolean(selection, "active")) return null;
            var activation = Wire.Map(Wire.Field(selection, "activation")); var item = Wire.Item(Wire.Field(activation, "challenge"));
            string hash; using (var sha = SHA256.Create()) hash = CompetitionProtocol.Hex(sha.ComputeHash(bytes));
            return new Dictionary<string, object> { ["protocol"] = Protocol, ["server_origin"] = Wire.Origin(Wire.String(selection, "server_origin")),
                ["selection_sha256"] = hash, ["challenge_id"] = Wire.String(item, "id"), ["revision"] = Wire.Integer(item, "revision") };
        }
        internal static string Interpreter(string root)
        {
            foreach (string relative in new[] { "Tools/仙剑98配置工具/仙剑98新补丁配置工具.exe", "Tools/仙剑98配置工具/仙剑配置工具.exe", "仙剑配置工具.exe" }) {
                string path = Path.Combine(root, relative);
                if (File.Exists(path) && (FileVersionInfo.GetVersionInfo(path).Comments ?? "").Contains(Protocol)) return path;
            }
            return null; // Never launch an older executable that would show its GUI.
        }
        internal static async Task Refresh(string root, CancellationToken cancellation, CompetitionStorage suppliedStorage = null, ICompetitionTransport suppliedTransport = null)
        {
            try {
                var envelope = ReadSelection(root); if (envelope == null) return;
                string executable = Interpreter(root); if (executable == null) return;
                var request = new Dictionary<string, object> { ["action"] = "refresh_cover", ["server_origin"] = envelope["server_origin"],
                    ["challenge_id"] = envelope["challenge_id"], ["revision"] = envelope["revision"] };
                var storage = suppliedStorage ?? new CompetitionStorage();
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var transport = suppliedTransport ?? new CompetitionHttpTransport()) {
                    deadline.CancelAfter(25000);
                    var cached = new Dictionary<string, object>(envelope) { ["cached_only"] = true };
                    await RunInterpreter(root, executable, cached, storage, deadline.Token).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    var reply = await new ChallengeService(root, storage, transport).Execute(Wire.Serialize(request), deadline.Token).ConfigureAwait(false);
                    if (!Wire.Boolean(reply, "ok")) return;
                    envelope["presentation"] = Wire.Field(reply, "presentation");
                    await RunInterpreter(root, executable, envelope, storage, deadline.Token).ConfigureAwait(false);
                }
            } catch { /* Display/network/asset failures cannot block local play. */ }
        }
        private static async Task RunInterpreter(string root, string executable, Dictionary<string, object> envelope, CompetitionStorage storage, CancellationToken cancellation)
        {
            string input = Path.Combine(storage.Root, "cover-requests", Guid.NewGuid().ToString("N") + ".json");
            try {
                CompetitionStorage.WriteAtomic(input, Wire.Serialize(envelope));
                using (var process = Process.Start(new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "--refresh-challenge-cover " + Quote(root) + " " + Quote(input) })) {
                    if (process == null) return;
                    // Cancellation stops waiting, never kills a local transaction.
                    while (!process.HasExited) await Task.Delay(80, cancellation).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) { throw; }
            catch { /* A bad cached cover must not prevent a newer online cover. */ }
            finally { try { File.Delete(input); } catch { } }
        }
        private static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0) throw new InvalidDataException("Invalid cover helper path.");
            int slashes = 0; for (int i = value.Length - 1; i >= 0 && value[i] == '\\'; --i) slashes++;
            return "\"" + value + new string('\\', slashes) + "\"";
        }
    }
}
