using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Something that happened this tick and must reach clients reliably (doc 06 §4 strategy E).
    /// <see cref="Audience"/> None = every client; otherwise only that player.
    /// </summary>
    public abstract class SimEvent
    {
        public EntityId Audience;
    }

    public sealed class PlayerJoinedEvent : SimEvent
    {
        public EntityId Player;
        public string Name = "";
        public byte Slot;
    }

    public sealed class PlayerLeftEvent : SimEvent
    {
        public EntityId Player;
    }

    /// <summary>Full container contents, sent when a player opens a container.</summary>
    public sealed class ContainerStateEvent : SimEvent
    {
        public ContainerState State = null!;
    }

    /// <summary>One versioned container mutation, sent to every viewer of the container.</summary>
    public sealed class ContainerDeltaEvent : SimEvent
    {
        public ContainerDelta Delta = null!;
    }

    /// <summary>The server closed the player's external container (walked out of range, container gone).</summary>
    public sealed class ContainerClosedEvent : SimEvent
    {
        public uint ContainerId;
    }

    /// <summary>Verdict on a client request; a rejection rolls back the client's optimistic preview.</summary>
    public sealed class RequestResultEvent : SimEvent
    {
        public uint RequestId;
        public OpResult Result;
    }

    public sealed class MailSpawnedEvent : SimEvent
    {
        /// <summary>Mail ids are sequential, so the batch is [FirstMailId, FirstMailId + Count).</summary>
        public uint FirstMailId;
        public ushort Count;
        public ushort Backlog;
    }

    public sealed class DeliveredEvent : SimEvent
    {
        public uint MailId;
        public ushort DestId;
        public ushort Paid;
        public EntityId Player;
        public ushort Kind;
    }

    public sealed class MisdeliveredEvent : SimEvent
    {
        public uint MailId;
        public ushort DestId;
        public ushort Penalty;
        public EntityId Player;
        public ushort Kind;
    }

    public sealed class PhaseChangedEvent : SimEvent
    {
        public byte Shift;
        public RunPhase Phase;
        /// <summary>0 when the phase has no clock.</summary>
        public uint PhaseEndTick;
        public int Quota;
        public RunOutcome Outcome;
    }

    /// <summary>The solo pause went on or off (doc 01 §1); <see cref="Tick"/> is the tick the server stands on.</summary>
    public sealed class PausedChangedEvent : SimEvent
    {
        public bool Paused;
        public uint Tick;
    }

    /// <summary>Payday payload (doc 01 §2.4), sent just before the PhaseChanged into Payday.</summary>
    public sealed class ShiftSummaryEvent : SimEvent
    {
        public ShiftSummary Summary = null!;
    }

    /// <summary>Results payload (doc 08 §3.2), sent just before the PhaseChanged into Results.</summary>
    public sealed class RunResultsEvent : SimEvent
    {
        public RunResults Results = null!;
    }
}
