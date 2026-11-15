// Frozen EXE -> public API 1 -> built Online DLL. No client, UI or network.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Pal98Timer;

static class SdlpalOnlineContractHost
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4194304 };
    static int checks;
    static void Check(bool yes, string why) { if (!yes) throw new Exception(why); ++checks; }
    static object Get(object value, string name) => value.GetType().GetProperty(name, All)?.GetValue(value) ?? value.GetType().GetField(name, All)?.GetValue(value);
    static void Set(object value, string name, object item) {
        var property = value.GetType().GetProperty(name, All);
        if (property != null) property.SetValue(value, item); else value.GetType().GetField(name, All).SetValue(value, item);
    }
    static object Call(Type type, object value, string name, params object[] args) => type.GetMethod(name, All).Invoke(value, args);
    static object Decode(Type type, string json) => Json.GetType().GetMethods().Single(m => m.Name == "Deserialize" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1).MakeGenericMethod(type).Invoke(Json, new object[] {json});
    static object Parse(Type type, object value) => Decode(type, Json.Serialize(value));
    static string Hash(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    static int Main(string[] args)
    {
        try {
            bool projected = args[4] == "projected";
            var host = typeof(OnlineSnapshotV1).Assembly;
            Check(HashBytes(File.ReadAllBytes(host.Location)) == HashBytes(File.ReadAllBytes(Path.Combine(args[0], "Pal98Timer.exe"))), "Probe uses the exact frozen host bytes");
            var online = Assembly.LoadFrom(args[1]);
            var snapshotType = host.GetType("Pal98Timer.SdlpalSnapshot", true);
            var rankingType = host.GetType("Pal98Timer.RankingConfiguration", true);
            var observationType = host.GetType("Pal98Timer.CompetitionObservation", true);
            var configType = host.GetType("Pal98Timer.SdlpalChallengeConfiguration", true);
            var routeType = host.GetType("Pal98Timer.CompetitionProtocol", true);
            var timelineType = host.GetType("Pal98Timer.TimelineIdentity", true);
            var create = host.GetType("Pal98Timer.GForm", true).GetMethod("CreateCompetitionSnapshot", All);
            var read = online.GetType("Pal98Timer.OnlineModule", true).GetMethod("ReadSnapshot", All);
            var cases = new List<object>();
            var roots = Directory.Exists(Path.Combine(args[2], "native-01")) ? new[] {Path.Combine(args[2], "native-01"),Path.Combine(args[2], "native-02")} : new[] {args[2]};
            var dirs = roots.Where(Directory.Exists).SelectMany(Directory.GetDirectories).Where(p => File.Exists(Path.Combine(p, "default/verification-gameplay-snapshot.json")))
                .GroupBy(Path.GetFileName, StringComparer.Ordinal).Select(g => g.First()).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).ToArray();
            Check(dirs.Length == 21, "Exactly 21 actual native content fixtures are required");
            foreach (string directory in dirs) {
                string content = Path.GetFileName(directory), source = Path.Combine(directory, "default/verification-gameplay-snapshot.json");
                string native = File.ReadAllText(source);
                var config = Activator.CreateInstance(configType, true);
                Call(configType, config, "ReadFiles", Path.Combine(directory, "default"), 7001, (ulong)DateTime.UtcNow.AddMinutes(1).ToFileTimeUtc(), content + "-v1");
                Check(Get(config, "Error") == null, "Read actual complete INI: " + content);
                var full = (Dictionary<string, string>)Get(config, "Facts");
                Check(full.Keys.Count(k => k.StartsWith("config.slot.", StringComparison.Ordinal)) == 83, "All 41 slots, priorities and hero: " + content);
                for (int mode = -1; mode < 8; ++mode) {
                    var state = Decode(snapshotType, native);
                    if (mode >= 0) {
                        Set(state, "wuqiang", true);
                        Set(state, "wuqiangPlus", (mode & 1) != 0);
                        Set(state, "prebattleSlotResidue", (mode & 2) != 0);
                        Set(state, "challengeConfigSha256", (mode & 4) != 0 ? Hash("offline runtime rule fixture:" + content) : null);
                    }
                    var raw = (Dictionary<string, string>)Call(snapshotType, state, "RankingFacts", false);
                    foreach (var pair in full) raw.Add(pair.Key, pair.Value);
                    if (mode >= 4) {
                        raw["config.slot.SwordFlight"] = "1";
                        raw["config.slot.PrioritySwordFlight"] = "77";
                    }
                    string id = (string)Call(rankingType, null, "Digest", raw);
                    var splits = new[] { new OnlineSplitV1("offline-contract-node", null, "in_progress") };
                    string route = (string)Call(routeType, null, "RouteHash", (object)new[] { "offline-contract-node" });
                    string timeline = (string)Call(timelineType, null, "Create", id, route, false);
                    object observation = Activator.CreateInstance(observationType, true);
                    var gameplay = new { schema = "PAL98.GameplayIdentity.v1", family = raw["family"], content_id = content + "-v1",
                        content_sha256 = raw["content"], rules_sha256 = id, fade_ms = Get(state, "fadeCycleMs"), map_speed_ticks = 0,
                        walk_frame_ms = Get(state, "walkFrameMs"), speed = Get(state, "speed"), battle_speed = Get(state, "battleSpeed") };
                    Set(observation, "Gameplay", Parse(observationType.GetField("Gameplay", All).FieldType, gameplay));
                    Set(observation, "Ranking", Parse(rankingType, new { schema = "PAL98.RankingConfiguration.v1", covered = true, configuration_id = id, rules = raw }));
                    Set(observation, "Hardcore", Activator.CreateInstance(observationType.GetField("Hardcore", All).FieldType, true));
                    Set(observation, "Token", "offline-contract-" + content); Set(observation, "Core", "SDLPAL_GESHI_EASY");
                    Set(observation, "DllHash", Get(state, "exeHash")); Set(observation, "GameVersion", "1.0.0.0");
                    Set(observation, "TimelineId", timeline); Set(observation, "GameTitle", "OFFLINE CONTRACT FIXTURE");
                    Set(observation, "FadeMilliseconds", Get(state, "fadeCycleMs")); Set(observation, "MapSpeedTicks", 0);
                    Set(observation, "ObservedAt", DateTimeOffset.UtcNow); Set(observation, "Step", -1);
                    Set(observation, "ValidationError", "OFFLINE CONTRACT FIXTURE; NOT A RUN"); Set(observation, "LiveTimingValid", false);
                    Set(observation, "Splits", Parse(observationType.GetField("Splits", All).FieldType, new[] {new {checkpoint_id="offline-contract-node",elapsed_ms=(long?)null,status="in_progress"}}));
                    var dto = (OnlineSnapshotV1)create.Invoke(null, new[] { observation });
                    if (projected && content == "pal98-classic-v6" && mode == 3)
                        RejectedSources(dto, read, rankingType, routeType, timelineType);
                    string originalDto = Json.Serialize(dto), originalObservation = Json.Serialize(observation);
                    object result = read.Invoke(null, new object[] {dto});
                    var resultRanking = Get(result, "Ranking"); var facts = (Dictionary<string, string>)Get(resultRanking, "rules");
                    string newId = (string)Get(resultRanking, "configuration_id");
                    Check(Json.Serialize(dto) == originalDto && Json.Serialize(observation) == originalObservation, "Frozen host values remain unchanged");
                    Check((bool)Call(resultRanking.GetType(), resultRanking, "Valid", Get(result, "Gameplay")), "Decoded Online ranking is valid");
                    var extra = new[] { "wuqiang_plus", "prebattle_slot_residue", "challenge_config_sha256" };
                    bool changed = projected && extra.Any(raw.ContainsKey);
                    foreach (var pair in raw.Where(p => !changed || !extra.Contains(p.Key)))
                        Check(facts.TryGetValue(pair.Key, out string value) && value == pair.Value, "Every source fact and rule survives");
                    if (changed) {
                        Check(!extra.Any(facts.ContainsKey), "No unsupported top-level extensions");
                        Check(newId != id && Get(Get(result, "Gameplay"), "rules_sha256").Equals(newId), "Gameplay and ranking share recomputed identity");
                        Check(Get(result, "TimelineId").Equals(Call(timelineType, null, "Create", newId, route, false)), "Timeline uses the same online identity");
                        foreach (var pair in new[] {new {old="wuqiang_plus",next="config.slot.RuntimeWuQiangPlus"},new {old="prebattle_slot_residue",next="config.slot.RuntimePrebattleSlotResidue"}})
                            Check(raw.ContainsKey(pair.old) ? facts[pair.next] == raw[pair.old] : !facts.ContainsKey(pair.next), "Mode value remains exact");
                        if (raw.ContainsKey("challenge_config_sha256")) Check(facts["config.slot.RuntimeChallengeDigest"].StartsWith("sha256-"), "Full digest has a typed lossless encoding");
                    } else Check(newId == id && facts.Count == raw.Count, "Compatible identity remains byte-exact");
                    cases.Add(new {label=content+"/"+(mode<0?"actual-default":"modes-"+mode), content, mode, nativeSource=source,
                        nativeSha256=Hash(native), rawRanking=Get(observation,"Ranking"), ranking=resultRanking, gameplay=Get(result,"Gameplay"),
                        timeline=Get(result,"TimelineId"), route, originalHostUnchanged=true, configurationChanged=changed});
                }
            }
            Directory.CreateDirectory(args[3]);
            File.WriteAllText(Path.Combine(args[3], "contracts.json"), Json.Serialize(cases), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(args[3], "summary.json"), Json.Serialize(new {passed=true,checks,contents=dirs.Length,cases=cases.Count,projected,
                timerSha256=HashBytes(File.ReadAllBytes(Path.Combine(args[0],"Pal98Timer.exe"))),onlineSha256=HashBytes(File.ReadAllBytes(args[1])),network=false,gameplay=false}), new UTF8Encoding(false));
            Console.WriteLine("PASS " + checks + " API1 contract checks / " + cases.Count + " cases / " + dirs.Length + " contents; projected=" + projected);
            return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
    static string HashBytes(byte[] bytes) {using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();}
    static void RejectedSources(OnlineSnapshotV1 dto, MethodInfo read, Type rankingType, Type routeType, Type timelineType)
    {
        foreach(string fault in new[]{"bad-id","bad-game-id","bad-timeline","missing-hero","missing-evidence","missing-complete","invalid-mode","reserved-collision"}) {
            var facts=dto.Ranking.Rules.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
            if(fault=="missing-hero")facts.Remove("config.slot.Hero");
            if(fault=="missing-evidence")facts.Remove("config.evidence");
            if(fault=="missing-complete")foreach(string key in facts.Keys.Where(k=>k.StartsWith("config.",StringComparison.Ordinal)).ToArray())facts.Remove(key);
            if(fault=="invalid-mode")facts["wuqiang_plus"]="0";
            if(fault=="reserved-collision")facts["config.slot.RuntimeWuQiangPlus"]="0";
            string id=(string)Call(rankingType,null,"Digest",facts);
            if(fault=="bad-id")id=new string('0',64);
            string gameId=fault=="bad-game-id"?new string('0',64):id;
            string route=(string)Call(routeType,null,"RouteHash",(object)dto.Splits.Select(s=>s.Id).ToArray());
            string timeline=(string)Call(timelineType,null,"Create",id,route,false);
            if(fault=="bad-timeline")timeline=new string('0',64);
            var game=new OnlineGameplayV1(dto.Gameplay.Schema,gameId,dto.Gameplay.ContentId,dto.Gameplay.ContentHash,dto.Gameplay.Family,dto.Gameplay.Fade,dto.Gameplay.Speed);
            var changed=new OnlineSnapshotV1(dto.Token,dto.Core,dto.DllHash,dto.GameVersion,dto.ValidationError,timeline,dto.GameTitle,
                game,new OnlineRankingV1(dto.Ranking.Schema,id,true,facts),dto.Hardcore,dto.Step,dto.Fade,dto.Speed,dto.Total,dto.ObservedAt,dto.Finished,dto.BeganHere,
                dto.Splits,dto.Extensions.ToDictionary(p=>p.Key,p=>p.Value));
            object result=read.Invoke(null,new object[]{changed});var ranking=Get(result,"Ranking");
            var actual=(Dictionary<string,string>)Get(ranking,"rules");
            Check(actual.Count==facts.Count&&facts.All(p=>actual[p.Key]==p.Value),"Invalid source is never repaired or stripped: "+fault);
            Check(Get(ranking,"configuration_id").Equals(id)&&Get(Get(result,"Gameplay"),"rules_sha256").Equals(gameId)&&Get(result,"TimelineId").Equals(timeline),
                "Invalid original identity is not replaced with a valid one: "+fault);
        }
    }
}
