using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The production <see cref="IMailboxLanding"/> (ADR-0015 section 5 rows <c>MAILBOX_ACCEPT</c> / <c>MAILBOX_MISDELIVER</c> /
/// <c>MAILBOX_REJECT</c>; ADR-0017 section 5 throw row; GDD T6 mailbox rows and F8; throwing-catching story 012). One call scope per
/// landing, in this order:
/// <list type="number">
/// <item><description><see cref="IMailApi.JudgeThrownLanding"/> (read-only) gives the terminal, so <c>FlightResolved</c> is raised first
/// (receiver = the destination, <c>unitsIn</c> / <c>unitsFumbled</c> 0);</description></item>
/// <item><description><see cref="IMailApi.ResolveThrownLanding"/> with the flight's thrower, snapshotted <c>thrownPm</c> and
/// <c>releaseTick</c>: Mail consumes through <c>MailConsumer.Consume</c> and <see cref="IInventoryApi.ConsumeUnit"/> on the Flight-form
/// ref (Throwing never consumes a unit);</description></item>
/// <item><description>a Reject entry or the wallet-floor remainder (the newest units; the flight's count less
/// <see cref="LandingResult.UnitsConsumed"/>) becomes one <see cref="WorldItemOrigin.Thrown"/>
/// WorldItem at the F8 point (<see cref="DropPoint.F8"/>) through <see cref="IInventoryApi.CreateWorldItem"/>;</description></item>
/// <item><description><see cref="FlightTable.Remove"/>.</description></item>
/// </list>
/// Mail's and Inventory's calls open their own scopes, nested inside this one, so the whole landing is one contiguous run of the log
/// that starts with <c>FlightResolved</c>. <c>FlightResolved.worldItem</c> is the id the creation will take (as story 011's landings):
/// a Reject announces it; Accept and Misdeliver announce 0, because whether a Misdeliver leaves a remainder is known only after Mail's
/// call (its <c>WorldItemSpawned</c> follows in the same run). A judgement the call contradicts, a refused creation or an id other than
/// the announced one records <see cref="CheckCode.ThrowingLandingFault"/>; units a refused creation leaves stay in the Flight column.
/// Allocation-free. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>IMailboxLanding port = new MailboxLanding(events, slots, inventory, mail, world, ids, flights, settings, checks, counters);</code></example>
internal sealed class MailboxLanding : IMailboxLanding
{
    private readonly EventLogFacade _events;
    private readonly ISlotView _slots;
    private readonly IInventoryApi _inventory;
    private readonly IMailApi _mail;
    private readonly IWorldQuery _world;
    private readonly EntityIds _ids;
    private readonly FlightTable _flights;
    private readonly int _pushbackMm;
    private readonly SimCheckRing _checks;
    private readonly SimCounters _counters;

    /// <summary>Creates the landing.</summary>
    /// <param name="events">The event-log writer.</param>
    /// <param name="slots">The slot table (the connected mask).</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="mail">Mail &amp; Destinations' API.</param>
    /// <param name="world">The world (map bounds, water and terrain).</param>
    /// <param name="ids">The world's id allocator (read only: the next WorldItem id).</param>
    /// <param name="flights">The live flights.</param>
    /// <param name="settings">The throw knobs (<see cref="ThrowSettings.DropPushbackMm"/>).</param>
    /// <param name="checks">The world's check ring.</param>
    /// <param name="counters">The world's counters.</param>
    public MailboxLanding(
        EventLogFacade events,
        ISlotView slots,
        IInventoryApi inventory,
        IMailApi mail,
        IWorldQuery world,
        EntityIds ids,
        FlightTable flights,
        ThrowSettings settings,
        SimCheckRing checks,
        SimCounters counters)
    {
        _events = events;
        _slots = slots;
        _inventory = inventory;
        _mail = mail;
        _world = world;
        _ids = ids;
        _flights = flights;
        _pushbackMm = settings.DropPushbackMm;
        _checks = checks;
        _counters = counters;
    }

    /// <inheritdoc />
    public void Land(int index, in Contact contact, uint tick)
    {
        var dest = new EntityId(contact.ReceiverKey);
        EntryId entry = _flights.Entry(index);
        var flying = new EntryRef(ContainerId.None, entry);
        uint releaseTick = _flights.ReleaseTick(index);
        LandingKind judged = _mail.JudgeThrownLanding(dest, in flying, releaseTick);
        EntityId announced = judged == LandingKind.Rejected ? TerminalResolver.NextWorldItem(_ids) : EntityId.None;
        var resolved = new FlightResolved
        {
            FlightId = _flights.Id(index).Value,
            Terminal = judged switch
            {
                LandingKind.Accepted => Terminal.MailboxAccept,
                LandingKind.Misdelivered => Terminal.MailboxMisdeliver,
                _ => Terminal.MailboxReject,
            },
            Rank = contact.Rank,
            Receiver = contact.ReceiverKey,
            ContactX = contact.Point.X,
            ContactY = contact.Point.Y,
            ContactZ = contact.Point.Z,
            Tick = tick,
            WorldItem = announced.Value,
        };

        using (EventCallScope scope = _events.BeginCall())
        {
            _ = _events.TryRaise(ThrowEventKinds.FlightResolved, _slots.ConnectedMask, in resolved);
            LandingResult r = _mail.ResolveThrownLanding(dest, in flying, _flights.Thrower(index), _flights.ThrownPm(index), releaseTick);
            // The flight's own count, not r.UnitsRemaining: Mail reports 0 remaining for an entry it cannot read (a non-mail stack).
            int remaining = _flights.Units(index) - r.UnitsConsumed;
            EntityId created = EntityId.None;
            if (remaining > 0)
            {
                Vec3Mm at = DropPoint.F8(_world, in contact.Point, in _flights.Vel(index), VolumeOf(dest), _pushbackMm, out bool onWater);
                created = _inventory.CreateWorldItem(entry, at, WorldItemOrigin.Thrown, onWater, tick);
            }

            if (r.Kind != judged || (remaining > 0 && created.IsNone) || (!announced.IsNone && created != announced))
            {
                _checks.Record(CheckCode.ThrowingLandingFault, tick, resolved.FlightId, created.Value);
                _counters.Add(CounterId.ThrowingLandingFaults);
            }
        }

        _flights.Remove(index);
    }

    private AabbMm VolumeOf(EntityId dest)
    {
        int n = _mail.DestinationCount;
        for (int i = 0; i < n; i++)
        {
            if (_mail.DestinationAt(i) == dest)
            {
                return _mail.CatchVolumeAt(i);
            }
        }

        return default;   // unreachable: the contact came from this registry's volumes
    }
}
