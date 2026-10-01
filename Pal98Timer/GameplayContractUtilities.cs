using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    internal static class CompetitionProtocol
    {
        internal const long MaxMilliseconds = 86400000;
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 32 }; }
        internal static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(new UTF8Encoding(false).GetBytes(text)));
        }
        internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        internal static bool Digest(string value) { return value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$"); }
        internal static bool Identifier(string value) { return value != null && Regex.IsMatch(value, @"^[^\s\x00-\x1f\x7f]{1,80}$"); }
        internal static string CheckpointId(string name)
        {
            // Keep the ordinary PAL98 names byte-for-byte. Unusual names have an
            // explicit injective namespace; display nicknames never enter identity.
            if (Identifier(name) && !name.StartsWith("id-sha256-", StringComparison.Ordinal)) return name;
            return "id-sha256-" + Hash(name ?? "");
        }
        internal static string RouteHash(IEnumerable<string> ids) { return Hash("PAL98.TimerRoute.v1\n" + string.Join("\n", ids) + "\n"); }
    }
    internal sealed class CompetitionSplit
    {
        public string checkpoint_id { get; set; }
        public long? elapsed_ms { get; set; }
        public string status { get; set; }
    }
    internal sealed class CompetitionObservation
    {
        internal string Token, Core, DllHash, GameVersion, ValidationError, TimelineId, GameTitle;
        internal GameplayIdentity Gameplay;
        internal RankingConfiguration Ranking;
        internal CompetitionHardcore Hardcore;
        internal int Step, FadeMilliseconds, MapSpeedTicks;
        internal long TotalMilliseconds;
        internal DateTimeOffset ObservedAt;
        internal bool Finished, BeganHere;
        internal CompetitionSplit[] Splits;
    }
}
