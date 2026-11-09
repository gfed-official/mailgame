using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Inventory;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing stories 010 and 011: a world built through <see cref="SimComposition.Build"/> with the real Grid Inventory, Player Character,
/// Mail and the shipped <see cref="ThrowingSystem"/> (its own <c>Bind</c>), over a supplied <see cref="WorldTables"/>, with a capture
/// sink. Two test-only seams: the selected hotbar slot is <see cref="Selection"/> (no production owner yet), and Inventory carries
/// story grid-inventory/009's fixture op and phase-8 hook, so hotbar entries are placed through the real mutator and flights can be
/// injected through the real <see cref="IInventoryApi.DetachForThrow"/>. The Throwing system may get a wrapped inventory or a mailbox
/// port double. Players join on construction.
/// </summary>
internal sealed class ThrowWorldRig
{
    private readonly CaptureSink _sink = new();
    private readonly InventoryWithActionOp _inv;
    private uint _req = 1000;
    private uint _seq;

    public ThrowWorldRig(WorldTables world, int players = 1, Func<IInventoryApi, IInventoryApi>? wrapInventory = null, IMailboxLanding? mailbox = null)
    {
        World = world;
        InventoryWithActionOp? inv = null;
        PlayersSystem? pc = null;
        MailSystem? mail = null;
        var profile = SimProfile.ForTest(
            "throw-world",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx => inv = new InventoryWithActionOp(
                new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots), new InvFixtureActionHandler())),
            new SimSystemEntry(SystemId.PlayerCharacter, ctx => pc = new PlayersSystem(
                world, ctx.Content, ctx.Samples, ctx.Slots, ctx.Tick, ctx.CheckRing, ctx.Counters, inv!.Inner)),
            new SimSystemEntry(SystemId.MailDestinations, ctx => mail = new MailSystem(
                ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, inv!.Inner, ctx.WalletGate, world, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots)),
            new SimSystemEntry(SystemId.Throwing, ctx => new ThrowingSystem(
                ctx.Events,
                ctx.Slots,
                wrapInventory is null ? inv!.Inner : wrapInventory(inv!.Inner),
                mail!,
                pc!,
                world,
                new ThrowSettings(ctx.Content.Throw),
                new ReceiverRanks(ctx.Content.Throw),
                ctx.Content.Player.ClaimToleranceMm,
                ctx.StatSheet,
                ctx.EntityIds,
                ctx.CheckRing,
                ctx.Counters,
                Selection,
                mailbox)));
        Host = SimComposition.Build(new SimBuildArgs(profile, BuiltInContent.CreateM1a(), 11) { World = world, EventSink = _sink });
        _inv = inv!;
        Players = pc!;
        Mail = mail!;
        Throwing = Host.Systems.OfType<ThrowingSystem>().Single();
        for (byte s = 0; s < players; s++)
        {
            var join = new Op { Kind = OpKind.PlayerJoin, ReqId = ++_req };
            join.SetPayload(new PlayerJoinPayload { Slot = s });
            Assert.True(Sim.EnqueueOp(ActorId.System, in join));
            Sim.Tick();
        }
    }

    public WorldTables World { get; }

    public SimHost Host { get; }

    public SimWorld Sim => Host.World;

    public InventorySystem Inventory => _inv.Inner;

    public PlayersSystem Players { get; }

    public MailSystem Mail { get; }

    public ThrowingSystem Throwing { get; }

    public FlightTable Flights => Throwing.Flights;

    /// <summary>The selected hotbar index per slot (all 0 until a test sets one).</summary>
    public HotbarDouble Selection { get; } = new();

    public List<EventEntry[]> Ticks => _sink.Ticks;

    public EventEntry[] LastLog => _sink.Last;

    public ContentSet Content => Host.Content;

    public static PlayerSlot Slot(int s) => PlayerSlot.FromValue((byte)s);

    public ActorId Actor(int s) => ActorId.FromEntity(Host.Services.Slots.Entry(Slot(s)).PlayerId);

    public Vec3Mm Spawn(int s) => PadSlots.Position(World, World.PostOffice, (byte)s);

    public ushort HotbarSlot(int s)
    {
        Assert.True(Inventory.Containers.TryGetSlot(Inventory.HotbarOf(Slot(s)), out ushort c));
        return c;
    }

    public DefIndex KindOf(string kind) => PlacementFixture.Index(Content.MailKinds, kind);

    public DefIndex ItemOf(string item) => PlacementFixture.Index(Content.Items, item);

    public void TickEmpty(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Sim.Tick();
        }
    }

    /// <summary>Ticks empty until the next tick to run is <paramref name="tick"/>.</summary>
    public void TickTo(uint tick) => TickEmpty((int)(tick - Sim.CurrentTick));

    /// <summary>Reports <paramref name="feet"/> for <paramref name="s"/> and ticks once, so it is the host's newest sample.</summary>
    public void Stand(int s, Vec3Mm feet)
    {
        var sample = new TransformSample { Seq = ++_seq, PosXMm = feet.X, PosYMm = feet.Y, PosZMm = feet.Z };
        Assert.True(Sim.TryWriteSample(Slot(s), in sample));
        Sim.Tick();
    }

    /// <summary>Places a mail stack of <paramref name="ids"/> at hotbar index <paramref name="index"/> of slot <paramref name="s"/> (one tick).</summary>
    public EntryId PlaceMail(int s, int index, string kind, params uint[] ids)
    {
        DefIndex k = KindOf(kind);
        MailKindDef def = Content.MailKinds[k];
        ushort e = Allocate(def.GridW, def.GridH, (byte)ids.Length);
        ref EntryRecord r = ref Inventory.Entries[e];
        r.IsMail = true;
        r.MailKind = k.Value;
        r.AddressId = 4u;
        Assert.True(Inventory.MailIds.TryCreate(ids, 0, out r.MailIds));
        return Place(s, index, e);
    }

    /// <summary>Places <paramref name="count"/> of item <paramref name="item"/> at hotbar index <paramref name="index"/> (one tick).</summary>
    public EntryId PlaceItem(int s, int index, string item, byte count)
    {
        DefIndex i = ItemOf(item);
        ItemDef def = Content.Items[i];
        ushort e = Allocate(def.GridW, def.GridH, count);
        Inventory.Entries[e].ItemId = i.Value;
        return Place(s, index, e);
    }

    /// <summary>Enqueues one <c>ThrowRelease</c> from slot <paramref name="s"/>, ticks once and returns its reply and the tick's log.</summary>
    public (Reply Reply, EventEntry[] Log, uint Tick) Release(int s, in ThrowReleasePayload p)
    {
        var op = new Op { Kind = ThrowOpKinds.ThrowRelease, ReqId = ++_req };
        op.SetPayload(in p);
        uint tick = Sim.CurrentTick;
        Assert.True(Sim.EnqueueOp(Actor(s), in op));
        Sim.Tick();
        Reply reply = Assert.Single(LastLog, e => e.Kind == EventKind.OpReply).PayloadAs<Reply>();
        Assert.Equal(op.ReqId, reply.ReqId);
        return (reply, LastLog, tick);
    }

    /// <summary>A valid release of <paramref name="entry"/> from <paramref name="feet"/> along <paramref name="dir"/>, right-handed.</summary>
    public static ThrowReleasePayload Payload(EntryId entry, byte units, Vec3Mm feet, short dx, short dy, short dz, sbyte side = 1) => new()
    {
        EntryId = entry.Value, Units = units, Side = side, FeetX = feet.X, FeetY = feet.Y, FeetZ = feet.Z, DirX = dx, DirY = dy, DirZ = dz,
    };

    /// <summary>
    /// Inside the next tick's phase 8: detaches each hotbar entry of slot 0 through <see cref="IInventoryApi.DetachForThrow"/> and appends
    /// a flight for it with the given origin and velocity, launched that tick (so the first sweep is the tick after). Returns the launch tick.
    /// </summary>
    public uint Inject(params (EntryId Entry, byte Units, Vec3Mm Origin, Vec3Mm Vel)[] flights) => InjectFor(0, flights);

    public uint InjectFor(int s, params (EntryId Entry, byte Units, Vec3Mm Origin, Vec3Mm Vel)[] flights)
    {
        uint tick = Sim.CurrentTick;
        _inv.Hook.Action = (in TickContext ctx) =>
        {
            foreach ((EntryId entry, byte units, Vec3Mm origin, Vec3Mm vel) in flights)
            {
                DetachResult d = Inventory.DetachForThrow(Slot(s), entry, units);
                Assert.Equal(0, (int)d.Reason);
                var init = new FlightInit { Entry = entry, Units = units, Thrower = Slot(s), Origin = origin, Vel = vel, LaunchTick = ctx.Tick, ReleaseTick = ctx.Tick };
                Assert.True(Flights.TryAppend(in init, out _));
            }
        };
        try
        {
            Sim.Tick();
        }
        finally
        {
            _inv.Hook.Action = null;
        }

        return tick;
    }

    /// <summary>Runs <paramref name="action"/> inside the next tick's phase 8.</summary>
    public void InPhase(Action action)
    {
        _inv.Hook.Action = (in TickContext ctx) => action();
        try
        {
            Sim.Tick();
        }
        finally
        {
            _inv.Hook.Action = null;
        }
    }

    /// <summary>Every payload of <paramref name="kind"/> in the captured logs, oldest first.</summary>
    public List<T> All<T>(EventKind kind)
        where T : unmanaged => Ticks.SelectMany(t => t).Where(e => e.Kind == kind).Select(e => e.PayloadAs<T>()).ToList();

    private ushort Allocate(int w, int h, byte count)
    {
        Assert.True(Inventory.Entries.TryAllocate(Sim.CurrentTick, out ushort e, out _));
        ref EntryRecord r = ref Inventory.Entries[e];
        r.W = (byte)w;
        r.H = (byte)h;
        Inventory.Entries.SetCount(e, count);
        return e;
    }

    private EntryId Place(int s, int index, ushort entry)
    {
        Inventory.Ledger.Recount(Inventory);
        ushort hotbar = HotbarSlot(s);
        InvFixtureActionHandler h = _inv.Handler;
        h.ValidateStep = (ref InvPlan p) => p.Touch(hotbar) < 0 ? CoreReason.BadRequest : (ushort)0;
        h.CommitStep = (ref InvPlan p) => Inventory.Mutator.Place(ref p, entry, hotbar, (byte)index, 0, false);
        try
        {
            var op = new Op { Kind = InvFixtureActionHandler.FixtureKind, ReqId = ++_req };
            op.SetPayload(default(MovePayload));
            Assert.True(Sim.EnqueueOp(Actor(s), in op));
            Sim.Tick();
            Assert.True(Assert.Single(LastLog, e => e.Kind == EventKind.OpReply).PayloadAs<Reply>().IsOk);
        }
        finally
        {
            h.ValidateStep = null;
            h.CommitStep = null;
        }

        return Inventory.Entries[entry].Id;
    }

    /// <summary>The selected hotbar index per slot, settable (the <c>HotbarSelect</c> op has no production owner yet).</summary>
    internal sealed class HotbarDouble : IHotbarSelection
    {
        private readonly byte[] _index = new byte[PlayerSlot.Count];

        public byte this[int slot]
        {
            get => _index[slot];
            set => _index[slot] = value;
        }

        public byte SelectedIndex(PlayerSlot slot) => _index[slot.Value];
    }
}

/// <summary>
/// Throwing's test world (stories 010 and 011): 40 x 20 tiles (80 m x 40 m). Dry flat ground at y = 0 for x up to
/// <see cref="ShoreXMm"/>, sea floor at -2000 (water) east of the next tile. A house box with a roof box above it, and a Post Office box,
/// all on the dry side; the pad centre is open ground well away from them.
/// </summary>
internal static class ThrowFixtureWorld
{
    public const int TilesX = 40;
    public const int TilesZ = 20;

    /// <summary>The last dry corner column's x, mm: corners at or west of it are y = 0.</summary>
    public const int ShoreXMm = 60_000;

    /// <summary>A point over water.</summary>
    public static readonly Vec3Mm Sea = new(70_000, 0, 20_000);

    /// <summary>The house walls (x 40-44 m, z 4-8 m, y 0-4 m).</summary>
    public static readonly AabbMm House = new(new Vec3Mm(40_000, 0, 4_000), new Vec3Mm(44_000, 4_000, 8_000));

    /// <summary>Its roof (y 4-6 m).</summary>
    public static readonly AabbMm Roof = new(new Vec3Mm(40_000, 4_000, 4_000), new Vec3Mm(44_000, 6_000, 8_000));

    /// <summary>The Post Office (x 4-10 m, z 30-36 m, y 0-5 m).</summary>
    public static readonly AabbMm PostOffice = new(new Vec3Mm(4_000, 0, 30_000), new Vec3Mm(10_000, 5_000, 36_000));

    /// <summary>The spawn pad centre.</summary>
    public static readonly Vec3Mm Pad = new(20_000, 0, 20_000);

    public static WorldTables Build()
    {
        int stride = TilesX + 1;
        var corners = new int[stride * (TilesZ + 1)];
        for (int cz = 0; cz <= TilesZ; cz++)
        {
            for (int cx = 0; cx <= TilesX; cx++)
            {
                corners[(cz * stride) + cx] = cx * 2000 <= ShoreXMm ? 0 : -2000;
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(TilesX, TilesZ, corners);
        b.AddSolid(EntityId.None, House, SolidKind.House);
        b.AddSolid(EntityId.None, Roof, SolidKind.Roof);
        b.AddSolid(EntityId.None, PostOffice, SolidKind.PostOffice);
        b.SetPostOffice(new Vec3Mm(7_000, 0, 29_000), new Vec3Mm(7_000, 1_000, 29_500), Pad);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }
}
