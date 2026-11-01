using System.Runtime.InteropServices;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Integration.Inventory.Fuzz;

/// <summary>
/// The fuzz's op types (story grid-inventory/018, H-R19-1 M-1a set). The generator writes the type into the low 5 bits of every
/// op's <see cref="Op.ReqId"/>, so a drained op or an executed command is classified without re-deriving it from the payload.
/// </summary>
internal enum FuzzOpType : byte
{
    /// <summary>Pre-population and joins: not a fuzz op, excluded from coverage (i).</summary>
    Setup = 0,
    Pick,
    MoveWhole,
    MoveSplit,
    MoveSwap,
    Merge,
    QuickMove,
    Return,
    Open,
    Close,
    Spawn,
    Detach,
    WorldItemCreate,
    Catch,
    Pickup,
    Consume,
    /// <summary>A malformed inventory op: must be rejected with <c>bad_request</c>, excluded from coverage (i).</summary>
    Malformed,
}

/// <summary>The automation-style call a <see cref="FuzzCommandPayload"/> asks <see cref="FuzzAutomationSystem"/> to make.</summary>
internal enum FuzzVerb : byte
{
    /// <summary><c>Spawn</c> X = container, Y = address, A = mail kind, B = flags, Z = first mail id, W = id count; an Intake remainder goes to <c>AppendBacklog</c> one id per call.</summary>
    Spawn = 1,

    /// <summary>The internal open-set API: <c>TryOpenExternal(Slot, X)</c>.</summary>
    Open = 2,

    /// <summary>The internal open-set API: <c>CloseExternal(Slot, A = 1 ? Leash : Close)</c>.</summary>
    Close = 3,

    /// <summary><c>DetachForThrow(Slot, X = entry, A = units)</c>; the detached entry becomes one of the system's flights.</summary>
    Detach = 4,

    /// <summary><c>CreateWorldItem(X = flight entry, RNG position, A = origin, B = on water, now)</c>: the drop / throw-land stand-in.</summary>
    Land = 5,

    /// <summary><c>TryCatch(Slot, X = flight entry, B = on water)</c>.</summary>
    Catch = 6,

    /// <summary>The pickup core: <c>Pickup.Apply(Y = actor, X = WorldItem, 0)</c>.</summary>
    Pickup = 7,

    /// <summary><c>ConsumeUnit(X = container, Y = entry, A = reason, Z = actor)</c>.</summary>
    Consume = 8,
}

/// <summary>Payload of the TEST-ONLY command op <see cref="FuzzAutomationSystem.CommandKind"/> (20 B, Pack = 1, no pad bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FuzzCommandPayload
{
    public byte Verb;
    public byte Slot;
    public byte A;
    public byte B;
    public uint X;
    public uint Y;
    public uint Z;
    public uint W;
}

/// <summary>What the automation stand-in reports to the fuzz's model and coverage (H-R19-1); null in the H-DET-1 worlds.</summary>
internal interface IFuzzObserver
{
    /// <summary>A mail unit entered Inventory custody (a <c>Spawn</c> placement or an accepted <c>AppendBacklog</c>).</summary>
    void MailEntered(uint mailId);

    /// <summary>One unit of ledger row <paramref name="row"/> was consumed (<paramref name="mailId"/> 0 for an item).</summary>
    void UnitConsumed(int row, uint mailId);

    /// <summary>A command finished: one fuzz op, checked after it like a drained op.</summary>
    void CommandExecuted(FuzzOpType type, in FuzzCommandPayload command, bool accepted);
}

/// <summary>
/// TEST-ONLY stand-in for the systems above Grid Inventory (story grid-inventory/018): composed as <see cref="SystemId.MailDestinations"/>
/// (which declares Grid Inventory in <c>SystemGraph</c>, so it reaches <see cref="IInventoryApi"/> through the composition), it binds
/// one System-only command op whose handler only stashes the command, and the <c>MailStep</c> phase (8), where it makes each stashed
/// call in FIFO order: the M-1a automation set (<see cref="FuzzVerb"/>) that has no player op yet. The commands travel the real op FIFO,
/// so the H-DET-1 log replays them like any op; the calls run in a later phase than the op drain, as Mail's and Throwing's do. Landing
/// positions are drawn from the world's own <c>RngNames.Mail</c> stream (created at the first Land command), so the run seed reaches the
/// dump. Its flights (entries <c>DetachForThrow</c> released and nothing caught or landed yet) are kept in release order. Never pokes
/// Inventory state: every mutation is an API call.
/// </summary>
internal sealed class FuzzAutomationSystem : ISimSystem
{
    /// <summary>The command op kind (inventory block, test-only, like the 0x00F8/0x00F9 fixtures; never registered by production).</summary>
    // Mail Dev default, approved by Evan 2026-11-01 1:48 PM PT by card click (TD-206 item 2): the M-1a calls with no player op travel the FIFO as the test-only System command op 0x00FA.
    public const OpKind CommandKind = (OpKind)0x00FA;

    private const int MaxPending = 256;

    private readonly InventorySystem _inventory;
    private readonly IRngStreams _rngView;
    private readonly ITickReader _tick;
    private readonly FuzzCommandPayload[] _pending = new FuzzCommandPayload[MaxPending];
    private readonly FuzzOpType[] _pendingTypes = new FuzzOpType[MaxPending];
    private readonly List<uint> _flights = new(1024);
    private int _pendingCount;
    private RngStream? _rng;

    public FuzzAutomationSystem(InventorySystem inventory, IRngStreams rng, ITickReader tick)
    {
        _inventory = inventory;
        _rngView = rng;
        _tick = tick;
    }

    /// <summary>The model and coverage sink (H-R19-1), or null.</summary>
    public IFuzzObserver? Observer { get; set; }

    /// <summary>Live flight entry ids, oldest release first (the generator's read-only view).</summary>
    public IReadOnlyList<uint> Flights => _flights;

    /// <summary>The tick of the first <see cref="FuzzVerb.Land"/>, whose position is the world's first RNG draw (landed or refused), or <see cref="uint.MaxValue"/>.</summary>
    public uint FirstDrawTick { get; private set; } = uint.MaxValue;

    /// <summary>Accepted command executions per <see cref="FuzzOpType"/>.</summary>
    public long[] Accepted { get; } = new long[(int)FuzzOpType.Malformed + 1];

    /// <summary>Builds a command op (System actor).</summary>
    public static Op Command(uint reqId, in FuzzCommandPayload payload)
    {
        var op = new Op { Kind = CommandKind, Actor = ActorId.System, ReqId = reqId };
        op.SetPayload(in payload);
        return op;
    }

    public void Bind(SimWorldBuilder builder)
    {
        builder.BindOp(OpDescriptor.For<FuzzCommandPayload>(CommandKind, true, 0, 0), new StashHandler(this));
        // Mail Dev default, approved by Evan 2026-11-01 1:48 PM PT by card click (TD-206 item 2): the M-1a calls with no player op are made in MailStep (phase 8).
        builder.Bind(PhaseId.MailStep, new RunPhase(this));
    }

    private void Run()
    {
        for (int i = 0; i < _pendingCount; i++)
        {
            ref readonly FuzzCommandPayload c = ref _pending[i];
            bool accepted = Execute(in c);
            if (accepted)
            {
                Accepted[(int)_pendingTypes[i]]++;
            }

            Observer?.CommandExecuted(_pendingTypes[i], in c, accepted);
        }

        _pendingCount = 0;
    }

    private bool Execute(in FuzzCommandPayload c)
    {
        PlayerSlot slot = c.Slot < PlayerSlot.Count ? PlayerSlot.FromValue(c.Slot) : PlayerSlot.None;
        switch ((FuzzVerb)c.Verb)
        {
            case FuzzVerb.Spawn:
                return Spawn(in c);
            case FuzzVerb.Open:
                return _inventory.TryOpenExternal(slot, new ContainerId(c.X));
            case FuzzVerb.Close:
            {
                bool had = !_inventory.OpenSet.ExternalOf(slot).IsNone;
                _inventory.CloseExternal(slot, c.A == 1 ? CloseCause.Leash : CloseCause.Close);
                return had;
            }

            case FuzzVerb.Detach:
            {
                DetachResult r = _inventory.DetachForThrow(slot, EntryId.FromValue(c.X), c.A);
                if (r.Reason == 0)
                {
                    _flights.Add(c.X);
                }

                return r.Reason == 0;
            }

            case FuzzVerb.Land:
            {
                // Mail Dev default, approved by Evan 2026-11-01 1:48 PM PT by card click (TD-206 item 5): landing positions are drawn from the world's RngNames.Mail stream (index 1), so another run seed diverges at the first Land command.
                _rng ??= _rngView.Create(in RngNames.Mail, 1);
                FirstDrawTick = Math.Min(FirstDrawTick, _tick.CurrentTick);
                var pos = new Vec3Mm((int)_rng.NextInt(200_000) - 100_000, 0, (int)_rng.NextInt(200_000) - 100_000);
                EntityId id = _inventory.CreateWorldItem(EntryId.FromValue(c.X), pos, (WorldItemOrigin)c.A, c.B == 1, _tick.CurrentTick);
                if (id.IsNone)
                {
                    return false;
                }

                _ = _flights.Remove(c.X);
                return true;
            }

            case FuzzVerb.Catch:
            {
                CatchResult r = _inventory.TryCatch(slot, new FlightPayload(EntryId.FromValue(c.X), new Vec3Mm(0, 0, 0), c.B == 1));
                if (r.Reason == 0)
                {
                    _ = _flights.Remove(c.X);
                }

                return r.Reason == 0;
            }

            case FuzzVerb.Pickup:
            {
                var extra = new InvReplyExtra();
                return _inventory.Pickup.Apply(new ActorId(c.Y), new EntityId(c.X), 0, ref extra).IsTaken;
            }

            case FuzzVerb.Consume:
                return Consume(in c);
            default:
                return false;
        }
    }

    private bool Spawn(in FuzzCommandPayload c)
    {
        int n = (int)Math.Min(c.W, 16u);
        Span<MailId> ids = stackalloc MailId[n];
        for (int i = 0; i < n; i++)
        {
            ids[i] = new MailId(c.Z + (uint)i);
        }

        var container = new ContainerId(c.X);
        var kind = DefIndex.FromValue(c.A);
        SpawnResult r = _inventory.Spawn(container, kind, c.Y, c.B, ids, MergePolicy.Merge);
        int entered = r.Placed;
        for (int i = 0; i < r.Placed; i++)
        {
            Observer?.MailEntered(ids[i].Value);
        }

        if (container == _inventory.IntakeContainer)
        {
            // Mail's own pattern for a spawn remainder (IInventoryApi.AppendBacklog): one unit per call; a refused id stays Mail's.
            for (int i = r.Placed; i < n; i++)
            {
                if (_inventory.AppendBacklog(new BacklogEntry(kind, c.Y, c.B, ids.Slice(i, 1))))
                {
                    entered++;
                    Observer?.MailEntered(ids[i].Value);
                }
            }
        }

        return entered > 0;
    }

    private bool Consume(in FuzzCommandPayload c)
    {
        var entry = EntryId.FromValue(c.Y);
        EntryPool entries = _inventory.Entries;
        uint oldest = 0;
        int row = -1;
        if (entries.TryGetSlot(entry, out ushort s) && entries[s].IsLive)
        {
            ref readonly EntryRecord e = ref entries[s];
            row = e.IsMail ? 0 : e.ItemId + 1;
            oldest = e.IsMail && e.Count > 0 ? _inventory.MailIds.Read(in e.MailIds)[0] : 0;
        }

        bool ok = _inventory.ConsumeUnit(new EntryRef(new ContainerId(c.X), entry), (ConsumeReason)c.A, new ActorId(c.Z));
        if (ok)
        {
            Observer?.UnitConsumed(row, oldest);
        }

        return ok;
    }

    private sealed class StashHandler : IOpHandler
    {
        private readonly FuzzAutomationSystem _owner;

        public StashHandler(FuzzAutomationSystem owner) => _owner = owner;

        public OpKind Kind => CommandKind;

        public Reply Apply(in Op op, in TickContext ctx)
        {
            if (_owner._pendingCount == MaxPending)
            {
                return Reply.Reject(in op, CoreReason.BadRequest);
            }

            _owner._pending[_owner._pendingCount] = op.PayloadAs<FuzzCommandPayload>();
            _owner._pendingTypes[_owner._pendingCount] = (FuzzOpType)(op.ReqId & InventoryFuzzGenerator.TypeMask);
            _owner._pendingCount++;
            return Reply.Ok(in op);
        }
    }

    private sealed class RunPhase : ISimPhase
    {
        private readonly FuzzAutomationSystem _owner;

        public RunPhase(FuzzAutomationSystem owner) => _owner = owner;

        public void Run(in TickContext ctx) => _owner.Run();
    }
}
