using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>The throw path into the consume routine (mail-destinations story 008; ADR-0017 section 5 throw row; GDD R13).</summary>
public sealed partial class MailSystem
{
    /// <inheritdoc />
    public LandingResult ResolveThrownLanding(EntityId dest, in EntryRef e, PlayerSlot thrower, int thrownPm, uint releaseTick)
    {
        byte before = _inventory.TryReadEntry(e, out EntryView v) ? v.Units : (byte)0;
        if (!TickGuard.IsMutable(_tick, _counters) || !_registry.TryGetOrdinal(dest, out DestinationOrdinal d))
        {
            return new LandingResult(LandingKind.Rejected, 0, before, false, MailRejectReason.None);
        }

        using EventCallScope scope = _events.BeginCall();
        var call = new ConsumeCall(DeliveryVia.Throw, d, e, thrower, releaseTick, thrownPm, _tick.CurrentTick, false);
        ConsumeResult r = _consumer.Consume(in call);
        LandingKind kind = r.Result switch
        {
            EvalResult.Accept => LandingKind.Accepted,
            EvalResult.Misdeliver => LandingKind.Misdelivered,
            _ => LandingKind.Rejected,
        };
        return new LandingResult(kind, (byte)r.Units, (byte)(before - r.Units), r.StoppedAtFloor, r.Reason);
    }
}
