using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pal98.Storage;
using Wire = Pal98.Challenges.ChallengeRelayContract;
using Runtime = Pal98.Challenges.OriginalChallengeRuntimeContract;

namespace Pal98Timer
{
    // Read on GameplayModeReader's worker, against the actual native snapshot.
    // The challenge is an online grouping scope; it cannot certify timing or alter PB files.
    internal sealed class OriginalChallengeIdentity
    {
        internal string Id, Title, Content, RulesHash;
        internal int Revision;
        internal bool Nostalgia;
        internal static string ReadFile(string root, string path, int limit)
        {
            string resolved=UserDataStore.ReadGamePath(root,path);
            using(var stream=new FileStream(resolved,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if(stream.Length>limit) throw new InvalidDataException("挑战文件超过容量。");
                using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true)) return reader.ReadToEnd();
            }
        }
        internal static OriginalChallengeIdentity Read(string root, GameplaySnapshot native)
        {
            if(!File.Exists(UserDataStore.ReadGamePath(root,"palmod/challenge-selection.v1.json"))) return null;
            var selection=Wire.Object(ReadFile(root,"palmod/challenge-selection.v1.json",1048576));
            if(Wire.Integer(selection,"schema")!=1) throw new InvalidDataException("挑战选择版本无效。");
            if(!Wire.Boolean(selection,"active")) return null;
            var activation=Wire.Map(Wire.Field(selection,"activation")); string content=Wire.String(activation,"content_id");
            Wire.Activation(activation,Wire.String(activation,"device_id"),content);
            var item=Wire.Map(Wire.Field(activation,"challenge")); var rules=Wire.Map(Wire.Field(item,"rules"));
            string ini=Wire.String(activation,"ini");
            if(ini!=ReadFile(root,"challenge-rules.ini",1048576)) throw new InvalidDataException("下载挑战规则已改变。");
            var config=ReadIni(ReadFile(root,"config.ini",1048576));
            var expected=Runtime.Content(content);
            if(Get(config,"Patch.DefaultPatch")!=Wire.String(expected,"package") || native==null || !native.covered || native.ranking?.covered!=true ||
                native.content_id!=Wire.String(expected,"content_id")) throw new InvalidDataException("当前运行内容与挑战不一致或尚未核验。");
            bool nostalgia=Get(config,"Hardcore.Enabled")=="1";
            if(!Runtime.Matches(content,Runtime.Values(ini),native.ranking.rules,nostalgia)) throw new InvalidDataException("当前实际玩法与下载挑战不一致。");
            string title=Wire.String(item,"title");
            // The dropdown decoration is presentation-only; never send it as a challenge name.
            if(title.StartsWith("在线：",StringComparison.Ordinal) || title.StartsWith("在线:",StringComparison.Ordinal)) title=title.Substring(3).Trim();
            return new OriginalChallengeIdentity {Id=Wire.String(item,"id"),Revision=Wire.Integer(item,"revision"),Title=title,Content=content,
                RulesHash=Wire.String(rules,"sha256"),Nostalgia=nostalgia};
        }
        private static Dictionary<string,string> ReadIni(string text)
        {
            var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); string section="";
            foreach(string raw in text.Replace("\r","").Split('\n')) {
                string line=raw.Trim(); if(line.Length==0 || line[0]==';' || line[0]=='#') continue;
                if(line.StartsWith("[",StringComparison.Ordinal) && line.EndsWith("]",StringComparison.Ordinal)) {section=line.Substring(1,line.Length-2);continue;}
                int equal=line.IndexOf('='); if(equal>0) result[section+"."+line.Substring(0,equal).Trim()]=line.Substring(equal+1).Trim();
            }
            return result;
        }
        private static string Get(Dictionary<string,string> values,string key) => values.TryGetValue(key,out string value) ? value : "";
        internal RankingConfiguration Decorate(RankingConfiguration ranking)
        {
            if(ranking?.covered!=true) return ranking;
            var copy=ranking.Copy();
            copy.rules["challenge.id"]=Id; copy.rules["challenge.revision"]=Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            copy.rules["challenge.content_id"]=Content; copy.rules["challenge.rules_sha256"]=RulesHash;
            copy.configuration_id=RankingConfiguration.Digest(copy.rules); return copy;
        }
        internal static void Apply(GameplaySnapshot snapshot,CompetitionObservation observation)
        {
            if(!string.IsNullOrEmpty(snapshot?.ChallengeError)) {observation.ValidationError=snapshot.ChallengeError;return;}
            var challenge=snapshot?.Challenge;
            if(challenge==null) return;
            if(observation.Hardcore?.requested!=challenge.Nostalgia) {observation.ValidationError="挑战怀旧状态与运行证据不一致。";return;}
            observation.Ranking=challenge.Decorate(observation.Ranking);
        }
    }
}
