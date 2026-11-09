using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail &amp; Destinations' Sim module (ADR-0017; ADR-0003 section 4; mail-destinations story 005). At construction, while the world
/// is <c>Constructed</c>, it builds the mail table (<see cref="MailTable"/>, sized from <c>content.Settings.MailTablePages</c>; there
/// is no <c>MailSettings</c> at M-1a, TD-177 item 1), the destination registry (<see cref="DestinationRegistry"/>, one row per
/// lot of the injected <see cref="IWorldQuery"/>) and, over it, the F9 complaint buckets (<see cref="ComplaintBuckets"/>, story 006).
/// It reaches the wallet only through the injected <see cref="IWalletGate"/>
/// (the <c>StubWallet</c> at M-1a) and never references the Economy.
/// <para>
/// It binds the <c>MailStep</c> phase (phase 8, <see cref="Step"/>: the feeder credits, 0 at M-1a, and nothing else when idle;
/// manifest batch firing hooks in with stories 012 to 014), the <see cref="MailEventKinds.ComplaintRaised"/> kind, and the
/// <see cref="SectionIds.Mail"/> dump section (the table rows, the registry, the team complaint state, then the consume routine's outcome
/// state). It implements and publishes <see cref="IMailApi"/>; its hand path runs the one consume routine, <see cref="MailConsumer"/>
/// (story 007), which also binds <see cref="MailEventKinds.DeliveryOutcome"/> and <see cref="MailEventKinds.MailConsumed"/>.
/// </para>
/// Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// // Composition: new MailSystem(context.Events, context.MailIds, context.Rng, context.Tick,
/// //     context.Api&lt;IInventoryApi&gt;(SystemId.GridInventory), context.WalletGate, world, context.Content, context.StatSheet,
/// //     context.CheckRing, context.Counters, context.Slots)
/// bool found = mail.TryGetDestinationFacts(lot.Destination, out DestinationFacts f);
/// </code>
/// </example>
[SimSystem(SystemId.MailDestinations)]
public sealed class MailSystem : ISimSystem, IMailApi
{
    // _rng is assigned but not read yet: the story prescribes this constructor shape for stories 006-009, which read it. Kept
    // deliberately; do not remove as unused.
    private readonly EventLogFacade _events;
    private readonly IRngStreams _rng;
    private readonly ITickReader _tick;
    private readonly IInventoryApi _inventory;
    private readonly IWalletGate _wallet;
    private readonly IStatSheet _stats;
    private readonly SimCounters _counters;
    private readonly MailTable _table;
    private readonly DestinationRegistry _registry;
    private readonly ComplaintBuckets _complaints;
    private readonly MailConsumer _consumer;

    // F11 feeder credits, one per placed feeder in ascending construct id (ADR-0017 section 7). M-1a has no feeders.
    private readonly int[] _feederCredits = Array.Empty<int>();

    /// <summary>Creates the module and builds its table and destination registry.</summary>
    /// <param name="events">The world's event-log writer facade.</param>
    /// <param name="mailIds">The world's mail id allocator (Mail only).</param>
    /// <param name="rng">Mail's own RNG streams view (<c>mail</c>, <c>mail.special</c>).</param>
    /// <param name="tick">The world's tick reader.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="wallet">The wallet port (Mail only).</param>
    /// <param name="world">The world; only its lots are read, here.</param>
    /// <param name="content">The accepted content set.</param>
    /// <param name="stats">The world's stat sheet.</param>
    /// <param name="checkRing">The world's check ring (mail table growth is recorded here).</param>
    /// <param name="counters">The world's counters.</param>
    /// <param name="slots">
    /// The player slot view: the consume routine names the delivering player in the inventory's removal record (TD-208 item 2 (A2)
    /// option (a), approved by Evan 2026-11-04 6:47 AM PT by card click (card t265s1214): "Approve A2 (TD-208 item 2) option (a):
    /// slot-to-player lookup, no ADR change, as listed in your message above"; the parameter is uncredited Mail Lead planning detail).
    /// </param>
    /// <param name="playerScale">The team <c>playerScale</c> from the profile's shift stub settings (TD-193 item 4); solo (1000 permille) when null.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>MailRegistryInvalid</c> when the destination registry rejects the world's lots or the content (TD-079 item 5).</exception>
    public MailSystem(
        EventLogFacade events,
        MailIds mailIds,
        IRngStreams rng,
        ITickReader tick,
        IInventoryApi inventory,
        IWalletGate wallet,
        IWorldQuery world,
        ContentSet content,
        IStatSheet stats,
        SimCheckRing checkRing,
        SimCounters counters,
        ISlotView slots,
        PlayerScale? playerScale = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(mailIds);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(checkRing);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(slots);
        _events = events;
        _rng = rng;
        _tick = tick;
        _inventory = inventory;
        _wallet = wallet;
        _stats = stats;
        _counters = counters;
        _table = new MailTable(content.Settings, mailIds, tick, checkRing);
        _registry = BuildRegistry(world, content);

        // Until story 009 adds player scaling, the team cap is the solo value of 40 points at any player count; with 8 players the
        // formula would give 155. Mail Dev default, approved by Evan 2026-11-03 9:15 PM PT by card click (TD-193 item 4).
        // Implementation detail (Mail Dev, not part of Evan's ruling): story 009 now passes the composition profile's playerScalePm
        // (ShiftStubSettings.PlayerScalePm, 1000 permille by default; placement an uncredited Mail Dev note, TD-211 item 2; the constructor row is the ADR-0003 section 4 erratum 2026-11-08, TD-211 item 3); 1000 when none is passed.
        _complaints = new ComplaintBuckets(_registry, content.Mail, playerScale?.Permille ?? SoloPlayerScalePm, counters);
        _consumer = new MailConsumer(
            this, events, inventory, wallet, stats, slots, counters, checkRing, _table, _registry, _complaints, content);
    }

    /// <summary>
    /// The wallet floor of a misdelivery penalty, cents. TD-208 item 9, approved by Evan 2026-11-04 9:56 AM PT by card click (card
    /// t271s1241): "Approve TD-208 item 9 as listed: a fixed −500¢ in the mail code until the Economy design adds a setting". The const's
    /// name and placement are uncredited (Mail Lead planning detail); a data key waits for the Economy ADR.
    /// </summary>
    public const int WalletFloorCents = -500;

    /// <summary>
    /// The zero-unit floor-stop throttle, ticks: a refused-at-the-floor <see cref="MailEventKinds.DeliveryOutcome"/> goes out at most once
    /// per (destination, player) per this many ticks (TD-208 item 10, approved by Evan 2026-11-04 9:56 AM PT by card click (card t271s1241):
    /// "Approve TD-208 item 10 as listed, including the story 013 zeroing step"; the listing: "The 30 is fixed in code, not a tuning
    /// setting"). The name is uncredited.
    /// </summary>
    public const int ZeroFloorBroadcastTicks = 30;

    /// <summary>
    /// The shift number pay and lateness are read against (TD-208 item 8, approved by Evan 2026-11-04 9:23 AM PT by card click (card
    /// t271s1234): "Approve TD-208 item 8 (mail code keeps its own shift number starting at 1; adds a step to story 013), as listed in
    /// your message above"). Starts at 1, not dumped; tests set it, and story 013 writes it at shift start. The member is uncredited
    /// Mail Lead planning detail.
    /// </summary>
    internal int CurrentShift { get; set; } = 1;

    /// <summary>
    /// The shift's wrong-delivery count: +1 per op that consumed at least one Misdeliver unit, stopping at 65,535 (TD-208 item 10).
    /// Dumped; story 013 zeroes it at shift start (its AC-6). The member is uncredited Mail Lead planning detail.
    /// </summary>
    internal ushort MisdeliveriesThisShift
    {
        get => _consumer.MisdeliveriesThisShift;
        set => _consumer.MisdeliveriesThisShift = value;
    }

    /// <summary>The consume routine (its outcome state for the dump round-trip and tests).</summary>
    internal MailConsumer Consumer => _consumer;

    /// <summary>The mail table (tests create rows through it).</summary>
    internal MailTable Table => _table;

    /// <summary>The solo <c>playerScale</c> (n = 1), permille: the default when the composition passes none.</summary>
    internal const int SoloPlayerScalePm = 1000;

    /// <summary>The wallet port this module was given (tests: the composition root's injection).</summary>
    internal IWalletGate WalletGate => _wallet;

    /// <summary>The destination registry built at construction.</summary>
    internal DestinationRegistry Registry => _registry;

    /// <summary>The F9 complaint buckets (stories 007 and 008 charge them, then call <see cref="RaiseComplaint"/>).</summary>
    internal ComplaintBuckets Complaints => _complaints;

    /// <inheritdoc />
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Bind(PhaseId.MailStep, new MailStepPhase(this));
        builder.BindEventKind(MailEventDescriptors.ComplaintRaised);
        builder.BindEventKind(MailEventDescriptors.DeliveryOutcome);
        builder.BindEventKind(MailEventDescriptors.MailConsumed);

        // The dump section moved here from MailTable.BindDump (TD-074 item 4).
        builder.BindStateContributor(new MailStateContributor(_table, _registry, _complaints, _consumer));
    }

    /// <inheritdoc />
    public HandConsumeSummary ConsumeHand(EntityId dest, ReadOnlySpan<EntryRef> inScanOrder, PlayerSlot actor, uint holdStartTick)
    {
        if (!TickGuard.IsMutable(_tick, _counters) || !_registry.TryGetOrdinal(dest, out DestinationOrdinal d))
        {
            return default;
        }

        using EventCallScope scope = _events.BeginCall();
        uint now = _tick.CurrentTick;
        int entriesGot = 0;
        int units = 0;
        long cents = 0;
        bool stopped = false;
        bool lateCharged = false;
        MailRejectReason firstReject = MailRejectReason.None;
        for (int i = 0; i < inScanOrder.Length; i++)
        {
            var call = new ConsumeCall(DeliveryVia.Hand, d, inScanOrder[i], actor, holdStartTick, MailConsumer.HandThrownPm, now, lateCharged);
            ConsumeResult r = _consumer.Consume(in call);
            if (r.Result == EvalResult.Reject && firstReject == MailRejectReason.None)
            {
                firstReject = r.Reason;
            }

            entriesGot += r.Units > 0 ? 1 : 0;
            units += r.Units;
            cents += r.CentsDelta;
            lateCharged |= r.LateCharged;

            // A floor stop or a refused removal ends the hand-over (ADR-0017 section 5 step 4; TD-208 item 12, asserts off).
            if (r.StoppedAtFloor || r.Aborted)
            {
                stopped = r.StoppedAtFloor;
                break;
            }
        }

        return new HandConsumeSummary(
            (ushort)Math.Min(entriesGot, ushort.MaxValue), (ushort)Math.Min(units, ushort.MaxValue), firstReject, stopped, cents);
    }

    /// <inheritdoc />
    public void SetDeliveriesState(bool open, uint closeTick, ulong unlockedDistrictMask)
    {
        if (!TickGuard.IsMutable(_tick, _counters))
        {
            return;
        }

        _registry.Deliveries.Set(open, closeTick, unlockedDistrictMask);
    }

    /// <inheritdoc />
    public bool TryGetDestinationFacts(EntityId dest, out DestinationFacts f) => _registry.TryGetDestinationFacts(dest, out f);

    /// <inheritdoc />
    public int DestinationCount => _registry.Count;

    /// <inheritdoc />
    public EntityId DestinationAt(int ordinal) => _registry.EntityIdOf(Row(ordinal));

    /// <inheritdoc />
    public AabbMm CatchVolumeAt(int ordinal) => _registry.CatchVolumeOf(Row(ordinal));

    /// <summary>
    /// Emits one <see cref="MailEventKinds.ComplaintRaised"/> at door <paramref name="d"/> when <paramref name="amountPm"/> is above 0,
    /// and nothing otherwise (M-R9-2a; Evan approved 2026-10-29 (TD-187 item 1)). One complaint is capped at 65.535 points because the
    /// event stores it in 16 bits. Shipped defaults max out at 40 points. Only raised settings hit the cap: a door cap above 32.767 at the
    /// 2.0 multiplier, a raised multiplier bound above about 3.3x at the default door cap, or a late complaint above 65.535 (Mail Dev
    /// default, approved by Evan 2026-11-03 9:15 PM PT by card click (TD-193 item 5)). Implementation detail (Mail Dev, not part of
    /// Evan's ruling): in permille, an amount above <see cref="ushort.MaxValue"/> (65,535) is raised as <see cref="ushort.MaxValue"/>;
    /// late complaints skip the multiplier; the defaults peak at 40,000 (20,000 x 2.0); a <c>penaltyMultMaxPm</c> bound above 3,276
    /// permille can reach the cut at the default 20,000 permille door cap. Inside a tick only. 0 B.
    /// </summary>
    /// <param name="d">The door the complaint is filed at.</param>
    /// <param name="source">Misdelivery or late.</param>
    /// <param name="amountPm">The amount, permille (a <see cref="ComplaintBuckets"/> charge's result).</param>
    internal void RaiseComplaint(DestinationOrdinal d, ComplaintSource source, int amountPm)
    {
        if (amountPm <= 0)
        {
            return;
        }

        var payload = new ComplaintRaisedPayload
        {
            Destination = _registry.EntityIdOf(d).Value,
            AmountPermille = (ushort)Math.Min(amountPm, ushort.MaxValue),
            Source = (byte)source,
        };
        // If the event log is full, the complaint event is dropped with nothing recorded (Mail Dev default, approved by Evan
        // 2026-11-03 9:15 PM PT by card click (TD-193 item 8)). Implementation detail (Mail Dev, not part of Evan's ruling): the
        // TryRaise result is ignored.
        // Correction (Mail Dev, not part of Evan's ruling): the event log still counts the drop (EventsDropped, OverflowInfo via
        // RecordDrop) and calls IOpRecorder.OnOverflow; only Mail records nothing itself.
        _ = _events.TryRaise(MailEventKinds.ComplaintRaised, MailEventDescriptors.AudienceAll, in payload);
    }

    /// <summary>
    /// Phase 8 (ADR-0017 section 7; M-PERF-1c): updates every feeder credit and touches nothing else when idle, so an idle tick visits
    /// no mail row and no complaint bucket (<see cref="CounterId.MailRowsVisited"/>, <see cref="CounterId.MailBucketsVisited"/>).
    /// 0 B.
    /// </summary>
    internal void Step()
    {
        int[] credits = _feederCredits;
        for (int i = 0; i < credits.Length; i++)
        {
            // F11 (test, add insertRateMilli, cap at 30,000) lands with the M-1b feeders; M-1a has none.
        }
    }

    private DestinationOrdinal Row(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, _registry.Count);
        return new DestinationOrdinal((ushort)ordinal);
    }

    private static DestinationRegistry BuildRegistry(IWorldQuery world, ContentSet content)
    {
        try
        {
            return new DestinationRegistry(world, content);
        }
        catch (ArgumentException e) when (e is not ArgumentNullException)
        {
            throw new SimBuildException(SimBuildErrorCode.MailRegistryInvalid, "Mail & Destinations: " + e.Message);
        }
    }
}

/// <summary>Mail's <c>MailStep</c> phase (phase 8, after <c>AgentsStep</c>; ADR-0003 section 1): <see cref="MailSystem.Step"/>.</summary>
internal sealed class MailStepPhase : ISimPhase
{
    private readonly MailSystem _system;

    public MailStepPhase(MailSystem system)
    {
        _system = system;
    }

    public void Run(in TickContext ctx) => _system.Step();
}
