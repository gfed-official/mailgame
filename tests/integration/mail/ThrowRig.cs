using System.Runtime.InteropServices;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 008's world: the real Inventory and Mail on the 10-lot M-1a street with an FX-W recording wallet, plus two
/// caller stubs bound to their real phases: a Delivery stand-in whose test-only op (phase 0) calls <see cref="IMailApi.ConsumeHand"/>,
/// and a Throwing stand-in that holds detached entries in Flight and calls <see cref="IMailApi.ResolveThrownLanding"/> in phase 4, in
/// the order the flights were armed. The real Throwing caller is throwing-catching story 012. Setup bodies run in a phase-12 probe.
/// </summary>
internal sealed class ThrowRig
{
    /// <summary>The destination every throw and hand-over goes to (registry row 0).</summary>
    public const int Door = 0;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();
    private Action? _setup;
    private uint _req = 1;

    public ThrowRig()
    {
        MailSystem? mail = null;
        InventorySystem? inventory = null;
        RecordingWallet? wallet = null;
        HandOpStub? hand = null;
        FlightStub? flights = null;
        var probe = new SetupProbe(() => _setup?.Invoke());
        SimProfile profile = SimProfile.ForTest(
            "md008",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx => inventory = new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
            new SimSystemEntry(SystemId.MailDestinations, ctx =>
            {
                wallet = new RecordingWallet();
                return mail = new MailSystem(
                    ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), wallet, ctx.World!, ctx.Content,
                    ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots);
            }),
            new SimSystemEntry(SystemId.ManualDelivery, ctx => hand = new HandOpStub(ctx.Api<IMailApi>(SystemId.MailDestinations))),
            new SimSystemEntry(SystemId.Throwing, ctx => flights = new FlightStub(ctx.Api<IMailApi>(SystemId.MailDestinations))),
            new SimSystemEntry(SystemId.RunStructure, _ => probe));
        Capture = new CaptureSink();
        Host = SimComposition.Build(new SimBuildArgs(profile, _content, 8) { WorldSource = M1aWorldSource.Instance, EventSink = Capture });
        Mail = mail!;
        Inventory = inventory!;
        Wallet = wallet!;
        HandOps = hand!;
        Flights = flights!;
        Join(0);
    }

    public SimHost Host { get; }

    public MailSystem Mail { get; }

    public InventorySystem Inventory { get; }

    public RecordingWallet Wallet { get; }

    public HandOpStub HandOps { get; }

    public FlightStub Flights { get; }

    public CaptureSink Capture { get; }

    public uint Now => Host.World.CurrentTick;

    public EntityId DoorId => Mail.Registry.EntityIdOf(new DestinationOrdinal(Door));

    /// <summary>Runs one tick; <paramref name="setup"/>, if any, runs in its phase 12.</summary>
    public void Tick(Action? setup = null)
    {
        _setup = setup;
        try
        {
            Host.World.Tick();
        }
        finally
        {
            _setup = null;
        }
    }

    public void Open() => Mail.Registry.Deliveries.Set(true, 0, DeliveriesState.DefaultUnlockedMask);

    public EntityId PlayerOf(byte slot) => Host.Services.Slots.PlayerOf(PlayerSlot.FromValue(slot));

    public uint AddressOf(int ordinal) => Mail.Registry.AddressOf(new DestinationOrdinal((ushort)ordinal)).Value;

    public void Join(byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = _req++ };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
        Tick();
        Assert.False(PlayerOf(slot).IsNone);
    }

    public void Leave(byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerLeave, ReqId = _req++ };
        op.SetPayload(new PlayerLeavePayload { PlayerId = PlayerOf(slot).Value });
        Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
        Tick();
        Assert.True(PlayerOf(slot).IsNone);
    }

    /// <summary>
    /// In a tick: a new hotbar stack of <paramref name="count"/> letters addressed to registry row <paramref name="ordinal"/>, each with
    /// spawn value <paramref name="spawnValue"/> (a letter's penalty is half of it, rounded half up).
    /// </summary>
    public EntryRef Give(byte slot, int ordinal, int count, ushort spawnValue = 8)
    {
        DefIndex letter = Letter();
        AddressId address = Mail.Registry.AddressOf(new DestinationOrdinal((ushort)ordinal));
        ContainerId hotbar = Inventory.HotbarOf(PlayerSlot.FromValue(slot));
        ActorId actor = ActorId.FromEntity(PlayerOf(slot));
        EntryId entry = default;
        for (int i = 0; i < count; i++)
        {
            MailId id = Mail.Table.Create(letter, address, spawnValue, (byte)Mail.CurrentShift, MailFlags.None);
            Assert.Equal(1, Inventory.Spawn(Inventory.IntakeContainer, letter, address.Value, 0, new[] { id }, MergePolicy.NoMerge).Placed);
            Assert.True(Inventory.TakeUnit(Inventory.IntakeContainer, Selector.Fifo, ActorId.System, out UnitRef u));
            Assert.True(Inventory.TryInsert(hotbar, in u, actor));
            if (i == 0)
            {
                entry = EntryId.Real(Inventory.Entries.LastId);   // the first insert made the stack; the rest merged into it
            }
        }

        return new EntryRef(hotbar, entry);
    }

    /// <summary>In a tick: <see cref="Give"/>, then the whole stack detached for a throw; returns its Flight-form ref.</summary>
    public EntryRef GiveFlying(byte slot, int ordinal, int count, ushort spawnValue = 8)
    {
        EntryRef held = Give(slot, ordinal, count, spawnValue);
        DetachResult r = Inventory.DetachForThrow(PlayerSlot.FromValue(slot), held.Entry, (byte)count);
        Assert.Equal((InvReason)0, r.Reason);
        return new EntryRef(ContainerId.None, r.Entry);
    }

    /// <summary>Enqueues a hand-over of one entry at <see cref="Door"/> (applied in the next tick's phase 0).</summary>
    public void EnqueueHand(byte slot, EntryRef e)
    {
        var op = new Op { Kind = HandOpStub.Kind, ReqId = _req++ };
        op.SetPayload(new HandOpPayload { Slot = slot, Container = e.Container.Value, Entry = e.Entry.Value });
        Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
    }

    /// <summary>Arms a landing at <see cref="Door"/> for the next tick's phase 4.</summary>
    public void Arm(EntryRef flying, byte thrower = 0, int thrownPm = 1000, uint? releaseTick = null) =>
        Flights.Pending.Add(new Landing(DoorId, flying, PlayerSlot.FromValue(thrower), thrownPm, releaseTick ?? Now));

    public T[] Events<T>(EventKind kind)
        where T : unmanaged =>
        Capture.Last.Where(x => x.Kind == kind).Select(x => x.PayloadAs<T>()).ToArray();

    private static DefIndex Letter()
    {
        ReadOnlySpan<MailKindDef> kinds = _content.MailKinds.AsSpan();
        for (int i = 0; i < kinds.Length; i++)
        {
            if (kinds[i].Id == "letter")
            {
                return DefIndex.FromValue((ushort)i);
            }
        }

        throw new InvalidOperationException("no letter kind");
    }

    private sealed class SetupProbe : ISimSystem, ISimPhase
    {
        private readonly Action _body;

        public SetupProbe(Action body) => _body = body;

        public void Bind(SimWorldBuilder builder) => builder.Bind(PhaseId.RunStep, this);

        public void Run(in TickContext ctx) => _body();
    }
}

/// <summary>One armed landing.</summary>
internal readonly record struct Landing(EntityId Dest, EntryRef Entry, PlayerSlot Thrower, int ThrownPm, uint ReleaseTick);

/// <summary>FX-W: a settable wallet that records each debit; <c>TryDebitPenalty</c> is true iff <c>balance − amount ≥ floor</c>.</summary>
internal sealed class RecordingWallet : IWalletGate
{
    public long Balance { get; set; }

    public int Calls { get; private set; }

    public List<long> Debits { get; } = new();

    public bool TryDebitPenalty(Cents amount, Cents floor)
    {
        Calls++;
        if (Balance - amount.Value < floor.Value)
        {
            return false;
        }

        Balance -= amount.Value;
        Debits.Add(amount.Value);
        return true;
    }

    public void Credit(Cents amount, CreditReason reason)
    {
        Calls++;
        Balance += amount.Value;
    }
}

/// <summary>Payload of the TEST-ONLY hand-over op (9 B, Pack = 1).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct HandOpPayload
{
    public byte Slot;
    public uint Container;
    public uint Entry;
}

/// <summary>The Delivery stand-in: its phase-0 op hands one entry to <see cref="ThrowRig.Door"/> with the hold started this tick.</summary>
internal sealed class HandOpStub : ISimSystem, IOpHandler
{
    /// <summary>The test-only op kind (never registered by production).</summary>
    public const OpKind Kind = (OpKind)0x0F08;

    private readonly IMailApi _mail;

    public HandOpStub(IMailApi mail) => _mail = mail;

    OpKind IOpHandler.Kind => Kind;

    public List<(HandConsumeSummary Summary, PhaseId Phase)> Applied { get; } = new();

    public void Bind(SimWorldBuilder builder) => builder.BindOp(OpDescriptor.For<HandOpPayload>(Kind, true, 0, 0), this);

    public Reply Apply(in Op op, in TickContext ctx)
    {
        HandOpPayload p = op.PayloadAs<HandOpPayload>();
        EntryRef e = new(new ContainerId(p.Container), EntryId.FromValue(p.Entry));
        HandConsumeSummary s = _mail.ConsumeHand(_mail.DestinationAt(ThrowRig.Door), new[] { e }, PlayerSlot.FromValue(p.Slot), ctx.Tick);
        Applied.Add((s, ctx.Phase));
        return Reply.Ok(in op);
    }
}

/// <summary>The Throwing stand-in: holds the armed flights (their entries stay in Flight) and lands them in phase 4, in arming order.</summary>
internal sealed class FlightStub : ISimSystem, ISimPhase
{
    private readonly IMailApi _mail;

    public FlightStub(IMailApi mail) => _mail = mail;

    public List<Landing> Pending { get; } = new();

    public List<(LandingResult Result, PhaseId Phase)> Results { get; } = new();

    public void Bind(SimWorldBuilder builder) => builder.Bind(PhaseId.ThrowingStep, this);

    public void Run(in TickContext ctx)
    {
        foreach (Landing l in Pending)
        {
            EntryRef e = l.Entry;
            Results.Add((_mail.ResolveThrownLanding(l.Dest, in e, l.Thrower, l.ThrownPm, l.ReleaseTick), ctx.Phase));
        }

        Pending.Clear();
    }
}
