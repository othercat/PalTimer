using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace Pal98Timer
{
    // PALDLL_DX9/hardcore_control_contract.h. Separate from HardcoreMode.v1.
    internal sealed class HardcoreControlSnapshot
    {
        internal uint Pid, ProducerVersion, State, Error, HelperPid;
        internal long Creation, HelperCreation;
        internal ulong PauseSerial, PauseQpc, RequestQpc;
        internal string Ticket;
        internal bool Owns(HardcoreSnapshot s) => s != null && Pid == s.Pid && Creation == s.ProcessCreation && ProducerVersion == s.ProducerVersion;
        internal static HardcoreControlSnapshot Decode(byte[] b, uint pid, long creation)
        {
            if (!HardcoreControlReader.Header(b, 0x31544350, pid, creation) || BitConverter.ToUInt32(b,28) != 1 ||
                BitConverter.ToUInt32(b,64) > 3 || BitConverter.ToUInt32(b,76) != 0 || !HardcoreControlReader.Zero(b,96,256)) return null;
            var s = new HardcoreControlSnapshot { Pid=pid, Creation=creation, ProducerVersion=BitConverter.ToUInt32(b,12),
                PauseSerial=BitConverter.ToUInt64(b,32), PauseQpc=BitConverter.ToUInt64(b,40),
                Ticket=HardcoreControlReader.Ticket(b,48), State=BitConverter.ToUInt32(b,64), Error=BitConverter.ToUInt32(b,68),
                HelperPid=BitConverter.ToUInt32(b,72), HelperCreation=BitConverter.ToInt64(b,80), RequestQpc=BitConverter.ToUInt64(b,88) };
            if ((s.PauseSerial == 0) != (s.PauseQpc == 0)) return null;
            if (s.State == 0 ? s.Ticket != null || s.RequestQpc != 0 || s.HelperPid != 0 || s.HelperCreation != 0 || s.Error != 0 :
                s.Ticket == null || s.RequestQpc == 0 || (s.State == 2 && (s.HelperPid == 0 || s.HelperCreation <= 0)) ||
                (s.State == 3 ? s.Error == 0 : s.Error != 0)) return null;
            return s;
        }
    }
    internal sealed class HardcoreRestartSnapshot
    {
        internal uint Pid, ProducerVersion, State, HelperPid, Error, NewPid;
        internal long Creation, HelperCreation, NewCreation;
        internal ulong ChangedQpc;
        internal string Ticket;
        internal static HardcoreRestartSnapshot Decode(byte[] b, uint pid, long creation)
        {
            if (!HardcoreControlReader.Header(b,0x31524350,pid,creation) || BitConverter.ToUInt32(b,28)>3 ||
                BitConverter.ToUInt32(b,68)!=0 || !HardcoreControlReader.Zero(b,88,256)) return null;
            var s=new HardcoreRestartSnapshot { Pid=pid, Creation=creation, ProducerVersion=BitConverter.ToUInt32(b,12),
                State=BitConverter.ToUInt32(b,28), Ticket=HardcoreControlReader.Ticket(b,32), HelperPid=BitConverter.ToUInt32(b,48),
                Error=BitConverter.ToUInt32(b,52), HelperCreation=BitConverter.ToInt64(b,56), NewPid=BitConverter.ToUInt32(b,64),
                NewCreation=BitConverter.ToInt64(b,72), ChangedQpc=BitConverter.ToUInt64(b,80) };
            if(s.State==0 ? s.Ticket!=null || s.HelperPid!=0 || s.HelperCreation!=0 || s.NewPid!=0 || s.NewCreation!=0 || s.Error!=0 || s.ChangedQpc!=0 :
                s.Ticket==null || s.ChangedQpc==0 || (s.State==2 && (s.HelperPid==0 || s.HelperCreation<=0 || s.NewPid==0 || s.NewCreation<=0)) ||
                (s.State==3 ? s.Error==0 : s.Error!=0) || (s.State!=2 && (s.NewPid!=0 || s.NewCreation!=0))) return null;
            return s;
        }
        internal bool Matches(HardcoreControlSnapshot request) => request!=null && Pid==request.Pid && Creation==request.Creation &&
            ProducerVersion==request.ProducerVersion && Ticket==request.Ticket && HelperPid==request.HelperPid && HelperCreation==request.HelperCreation;
    }
    internal sealed class HardcoreControlReader : IDisposable
    {
        internal const uint ProducerVersion=0x01070200;
        internal const string ControlPrefix="Local\\PAL98.HardcoreControl.v1.", RestartPrefix="Local\\PAL98.HardcoreRestart.v1.";
        private MemoryMappedFile control, restart;
        private MemoryMappedViewAccessor controlView, restartView;
        private uint pid;
        private long creation;
        private ulong pauseSerial;
        internal bool ControlBusy { get; private set; }
        internal bool RestartBusy { get; private set; }
        internal bool Matches(HardcoreSnapshot snapshot) => snapshot!=null && pid==snapshot.Pid && creation==snapshot.ProcessCreation;
        internal bool Attached => controlView!=null && restartView!=null;
        internal bool Attach(HardcoreSnapshot snapshot, bool carryEvents=false)
        {
            if(snapshot==null || !snapshot.Requested || snapshot.ProducerVersion<ProducerVersion) return false;
            Dispose();
            try {
                control=MemoryMappedFile.OpenExisting(ControlPrefix+snapshot.Pid,MemoryMappedFileRights.Read);
                restart=MemoryMappedFile.OpenExisting(RestartPrefix+snapshot.Pid,MemoryMappedFileRights.Read);
                controlView=control.CreateViewAccessor(0,256,MemoryMappedFileAccess.Read);
                restartView=restart.CreateViewAccessor(0,256,MemoryMappedFileAccess.Read);
                pid=snapshot.Pid; creation=snapshot.ProcessCreation;
                var initial=ReadControl(); var initialRestart=ReadRestart();
                if(initial==null || initial.ProducerVersion!=snapshot.ProducerVersion || initialRestart==null ||
                    initialRestart.ProducerVersion!=snapshot.ProducerVersion) { Dispose(); return false; }
                pauseSerial=carryEvents?0:initial.PauseSerial;
                return true;
            } catch(Exception e) when(ReadFailure(e)) { Dispose(); return false; }
        }
        internal HardcoreControlSnapshot ReadControl()
        {
            var bytes=Read(controlView,out bool busy); ControlBusy=busy;
            return HardcoreControlSnapshot.Decode(bytes,pid,creation);
        }
        internal HardcoreRestartSnapshot ReadRestart()
        {
            var bytes=Read(restartView,out bool busy); RestartBusy=busy;
            return HardcoreRestartSnapshot.Decode(bytes,pid,creation);
        }
        internal HardcoreRestartSnapshot RefreshStoppedHelperResult(HardcoreRestartSnapshot snapshot,bool helperAlive)
        {
            // The helper can publish Launched and exit after the first read.
            return !helperAlive && snapshot!=null && snapshot.State==1 ? ReadRestart() : snapshot;
        }
        internal uint TakePauseEvents(HardcoreControlSnapshot current)
        {
            if(current==null || current.Pid!=pid || current.Creation!=creation) return 0;
            if(current.PauseSerial<pauseSerial) return 0; // never replay an older sequence
            ulong count=current.PauseSerial-pauseSerial; pauseSerial=current.PauseSerial;
            // A local reader should never accumulate an unbounded UI work queue.
            return count<=32 ? (uint)count : 0;
        }
        private static byte[] Read(MemoryMappedViewAccessor view,out bool busy)
        {
            busy=false;
            if(view==null) return null;
            try {
                for(int attempt=0;attempt<3;++attempt) {
                    uint before=view.ReadUInt32(24); if(before==0) return null;
                    if((before&1)!=0) { Thread.Yield(); continue; }
                    Thread.MemoryBarrier(); var b=new byte[256];
                    if(view.ReadArray(0,b,0,256)!=256) return null;
                    Thread.MemoryBarrier();
                    if(before==view.ReadUInt32(24) && before==BitConverter.ToUInt32(b,24)) return b;
                    Thread.Yield();
                }
                busy=true; // only seqlock contention, never a missing or invalid record
            } catch(Exception e) when(ReadFailure(e)) { }
            return null;
        }
        internal static bool Header(byte[] b,uint magic,uint pid,long creation) => b!=null && b.Length==256 && pid!=0 && creation>0 &&
            BitConverter.ToUInt32(b,0)==magic && BitConverter.ToUInt16(b,4)==1 && BitConverter.ToUInt16(b,6)==256 &&
            BitConverter.ToUInt32(b,8)==pid && BitConverter.ToInt64(b,16)==creation && BitConverter.ToUInt32(b,12)>=ProducerVersion &&
            BitConverter.ToUInt32(b,24)!=0 && (BitConverter.ToUInt32(b,24)&1)==0;
        internal static bool Zero(byte[] b,int start,int end) { for(int i=start;i<end;++i) if(b[i]!=0) return false; return true; }
        internal static string Ticket(byte[] b,int offset) {
            if(Zero(b,offset,offset+16)) return null;
            var ticket=new byte[16]; Buffer.BlockCopy(b,offset,ticket,0,16); return new Guid(ticket).ToString("N");
        }
        internal static bool Alive(uint id,long time)
        {
            try { using(var p=Process.GetProcessById(checked((int)id))) return !p.HasExited && p.StartTime.ToUniversalTime().ToFileTimeUtc()==time; }
            catch(Exception e) when(ReadFailure(e) || e is OverflowException) { return false; }
        }
        private static bool ReadFailure(Exception e) => e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
            e is InvalidOperationException || e is System.ComponentModel.Win32Exception;
        public void Dispose()
        {
            controlView?.Dispose(); restartView?.Dispose(); control?.Dispose(); restart?.Dispose();
            controlView=restartView=null; control=restart=null; pid=0; creation=0; pauseSerial=0;
            ControlBusy=RestartBusy=false;
        }
    }
}
