using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.ManualDelivery;

/// <summary>
/// Inventory, Players, Mail and Delivery composed over the 10-lot M-1a street, Delivery through the shipped
/// <see cref="SimComposition.ConstructManualDelivery"/> case, Mail with a settable stub wallet (<c>TryDebitPenalty</c> true iff
/// <c>wallet − cents ≥ floor</c>) and a phase-12 probe for building stacks inside a tick. Players 0 and 1 join; deliveries are open.
/// Requests go through the op queue and are answered in the next tick's phase 0.
/// </summary>
internal sealed class DeliverRig
{
    /// <summary>The destination the requests target (registry row 0).</summary>
    public const int Right = 0;

    /// <summary>A wrong-house address (registry row 1).</summary>
    public const int Wrong = 1;

    private static readonly ContentSet _m1a = BuiltInContent.CreateM1a();
    private readonly ContentSet _content;
    private readonly uint[] _seq = new uint[PlayerSlot.Count];
    private Action? _next;
    private uint _req = 1;

    public DeliverRig(ContentSet? content = null)
    {
        _content = content ?? _m1a;
        MailSystem? mail = null;
        InventorySystem? inventory = null;
        PlayersSystem? players = null;
        DeliverySystem? delivery = null;
        StubWallet? wallet = null;
        var probe = new ProbeSystem(() => _next?.Invoke());
        SimProfile profile = SimProfile.ForTest(
            "md005",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx => inventory = new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
            new SimSystemEntry(SystemId.PlayerCharacter, ctx => players = new PlayersSystem(ctx.World!, ctx.Content, ctx.Samples, ctx.Slots, ctx.Tick, ctx.CheckRing, ctx.Counters)),
            new SimSystemEntry(SystemId.MailDestinations, ctx =>
            {
                wallet = new StubWallet();
                return mail = new MailSystem(
                    ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), wallet, ctx.World!, ctx.Content,
                    ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots);
            }),
            new SimSystemEntry(SystemId.ManualDelivery, ctx => delivery = SimComposition.ConstructManualDelivery(ctx)!),
            new SimSystemEntry(SystemId.RunStructure, _ => probe));
        Capture = new CaptureSink();
        Host = SimComposition.Build(new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance, EventSink = Capture });
        Mail = mail!;
        Inventory = inventory!;
        Players = players!;
        Delivery = delivery!;
        Wallet = wallet!;
        Join(0);
        Join(1);
        Mail.Registry.Deliveries.Set(true, 0, DeliveriesState.DefaultUnlockedMask);
    }

    public SimHost Host { get; }

    public MailSystem Mail { get; }

    public InventorySystem Inventory { get; }

    public PlayersSystem Players { get; }

    public DeliverySystem Delivery { get; }

    public StubWallet Wallet { get; }

    public CaptureSink Capture { get; }

    public uint Now => Host.World.CurrentTick;

    public void Run(Action? body = null)
    {
        _next = body;
        try
        {
            Host.World.Tick();
        }
        finally
        {
            _next = null;
        }
    }

    public EntityId PlayerOf(byte slot) => Host.Services.Slots.PlayerOf(PlayerSlot.FromValue(slot));

    public ActorId Actor(byte slot) => ActorId.FromEntity(PlayerOf(slot));

    public EntityId Dest(int ordinal) => Mail.Registry.EntityIdOf(new DestinationOrdinal((ushort)ordinal));

    public ContainerId Cursor(byte slot) => Inventory.CursorOf(PlayerSlot.FromValue(slot));

    public ContainerId Hotbar(byte slot) => Inventory.HotbarOf(PlayerSlot.FromValue(slot));

    public ContainerId Pack(byte slot) => Inventory.PlayerInventoryOf(PlayerSlot.FromValue(slot));

    public uint Version(ContainerId c)
    {
        Assert.True(Inventory.TryGetHeader(c, out ContainerHeader h));
        return h.Version;
    }

    public int Units(EntryRef e) => Inventory.TryReadEntry(e, out EntryView v) ? v.Units : 0;

    /// <summary>A new stack of <paramref name="count"/> units of <paramref name="kind"/> addressed to registry row <paramref name="ordinal"/>, in <paramref name="c"/> (built in a tick).</summary>
    public EntryRef Give(byte slot, ContainerId c, string kind, int ordinal, int count)
    {
        EntryRef made = default;
        Run(() =>
        {
            DefIndex k = Kind(kind);
            ushort value = (ushort)_content.MailKinds[k].BaseValueCents;
            AddressId address = Mail.Registry.AddressOf(new DestinationOrdinal((ushort)ordinal));
            EntryId entry = default;
            for (int i = 0; i < count; i++)
            {
                MailId id = Mail.Table.Create(k, address, value, (byte)Mail.CurrentShift, MailFlags.None);
                Assert.Equal(1, Inventory.Spawn(Inventory.IntakeContainer, k, address.Value, 0, new[] { id }, MergePolicy.NoMerge).Placed);
                Assert.True(Inventory.TakeUnit(Inventory.IntakeContainer, Selector.Fifo, ActorId.System, out UnitRef u));
                Assert.True(Inventory.TryInsert(c, in u, Actor(slot)));
                if (i == 0)
                {
                    entry = EntryId.Real(Inventory.Entries.LastId);   // the first insert made the stack; the rest merged into it
                }
            }

            made = new EntryRef(c, entry);
        });
        Assert.Equal(count, Units(made));
        return made;
    }

    /// <summary>Picks <paramref name="e"/> onto the slot's cursor with a real <c>Pick</c> op (applied in the next tick); returns the cursor ref.</summary>
    public EntryRef PickToCursor(byte slot, EntryRef e)
    {
        var op = new Op { Kind = InvOpKinds.Pick, ReqId = _req++ };
        op.SetPayload(new PickPayload { From = e.Container.Value, EntryId = e.Entry.Value });
        Assert.True(Host.World.EnqueueOp(Actor(slot), in op));
        Run();
        var onCursor = new EntryRef(Cursor(slot), e.Entry);
        Assert.True(Units(onCursor) > 0);
        return onCursor;
    }

    /// <summary>Reports the slot's feet <paramref name="dx"/> mm along x from where a claim at destination <paramref name="ordinal"/>'s anchor puts the chest, then ticks so Players ingests it; returns the claim.</summary>
    public ClaimedPosition Stand(byte slot, int ordinal, int dx = 0, SampleFlags flags = SampleFlags.None)
    {
        Vec3Mm anchor = Mail.Registry.AnchorOf(new DestinationOrdinal((ushort)ordinal));
        var feet = new ClaimedPosition(anchor.X + dx, anchor.Y - Players.Settings.ChestMm, anchor.Z);
        var s = new TransformSample { Seq = ++_seq[slot], PosXMm = feet.X, PosYMm = feet.Y, PosZMm = feet.Z, Flags = (byte)flags };
        Assert.True(Host.World.TryWriteSample(PlayerSlot.FromValue(slot), in s));
        Run();
        return feet;
    }

    /// <summary>Queues one <c>deliver_matching</c> from <paramref name="slot"/> for registry row <paramref name="ordinal"/> (no tick).</summary>
    public void Queue(byte slot, int ordinal, in ClaimedPosition claim, uint selectedEntryId = 0, uint? holdStartTick = null, EntityId? mailbox = null)
    {
        var op = new Op { Kind = DeliveryOpKinds.DeliverMatching, ReqId = _req++ };
        op.SetPayload(new DeliverMatchingPayload
        {
            MailboxId = (mailbox ?? Dest(ordinal)).Value,
            SelectedEntryId = selectedEntryId,
            HoldStartTick = holdStartTick ?? Now,
            Claim = claim,
        });
        Assert.True(Host.World.EnqueueOp(Actor(slot), in op));
    }

    /// <summary>Stands at the anchor, queues one request and runs the applying tick; returns its reply.</summary>
    public Reply Deliver(byte slot, int ordinal = Right, uint selectedEntryId = 0)
    {
        ClaimedPosition claim = Stand(slot, ordinal);
        Queue(slot, ordinal, in claim, selectedEntryId);
        Run();
        return Assert.Single(Replies());
    }

    public Reply[] Replies() =>
        Capture.Last.Where(x => x.Kind == EventKind.OpReply).Select(x => x.PayloadAs<Reply>()).Where(r => r.Kind == DeliveryOpKinds.DeliverMatching).ToArray();

    public DeliveryOutcomePayload[] Outcomes() =>
        Capture.Last.Where(x => x.Kind == MailEventKinds.DeliveryOutcome).Select(x => x.PayloadAs<DeliveryOutcomePayload>()).ToArray();

    public DefIndex Kind(string id)
    {
        ReadOnlySpan<MailKindDef> kinds = _content.MailKinds.AsSpan();
        for (int i = 0; i < kinds.Length; i++)
        {
            if (kinds[i].Id == id)
            {
                return DefIndex.FromValue((ushort)i);
            }
        }

        throw new ArgumentException(id);
    }

    private void Join(byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = _req++ };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
        Run();
        Assert.False(PlayerOf(slot).IsNone);
    }

    /// <summary>A stub wallet: a settable balance; a penalty is refused (and nothing changes) when it would land below the floor.</summary>
    public sealed class StubWallet : IWalletGate
    {
        public long Balance { get; set; }

        public int Debits { get; private set; }

        public bool TryDebitPenalty(Cents amount, Cents floor)
        {
            Debits++;
            if (Balance - amount.Value < floor.Value)
            {
                return false;
            }

            Balance -= amount.Value;
            return true;
        }

        public void Credit(Cents amount, CreditReason reason) => Balance += amount.Value;
    }

    private sealed class ProbeSystem : ISimSystem, ISimPhase
    {
        private readonly Action _body;

        public ProbeSystem(Action body)
        {
            _body = body;
        }

        public void Bind(SimWorldBuilder builder) => builder.Bind(PhaseId.RunStep, this);

        public void Run(in TickContext ctx) => _body();
    }
}
