using Pal98Timer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static partial class LiveBehavior
{
    static async Task ConnectivityRecoveryTests(string root)
    {
        foreach (bool manual in new[] { true, false }) {
            var wire = new Transport(); int reads = 0; bool fail = true;
            using (var game = Process.GetCurrentProcess()) {
                var client = new CompetitionClient(new CompetitionStorage(Path.Combine(root, "retry-" + manual), () => "fixture-retry"), wire, new Auth(), true,
                    readGameSettings: p => { Interlocked.Increment(ref reads); if (fail) throw new InvalidDataException("fixture configuration unavailable");
                        return new CompetitionSettings { Enabled = true, Server = "https://fixture.invalid" }; });
                await Until(() => client.DeviceIdentityReady, "retry client initialized");
                Check(client.StatusCaption == " [联机等待游戏]", "no game is visible rather than blank");
                client.ObserveGame(game);
                await Until(() => client.StatusCaption == " [联机配置读取失败]", "configuration failure reaches persistent status");
                Check(wire.Devices == 0 && client.StatusDetail.Contains("自动重读"), "invalid configuration grants no network authority and explains recovery");
                for (int i=0; i<100; i++) client.ObserveGame(game);
                await Task.Delay(350); Check(reads == 1, "timing polls cannot spin configuration retries");
                fail = false; if (manual) client.Retry();
                await Until(() => client.StatusCaption.StartsWith(" [已连接:"), (manual ? "manual" : "automatic") + " retry reconnects the SAME process");
                Check(reads == 2 && wire.Devices > 0 && wire.Challenges > 0 && wire.Sessions == 1 && wire.Updates > 0,
                    "recovery performs registration, authentication, session and heartbeat");
                await client.CloseAsync();
            }
        }
        string lockedRoot = Path.Combine(root,"settings-write-recovery");
        var lockedWire = new Transport();
        var locked = new CompetitionClient(new CompetitionStorage(lockedRoot, () => "fixture-storage"), lockedWire, new Auth(), true,
            readGameSettings: p => new CompetitionSettings { Enabled = true, Server = "https://fixture.invalid" });
        using (var process = Process.GetCurrentProcess()) {
            locked.ObserveGame(process);
            await Until(() => locked.StatusCaption.StartsWith(" [已连接:"), "storage recovery fixture connected");
            var started = Observe("storage-recovery",0); locked.Publish(started);
            using (var file = File.Open(Path.Combine(lockedRoot,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read)) {
                var appearance = locked.Settings; appearance.OverlayColor="#00AA00"; locked.ConfigureAppearance(appearance);
                await Until(() => locked.GameSettingsError.Length != 0 && !locked.Enabled, "failed appearance save visibly suspends network authority");
            }
            locked.Retry();
            await Until(() => locked.StatusCaption.StartsWith(" [已连接:"), "released settings file recovers without restarting game");
            var continued = Observe("storage-recovery",1); locked.Publish(continued);
            Check(continued.Context.RunId == started.Context.RunId && continued.Context.StartedAt == started.Context.StartedAt,
                "transient storage failure preserves original run identity and start evidence");
            await locked.CloseAsync();
        }
        var throwing = new Transport { ThrowRequests = true };
        var broken = Client(root, "throwing-transport", throwing, new Auth());
        await Until(() => broken.StatusCaption == " [连接失败]", "unexpected background exceptions are visible");
        Check(broken.StatusDetail.Contains("后台处理失败"), "unexpected failure explains local timing remains available");
        throwing.ThrowRequests = false; broken.Retry();
        await Until(() => broken.StatusCaption.StartsWith(" [已连接:"), "unexpected transport failure recovers");
        throwing.UpdateStatus = 503; broken.Retry();
        await Until(() => broken.StatusCaption == " [连接失败]", "failed heartbeat immediately overrides a previous fresh ACK in UI");
        throwing.UpdateStatus = 200; broken.Retry();
        await Until(() => broken.StatusCaption.StartsWith(" [已连接:"), "heartbeat recovery restores connected UI");
        await broken.CloseAsync();
        var unavailable = Client(root, "auth-unavailable-visible", new Transport(), new Auth { Available = false });
        await Until(() => unavailable.StatusCaption == " [联机认证组件不可用]", "missing authentication has distinct UI status");
        await unavailable.CloseAsync();
        var unapproved = Client(root, "approval-visible", new Transport { Approved = false }, new Auth());
        await Until(() => unapproved.StatusCaption == " [联机构建待批准]", "pending approval is distinguished from a transport failure");
        await unapproved.CloseAsync();
    }

    // Real production HttpClient against an isolated loopback peer. The fake
    // protocol responder is used only at the server end; no external requests.
    sealed class LoopbackPeer : IDisposable
    {
        readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        internal readonly Transport Protocol = new Transport();
        internal string Origin;
        internal volatile int Requests;
        internal Exception Error;
        readonly Task worker;
        string device = "";
        internal LoopbackPeer() {
            listener.Start(); Origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            worker = Task.Run((Func<Task>)Serve);
        }
        async Task Serve() {
            try {
                while (!cancel.IsCancellationRequested) using (var socket = await listener.AcceptTcpClientAsync()) using (var stream = socket.GetStream()) {
                    var header = new StringBuilder(); var one = new byte[1];
                    while (header.Length < 16384 && !header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) {
                        if (await stream.ReadAsync(one, 0, 1) != 1) throw new EndOfStreamException(); header.Append((char)one[0]);
                    }
                    var lines = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    var request = lines[0].Split(' '); int length = 0; bool authorized = false;
                    foreach (var line in lines) {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                        if (line.StartsWith("X-PAL-HWID:", StringComparison.OrdinalIgnoreCase)) device = line.Substring(11).Trim();
                        if (line.StartsWith("Authorization: Bearer ", StringComparison.OrdinalIgnoreCase)) authorized = true;
                    }
                    if (!authorized || length > 1048576) throw new InvalidDataException("fixture request contract");
                    if (header.ToString().IndexOf("Expect: 100-continue", StringComparison.OrdinalIgnoreCase) >= 0) {
                        var ready = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"); await stream.WriteAsync(ready, 0, ready.Length);
                    }
                    byte[] bytes = new byte[length]; int read = 0;
                    while (read < length) { int count = await stream.ReadAsync(bytes, read, length-read); if (count == 0) throw new EndOfStreamException(); read += count; }
                    string body = Encoding.UTF8.GetString(bytes);
                    if (body.Length > 0) { var value = CompetitionProtocol.Json().Deserialize<Dictionary<string,object>>(body); object id; if (value.TryGetValue("hwid", out id)) device = (string)id; }
                    var response = await Protocol.Send(request[0], Origin + request[1], device, "fixture", body, 5000, cancel.Token);
                    Interlocked.Increment(ref Requests);
                    var payload = Encoding.UTF8.GetBytes(response.Body ?? "");
                    var responseHeader = Encoding.ASCII.GetBytes("HTTP/1.1 " + response.Status + " Fixture\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\nDate: " + DateTime.UtcNow.ToString("R") + "\r\n\r\n");
                    await stream.WriteAsync(responseHeader, 0, responseHeader.Length); await stream.WriteAsync(payload, 0, payload.Length);
                }
            } catch (Exception ex) { if (!cancel.IsCancellationRequested) Error = ex; }
        }
        public void Dispose() { cancel.Cancel(); listener.Stop(); worker.GetAwaiter().GetResult(); cancel.Dispose(); }
    }
    static async Task LoopbackHttpTests(string root)
    {
        using (var peer = new LoopbackPeer()) {
            var client = new CompetitionClient(new CompetitionStorage(Path.Combine(root,"real-http"), () => "fixture-loopback"), new CompetitionHttpTransport(), new Auth());
            try {
                client.Configure(new CompetitionSettings { Enabled = true, Server = peer.Origin });
                await Until(() => client.StatusCaption == " [已连接:1042]" || peer.Error != null, "production HTTP transport reaches a loopback peer");
                if (peer.Error != null) throw peer.Error;
                Check(peer.Protocol.Devices == 1 && peer.Protocol.Challenges == 1 && peer.Protocol.Sessions == 1 && peer.Protocol.Updates > 0,
                    "real HTTP completed device, auth status, challenge, session and ACK");
                peer.Protocol.UpdateStatus = 503; client.Retry();
                await Until(() => client.StatusCaption == " [连接失败]", "real HTTP 503 appears immediately");
                peer.Protocol.UpdateStatus = 200; client.Retry();
                await Until(() => client.StatusCaption == " [已连接:1042]", "real HTTP manual retry reconnects");
                Check(peer.Requests >= 10 && peer.Error == null, "all HTTP requests stayed on isolated loopback");
            } finally { await client.CloseAsync(); }
        }
    }
}
