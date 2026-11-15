// Refs othercat/PalTimer#67; othercat/sdlpal-lostedit#313.
using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using Pal98.Challenges;

namespace Pal98Timer
{
    internal sealed class SdlpalWealthState
    {
        public int schema { get; set; }
        public bool enabled { get; set; }
        public string opponent { get; set; }
        public long total { get; set; }
        public bool ready { get; set; }
        public long? peerTotal { get; set; }
    }
    internal sealed class WealthObservation
    {
        public int schema_version { get; set; } = 1;
        public string opponent { get; set; }
        public long total { get; set; }
    }
    internal sealed class WealthReply
    {
        public int schema_version { get; set; }
        public string status { get; set; }
        public string opponent { get; set; }
        public long? total { get; set; }
        public int max_age_ms { get; set; }
    }
    internal sealed class WealthLocalReport
    {
        public int pid { get; set; }
        public ulong created { get; set; }
        public ulong epoch { get; set; }
        public ulong published { get; set; }
        public string opponent { get; set; }
        public long total { get; set; }
        internal bool Fresh => pid > 0 && created > 0 && epoch > 0 && published <= SdlpalBridge.GetTickCount64() &&
            SdlpalBridge.GetTickCount64() - published < 2000 && EffectiveChallengeConfiguration.ValidWealthOpponent(opponent,true);
        internal WealthObservation Observation => new WealthObservation {opponent=opponent,total=total};
        internal static WealthLocalReport Capture(SdlpalSnapshot s) => s != null && !s.diagnostic && s.wealthBalance?.schema == 1 && s.wealthBalance.enabled ?
            new WealthLocalReport {pid=s.Pid,created=s.Created,epoch=s.epoch,published=s.Published,opponent=s.wealthBalance.opponent,total=s.wealthBalance.total} : null;
    }
    internal static class WealthChannel
    {
        internal const string Feature="wealth-balance.v1";
        internal static bool ValidReply(WealthLocalReport local, WealthReply reply, long roundTripMs) => local != null && local.Fresh && reply?.schema_version == 1 &&
            reply.status=="ready" && reply.opponent==local.opponent && reply.total.HasValue && reply.max_age_ms>roundTripMs && reply.max_age_ms<=20000 && roundTripMs>=0;
        internal static void Publish(WealthLocalReport local, WealthReply reply, long roundTripMs)
        {
            if(local==null) return;
            try {
                using(var process=Process.GetProcessById(local.pid))
                using(var snapshot=MemoryMappedFile.OpenExisting("Local\\SDLPAL.LostEdit.v1."+local.pid,MemoryMappedFileRights.Read))
                using(var source=snapshot.CreateViewAccessor(0,64,MemoryMappedFileAccess.Read))
                using(var gate=new Mutex(false,"Local\\SDLPAL.LostEdit.WealthLock.v1."+local.pid)) {
                    if(process.HasExited || (ulong)process.StartTime.ToUniversalTime().ToFileTimeUtc()!=local.created || source.ReadUInt64(32)!=local.created || source.ReadUInt64(40)!=local.epoch) return;
                    bool held=false;
                    try {
                        try {held=gate.WaitOne(0);} catch(AbandonedMutexException) {held=true;}
                        if(!held) return;
                        using(var map=MemoryMappedFile.OpenExisting("Local\\SDLPAL.LostEdit.Wealth.v1."+local.pid,MemoryMappedFileRights.ReadWrite))
                        using(var view=map.CreateViewAccessor(0,512,MemoryMappedFileAccess.ReadWrite)) {
                            byte[] magic=new byte[8];view.ReadArray(0,magic,0,8);
                            if(Encoding.ASCII.GetString(magic)!="SDLWLT1\0" || view.ReadUInt32(8)!=1 || view.ReadUInt32(12)!=512 || view.ReadInt32(16)!=local.pid || view.ReadUInt64(24)!=local.created) return;
                            bool ready=ValidReply(local,reply,roundTripMs);
                            uint sequence=(view.ReadUInt32(32)+2)&~1u;
                            view.Write(32,sequence-1);Thread.MemoryBarrier();
                            view.Write(36,ready?1u:0u);view.Write(40,local.epoch);
                            view.Write(48,ready?SdlpalBridge.GetTickCount64()+(ulong)(reply.max_age_ms-roundTripMs):0);
                            view.Write(56,ready?reply.total.Value:0);
                            byte[] name=new byte[324];byte[] raw=Encoding.UTF8.GetBytes(local.opponent??"");
                            if(raw.Length<name.Length) Array.Copy(raw,name,raw.Length);
                            view.WriteArray(64,name,0,name.Length);Thread.MemoryBarrier();view.Write(32,sequence);
                        }
                    } finally {if(held) gate.ReleaseMutex();}
                }
            } catch(Exception error) when(error is System.IO.IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception) { }
        }
    }
}
