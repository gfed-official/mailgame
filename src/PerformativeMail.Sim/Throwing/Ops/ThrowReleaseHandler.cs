using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The phase-0 <c>ThrowRelease</c> handler (ADR-0015 section 1; ADR-0007; ADR-0008 section 5; GDD T2, T4 "Launch tick"; throwing-catching
/// story 010): <see cref="Apply"/> is story 009's pure <see cref="ThrowValidator.Validate(in Op, uint, in ThrowHostFacts, out ThrowPlan)"/>,
/// then on a pass <see cref="Commit"/>, then exactly one reply. The commit launches at the apply tick <c>T</c>, never back-dated:
/// <list type="number">
/// <item>the entry's facts (<see cref="IInventoryApi.TryReadThrowable"/>) and the thrower's StatSheet, read once:
/// <see cref="Stat.ThrowRangePermille"/> for F1 and <see cref="Stat.ThrownCorrectPayMultPm"/> snapshotted as <c>thrownPm</c>;</item>
/// <item>F1, <see cref="LaunchMath.Compute"/> from the plan's feet with the real <see cref="IChestOriginProbe"/>;</item>
/// <item><c>Tat ← tat'</c>: the plan's <see cref="ThrowPlan.TatNext"/>, unchanged at M-1a (erratum E5; story 028 adds the bucket);</item>
/// <item><see cref="IInventoryApi.DetachForThrow"/> (PLACED → FLIGHT, its <c>Removed</c> and <c>InvVersion</c> records);</item>
/// <item>the <c>FlightRecord</c> (<see cref="FlightTable.TryAppend"/>): <c>releaseTick = launchTick = T</c>, <c>openAtRelease</c> from
/// <see cref="IMailApi.GetDeliveriesState"/>, <c>hop = 0</c> (relays are M0);</item>
/// <item><c>ThrowLaunched</c> to every connected slot, <see cref="ThrowLaunched.Flags"/> 0 or <see cref="ThrowLaunched.OriginClampedBit"/>;</item>
/// <item>the <c>ok</c> reply.</item>
/// </list>
/// So the thrower's log reads Grid <c>Removed</c> → <c>InvVersion</c> → <c>ThrowLaunched</c> → <c>ok</c>, in one call scope. The commit never
/// re-validates; a step that refuses after a passing validation (unreachable in a correct world) records
/// <see cref="CheckCode.ThrowingCommitFault"/> and <see cref="CounterId.ThrowingCommitFaults"/> and is answered <c>bad_request</c> with no
/// flight and no event. No RNG. Allocation-free. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(ThrowDescriptors.ThrowRelease, new ThrowReleaseHandler(in deps));</code></example>
internal sealed class ThrowReleaseHandler : IOpHandler
{
    private readonly ThrowReleaseDeps _d;

    /// <summary>Creates the handler.</summary>
    /// <param name="deps">The ports and state it reads and writes.</param>
    /// <exception cref="ArgumentNullException">A reference in <paramref name="deps"/> is null.</exception>
    public ThrowReleaseHandler(in ThrowReleaseDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps.Events);
        ArgumentNullException.ThrowIfNull(deps.Slots);
        ArgumentNullException.ThrowIfNull(deps.Inventory);
        ArgumentNullException.ThrowIfNull(deps.Mail);
        ArgumentNullException.ThrowIfNull(deps.Players);
        ArgumentNullException.ThrowIfNull(deps.Stats);
        ArgumentNullException.ThrowIfNull(deps.Hotbar);
        ArgumentNullException.ThrowIfNull(deps.Corroborator);
        ArgumentNullException.ThrowIfNull(deps.ChestProbe);
        ArgumentNullException.ThrowIfNull(deps.Settings);
        ArgumentNullException.ThrowIfNull(deps.Flights);
        ArgumentNullException.ThrowIfNull(deps.Tat);
        ArgumentNullException.ThrowIfNull(deps.Checks);
        ArgumentNullException.ThrowIfNull(deps.Counters);
        _d = deps;
    }

    /// <inheritdoc />
    public OpKind Kind => ThrowOpKinds.ThrowRelease;

    /// <inheritdoc />
    public Reply Apply(in Op op, in TickContext ctx)
    {
        var facts = new ThrowHostFacts(
            _d.Inventory, _d.Players, _d.Slots, _d.Hotbar, _d.Corroborator, _d.ClaimToleranceMm, _d.Flights, _d.Tat, _d.Counters);
        ThrowCheck check = ThrowValidator.Validate(in op, ctx.Tick, in facts, out ThrowPlan plan);
        if (!check.IsOk)
        {
            return Reply.Reject(in op, check.Reason);
        }

        return Commit(in plan, ctx.Tick) ? Reply.Ok(in op) : Reply.Reject(in op, ThrowReasons.BadRequest);
    }

    /// <summary>The launch (see the type remarks). True once the flight exists and <c>ThrowLaunched</c> is raised.</summary>
    /// <param name="plan">The plan the validation passed.</param>
    /// <param name="tick">The apply tick <c>T</c>.</param>
    /// <returns>False only on a commit fault (recorded), with no flight and no event.</returns>
    internal bool Commit(in ThrowPlan plan, uint tick)
    {
        PlayerSlot slot = plan.Thrower;
        if (!_d.Inventory.TryReadThrowable(plan.Entry, out ThrowableEntry entry))
        {
            return Fault(tick, 1, in plan);
        }

        // Latched at op start (ADR-0010): later sheet changes and a disconnect leave the record as launched.
        int rangePm = _d.Stats.Read(Stat.ThrowRangePermille, slot: slot);
        int thrownPm = _d.Stats.Read(Stat.ThrownCorrectPayMultPm, slot: slot);

        // Body yaw only feeds F1's h < 2048 branch, which a direction that passed check 8 never reaches.
        var input = new LaunchInput(
            plan.FeetUsed, plan.DirX, plan.DirY, plan.DirZ, plan.Side, entry.WeightClass, rangePm, 0, _d.Settings, _d.ChestProbe);
        if (!LaunchMath.Compute(in input, out Launch launch, out _))
        {
            return Fault(tick, 2, in plan);
        }

        _d.Tat[slot.Value] = plan.TatNext;

        using EventCallScope scope = _d.Events.BeginCall();
        DetachResult detached = _d.Inventory.DetachForThrow(slot, plan.Entry, plan.Units);
        if (detached.Reason != 0)
        {
            return Fault(tick, 3, in plan);
        }

        _d.Mail.GetDeliveriesState(out bool open, out _);
        var init = new FlightInit
        {
            Entry = detached.Entry,
            Units = detached.Units,
            KindDef = entry.KindDef,
            Thrower = slot,
            ClassPts = entry.ClassPts,
            Origin = launch.Origin,
            Vel = launch.Vel,
            LaunchTick = tick,
            ThrownPm = thrownPm,
            ReleaseTick = tick,
            OpenAtRelease = open,
            Hop = 0,
            OriginClamped = plan.OriginClamped,
        };

        // Check 10 refused exactly when this would, so the append always succeeds after a passing validation.
        if (!_d.Flights.TryAppend(in init, out int index))
        {
            return Fault(tick, 4, in plan);
        }

        var launched = new ThrowLaunched
        {
            FlightId = _d.Flights.Id(index).Value,
            Thrower = slot.Value,
            Flags = plan.OriginClamped ? ThrowLaunched.OriginClampedBit : (byte)0,
            EntryId = detached.Entry.Value,
            Units = detached.Units,
            Hop = 0,
            KindDef = entry.KindDef,
            OriginX = launch.Origin.X,
            OriginY = launch.Origin.Y,
            OriginZ = launch.Origin.Z,
            VelX = launch.Vel.X,
            VelY = launch.Vel.Y,
            VelZ = launch.Vel.Z,
            LaunchTick = tick,
        };
        _ = _d.Events.TryRaise(ThrowEventKinds.ThrowLaunched, _d.Slots.ConnectedMask, in launched);
        return true;
    }

    private bool Fault(uint tick, int step, in ThrowPlan plan)
    {
        _d.Checks.Record(CheckCode.ThrowingCommitFault, tick, step, plan.Entry.Value);
        _d.Counters.Add(CounterId.ThrowingCommitFaults);
        return false;
    }
}

/// <summary>
/// The ports and state <see cref="ThrowReleaseHandler"/> reads and writes (throwing-catching story 010): the validator's facts (story 009),
/// the StatSheet, the chest probe and settings for F1, Mail's deliveries state, the event log and the fault sinks. <see cref="Tat"/> is the
/// owning <see cref="ThrowingSystem"/>'s per-slot array. Built once by <see cref="ThrowingSystem.Bind"/>.
/// </summary>
internal readonly struct ThrowReleaseDeps
{
    /// <summary>The event-log writer.</summary>
    public EventLogFacade Events { get; init; }

    /// <summary>The slot table (actor to slot, the connected mask).</summary>
    public ISlotView Slots { get; init; }

    /// <summary>Grid Inventory's API.</summary>
    public IInventoryApi Inventory { get; init; }

    /// <summary>Mail's API (the deliveries state only).</summary>
    public IMailApi Mail { get; init; }

    /// <summary>Players' API (the validator's checks 3 to 6).</summary>
    public IPlayersApi Players { get; init; }

    /// <summary>The world's StatSheet.</summary>
    public IStatSheet Stats { get; init; }

    /// <summary>The selected hotbar slot (check 2).</summary>
    public IHotbarSelection Hotbar { get; init; }

    /// <summary>Claim-and-corroborate (check 9).</summary>
    public IClaimCorroborator Corroborator { get; init; }

    /// <summary><c>claimToleranceMm</c>.</summary>
    public int ClaimToleranceMm { get; init; }

    /// <summary>F1's chest-to-origin probe.</summary>
    public IChestOriginProbe ChestProbe { get; init; }

    /// <summary>The throw knobs.</summary>
    public ThrowSettings Settings { get; init; }

    /// <summary>The live flights.</summary>
    public FlightTable Flights { get; init; }

    /// <summary>The per-slot leaky-bucket clocks, indexed by slot value.</summary>
    public uint[] Tat { get; init; }

    /// <summary>The world's check ring (commit faults).</summary>
    public SimCheckRing Checks { get; init; }

    /// <summary>The world's counters.</summary>
    public SimCounters Counters { get; init; }
}
