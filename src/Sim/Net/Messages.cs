using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim.Net
{
    public enum MsgType : byte
    {
        // client → server
        Hello = 1,
        Inputs = 2,
        Request = 3,
        // server → client
        Welcome = 16,
        Reject = 17,
        Snapshot = 18,
        Events = 19,
    }

    public enum RejectReason : byte
    {
        ProtocolMismatch = 1,
        ServerFull = 2,
        BadHello = 3,
        /// <summary>Join-in-progress is only allowed in the Lobby and during Prep (doc 06 §5).</summary>
        RunInProgress = 4,
    }

    public static class Protocol
    {
        /// <summary>
        /// One line per message layout. Any change to the wire format must change this text, which
        /// changes the protocol hash and makes old builds fail the connect check (doc 06 §2).
        /// </summary>
        private const string Schema =
            "m1.0;" +
            "Hello:u32 hash,str name;" +
            "Inputs:u8 n,{u32 seq,i8 mx,i8 mz,u16 yaw,u8 buttons,u8 held};" +
            "Request:u32 id,u8 kind,u32 container,u32 entry,u32 to,u8 x,u8 y,u8 rot,u16 count,u16 dest,u8 sort;" +
            "Kinds:open,close,move,quick,sort,deliver,start,ready,continue;" +
            "Welcome:u32 player,u32 tick,u32 seed,str map,u64 mapHash,u32 intake,global,u8 n,{u32 id,str name,u8 slot},state hotbar,state inventory;" +
            "Reject:u8 reason;" +
            "Snapshot:u32 tick,u32 ack,u8 flags,[own:f32 x,f32 y,f32 z,f32 vy,u8 grounded,f32 speed],u8 n,{u32 id,i32 xcm,i16 ycm,i32 zcm,u16 yaw,u8 held,u8 flags},[global];" +
            "Global:u8 shift,u8 phase,u32 end,i32 quota,i32 earnings,i32 wallet,u8 complaint,u16 backlog,u16 deliveries,u16 misdeliveries,u8 outcome,u32 host;" +
            "Events:u16 n,{u8 type,...};" +
            "Ev:joined,left,cstate,cdelta,cclosed,result,spawned,delivered,misdelivered,phase,summary,results;" +
            "Phase:u8 shift,u8 phase,u32 end,i32 quota,u8 outcome;" +
            "Summary:u8 shift,i32 earned,i32 quota,u8 met,i32 bonus,u8 n,{u16 deliveries},u16 misdeliveries,u16 late,u32 mvp,u16 mvpDeliveries;" +
            "Results:u8 outcome,u8 shifts,i32 earned,i32 score,i32 deliveries,i32 misdeliveries,u8 n,{u32 id,u16 deliveries,u16 misdeliveries,i32 earned,i32 xp};" +
            "Entry:u32 id,u8 x,u8 y,u8 w,u8 h,u8 rot,u8 kind,u16 def,(mail:u32 addr,u8 unit,u16 n,{u32 id,u16 value,u8 spawn,u8 deadline,u8 flags}|item:i32 count)";

        /// <summary>32-bit hash of the message schema plus the content data hash.</summary>
        public static uint Hash(ContentDb content)
        {
            var h = Fnv32.Create();
            h.Add(Schema);
            h.Add(content.ContentHash);
            return h.Value;
        }
    }

    public sealed class HelloMsg
    {
        public uint ProtocolHash;
        public string Name = "";

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Hello);
            w.U32(ProtocolHash);
            w.Str(Name);
        }

        public static HelloMsg Read(NetReader r) => new HelloMsg { ProtocolHash = r.U32(), Name = r.Str() };
    }

    /// <summary>The last few inputs, oldest first (doc 06 §3: each packet repeats the last 3 for loss cover).</summary>
    public sealed class InputsMsg
    {
        public const int MaxInputs = 3;
        public readonly List<PlayerInput> Inputs = new List<PlayerInput>(MaxInputs);

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Inputs);
            w.U8((byte)Inputs.Count);
            foreach (var i in Inputs)
            {
                w.U32(i.Seq); w.I8(i.MoveX); w.I8(i.MoveZ); w.U16(i.Yaw); w.U8((byte)i.Buttons); w.U8(i.HeldSlot);
            }
        }

        public static InputsMsg Read(NetReader r)
        {
            var m = new InputsMsg();
            int n = r.U8();
            if (n > MaxInputs) throw new NetFormatException("too many inputs in one packet");
            for (int i = 0; i < n; i++)
            {
                m.Inputs.Add(new PlayerInput
                {
                    Seq = r.U32(), MoveX = r.I8(), MoveZ = r.I8(), Yaw = r.U16(),
                    Buttons = (InputButtons)r.U8(), HeldSlot = r.U8(),
                });
            }
            return m;
        }
    }

    public static class RequestMsg
    {
        public static void Write(NetWriter w, in SimRequest q)
        {
            w.U8((byte)MsgType.Request);
            w.U32(q.RequestId); w.U8((byte)q.Kind); w.U32(q.Container); w.U32(q.EntryId); w.U32(q.ToContainer);
            w.U8(q.X); w.U8(q.Y); w.Bool(q.Rotated); w.U16(q.Count); w.U16(q.DestId); w.U8((byte)q.SortKey);
        }

        public static SimRequest Read(NetReader r) => new SimRequest
        {
            RequestId = r.U32(), Kind = (RequestKind)r.U8(), Container = r.U32(), EntryId = r.U32(), ToContainer = r.U32(),
            X = r.U8(), Y = r.U8(), Rotated = r.Bool(), Count = r.U16(), DestId = r.U16(), SortKey = (SortKey)r.U8(),
        };
    }

    public struct PlayerInfo
    {
        public EntityId Id;
        public string Name;
        public byte Slot;
    }

    /// <summary>Join state (doc 06 §5 step 4), small enough in M0 to be one message.</summary>
    public sealed class WelcomeMsg
    {
        public EntityId PlayerId;
        public uint Tick;
        public uint Seed;
        public string MapId = "";
        public ulong MapHash;
        public uint IntakeContainerId;
        public GlobalState Global;
        public readonly List<PlayerInfo> Players = new List<PlayerInfo>();
        public ContainerState Hotbar = null!;
        public ContainerState Inventory = null!;

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Welcome);
            w.U32(PlayerId.Value); w.U32(Tick); w.U32(Seed); w.Str(MapId); w.U64(MapHash); w.U32(IntakeContainerId);
            GlobalCodec.Write(w, Global);
            w.U8((byte)Players.Count);
            foreach (var p in Players) { w.U32(p.Id.Value); w.Str(p.Name); w.U8(p.Slot); }
            ContainerCodec.WriteState(w, Hotbar);
            ContainerCodec.WriteState(w, Inventory);
        }

        public static WelcomeMsg Read(NetReader r)
        {
            var m = new WelcomeMsg
            {
                PlayerId = new EntityId(r.U32()), Tick = r.U32(), Seed = r.U32(), MapId = r.Str(), MapHash = r.U64(),
                IntakeContainerId = r.U32(), Global = GlobalCodec.Read(r),
            };
            int n = r.U8();
            for (int i = 0; i < n; i++)
                m.Players.Add(new PlayerInfo { Id = new EntityId(r.U32()), Name = r.Str(), Slot = r.U8() });
            m.Hotbar = ContainerCodec.ReadState(r);
            m.Inventory = ContainerCodec.ReadState(r);
            return m;
        }
    }

    public static class RejectMsg
    {
        public static void Write(NetWriter w, RejectReason reason)
        {
            w.U8((byte)MsgType.Reject);
            w.U8((byte)reason);
        }

        public static RejectReason Read(NetReader r) => (RejectReason)r.U8();
    }

    public static class GlobalCodec
    {
        public static void Write(NetWriter w, in GlobalState g)
        {
            w.U8(g.Shift); w.U8((byte)g.Phase); w.U32(g.PhaseEndTick); w.I32(g.Quota); w.I32(g.ShiftEarnings);
            w.I32(g.Wallet); w.U8(g.Complaint); w.U16(g.Backlog); w.U16(g.Deliveries); w.U16(g.Misdeliveries);
            w.U8((byte)g.Outcome); w.U32(g.Host.Value);
        }

        public static GlobalState Read(NetReader r) => new GlobalState
        {
            Shift = r.U8(), Phase = (RunPhase)r.U8(), PhaseEndTick = r.U32(), Quota = r.I32(), ShiftEarnings = r.I32(),
            Wallet = r.I32(), Complaint = r.U8(), Backlog = r.U16(), Deliveries = r.U16(), Misdeliveries = r.U16(),
            Outcome = (RunOutcome)r.U8(), Host = new EntityId(r.U32()),
        };
    }

    /// <summary>One player in a snapshot. Position is quantised to 1 cm on the wire (doc 06 §4).</summary>
    public struct PlayerSnap
    {
        public EntityId Id;
        public float X, Y, Z;
        public ushort Yaw;
        public byte HeldSlot;
        /// <summary>Ready flag of the Lobby and Prep.</summary>
        public bool Ready;
    }

    /// <summary>
    /// 20 Hz unreliable state (doc 06 §4 strategy S). The receiving player's own movement state is
    /// sent unquantised so reconciliation replays from exactly what the server has.
    /// </summary>
    public sealed class SnapshotMsg
    {
        private const byte FlagOwn = 1, FlagGlobal = 2;
        private const byte PlayerReady = 1;

        public uint Tick;
        /// <summary>Seq of the last input the server has simulated for the receiving player.</summary>
        public uint AckSeq;
        public bool HasOwn;
        public PlayerMoveState Own;
        public float OwnSpeedMult = 1f;
        public readonly List<PlayerSnap> Players = new List<PlayerSnap>();
        public bool HasGlobal;
        public GlobalState Global;

        public void Write(NetWriter w)
        {
            w.U8((byte)MsgType.Snapshot);
            w.U32(Tick);
            w.U32(AckSeq);
            w.U8((byte)((HasOwn ? FlagOwn : 0) | (HasGlobal ? FlagGlobal : 0)));
            if (HasOwn)
            {
                w.F32(Own.X); w.F32(Own.Y); w.F32(Own.Z); w.F32(Own.VelY); w.Bool(Own.Grounded); w.F32(OwnSpeedMult);
            }
            w.U8((byte)Players.Count);
            foreach (var p in Players)
            {
                w.U32(p.Id.Value);
                w.I32(ToCm(p.X)); w.I16((short)ToCm(p.Y)); w.I32(ToCm(p.Z));
                w.U16(p.Yaw); w.U8(p.HeldSlot); w.U8((byte)(p.Ready ? PlayerReady : 0));
            }
            if (HasGlobal) GlobalCodec.Write(w, Global);
        }

        public static SnapshotMsg Read(NetReader r)
        {
            var m = new SnapshotMsg { Tick = r.U32(), AckSeq = r.U32() };
            byte flags = r.U8();
            m.HasOwn = (flags & FlagOwn) != 0;
            m.HasGlobal = (flags & FlagGlobal) != 0;
            if (m.HasOwn)
            {
                m.Own = new PlayerMoveState { X = r.F32(), Y = r.F32(), Z = r.F32(), VelY = r.F32(), Grounded = r.Bool() };
                m.OwnSpeedMult = r.F32();
            }
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                m.Players.Add(new PlayerSnap
                {
                    Id = new EntityId(r.U32()),
                    X = r.I32() / 100f, Y = r.I16() / 100f, Z = r.I32() / 100f,
                    Yaw = r.U16(), HeldSlot = r.U8(), Ready = (r.U8() & PlayerReady) != 0,
                });
            }
            if (m.HasGlobal) m.Global = GlobalCodec.Read(r);
            return m;
        }

        private static int ToCm(float metres) => (int)System.MathF.Round(metres * 100f);
    }

    public static class EventsMsg
    {
        public static void Write(NetWriter w, List<SimEvent> events)
        {
            w.U8((byte)MsgType.Events);
            w.U16((ushort)events.Count);
            foreach (var e in events) EventCodec.Write(w, e);
        }

        public static List<SimEvent> Read(NetReader r)
        {
            int n = r.Count16(2);
            var list = new List<SimEvent>(n);
            for (int i = 0; i < n; i++) list.Add(EventCodec.Read(r));
            return list;
        }
    }
}
