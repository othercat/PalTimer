using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pal98Timer
{
    // API 1 is defined by the frozen host. It exposes values, never TimerCore,
    // checkpoints, Process handles or mutable game memory objects. Additive
    // extensions use Extensions; incompatible changes require a new API.
    public interface IPalTimerOnlineV1 : IDisposable
    {
        int ApiVersion { get; }
        bool Enabled { get; }
        string LiveCaption { get; }
        void ObserveTarget(int processId, long generation);
        void Publish(OnlineSnapshotV1 snapshot);
        void PublishClock(string token, long elapsed, bool running, long observedTick);
        void Invalidate(string token);
        void ShowSettings(IWin32Window owner);
        void UiTick();
        Task<bool> CloseAsync();
    }

    public sealed class OnlineGameplayV1
    {
        public string Schema { get; }
        public string RulesHash { get; }
        public string ContentId { get; }
        public string ContentHash { get; }
        public string Family { get; }
        public int Fade { get; }
        public int Speed { get; }
        public OnlineGameplayV1(string schema, string rules, string content, string hash, string family, int fade, int speed)
        { Schema = schema; RulesHash = rules; ContentId = content; ContentHash = hash; Family = family; Fade = fade; Speed = speed; }
    }
    public sealed class OnlineRankingV1
    {
        public string Schema { get; }
        public string Id { get; }
        public bool Covered { get; }
        public IReadOnlyDictionary<string, string> Rules { get; }
        public OnlineRankingV1(string schema, string id, bool covered, IDictionary<string, string> rules)
        {
            Schema = schema; Id = id; Covered = covered;
            Rules = rules == null ? null : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(rules, StringComparer.Ordinal));
        }
    }
    public sealed class OnlineHardcoreV1
    {
        public bool Requested { get; }
        public bool Verified { get; }
        public int RulesVersion { get; }
        public string Status { get; }
        public OnlineHardcoreV1(bool requested, bool verified, int rules, string status)
        { Requested = requested; Verified = verified; RulesVersion = rules; Status = status; }
    }
    public sealed class OnlineSplitV1
    {
        public string Id { get; }
        public long? Elapsed { get; }
        public string Status { get; }
        public OnlineSplitV1(string id, long? elapsed, string status) { Id = id; Elapsed = elapsed; Status = status; }
    }
    public sealed class OnlineSnapshotV1
    {
        public string Token { get; }
        public string Core { get; }
        public string DllHash { get; }
        public string GameVersion { get; }
        public string ValidationError { get; }
        public string TimelineId { get; }
        public string GameTitle { get; }
        public OnlineGameplayV1 Gameplay { get; }
        public OnlineRankingV1 Ranking { get; }
        public OnlineHardcoreV1 Hardcore { get; }
        public int Step { get; }
        public int Fade { get; }
        public int Speed { get; }
        public long Total { get; }
        public DateTimeOffset ObservedAt { get; }
        public bool Finished { get; }
        public bool BeganHere { get; }
        public IReadOnlyList<OnlineSplitV1> Splits { get; }
        public IReadOnlyDictionary<string, string> Extensions { get; }
        public OnlineSnapshotV1(string token, string core, string dllHash, string gameVersion, string error,
            string timeline, string title, OnlineGameplayV1 gameplay, OnlineRankingV1 ranking, OnlineHardcoreV1 hardcore,
            int step, int fade, int speed, long total, DateTimeOffset observedAt, bool finished, bool beganHere,
            IEnumerable<OnlineSplitV1> splits, IDictionary<string, string> extensions = null)
        {
            Token = token; Core = core; DllHash = dllHash; GameVersion = gameVersion; ValidationError = error;
            TimelineId = timeline; GameTitle = title; Gameplay = gameplay; Ranking = ranking; Hardcore = hardcore;
            Step = step; Fade = fade; Speed = speed; Total = total; ObservedAt = observedAt; Finished = finished; BeganHere = beganHere;
            Splits = Array.AsReadOnly(splits.ToArray());
            Extensions = new ReadOnlyDictionary<string, string>(extensions == null ? new Dictionary<string, string>() : new Dictionary<string, string>(extensions));
        }
    }
}
