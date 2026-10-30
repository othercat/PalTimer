using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pal98.LocalDisplay;

namespace Pal98Timer
{
    // A bounded display program, not C#, SSS or a general-purpose interpreter.
    internal static class IdentityBehaviorSnapshot
    {
        internal const string Protocol = "PAL98.IdentityBehaviors.v1", Feature = "identity-behaviors.v1";
        internal const string RelativePath = "palmod/player-behavior.v1.json";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static Dictionary<string, object> Object(object value, params string[] keys)
        {
            var fields = value as Dictionary<string, object>;
            if (fields == null || fields.Count != keys.Length || keys.Any(k => !fields.ContainsKey(k)))
                throw new InvalidDataException("Invalid behavior object.");
            return fields;
        }
        static int Number(object value, int maximum)
        {
            if (!(value is int) || (int)value < 0 || (int)value > maximum) throw new InvalidDataException("Invalid behavior number.");
            return (int)value;
        }
        static IList Array(object value, int maximum)
        {
            var list = value as IList;
            if (list == null || list.Count > maximum) throw new InvalidDataException("Invalid behavior list.");
            return list;
        }
        static void Script(object value)
        {
            var script = Object(value, "schema", "steps");
            if (Number(script["schema"], 1) != 1) throw new InvalidDataException("Unknown display script.");
            foreach (object raw in Array(script["steps"], 16)) {
                var step = raw as Dictionary<string, object>; object op;
                if (step == null || !step.TryGetValue("op", out op)) throw new InvalidDataException("Invalid display step.");
                if (Equals(op, "portrait")) {
                    Object(raw, "op", "asset", "x", "y");
                    if (!Equals(step["asset"], "mafan")) throw new InvalidDataException("Unknown portrait.");
                    Number(step["x"], 229); Number(step["y"], 132);
                } else if (Equals(op, "text")) {
                    Object(raw, "op", "text", "x", "y", "color");
                    int x = Number(step["x"], 311); Number(step["y"], 183); Number(step["color"], 255);
                    string text = step["text"] as string;
                    if (String.IsNullOrEmpty(text) || text.Length > 39 || text.Any(c => Char.IsControl(c) || Char.IsSurrogate(c)) ||
                        x + text.Sum(c => c < 128 ? 8 : 16) >= 320) throw new InvalidDataException("Invalid display text.");
                } else throw new InvalidDataException("Unknown display operation.");
            }
        }
        internal static byte[] Build(Dictionary<string, object> reply, PlayerIdentity identity, string serverPlayerId, bool required)
        {
            object raw; int revision = 0; IList bindings = new object[0];
            if (reply.TryGetValue("identity_behaviors", out raw)) {
                var bundle = Object(raw, "protocol", "revision", "player_id", "bindings");
                if (!Equals(bundle["protocol"], Protocol) || !Equals(bundle["player_id"], serverPlayerId))
                    throw new InvalidDataException("Behavior identity mismatch.");
                revision = Number(bundle["revision"], Int32.MaxValue);
                bindings = Array(bundle["bindings"], 1);
                foreach (object binding in bindings) {
                    var fields = Object(binding, "hook", "script");
                    if (!Equals(fields["hook"], "geshi.death-taunt")) throw new InvalidDataException("Unknown identity hook.");
                    Script(fields["script"]);
                }
                if (serverPlayerId == null && bindings.Count != 0) throw new InvalidDataException("Unbound identity has a behavior.");
            } else if (required) throw new InvalidDataException("Missing identity behavior capability.");
            var snapshot = new Dictionary<string, object> {
                {"schema", 1}, {"protocol", Protocol}, {"server_origin", identity.server_origin},
                {"identity_revision", identity.revision}, {"revision", revision}, {"player_id", identity.player_id},
                {"bindings", identity.bound ? bindings : new object[0]}
            };
            byte[] bytes = Utf8.GetBytes(CompetitionProtocol.Json().Serialize(snapshot));
            if (bytes.Length > 16384) throw new InvalidDataException("Behavior snapshot too large.");
            return bytes;
        }
    }
}
