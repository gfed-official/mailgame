using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 014 (GDD R10, R11; ADR-0017 sections 6 and 9, ADR-0009, ADR-0004): batch firing in Mail's phase 8 over the
/// real Inventory (behind a wrapper that counts Mail's <c>Spawn</c> and <c>AppendBacklog</c> calls) and the shift stub (Prep 6 ticks,
/// Delivery 10 s: open from tick 6, closed from 306). Each test scripts the locked manifest after Prep (batch ticks are offsets from 6),
/// so the units, keys and ticks are exact. The mixed-key partial-fill row (AC-5) is dropped (greybox scope).
/// </summary>
public class BatchIntakeBacklogTests
{
    private const uint Start = 6;

    // ---- AC-1 ----

    [Fact]
    [Trait("Id", "M-R2-1a")]
    [Trait("Id", "M-X-1b")]
    public void Ac1_MailIdsInManifestOrder_OneSpawnPerSameKeyGroup_EachUnitItsOwnIntakeEntry_OutfeedInManifestOrder()
    {
        // Arrange: the mail counter at 41; one batch of three A units, then one alternating A, B, A, B.
        var rig = new Rig(lastMailId: 41);
        (DefIndex Kind, AddressId Address) a = (rig.Letter, rig.Address(0));
        (DefIndex Kind, AddressId Address) b = (rig.Postcard, rig.Address(1));
        rig.Script((2, new[] { a, a, a }), (4, new[] { a, b, a, b }));

        // Act
        rig.TickTo(Start + 5);

        // Assert: the calls, then the rows, then the Intake's outfeed.
        Assert.Equal(new uint[] { 42, 43, 44 }, rig.Inventory.SpawnCalls[0]);
        Assert.Equal(new[] { new uint[] { 45 }, new uint[] { 46 }, new uint[] { 47 }, new uint[] { 48 } }, rig.Inventory.SpawnCalls.Skip(1));
        Assert.Equal(0, rig.Inventory.AppendCalls);
        var keys = new[] { a, a, a, a, b, a, b };
        for (uint id = 42; id <= 48; id++)
        {
            Assert.True(rig.Mail.Table.TryGetRow(new MailId(id), out MailRow row));
            Assert.Equal(keys[id - 42], (row.Kind, row.Address));
        }

        UnitRef[] outfeed = Array.Empty<UnitRef>();
        rig.Run(() => outfeed = rig.Drain(7));
        Assert.Equal(new uint[] { 42, 43, 44, 45, 46, 47, 48 }, outfeed.Select(u => u.Mail.Value));
        Assert.Equal(7, outfeed.Select(u => u.Entry).Distinct().Count());
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "M-R11-1a")]
    [Trait("Id", "M-R11-1b")]
    public void Ac2_OneFreeCellAnd60Units_Places1AndAppends59InOrder_AndALaterSpawnAppendsWhileTheBacklogHoldsUnitsEvenWithACellFree()
    {
        // Arrange: an Intake with one free cell; batch 0 is 60 letters for one house, batch 1 one more, 10 ticks later.
        var rig = new Rig();
        (DefIndex, AddressId) a = (rig.Letter, rig.Address(0));
        rig.Script((2, Enumerable.Repeat(a, 60).ToArray()), (12, new[] { a }));
        rig.Fill(rig.IntakeCells - 1);

        // Act 1: batch 0.
        rig.TickTo(Start + 3);

        // Assert 1
        Assert.Equal(1, rig.Inventory.SpawnPlaced);
        Assert.Equal(59, rig.Inventory.AppendCalls);
        Assert.Equal(59, rig.Mail.BacklogUnits);
        Assert.Equal(Enumerable.Range(1, 59).Select(i => rig.FirstBatchId + (uint)i), rig.Inventory.Appended);

        // Act 2: free 33 cells in the batch-1 tick; the drain (phase 2, 32 per tick) leaves one free before Mail's phase 8.
        rig.TickTo(Start + 12);
        rig.Run(() => rig.Drain(33));

        // Assert 2: appended without a Spawn call.
        Assert.Single(rig.Inventory.SpawnCalls);
        Assert.Equal(60, rig.Inventory.AppendCalls);
        Assert.Equal(59 - 32 + 1, rig.Mail.BacklogUnits);
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "M-R11-1d")]
    [Trait("Id", "M-X-1d")]
    public void Ac3_ConservationHoldsEveryTick_And40BacklogEntriesAgainstThe32Drain_Leave8()
    {
        // Arrange: a full Intake; one 40-letter batch, all appended.
        var rig = new Rig();
        (DefIndex, AddressId) a = (rig.Letter, rig.Address(0));
        rig.Script((2, Enumerable.Repeat(a, 40).ToArray()));
        rig.Fill(rig.IntakeCells);
        rig.TickTo(Start + 3);
        Assert.Equal(40, rig.Mail.BacklogUnits);

        // Act: 40 cells freed in phase 0 of one tick; that tick's drain moves 32.
        rig.Run(() => rig.Drain(40));

        // Assert
        Assert.Equal(8, rig.Mail.BacklogUnits);
        rig.TickTo(Start + 20);
        Assert.Equal(0, rig.MaxConservationMismatches);
    }

    // ---- AC-4 ----

    [Theory]
    [Trait("Id", "M-R10-1e")]
    [Trait("Id", "M-PERF-1d")]
    [InlineData(320, 4, 64, 0)] // empty Intake: one Spawn per same-key run, all placed
    [InlineData(0, 1, 0, 64)]   // full Intake: the first run's Spawn places nothing, then 64 appends and no further Spawn
    public void Ac4_AFiredBatchMakesExactlyGSpawnsPlusOneAppendPerRemainingUnit_RaisesOneBatchArrived_AndAClosedBatchTickFiresNothing(
        int freeCells, int spawnCalls, int placed, int appended)
    {
        // Arrange: 64 units in four same-key runs of 16; a second batch scheduled after deliveries close (tick 306).
        var rig = new Rig();
        (DefIndex, AddressId)[] runs = { (rig.Letter, rig.Address(0)), (rig.Postcard, rig.Address(0)), (rig.Letter, rig.Address(1)), (rig.Postcard, rig.Address(1)) };
        rig.Script((2, runs.SelectMany(k => Enumerable.Repeat(k, 16)).ToArray()), (305, new[] { runs[0] }));
        rig.Fill(rig.IntakeCells - freeCells);
        uint idsBefore = rig.Host.Services.MailIds.Peek();

        // Act
        rig.TickTo(Start + 320);

        // Assert: exact counts, one BatchArrived{64}, and the closed batch made no id and no call.
        Assert.Equal(spawnCalls, rig.Inventory.SpawnCalls.Count);
        Assert.Equal(placed, rig.Inventory.SpawnPlaced);
        Assert.Equal(appended, rig.Inventory.AppendCalls);
        Assert.Equal(64, rig.Inventory.SpawnPlaced + rig.Inventory.AppendCalls);
        Assert.Equal(new ushort[] { 64 }, rig.BatchArrived);
        Assert.Equal(idsBefore + 64, rig.Host.Services.MailIds.Peek());
    }

    /// <summary>
    /// Inventory (wrapped), Mail, a phase-0 probe op and the shift stub on the M-1a street, ticked to the lock (end of tick 5) so the test
    /// can script the manifest.
    /// </summary>
    private sealed class Rig : ISimSystem, IOpHandler, IEventSink
    {
        private static readonly ContentSet _content = BuiltInContent.CreateM1a();
        private static readonly OpKind _probeOp = (OpKind)0x0100;
        private Action? _next;
        private uint _req = 1;
        private InventorySystem? _real;
        private MailSystem? _mail;

        public Rig(uint lastMailId = 0)
        {
            var settings = new ShiftStubSettings { PrepTicks = (int)Start, DeliverySeconds = 10 };
            SimProfile profile = SimProfile.ForTest(
                "md014",
                null,
                new SimSystemEntry(SystemId.GridInventory, ctx => _real = new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
                new SimSystemEntry(SystemId.MailDestinations, ctx =>
                {
                    Inventory = new CountingInventory(ctx.Api<IInventoryApi>(SystemId.GridInventory));
                    return _mail = new MailSystem(
                        ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, Inventory, ctx.WalletGate, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots);
                }),
                new SimSystemEntry(SystemId.ManualDelivery, _ => this),
                new SimSystemEntry(SystemId.RunStructure, ctx => new ShiftStub(ctx.Api<IMailApi>(SystemId.MailDestinations), ctx.RunControl, ctx.Events, settings)));
            Host = SimComposition.Build(new SimBuildArgs(profile, _content, 7)
            {
                WorldSource = M1aWorldSource.Instance, EventSink = this, Counters = new SimInitialCounters { LastMailId = lastMailId },
            });
            TickTo(Start);
            Assert.True(Mail.Spawner.Locked);
            Assert.Equal(Start, Mail.Spawner.DeliveryStartTick);
        }

        public SimHost Host { get; }

        public MailSystem Mail => _mail!;

        public CountingInventory Inventory { get; private set; } = null!;

        public List<ushort> BatchArrived { get; } = new();

        public long MaxConservationMismatches { get; private set; }

        public DefIndex Letter => KindOf("letter");

        public DefIndex Postcard => KindOf("postcard");

        public int IntakeCells => 20 * 16;

        public uint FirstBatchId { get; private set; }

        public OpKind Kind => _probeOp;

        public AddressId Address(int ordinal) => Mail.Registry.AddressOf(new DestinationOrdinal((ushort)ordinal));

        public void Bind(SimWorldBuilder builder) => builder.BindOp(OpDescriptor.Empty(_probeOp, true, 0, 0), this);

        public Reply Apply(in Op op, in TickContext ctx)
        {
            _next?.Invoke();
            return Reply.Ok(in op);
        }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            foreach (EventEntry e in entries)
            {
                if (e.Kind == MailEventKinds.BatchArrived)
                {
                    BatchArrived.Add(e.PayloadAs<BatchArrivedPayload>().Units);
                }
            }
        }

        /// <summary>Replaces the locked manifest: each batch at its offset from Delivery start, with its units' (kind, address).</summary>
        public void Script(params (long Offset, (DefIndex Kind, AddressId Address)[] Units)[] batches)
        {
            ShiftManifest m = Mail.Spawner.Manifest;
            m.Reset(0);
            int unit = 0;
            for (int b = 0; b < batches.Length; b++)
            {
                m.SetBatch(b, batches[b].Offset, batches[b].Units.Length, unit);
                foreach ((DefIndex kind, AddressId address) in batches[b].Units)
                {
                    m.SetUnit(unit++, kind, address, MailFlags.None, 8, 1, b);
                }
            }

            m.Complete(unit, batches.Length);
            FirstBatchId = Host.Services.MailIds.Peek() + 1;
        }

        /// <summary>In one tick: <paramref name="units"/> letters straight into the Intake (not through Mail's spawner).</summary>
        public void Fill(int units)
        {
            Run(() =>
            {
                AddressId address = Address(9);
                for (int left = units; left > 0;)
                {
                    int n = Math.Min(left, 255);
                    var ids = new MailId[n];
                    for (int i = 0; i < n; i++)
                    {
                        ids[i] = Mail.Table.Create(Letter, address, 8, 1, MailFlags.None);
                    }

                    Assert.Equal(n, _real!.Spawn(_real.IntakeContainer, Letter, address.Value, 0, ids, MergePolicy.NoMerge).Placed);
                    left -= n;
                }
            });
            FirstBatchId = Host.Services.MailIds.Peek() + 1;
        }

        /// <summary>Inside a tick: takes <paramref name="units"/> units off the Intake outfeed.</summary>
        public UnitRef[] Drain(int units)
        {
            var taken = new UnitRef[units];
            for (int i = 0; i < units; i++)
            {
                Assert.True(_real!.TakeUnit(_real.IntakeContainer, Selector.Fifo, ActorId.System, out taken[i]));
            }

            return taken;
        }

        /// <summary>Ticks until the next tick to run is <paramref name="tick"/>.</summary>
        public void TickTo(uint tick)
        {
            while (Host.World.CurrentTick < tick)
            {
                Tick();
            }
        }

        /// <summary>Runs one tick whose phase 0 runs <paramref name="body"/>.</summary>
        public void Run(Action body)
        {
            _next = body;
            var op = new Op { Kind = _probeOp, ReqId = _req++ };
            Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
            Tick();
            _next = null;
        }

        private void Tick()
        {
            Host.World.Tick();
            MaxConservationMismatches = Math.Max(MaxConservationMismatches, Host.World.Counters[CounterId.InventoryConservationMismatches]);
        }

        private static DefIndex KindOf(string id)
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
    }

    /// <summary>The real Inventory behind a wrapper that records Mail's <c>Spawn</c> and <c>AppendBacklog</c> calls.</summary>
    private sealed class CountingInventory : IInventoryApi
    {
        private readonly IInventoryApi _inner;

        public CountingInventory(IInventoryApi inner) => _inner = inner;

        public List<uint[]> SpawnCalls { get; } = new();

        public int SpawnPlaced { get; private set; }

        public int AppendCalls => Appended.Count;

        public List<uint> Appended { get; } = new();

        public int BacklogUnits => _inner.BacklogUnits;

        public int BacklogEntries => _inner.BacklogEntries;

        public ContainerId IntakeContainer => _inner.IntakeContainer;

        public SpawnResult Spawn(ContainerId c, DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids, MergePolicy p)
        {
            SpawnCalls.Add(ids.ToArray().Select(i => i.Value).ToArray());
            SpawnResult r = _inner.Spawn(c, mailKind, addressId, flags, ids, p);
            SpawnPlaced += r.Placed;
            return r;
        }

        public bool AppendBacklog(in BacklogEntry e)
        {
            foreach (MailId id in e.Ids)
            {
                Appended.Add(id.Value);
            }

            return _inner.AppendBacklog(in e);
        }

        public bool ConsumeUnit(EntryRef entry, ConsumeReason reason, ActorId actor) => _inner.ConsumeUnit(entry, reason, actor);

        public bool TryReadEntry(EntryRef entry, out EntryView v) => _inner.TryReadEntry(entry, out v);

        public bool PeekOutfeed(ContainerId c, Selector s, out UnitRef u) => _inner.PeekOutfeed(c, s, out u);

        public bool TakeUnit(ContainerId c, Selector s, ActorId actor, out UnitRef u) => _inner.TakeUnit(c, s, actor, out u);

        public EntityId CreateWorldItem(EntryId entry, Vec3Mm posMm, WorldItemOrigin origin, bool onWater, uint createdTick) =>
            _inner.CreateWorldItem(entry, posMm, origin, onWater, createdTick);

        public DetachResult DetachForThrow(PlayerSlot slot, EntryId entryId, byte units) => _inner.DetachForThrow(slot, entryId, units);

        public CatchResult TryCatch(PlayerSlot slot, in FlightPayload flight) => _inner.TryCatch(slot, in flight);

        public bool TryInsert(ContainerId c, in UnitRef u, ActorId actor) => _inner.TryInsert(c, in u, actor);

        public int CountItem(ReadOnlySpan<ContainerId> cs, DefIndex itemId) => _inner.CountItem(cs, itemId);

        public short WeightPoints(PlayerSlot slot) => _inner.WeightPoints(slot);

        public int SpeedPermille(PlayerSlot slot) => _inner.SpeedPermille(slot);

        public IPanelView HostPanelView(PlayerSlot slot) => _inner.HostPanelView(slot);

        public DetachCheck CheckDetachForThrow(PlayerSlot slot, uint rawEntryId, byte units, out byte hotbarIndex) =>
            _inner.CheckDetachForThrow(slot, rawEntryId, units, out hotbarIndex);
    }
}
