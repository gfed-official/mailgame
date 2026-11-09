using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The winner → terminal switch of <c>Throwing.Step</c> (ADR-0015 section 5; GDD T6 "Every flight ends in exactly one terminal state" rows
/// Solid, Terrain, Water, TIMEOUT; throwing-catching story 011). Each call resolves one live flight exactly once: one <c>FlightResolved</c>
/// to every connected slot, then one WorldItem (<see cref="WorldItemOrigin.Thrown"/>, created at the landing tick so Grid's 300 s timer
/// starts there) through <see cref="IInventoryApi.CreateWorldItem"/> in the same call scope, then <see cref="FlightTable.Remove"/>.
/// <list type="bullet">
/// <item><description>terrain: <see cref="Terminal.Ground"/> at the contact point;</description></item>
/// <item><description>water: <see cref="Terminal.Water"/>, floating at the contact point (<c>onWater</c>; never moved to land);</description></item>
/// <item><description>a world solid (house, roof, the Post Office) or a map side: the item drops straight down onto the surface below the
/// contact (<see cref="SurfaceBelow"/>, so never onto a roof), <see cref="Terminal.Ground"/>, or <see cref="Terminal.Water"/> over water;</description></item>
/// <item><description>a mailbox: <see cref="IMailboxLanding"/> (<see cref="MailboxLanding"/>, story 012); an armed catcher: <see cref="ICatchLanding"/> when bound (M0);</description></item>
/// <item><description>no contact through age <c>maxFlightTicks</c>: <see cref="Terminal.Timeout"/> on the surface below <c>p(90)</c>, with
/// that surface's rank.</description></item>
/// </list>
/// Every landing point is first clamped 1000 mm inside the map (<see cref="IWorldQuery.ClampInsideMap"/>); the walkable snap and its
/// search back along −v are greybox-deferred (simplify table, tc-011). <c>FlightResolved.worldItem</c> is the id the creation will
/// take (the WorldItem class's next counter), because the event precedes the creation's <c>WorldItemSpawned</c>; a creation that refuses
/// or takes another id records <see cref="CheckCode.ThrowingLandingFault"/> and leaves the units in the Flight column. The terminal byte
/// is always a defined <see cref="Terminal"/> member and <see cref="FlightResolved.Pad"/> is 0, so every client's codec accepts it.
/// Allocation-free. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>if (FlightSweep.FirstContact(in p0, in p1, in view, id, out Contact c)) { resolver.Resolve(index, in c, tick); }</code></example>
internal sealed class TerminalResolver
{
    /// <summary>How far inside the map every landing point is clamped, mm (GDD T6 "1 m inside the edge").</summary>
    public const int MapInsetMm = 1000;

    private readonly EventLogFacade _events;
    private readonly ISlotView _slots;
    private readonly IInventoryApi _inventory;
    private readonly IWorldQuery _world;
    private readonly EntityIds _ids;
    private readonly FlightTable _flights;
    private readonly ReceiverRanks _ranks;
    private readonly SimCheckRing _checks;
    private readonly SimCounters _counters;

    /// <summary>Creates the resolver.</summary>
    /// <param name="events">The event-log writer.</param>
    /// <param name="slots">The slot table (the connected mask).</param>
    /// <param name="inventory">Grid Inventory's API (<see cref="IInventoryApi.CreateWorldItem"/>).</param>
    /// <param name="world">The world (surfaces and bounds).</param>
    /// <param name="ids">The world's id allocator (read only: the next WorldItem id).</param>
    /// <param name="flights">The live flights.</param>
    /// <param name="ranks">The rank table.</param>
    /// <param name="checks">The world's check ring.</param>
    /// <param name="counters">The world's counters.</param>
    /// <param name="mailbox">The mailbox port (<see cref="MailboxLanding"/> in production).</param>
    public TerminalResolver(
        EventLogFacade events,
        ISlotView slots,
        IInventoryApi inventory,
        IWorldQuery world,
        EntityIds ids,
        FlightTable flights,
        ReceiverRanks ranks,
        SimCheckRing checks,
        SimCounters counters,
        IMailboxLanding mailbox)
    {
        _events = events;
        _slots = slots;
        _inventory = inventory;
        _world = world;
        _ids = ids;
        _flights = flights;
        _ranks = ranks;
        _checks = checks;
        _counters = counters;
        Mailbox = mailbox;
    }

    /// <summary>The mailbox port.</summary>
    public IMailboxLanding Mailbox { get; }

    /// <summary>The catcher port (story 023, M0); null at M-1a.</summary>
    public ICatchLanding? Catch { get; set; }

    /// <summary>Resolves the flight at <paramref name="index"/> on its winning contact.</summary>
    /// <param name="index">The live flight's position.</param>
    /// <param name="c">The winning contact.</param>
    /// <param name="tick">The tick being stepped.</param>
    public void Resolve(int index, in Contact c, uint tick)
    {
        switch (c.Kind)
        {
            case ReceiverKind.Mailbox:
                Mailbox.Land(index, in c, tick);
                return;
            case ReceiverKind.ArmedCatcher when Catch is not null:
                Catch.Land(index, in c, tick);
                return;
            case ReceiverKind.Ground:
                Land(index, Terminal.Ground, c.Rank, 0, in c.Point, OnMap(in c.Point), false, tick);
                return;
            case ReceiverKind.Water:
                Land(index, Terminal.Water, c.Rank, 0, in c.Point, OnMap(in c.Point), true, tick);
                return;
            default:
                // Solids and the map sides; goons, teammates and belts have no receivers before M-1b / M0.
                DropStraightDown(index, in c, tick);
                return;
        }
    }

    /// <summary>TIMEOUT: no contact through age <c>maxFlightTicks</c>; the item drops onto the surface directly below <paramref name="last"/>.</summary>
    /// <param name="index">The live flight's position.</param>
    /// <param name="last">The flight's position at its last age, <c>p(90)</c>.</param>
    /// <param name="tick">The tick being stepped.</param>
    public void Timeout(int index, in Vec3Mm last, uint tick)
    {
        ReceiverKind below = SurfaceBelow.At(_world, OnMap(in last), out Vec3Mm at);
        Land(index, Terminal.Timeout, _ranks.Of(below), 0, in last, at, below == ReceiverKind.Water, tick);
    }

    private void DropStraightDown(int index, in Contact c, uint tick)
    {
        ReceiverKind below = SurfaceBelow.At(_world, OnMap(in c.Point), out Vec3Mm at);
        bool water = below == ReceiverKind.Water;
        uint receiver = c.ReceiverKey >= FlightSweep.MapSideKeyBase ? 0u : c.ReceiverKey;
        Land(index, water ? Terminal.Water : Terminal.Ground, c.Rank, receiver, in c.Point, at, water, tick);
    }

    /// <summary>The id the next WorldItem creation will take (the class's next counter), or none when the class is exhausted.</summary>
    /// <param name="ids">The world's id allocator.</param>
    /// <returns>The id <c>FlightResolved.worldItem</c> announces.</returns>
    internal static EntityId NextWorldItem(EntityIds ids)
    {
        uint last = ids.Peek(EntityClass.WorldItem);
        return last < EntityId.CounterMask ? EntityId.Compose(EntityClass.WorldItem, last + 1) : EntityId.None;
    }

    private Vec3Mm OnMap(in Vec3Mm p) => _world.ClampInsideMap(in p, MapInsetMm);

    private void Land(int index, Terminal terminal, byte rank, uint receiver, in Vec3Mm contact, Vec3Mm at, bool onWater, uint tick)
    {
        EntityId announced = NextWorldItem(_ids);
        var resolved = new FlightResolved
        {
            FlightId = _flights.Id(index).Value,
            Terminal = terminal,
            Rank = rank,
            Receiver = receiver,
            ContactX = contact.X,
            ContactY = contact.Y,
            ContactZ = contact.Z,
            Tick = tick,
            WorldItem = announced.Value,
        };

        using (EventCallScope scope = _events.BeginCall())
        {
            _ = _events.TryRaise(ThrowEventKinds.FlightResolved, _slots.ConnectedMask, in resolved);
            EntityId created = _inventory.CreateWorldItem(_flights.Entry(index), at, WorldItemOrigin.Thrown, onWater, tick);
            if (created.IsNone || created != announced)
            {
                _checks.Record(CheckCode.ThrowingLandingFault, tick, resolved.FlightId, created.Value);
                _counters.Add(CounterId.ThrowingLandingFaults);
            }
        }

        _flights.Remove(index);
    }
}
