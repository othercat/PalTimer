using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pal98Timer;
using Pal98.Storage;
using Pal98.Challenges;
using Wire = Pal98.Challenges.ChallengeRelayContract;

internal static partial class ChallengeBehavior
{
    private sealed class FaultTransport : ICompetitionTransport
    {
        internal CompetitionHttpResult Result;
        public void Dispose() { }
        public Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret, string body, int timeoutMilliseconds, CancellationToken stop)
            => Task.FromResult(Result);
    }
    static async Task OriginalCases(string output)
    {
        string root = Path.Combine(output,"original"), local = Path.Combine(output,"original-user"), personal = Path.Combine(output,"original-timer");
        Directory.CreateDirectory(Path.Combine(root,"patches")); Directory.CreateDirectory(Path.Combine(root,"palmod"));
        File.WriteAllText(Path.Combine(root,"PAL.dll"),"fixture"); File.WriteAllText(Path.Combine(root,"PALOLD.dll"),"fixture");
        File.WriteAllText(Path.Combine(root,"config.ini"),"[Patch]\nDefaultPatch=pal98fix-classic-v5\n");
        File.WriteAllText(Path.Combine(root,"palmod/common-tools.v1.json"),"{\"schema\":\"PAL98.ToolLaunchSettings.v1\",\"competition_upload\":{\"schema\":\"PAL98.TimerOnlineSettings.v1\",\"xiaorou\":{\"server\":\"https://www.pallab.top\",\"enabled\":false,\"use_custom_competition\":true,\"custom_competition_id\":\"legacy\"}}}");
        var store = new CompetitionStorage(personal,()=>"challenge-fixture-device");
        var fake = new Fake {Player=new ChallengePlayer {bound=true,id=playerId,display_name="原版玩家"},Status="bound"};
        var reply = await new ChallengeService(root,store,fake,localAppData:local).Execute("{\"action\":\"refresh\"}",CancellationToken.None);
        Check(Wire.Boolean(reply,"ok"),"original challenge refresh accepts retired disabled/custom-competition settings: " + Wire.String(reply,"detail"));
        string cache = Path.Combine(root,Wire.CacheFile);
        Check(File.Exists(cache) && Pal98.LocalDisplay.LocalPlayerIdentity.Load(root).Value.display_name=="原版玩家", "original timer acquires identity without launching gameplay");
        string before=File.ReadAllText(cache);
        var fault = new FaultTransport {Result=new CompetitionHttpResult {Status=0,ReceivedResponse=true}};
        reply=await new ChallengeService(root,store,fault,localAppData:local).Execute("{\"action\":\"refresh\"}",CancellationToken.None);
        Check(!Wire.Boolean(reply,"ok") && Wire.String(reply,"code")=="http_0" && File.ReadAllText(cache)==before,"unreadable received response preserves cache and is not an offline outage");
        fault.Result=new CompetitionHttpResult {Status=409,ReceivedResponse=true,ErrorDetail="挑战版本已更新，请刷新目录。"};
        reply=await new ChallengeService(root,store,fault,localAppData:local).Execute("{\"action\":\"refresh\"}",CancellationToken.None);
        Check(Wire.String(reply,"detail").Contains("HTTP 409") && Wire.String(reply,"detail").Contains("挑战版本已更新"),"challenge rejection exposes HTTP status and bounded server reason");
        fault.Result=new CompetitionHttpResult {Status=0};
        reply=await new ChallengeService(root,store,fault,localAppData:local).Execute("{\"action\":\"refresh\"}",CancellationToken.None);
        Check(Wire.String(reply,"code")=="network_unavailable" && File.ReadAllText(cache)==before,"no-response outage preserves existing original cache");

        var user = new UserDataStore(root,local);
        Directory.CreateDirectory(user.ComponentDirectory("game"));
        user.WriteText("game","config.ini",File.ReadAllText(Path.Combine(root,"config.ini")),Path.Combine(root,"config.ini"));
        user.WriteText("game","palmod/common-tools.v1.json",File.ReadAllText(Path.Combine(root,"palmod/common-tools.v1.json")),Path.Combine(root,"palmod/common-tools.v1.json"));
        reply=await new ChallengeService(root,store,fake,localAppData:local).Execute("{\"action\":\"refresh\"}",CancellationToken.None);
        Check(Wire.Boolean(reply,"ok") && File.Exists(Path.Combine(user.ComponentDirectory("game"),Wire.CacheFile)),"original challenge cache follows per-install user-data fallback");
        Check(Pal98.LocalDisplay.LocalPlayerIdentity.Load(user.ComponentDirectory("game")).Value.display_name=="原版玩家", "fallback identity and challenge cache share the same effective game root");

        var read=typeof(CompetitionHttpTransport).GetMethod("ReadChallengeError",BindingFlags.NonPublic|BindingFlags.Static);
        using(var response=new HttpResponseMessage(HttpStatusCode.Conflict) {Content=new StringContent("{\"detail\":\"原因 fixture-secret fixture-hwid\\u0001\"}",Encoding.UTF8,"application/json")}) {
            string detail=await (Task<string>)read.Invoke(null,new object[] {response,"fixture-secret","fixture-hwid",CancellationToken.None});
            Check(detail.Contains("原因") && !detail.Contains("fixture-secret") && !detail.Contains("fixture-hwid") && !detail.Contains("\u0001"),"HTTP challenge detail strips current credentials, HWID and control characters");
        }
        using(var response=new HttpResponseMessage(HttpStatusCode.InternalServerError) {Content=new StringContent(new string('x',9000))}) {
            string detail=await (Task<string>)read.Invoke(null,new object[] {response,"x","y",CancellationToken.None});
            Check(detail.Contains("超过容量"),"oversized error body is bounded without displaying raw HTML or payload");
        }
        string input=Path.Combine(output,"startup-request.json"), result=Path.Combine(output,"startup-response.json");
        File.WriteAllText(input,"{\"action\":\"refresh\"}");
        string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Pal98Timer.exe");
        using(var child=Process.Start(new ProcessStartInfo(exe,"--challenge-request \""+root+"\" \""+input+"\" \""+result+"\"") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe)})) {
            if(!child.WaitForExit(10000)) {child.Kill();throw new Exception("Timer dependency failure did not exit without UI");}
            Check(child.ExitCode!=0 && File.Exists(result),"real timer CLI returns a structured failure without GUI when dependencies are missing");
        }
        var failure=Wire.Object(File.ReadAllText(result));
        Check(!Wire.Boolean(failure,"ok") && Wire.String(failure,"code")=="dependency_missing", "CLI dependency failure remains readable without the online module");
    }
}
