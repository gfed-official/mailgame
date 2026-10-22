using System.Reflection;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// TEST-ONLY arena-acquisition shape for the fixture op (story grid-inventory/005, caller rule): the blocks a commit acquires, in commit
/// order and each sized at the moment it is acquired, and the commit itself (the real arena calls). <see cref="Prepare"/> builds the
/// arena state: inputs created, every class drained to 0 free blocks, then exactly one block of the fitting class freed for every
/// acquisition except <see cref="Omitted"/>. So the full request fails and the request without <see cref="Omitted"/> passes.
/// </summary>
internal sealed class ArenaShape
{
    private readonly Func<MailIdArena, MailIdsHandle[]> _inputs;

    private ArenaShape(string name, int[] blocks, int omitted, Func<MailIdArena, MailIdsHandle[]> inputs)
    {
        Name = name;
        Blocks = blocks;
        Omitted = omitted;
        _inputs = inputs;
    }

    public string Name { get; }

    /// <summary>Every block the commit acquires, in order.</summary>
    public int[] Blocks { get; }

    /// <summary>The index the under-sized request leaves out.</summary>
    public int Omitted { get; }

    public MailIdsHandle[] Inputs { get; private set; } = Array.Empty<MailIdsHandle>();

    /// <summary>One success flag per acquisition of the last commit.</summary>
    public bool[]? Results { get; private set; }

    public int[] UnderSized => Blocks.Where((_, i) => i != Omitted).ToArray();

    /// <summary>Multi-block: union(2, 2) = 4, union(3, 3) = 6, append 3 onto 4 = 7; the under-sized request omits the append.</summary>
    public static ArenaShape MultiBlock() => new(
        "multi-block", new[] { 4, 6, 7 }, 2,
        arena => new[]
        {
            Create(arena, 1, 3), Create(arena, 2, 4), Create(arena, 10, 12, 14), Create(arena, 11, 13, 15), Create(arena, 20, 22, 24, 26),
        });

    /// <summary>None input: union(none, 3 ids) = 3, then TryAppend of 5 ids on a none handle (a create) = 5; omits the none-input union.</summary>
    public static ArenaShape NoneInput() => new("none-input", new[] { 3, 5 }, 0, arena => new[] { Create(arena, 30, 31, 32) });

    /// <summary>Chained unions: union(a 2, b 2) = 4, then union(ab, c 3) = 7; omits the first (|a|+|b|) block.</summary>
    public static ArenaShape Chained() => new(
        "chained", new[] { 4, 7 }, 0, arena => new[] { Create(arena, 40, 42), Create(arena, 41, 43), Create(arena, 44, 45, 46) });

    public static ArenaShape ByName(string name) => name switch
    {
        "multi-block" => MultiBlock(),
        "none-input" => NoneInput(),
        "chained" => Chained(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>Runs the commit's acquisitions on the arena (the real calls; a failed one writes record 12).</summary>
    public void Commit(MailIdArena arena, uint tick)
    {
        MailIdsHandle[] h = Inputs;
        switch (Name)
        {
            case "multi-block":
            {
                bool u1 = arena.TryUnion(in h[0], in h[1], tick, out MailIdsHandle r1);
                bool u2 = arena.TryUnion(in h[2], in h[3], tick, out MailIdsHandle r2);
                bool u3 = arena.TryAppend(ref h[4], new uint[] { 21, 23, 25 }, tick);
                Results = new[] { u1, u2, u3 };
                _ = r1;
                _ = r2;
                break;
            }

            case "none-input":
            {
                MailIdsHandle none = default;
                bool u1 = arena.TryUnion(in none, in h[0], tick, out MailIdsHandle r1);
                MailIdsHandle created = default;
                bool u2 = arena.TryAppend(ref created, new uint[] { 50, 51, 52, 53, 54 }, tick);
                Results = new[] { u1, u2 };
                _ = r1;
                break;
            }

            default:
            {
                bool u1 = arena.TryUnion(in h[0], in h[1], tick, out MailIdsHandle ab);
                bool u2 = u1 && arena.TryUnion(in ab, in h[2], tick, out _);
                Results = new[] { u1, u2 };
                break;
            }
        }
    }

    /// <summary>Builds the shape's arena state (see the class summary). Returns the handles drained into each class (kept live).</summary>
    public List<MailIdsHandle> Prepare(MailIdArena arena)
    {
        Inputs = _inputs(arena);
        var held = new List<MailIdsHandle>[MailIdArena.ClassCount];
        uint next = 1_000_000u;
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            held[c] = new List<MailIdsHandle>();
            int size = MailIdArena.ClassSize(c);
            while (arena.FreeBlocks(c) > 0)
            {
                var ids = new uint[size];
                for (int i = 0; i < size; i++)
                {
                    ids[i] = next++;
                }

                Assert.True(arena.TryCreate(ids, 0, out MailIdsHandle h));
                Assert.Equal(c, h.ClassIndex);
                held[c].Add(h);
            }
        }

        for (int i = 0; i < Blocks.Length; i++)
        {
            if (i == Omitted)
            {
                continue;
            }

            List<MailIdsHandle> list = held[MailIdArena.ClassFor(Blocks[i])];
            MailIdsHandle victim = list[^1];
            list.RemoveAt(list.Count - 1);
            arena.Free(in victim);
        }

        Assert.False(arena.CanReserve(Blocks));
        Assert.True(arena.CanReserve(UnderSized));
        return held.SelectMany(l => l).ToList();
    }

    public override string ToString() => Name;

    private static MailIdsHandle Create(MailIdArena arena, params uint[] ids)
    {
        Assert.True(arena.TryCreate(ids, 0, out MailIdsHandle h));
        return h;
    }
}

/// <summary>
/// TEST-ONLY fixture op handler (story grid-inventory/005): a minimal transfer op over <see cref="MovePayload"/> that exercises the real
/// shared steps and the real <see cref="InventoryMutator"/> until the handlers of stories 007+ exist. <c>Validate</c> resolves the
/// containers and the entry (<c>stale_entry</c>), checks bounds and cells (<c>out_of_bounds</c>, <c>blocked</c>; the entry's own cells
/// are free to it), builds the <see cref="InvPlan"/> (touched set, primaries, placement, and the acquired blocks of the configured
/// <see cref="Shape"/>: <see cref="Requested"/> or all of them) and ends with <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>.
/// <c>Commit</c> is <see cref="InventoryMutator.Transfer"/> plus the shape's real acquisitions, then <see cref="InventoryMutator.End"/>.
/// </summary>
internal sealed class InvFixtureTransferHandler : IOpHandler
{
    public const OpKind FixtureKind = (OpKind)0x00F8;

    public static readonly OpDescriptor Descriptor = OpDescriptor.For<MovePayload>(
        FixtureKind, false, 0, 0,
        InvReasons.Def(InvReason.StaleEntry), InvReasons.Def(InvReason.Blocked), InvReasons.Def(InvReason.OutOfBounds), InvReasons.Def(InvReason.CapacityExhausted));

    public InventorySystem System { get; set; } = null!;

    public OpKind Kind => FixtureKind;

    public ArenaShape? Shape { get; set; }

    /// <summary>The block sizes the validator passes; null = every block of <see cref="Shape"/>.</summary>
    public int[]? Requested { get; set; }

    public int Commits { get; private set; }

    /// <summary>Containers <c>Validate</c> touches before the source and destination (a handler planning more containers).</summary>
    public ContainerId[] ExtraTouches { get; set; } = Array.Empty<ContainerId>();

    /// <summary>TEST-ONLY contract break: <c>Validate</c> does not touch the destination, so the commit marks an untouched container.</summary>
    public bool SkipDestinationTouch { get; set; }

    public Reply Apply(in Op op, in TickContext ctx)
    {
        MovePayload p = op.PayloadAs<MovePayload>();
        var plan = new InvPlan();
        ushort reason = Validate(in p, ref plan, out ushort entrySlot);
        var extra = new InvReplyExtra();
        if (reason != 0)
        {
            System.Mutator.FillVersions(in plan, ref extra);
            Reply rejected = Reply.Reject(in op, reason);
            rejected.SetExtra(in extra);
            return rejected;
        }

        InventoryMutator m = System.Mutator;
        m.Begin(ref plan, op.Actor, InvCause.None);
        m.Transfer(ref plan, entrySlot, plan.Placement.ContainerSlot, plan.Placement.X, plan.Placement.Y, plan.Placement.Rotated);
        Shape?.Commit(System.MailIds, ctx.Tick);
        m.End(ref plan, ref extra);
        Commits++;
        return Reply.Ok(in op, in extra);
    }

    private ushort Validate(in MovePayload p, ref InvPlan plan, out ushort entrySlot)
    {
        entrySlot = 0;
        ContainerTable containers = System.Containers;
        if (!containers.TryGetSlot(new ContainerId(p.From), out ushort from) || !containers.TryGetSlot(new ContainerId(p.To), out ushort to))
        {
            return CoreReason.BadRequest;
        }

        plan.SetPrimary(from, to);
        if (!System.Entries.TryGetSlot(EntryId.Real(p.EntryId), out entrySlot) || System.Entries[entrySlot].ContainerSlot != from)
        {
            return (ushort)InvReason.StaleEntry;
        }

        ref readonly EntryRecord e = ref System.Entries[entrySlot];
        ref readonly ContainerHeader h = ref containers[to];
        bool rotated = p.Rotated != 0;
        Footprint.Canonicalise(e.W, e.H, ref rotated);
        int fw = 1;
        int fh = 1;
        if (h.Layout == ContainerLayout.Slots)
        {
            rotated = false; // a slots layout holds every entry as 1 x 1 unrotated (the mutator and PlacementRules agree)
        }
        else
        {
            Footprint.Effective(e.W, e.H, rotated, out fw, out fh);
        }

        if (p.X < 0 || p.Y < 0 || p.X + fw > h.W || p.Y + fh > h.H)
        {
            return (ushort)InvReason.OutOfBounds;
        }

        for (int y = p.Y; y < p.Y + fh; y++)
        {
            for (int x = p.X; x < p.X + fw; x++)
            {
                ushort owner = System.Occupancy.OwnerAt(to, h.W, x, y);
                if (owner != Occupancy.Free && owner != entrySlot)
                {
                    return (ushort)InvReason.Blocked;
                }
            }
        }

        // Contract (InvPlan): touch every container the commit may change; a full set is refused before any mutation.
        foreach (ContainerId extra in ExtraTouches)
        {
            if (!containers.TryGetSlot(extra, out ushort extraSlot) || plan.Touch(extraSlot) < 0)
            {
                return CoreReason.BadRequest;
            }
        }

        if (plan.Touch(from) < 0 || (!SkipDestinationTouch && plan.Touch(to) < 0))
        {
            return CoreReason.BadRequest;
        }

        plan.Placement = new InvPlacement(entrySlot, to, (byte)p.X, (byte)p.Y, rotated);
        if (Shape is not null)
        {
            foreach (int units in Requested ?? Shape.Blocks)
            {
                _ = plan.AddAcquiredBlock(units);
            }
        }

        return System.RequestCheck.CheckArenaReservation(in plan);
    }
}

/// <summary>TEST-ONLY system: the real <see cref="InventorySystem"/> plus the <see cref="InvFixtureTransferHandler"/>.</summary>
internal sealed class InventoryWithFixtureOp : ISimSystem
{
    public InventoryWithFixtureOp(InventorySystem inner, InvFixtureTransferHandler handler, SimCheckRing ring)
    {
        Inner = inner;
        Handler = handler;
        Ring = ring;
        handler.System = inner;
    }

    public InventorySystem Inner { get; }

    public InvFixtureTransferHandler Handler { get; }

    public SimCheckRing Ring { get; }

    public void Bind(SimWorldBuilder builder)
    {
        Inner.Bind(builder);
        builder.BindOp(InvFixtureTransferHandler.Descriptor, Handler);
    }
}

/// <summary>
/// A composed M-1a world (real content, real <see cref="InventorySystem"/> with the world's event log, Sim Core's own <c>PlayerJoin</c>)
/// with player A in slot 0 and player B in slot 1, a fixture external container <see cref="Chest"/> (a chest stand-in: player_inventory's
/// def and dimensions, no owner) and the <see cref="InvFixtureTransferHandler"/>. Entries are arranged directly between ticks (no event,
/// no version change).
/// </summary>
internal sealed class InvEventRig
{
    private readonly InventoryWithFixtureOp _system;

    public InvEventRig()
    {
        Sink = new CaptureSink();
        var handler = new InvFixtureTransferHandler();
        var profile = SimProfile.ForTest(
            "inventory-events",
            null,
            new SimSystemEntry(
                SystemId.GridInventory,
                ctx => new InventoryWithFixtureOp(new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots), handler, ctx.CheckRing)));
        Host = SimComposition.Build(new SimBuildArgs(profile, BuiltInContent.CreateM1a(), 42) { EventSink = Sink });
        _system = (InventoryWithFixtureOp)Host.Systems.Single();
        Join(0, 1);
        Join(1, 2);
        PlayerA = ActorId.FromEntity(Host.Services.Slots.Entry(PlayerSlot.FromValue(0)).PlayerId);
        PlayerB = ActorId.FromEntity(Host.Services.Slots.Entry(PlayerSlot.FromValue(1)).PlayerId);
        HotbarA = Inventory.HotbarOf(PlayerSlot.FromValue(0));
        InventoryA = Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        CursorA = Inventory.CursorOf(PlayerSlot.FromValue(0));
        Assert.True(Inventory.TryGetHeader(InventoryA, out ContainerHeader inv));
        Assert.True(Inventory.TryCreateContainer(inv.DefIndex, inv.W, inv.H, inv.Layout, ContainerHeader.NoOwner, out ContainerId chest));
        Chest = chest;
    }

    public SimHost Host { get; }

    public CaptureSink Sink { get; }

    public InventorySystem Inventory => _system.Inner;

    public InvFixtureTransferHandler Handler => _system.Handler;

    public SimCheckRing Ring => _system.Ring;

    public ActorId PlayerA { get; }

    public ActorId PlayerB { get; }

    public ContainerId HotbarA { get; }

    public ContainerId InventoryA { get; }

    public ContainerId CursorA { get; }

    public ContainerId Chest { get; }

    public ushort SlotOf(ContainerId id)
    {
        Assert.True(Inventory.Containers.TryGetSlot(id, out ushort slot));
        return slot;
    }

    public ref ContainerHeader Header(ContainerId id) => ref Inventory.Containers[SlotOf(id)];

    public void Subscribe(ContainerId id, byte mask) => Inventory.Subscribers.Set(SlotOf(id), mask);

    /// <summary>Arranges a mail entry of <paramref name="mailIds"/> units at (x, y) of <paramref name="container"/> (no event, no version change).</summary>
    public EntryId AddMail(ContainerId container, byte x, byte y, uint[] mailIds, byte w = 1, byte h = 1)
    {
        ushort cslot = SlotOf(container);
        EntryPool pool = Inventory.Entries;
        Assert.True(pool.TryAllocate(Host.World.CurrentTick, out ushort slot, out EntryId id));
        ref EntryRecord e = ref pool[slot];
        e.IsMail = true;
        e.MailKind = 3;
        e.AddressId = 0x0001_2345u;
        e.Flags = 1;
        e.W = w;
        e.H = h;
        e.X = x;
        e.Y = y;
        pool.SetCount(slot, (byte)mailIds.Length);
        Assert.True(Inventory.MailIds.TryCreate(mailIds, 0, out e.MailIds));
        ref ContainerHeader header = ref Inventory.Containers[cslot];
        Inventory.Occupancy.Place(cslot, header.W, header.H, x, y, w, h, slot);
        e.ContainerSlot = cslot;
        header.EntryCount++;
        header.FreeCells -= (ushort)(w * h);
        return id;
    }

    public Op Move(ContainerId from, EntryId entry, ContainerId to, short x, short y)
    {
        var op = new Op { Kind = InvFixtureTransferHandler.FixtureKind, ReqId = 77 };
        op.SetPayload(new MovePayload { From = from.Value, EntryId = entry.Value, To = to.Value, X = x, Y = y });
        return op;
    }

    /// <summary>Enqueues <paramref name="op"/> for <paramref name="actor"/>, ticks once and returns that tick's log.</summary>
    public EventEntry[] Run(ActorId actor, in Op op)
    {
        int before = Sink.Ticks.Count;
        Assert.True(Host.World.EnqueueOp(actor, in op));
        Host.World.Tick();
        Assert.Equal(before + 1, Sink.Ticks.Count);
        return Sink.Last;
    }

    /// <summary>The entries a slot's peer is sent: a non-Internal class with the slot's audience bit.</summary>
    public static EventEntry[] DeliveredTo(EventEntry[] log, int slot) =>
        log.Where(e => e.Class != ReplicationClass.Internal && (e.Audience & (1 << slot)) != 0).ToArray();

    public static Reply ReplyOf(EventEntry[] log) => Assert.Single(log, e => e.Kind == EventKind.OpReply).PayloadAs<Reply>();

    /// <summary>Records written to the check ring so far (overwritten ones included) plus rate-limited suppressions of record 12.</summary>
    public long RingWrites => Ring.Count + (long)Ring.Overwritten + Ring.Suppressed(CheckCode.InventoryArenaExhausted);

    /// <summary>Every container header, occupancy row, entry record and the arena's private state, as one comparable list.</summary>
    public List<long> Snapshot()
    {
        var values = new List<long>();
        ContainerTable containers = Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            ContainerHeader h = containers[(ushort)s];
            values.AddRange(new long[] { h.Id.Value, h.W, h.H, h.OwnerSlot, h.FreeCells, h.EntryCount, h.Version, h.LastReplaceVersion, h.StatEpoch });
            for (int y = 0; y < h.H; y++)
            {
                values.Add((long)Inventory.Occupancy.RowBits((ushort)s, y));
                for (int x = 0; x < h.W; x++)
                {
                    values.Add(Inventory.Occupancy.OwnerAt((ushort)s, h.W, x, y));
                }
            }
        }

        EntryPool pool = Inventory.Entries;
        values.Add(pool.LastId);
        values.Add(pool.LiveCount);
        for (int s = 0; s < pool.Capacity; s++)
        {
            EntryRecord r = pool[(ushort)s];
            values.AddRange(new long[] { r.Id.Value, r.Count, r.ContainerSlot, r.X, r.Y, r.Rotated ? 1 : 0, r.MailIds.Raw });
        }

        MailIdArena arena = Inventory.MailIds;
        values.Add(arena.LiveBlocks);
        values.Add(arena.LiveUnits);
        foreach (string name in new[] { "_slabs", "_freeStacks", "_live" })
        {
            var outer = (Array)typeof(MailIdArena).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(arena)!;
            foreach (Array inner in outer)
            {
                foreach (object v in inner)
                {
                    values.Add(Convert.ToInt64(v));
                }
            }
        }

        foreach (string name in new[] { "_freeCount", "_peakLive" })
        {
            foreach (int v in (int[])typeof(MailIdArena).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(arena)!)
            {
                values.Add(v);
            }
        }

        return values;
    }

    private void Join(byte slot, uint req)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = req };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
        Host.World.Tick();
    }
}

/// <summary>TEST-ONLY validate step of <see cref="InvFixtureActionHandler"/>: builds the plan, returns 0 or a reason.</summary>
internal delegate ushort InvPlanStep(ref InvPlan plan);

/// <summary>TEST-ONLY commit step of <see cref="InvFixtureActionHandler"/>: applies the plan through the mutator.</summary>
internal delegate void InvCommitStep(ref InvPlan plan);

/// <summary>
/// TEST-ONLY op handler (story grid-inventory/009): runs inside a real tick either caller-supplied steps (<see cref="ValidateStep"/> /
/// <see cref="CommitStep"/>, the mutator-backed arrange helpers) or one configured <see cref="AutoPlace"/> call (Plan → the shared
/// arena-reservation step → Begin / Commit / End). On a rejected AutoPlace it records the plan's no-fit facts after the reject was decided
/// (<see cref="RecordNoFitsOnReject"/>), standing in for an <c>IInventoryApi</c>-style caller: never inside the validate step.
/// </summary>
internal sealed class InvFixtureActionHandler : IOpHandler
{
    public const OpKind FixtureKind = (OpKind)0x00F9;

    public static readonly OpDescriptor Descriptor = OpDescriptor.For<MovePayload>(
        FixtureKind, false, 0, 0,
        InvReasons.Def(InvReason.NotAllowed), InvReasons.Def(InvReason.NotCarryable), InvReasons.Def(InvReason.NoRoom), InvReasons.Def(InvReason.CapacityExhausted));

    public InventorySystem System { get; set; } = null!;

    public OpKind Kind => FixtureKind;

    public InvPlanStep? ValidateStep { get; set; }

    public InvCommitStep? CommitStep { get; set; }

    public PlacementSubject Subject;

    public int Units;

    public ushort Source = AutoPlace.NoSource;

    public ushort[] Targets = new ushort[AutoPlacePlan.MaxTargets];

    public int TargetCount;

    public MergePolicy Policy;

    public PlaceOrder Order;

    public bool RecordNoFitsOnReject = true;

    /// <summary>The last AutoPlace plan.</summary>
    public AutoPlacePlan LastPlan;

    /// <summary>The last call's reason (0 = committed).</summary>
    public ushort LastReason;

    public Reply Apply(in Op op, in TickContext ctx)
    {
        var plan = new InvPlan();
        var extra = new InvReplyExtra();
        bool auto = ValidateStep is null;
        ushort reason = auto
            ? System.AutoPlace.Plan(ref plan, in Subject, Units, Source, Targets.AsSpan(0, TargetCount), Policy, Order, out LastPlan)
            : ValidateStep!(ref plan);
        if (reason == 0)
        {
            reason = System.RequestCheck.CheckArenaReservation(in plan);
        }

        LastReason = reason;
        if (reason != 0)
        {
            if (auto && RecordNoFitsOnReject)
            {
                System.AutoPlace.RecordNoFits(in LastPlan);
            }

            System.Mutator.FillVersions(in plan, ref extra);
            Reply rejected = Reply.Reject(in op, reason);
            rejected.SetExtra(in extra);
            return rejected;
        }

        InventoryMutator m = System.Mutator;
        m.Begin(ref plan, op.Actor, InvCause.None);
        if (auto)
        {
            System.AutoPlace.Commit(ref plan, in LastPlan);
        }
        else
        {
            CommitStep?.Invoke(ref plan);
        }

        m.End(ref plan, ref extra);
        return Reply.Ok(in op, in extra);
    }
}

/// <summary>TEST-ONLY in-tick caller (story grid-inventory/012): the body a <see cref="InvPhaseHook"/> runs inside the world's <c>MailStep</c> phase.</summary>
internal delegate void InvPhaseAction(in TickContext ctx);

/// <summary>
/// TEST-ONLY phase binding (story grid-inventory/012): runs <see cref="Action"/> inside <c>MailStep</c> (phase 8, where Mail calls
/// <c>Spawn</c>), standing in for an in-tick <see cref="IInventoryApi"/> caller. A null action is a no-op; invoking a cached delegate
/// allocates nothing.
/// </summary>
internal sealed class InvPhaseHook : ISimPhase
{
    public InvPhaseAction? Action { get; set; }

    public void Run(in TickContext ctx) => Action?.Invoke(in ctx);
}

/// <summary>TEST-ONLY system: the real <see cref="InventorySystem"/> plus the <see cref="InvFixtureActionHandler"/> and an <see cref="InvPhaseHook"/>.</summary>
internal sealed class InventoryWithActionOp : ISimSystem
{
    public InventoryWithActionOp(InventorySystem inner, InvFixtureActionHandler handler)
    {
        Inner = inner;
        Handler = handler;
        handler.System = inner;
    }

    public InventorySystem Inner { get; }

    public InvFixtureActionHandler Handler { get; }

    public InvPhaseHook Hook { get; } = new();

    public void Bind(SimWorldBuilder builder)
    {
        Inner.Bind(builder);
        builder.BindOp(InvFixtureActionHandler.Descriptor, Handler);
        builder.Bind(PhaseId.MailStep, Hook);
    }
}

/// <summary>
/// The story grid-inventory/009 rig: a composed M-1a world (real content, stat sheet, <see cref="InventorySystem"/> and event log) with
/// player A in slot 0, the <see cref="InvFixtureActionHandler"/>, and mutator-backed arrange helpers, so the StackKey index is maintained
/// exactly as in production (Planner R8: the direct-write fixtures would leave it empty). Fixture grids are <c>player_inventory</c>-def
/// instances with explicit dims (no chest/bike def at M-1a). Every mutation runs in its own tick.
/// </summary>
internal sealed class InvAutoPlaceRig
{
    private readonly InventoryWithActionOp _system;
    private readonly GatedSink _gate;
    private uint _reqId = 100;

    /// <summary>
    /// Builds the rig; <paramref name="mailIdArenaUnits"/> and <paramref name="entryPoolCapacity"/> override the arena budget and the
    /// entry pool of this test world only (the defaults are the balance's); <paramref name="content"/> replaces the M-1a built-in set
    /// (story 012's perf fixture adds a container def); <paramref name="tune"/> edits the built settings (story 016's synthetic weight knobs).
    /// </summary>
    public InvAutoPlaceRig(int? mailIdArenaUnits = null, ContentSet? content = null, int? entryPoolCapacity = null, Func<InventorySettings, InventorySettings>? tune = null)
    {
        Sink = new CaptureSink();
        _gate = new GatedSink(Sink);
        var handler = new InvFixtureActionHandler();
        var profile = SimProfile.ForTest(
            "inventory-autoplace",
            null,
            new SimSystemEntry(
                SystemId.GridInventory,
                ctx => new InventoryWithActionOp(
                    mailIdArenaUnits is null && entryPoolCapacity is null && tune is null
                        ? new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)
                        : new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, Settings(ctx.Content.Inventory, mailIdArenaUnits, entryPoolCapacity, tune), ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots),
                    handler)));
        Host = SimComposition.Build(new SimBuildArgs(profile, content ?? BuiltInContent.CreateM1a(), 42) { EventSink = _gate });
        _system = (InventoryWithActionOp)Host.Systems.Single();
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 1 };
        join.SetPayload(new PlayerJoinPayload { Slot = 0 });
        Assert.True(Host.World.EnqueueOp(ActorId.System, in join));
        Host.World.Tick();
        PlayerA = ActorId.FromEntity(Host.Services.Slots.Entry(PlayerSlot.FromValue(0)).PlayerId);
        InventoryA = SlotOf(Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0)));
        HotbarA = SlotOf(Inventory.HotbarOf(PlayerSlot.FromValue(0)));
        Intake = SlotOf(Inventory.IntakeContainer);
    }

    public SimHost Host { get; }

    public CaptureSink Sink { get; }

    private static InventorySettings Settings(InventoryBalance balance, int? mailIdArenaUnits, int? entryPoolCapacity, Func<InventorySettings, InventorySettings>? tune)
    {
        InventorySettings s = InventorySettings.FromBalance(balance);
        s = s with
        {
            MailIdArenaUnits = mailIdArenaUnits ?? s.MailIdArenaUnits,
            EntryPoolCapacity = entryPoolCapacity ?? s.EntryPoolCapacity,
        };
        return tune is null ? s : tune(s);
    }

    /// <summary>False stops the copy of each tick's log into <see cref="Sink"/> (which allocates), for the 0 B windows; true by default.</summary>
    public bool CaptureEvents
    {
        get => _gate.Enabled;
        set => _gate.Enabled = value;
    }

    public InventorySystem Inventory => _system.Inner;

    public InvFixtureActionHandler Handler => _system.Handler;

    /// <summary>The <c>MailStep</c> hook: an action set here runs inside every following tick (story 012's in-tick API caller).</summary>
    public InvPhaseHook Hook => _system.Hook;

    public ContentSet Content => Host.Content;

    public SimCounters Counters => Host.World.Counters;

    public ActorId PlayerA { get; }

    /// <summary>Player A's 8 x 2 player_inventory.</summary>
    public ushort InventoryA { get; }

    /// <summary>Player A's 8 x 1 slots hotbar (slot 0 reserved).</summary>
    public ushort HotbarA { get; }

    /// <summary>The 20 x 16 mail-only po_intake.</summary>
    public ushort Intake { get; }

    public EntryPool Entries => Inventory.Entries;

    public StackIndex Index => Inventory.Mutator.StackIndex;

    public ref ContainerHeader Header(ushort slot) => ref Inventory.Containers[slot];

    public ushort SlotOf(ContainerId id)
    {
        Assert.True(Inventory.Containers.TryGetSlot(id, out ushort slot));
        return slot;
    }

    /// <summary>A fixture grid: a <paramref name="def"/>-def instance of <paramref name="w"/> x <paramref name="h"/> with no owner.</summary>
    public ushort CreateGrid(int w, int h, string def = "player_inventory")
    {
        DefIndex d = PlacementFixture.Index(Content.Containers, def);
        ContainerDef cd = Content.Containers[d];
        Assert.True(Inventory.TryCreateContainer(d, w, h, cd.Layout, ContainerHeader.NoOwner, out ContainerId id));
        return SlotOf(id);
    }

    public DefIndex KindIndex(string kind) => PlacementFixture.Index(Content.MailKinds, kind);

    public DefIndex ItemIndex(string item) => PlacementFixture.Index(Content.Items, item);

    /// <summary>A mail subject with <c>effMax</c> read from the world's sheet now.</summary>
    public PlacementSubject Mail(string kind, uint address, byte flags = 0)
    {
        DefIndex k = KindIndex(kind);
        return PlacementSubject.ForMail(Content.MailKinds[k], k, address, flags, Host.StatSheet);
    }

    public PlacementSubject Item(string item)
    {
        DefIndex i = ItemIndex(item);
        return PlacementSubject.ForItem(Content.Items[i], i);
    }

    public PlacementSubject SubjectOf(ushort entry) => PlacementSubject.Of(Content, Host.StatSheet, in Entries[entry]);

    /// <summary>Consecutive ids <paramref name="first"/>, first + 1, ... (<paramref name="count"/> of them).</summary>
    public static uint[] Ids(uint first, int count)
    {
        var ids = new uint[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = first + (uint)i;
        }

        return ids;
    }

    public uint[] IdsOf(ushort entry) => Inventory.MailIds.Read(in Entries[entry].MailIds).ToArray();

    /// <summary>A detached (unplaced) mail entry holding <paramref name="ids"/> (between ticks: allocation raises nothing).</summary>
    public ushort DetachedMail(string kind, uint address, uint[] ids, byte flags = 0)
    {
        DefIndex k = KindIndex(kind);
        MailKindDef def = Content.MailKinds[k];
        Assert.True(Entries.TryAllocate(Host.World.CurrentTick, out ushort slot, out _));
        ref EntryRecord e = ref Entries[slot];
        e.IsMail = true;
        e.MailKind = k.Value;
        e.AddressId = address;
        e.Flags = flags;
        e.W = (byte)def.GridW;
        e.H = (byte)def.GridH;
        Entries.SetCount(slot, (byte)ids.Length);
        Assert.True(Inventory.MailIds.TryCreate(ids, 0, out e.MailIds));
        return slot;
    }

    /// <summary>A detached item entry of <paramref name="count"/>.</summary>
    public ushort DetachedItem(string item, byte count)
    {
        DefIndex i = ItemIndex(item);
        ItemDef def = Content.Items[i];
        Assert.True(Entries.TryAllocate(Host.World.CurrentTick, out ushort slot, out _));
        ref EntryRecord e = ref Entries[slot];
        e.ItemId = i.Value;
        e.W = (byte)def.GridW;
        e.H = (byte)def.GridH;
        Entries.SetCount(slot, count);
        return slot;
    }

    /// <summary>Places a mail entry through <see cref="InventoryMutator.Place"/> (one tick).</summary>
    public ushort PlaceMail(ushort container, string kind, int x, int y, uint address, uint[] ids, bool rotated = false, byte flags = 0)
    {
        ushort slot = DetachedMail(kind, address, ids, flags);
        PlaceDetached(slot, container, x, y, rotated);
        return slot;
    }

    /// <summary>Places an item entry through <see cref="InventoryMutator.Place"/> (one tick).</summary>
    public ushort PlaceItem(ushort container, string item, int x, int y, byte count, bool rotated = false)
    {
        ushort slot = DetachedItem(item, count);
        PlaceDetached(slot, container, x, y, rotated);
        return slot;
    }

    public void PlaceDetached(ushort entry, ushort container, int x, int y, bool rotated = false)
    {
        Reply r = Step(
            (ref InvPlan p) => p.Touch(container) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => Inventory.Mutator.Place(ref p, entry, container, (byte)x, (byte)y, rotated));
        Assert.True(r.IsOk);
    }

    /// <summary>Runs one tick whose fixture op runs <paramref name="validate"/> then (on 0) <paramref name="commit"/>; returns the reply.</summary>
    public Reply Step(InvPlanStep validate, InvCommitStep commit)
    {
        Handler.ValidateStep = validate;
        Handler.CommitStep = commit;
        try
        {
            return ReplyOf(RunTick());
        }
        finally
        {
            Handler.ValidateStep = null;
            Handler.CommitStep = null;
        }
    }

    /// <summary>One AutoPlace through the fixture op (one tick); the plan is <see cref="InvFixtureActionHandler.LastPlan"/>.</summary>
    public ushort AutoPlace(in PlacementSubject subject, int units, ushort source, MergePolicy policy, PlaceOrder order, params ushort[] targets)
    {
        Handler.Subject = subject;
        Handler.Units = units;
        Handler.Source = source;
        targets.CopyTo(Handler.Targets, 0);
        Handler.TargetCount = targets.Length;
        Handler.Policy = policy;
        Handler.Order = order;
        Reply r = ReplyOf(RunTick());
        Assert.Equal(Handler.LastReason, r.Reason);
        return r.Reason;
    }

    /// <summary>AutoPlace of a source entry's whole count, its subject read now, merge policy and pass-first order.</summary>
    public ushort AutoPlace(ushort source, params ushort[] targets) =>
        AutoPlace(SubjectOf(source), Entries[source].Count, source, MergePolicy.Merge, PlaceOrder.PassFirst, targets);

    public AutoPlacePlan LastPlan => Handler.LastPlan;

    /// <summary>The tick's log (between ticks only).</summary>
    public EventEntry[] LastLog => Sink.Last;

    /// <summary>Enqueues the fixture op for player A, ticks once and returns that tick's log.</summary>
    public EventEntry[] RunTick()
    {
        TickOnce();
        return Sink.Last;
    }

    /// <summary>Enqueues the fixture op for player A and ticks once (allocation-free; the outcome is <see cref="InvFixtureActionHandler.LastReason"/>).</summary>
    public void TickOnce()
    {
        var op = new Op { Kind = InvFixtureActionHandler.FixtureKind, ReqId = _reqId++ };
        op.SetPayload(default(MovePayload));
        Assert.True(Host.World.EnqueueOp(PlayerA, in op));
        Host.World.Tick();
    }

    /// <summary>Frees a detached entry (its arena block, then its pool slot) between ticks.</summary>
    public void FreeDetached(ushort entry)
    {
        ref EntryRecord e = ref Entries[entry];
        Assert.Equal(EntryRecord.NoContainer, e.ContainerSlot);
        Inventory.MailIds.Free(in e.MailIds);
        Entries.Free(entry);
    }

    public static Reply ReplyOf(EventEntry[] log) => Assert.Single(log, e => e.Kind == EventKind.OpReply).PayloadAs<Reply>();

    /// <summary>The live entry whose top-left is (x, y) of the container, or <see cref="Occupancy.Free"/>.</summary>
    public ushort EntryAt(ushort container, int x, int y)
    {
        ushort owner = Inventory.Occupancy.OwnerAt(container, Header(container).W, x, y);
        return owner != Occupancy.Free && Entries[owner].X == x && Entries[owner].Y == y ? owner : Occupancy.Free;
    }
}

/// <summary>TEST-ONLY sink gate of <see cref="InvAutoPlaceRig"/>: forwards each tick's log to the inner sink while <see cref="Enabled"/>.</summary>
internal sealed class GatedSink : IEventSink
{
    private readonly IEventSink _inner;

    public GatedSink(IEventSink inner) => _inner = inner;

    public bool Enabled { get; set; } = true;

    public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
    {
        if (Enabled)
        {
            _inner.Deliver(tick, entries, in overflow);
        }
    }
}
