using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// One entry handed to <see cref="MailConsumer.Consume"/> (ADR-0017 section 5): the path, the destination, the entry, the acting player, the
/// action start tick (the closing-grace input; none for a feeder or NPC), the thrown multiplier and the apply tick. The field set is
/// uncredited implementation detail (story 007 implementation notes); <see cref="LateChargedInCall"/> is this story's addition so that
/// two late entries in one hold make one late charge (AC-10).
/// </summary>
/// <example><code>var call = new ConsumeCall(DeliveryVia.Hand, d, entry, actor, holdStartTick, MailConsumer.HandThrownPm, now, false);</code></example>
internal readonly struct ConsumeCall
{
    /// <summary>Creates a call.</summary>
    /// <param name="via">The path.</param>
    /// <param name="destination">The destination's registry row.</param>
    /// <param name="entry">The entry to consume from.</param>
    /// <param name="actor">The acting player's slot.</param>
    /// <param name="actionStartTick">The tick the action started (hold start, release), or null for no grace.</param>
    /// <param name="thrownPm">The thrown pay multiplier, permille (1000 on the hand path).</param>
    /// <param name="applyTick">The tick the consume is applied.</param>
    /// <param name="lateChargedInCall">True when an earlier entry of the same hold already made the late charge.</param>
    public ConsumeCall(
        DeliveryVia via, DestinationOrdinal destination, EntryRef entry, PlayerSlot actor, uint? actionStartTick, int thrownPm, uint applyTick,
        bool lateChargedInCall)
    {
        Via = via;
        Destination = destination;
        Entry = entry;
        Actor = actor;
        ActionStartTick = actionStartTick;
        ThrownPm = thrownPm;
        ApplyTick = applyTick;
        LateChargedInCall = lateChargedInCall;
    }

    /// <summary>The path.</summary>
    public DeliveryVia Via { get; }

    /// <summary>The destination's registry row.</summary>
    public DestinationOrdinal Destination { get; }

    /// <summary>The entry to consume from.</summary>
    public EntryRef Entry { get; }

    /// <summary>The acting player's slot.</summary>
    public PlayerSlot Actor { get; }

    /// <summary>The tick the action started, or null (no closing grace).</summary>
    public uint? ActionStartTick { get; }

    /// <summary>The thrown pay multiplier, permille.</summary>
    public int ThrownPm { get; }

    /// <summary>The tick the consume is applied (lateness and complaint time).</summary>
    public uint ApplyTick { get; }

    /// <summary>True when an earlier entry of the same call already made the late charge.</summary>
    public bool LateChargedInCall { get; }
}

/// <summary>What <see cref="MailConsumer.Consume"/> did with one entry (uncredited implementation detail).</summary>
/// <param name="Result">The evaluation (Reject also for an entry that could not be read).</param>
/// <param name="Reason">The Reject reason; none otherwise, and none for an unreadable entry.</param>
/// <param name="Units">Units consumed.</param>
/// <param name="CentsDelta">Pay minus penalties, cents, including a penalty that stays after a refused removal.</param>
/// <param name="StoppedAtFloor">True when the wallet floor refused a penalty.</param>
/// <param name="Aborted">True when <c>IInventoryApi.ConsumeUnit</c> refused a unit (asserts off): the caller stops the hand-over.</param>
/// <param name="LateCharged">True when this entry made the call's late charge.</param>
internal readonly record struct ConsumeResult(
    EvalResult Result, MailRejectReason Reason, int Units, long CentsDelta, bool StoppedAtFloor, bool Aborted, bool LateCharged);
