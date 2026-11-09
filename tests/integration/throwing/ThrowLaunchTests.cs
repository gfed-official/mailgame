using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.ThrowingCatching;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Integration.ThrowingCatching;

/// <summary>
/// throwing-catching story 010 (ADR-0015 section 1; ADR-0009 section 6; ADR-0008 section 5; ADR-0010; GDD T2, T4 "Launch tick"): the
/// <c>ThrowRelease</c> handler's commit through the op ring of a real world (<see cref="ThrowWorldRig"/>: Grid Inventory, Players, Mail
/// and the shipped Throwing; the selected hotbar slot is the rig's double). The chest-blocked launch (TC-E-1 a) and the first sweep at
/// T+1 (TC-T4-1 c) are asserted end to end in <c>ThrowingStepTests</c>.
/// </summary>
public class ThrowLaunchTests
{
    private const byte Index = 3;
    private const short DirY = 3406;
    private const short DirZ = -16_026;

    // FX-T: feet (10000, 0, 20000) on the fixture's dry ground at y = 0.
    private static readonly Vec3Mm _feet = new(10_000, 0, 20_000);

    private static ThrowWorldRig Standing(Func<IInventoryApi, IInventoryApi>? wrap = null)
    {
        var rig = new ThrowWorldRig(ThrowFixtureWorld.Build(), wrapInventory: wrap);
        rig.Stand(0, _feet);
        rig.Selection[0] = Index;
        return rig;
    }

    private static ThrowReleasePayload Release(EntryId entry, byte units) => ThrowWorldRig.Payload(entry, units, _feet, 0, DirY, DirZ);

    // ---------------------------------------------------------------- TC-T2-1: what launches

    [Theory]
    [Trait("Id", "TC-T2-1")]
    [InlineData("letter", 20, 1)]   // FX-L20, light
    [InlineData("medium", 1, 3)]
    [InlineData("large", 1, 8)]
    [InlineData("axe", 1, 1)]       // standard issue, light, value 0: non-mail
    public void SelectedEntryThrown_IsOneFlightHoldingItsUnits_TheSlotEmpty_OneSeal_UnitsInFlight(string def, byte units, int classPts)
    {
        // Arrange
        ThrowWorldRig rig = Standing();
        bool mail = def != "axe";
        uint[] ids = Enumerable.Range(101, units).Select(i => (uint)i).ToArray();
        EntryId entry = mail ? rig.PlaceMail(0, Index, def, ids) : rig.PlaceItem(0, Index, def, units);
        ushort hotbar = rig.HotbarSlot(0);
        uint versionBefore = rig.Inventory.Containers[hotbar].Version;
        Assert.True(rig.Inventory.Entries.TryGetSlot(entry, out ushort pool));
        int row = UnitLedger.RowOf(in rig.Inventory.Entries[pool]);

        // Act
        (Reply reply, EventEntry[] log, _) = rig.Release(0, Release(entry, units));

        // Assert: one flight carrying the entry, by its id (the mailIds stay under it in the arena)
        Assert.True(reply.IsOk);
        Assert.Equal(1, rig.Flights.LiveFlights);
        Assert.Equal((entry, units, (byte)0), (rig.Flights.Entry(0), rig.Flights.Units(0), rig.Flights.Hop(0)));
        Assert.Equal(mail ? rig.KindOf(def).Value : rig.ItemOf(def).Value, rig.Flights.KindDef(0));
        Assert.Equal(classPts, rig.Flights.ClassPts(0));
        Assert.True(rig.Inventory.Entries.TryGetSlot(entry, out ushort still));
        Assert.Equal(EntryRecord.NoContainer, rig.Inventory.Entries[still].ContainerSlot);
        if (mail)
        {
            Assert.Equal(ids, rig.Inventory.MailIds.Read(in rig.Inventory.Entries[still].MailIds).ToArray());
        }

        // the hotbar cell is empty, the container bumped once under one seal, and the units are in the Flight column
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(hotbar, rig.Inventory.Containers[hotbar].W, Index, 0));
        Assert.Equal(versionBefore + 1, rig.Inventory.Containers[hotbar].Version);
        InvVersionPayload seal = Assert.Single(log, e => e.Kind == InvEventKinds.Version).PayloadAs<InvVersionPayload>();
        Assert.Equal(seal.VersionFrom + 1, seal.VersionTo);
        Assert.Equal(units, rig.Inventory.Ledger.Live(row, LedgerState.Flight));
        Assert.Equal(0, Assert.Single(log, e => e.Kind == ThrowEventKinds.ThrowLaunched).PayloadAs<ThrowLaunched>().Hop);
    }

    // ---------------------------------------------------------------- FX-T launch, and the wire round trip

    [Fact]
    [Trait("Id", "TC-T4-1")]
    public void FxTRelease_StoresTheF1OriginAndVelocity_AndThrowLaunchedCarriesThemWithTheApplyTick_ThroughTheCodec()
    {
        // Arrange
        ThrowWorldRig rig = Standing();
        EntryId entry = rig.PlaceMail(0, Index, "letter", 7);

        // Act
        (Reply reply, EventEntry[] log, uint t) = rig.Release(0, Release(entry, 1));

        // Assert: GDD FX-T integers (default ThrowRangePermille 1000 -> speedPm 1000), launchTick = releaseTick = T
        Assert.True(reply.IsOk);
        var origin = new Vec3Mm(10_250, 1_200, 19_700);
        var vel = new Vec3Mm(0, 2_911, -13_695);
        Assert.Equal((origin, vel, t, t), (rig.Flights.Origin(0), rig.Flights.Vel(0), rig.Flights.LaunchTick(0), rig.Flights.ReleaseTick(0)));
        EventEntry raised = Assert.Single(log, e => e.Kind == ThrowEventKinds.ThrowLaunched);
        ThrowLaunched l = raised.PayloadAs<ThrowLaunched>();
        Assert.Equal(
            (origin.X, origin.Y, origin.Z, vel.X, vel.Y, vel.Z, t, (byte)0, rig.Flights.Id(0).Value),
            (l.OriginX, l.OriginY, l.OriginZ, l.VelX, l.VelY, l.VelZ, l.LaunchTick, l.Flags, l.FlightId));

        // the raised entry survives the client codec (its flags byte has no undefined bit)
        var codec = new ThrowLaunchedCodec();
        Span<byte> wire = stackalloc byte[64];
        int n = codec.Encode(in raised, wire);
        EventEntry back = default;
        Assert.True(codec.TryDecode(wire[..n], ref back, out _));
        Assert.Equal(l, back.PayloadAs<ThrowLaunched>());
    }

    // ---------------------------------------------------------------- Snapshots

    [Theory]
    [Trait("Id", "TC-F1-1")]
    [InlineData(0, 1000)]
    [InlineData(1100, 1100)]   // the Later vector: the stat takes Mul only, so its "+100" is x1100 permille
    public void ThrownPm_IsSnapshottedAtTheApplyTick_ALaterSheetChangeOrADisconnectLeavesTheRecord(int mulPm, int expected)
    {
        // Arrange
        ThrowWorldRig rig = Standing();
        if (mulPm != 0)
        {
            Push(rig, 1, mulPm);
        }

        EntryId entry = rig.PlaceMail(0, Index, "letter", 7);

        // Act
        Assert.True(rig.Release(0, Release(entry, 1)).Reply.IsOk);
        int launched = rig.Flights.ThrownPm(0);
        Push(rig, 2, 1500);
        var leave = new Op { Kind = OpKind.PlayerLeave, ReqId = 77 };
        leave.SetPayload(new PlayerLeavePayload { PlayerId = rig.Host.Services.Slots.Entry(ThrowWorldRig.Slot(0)).PlayerId.Value });
        Assert.True(rig.Sim.EnqueueOp(ActorId.System, in leave));
        rig.Sim.Tick();

        // Assert
        Assert.Equal(expected, launched);
        Assert.Equal(1, rig.Flights.LiveFlights);
        Assert.Equal(expected, rig.Flights.ThrownPm(0));
    }

    // ---------------------------------------------------------------- Release tick and openAtRelease

    [Theory]
    [Trait("Id", "TC-T4-1")]
    [InlineData(true)]
    [InlineData(false)]   // Prep, Payday, Draft: closed, and the release still launches
    public void Release_RecordsTheApplyTickAndWhetherDeliveriesWereOpen_AndLaunchesEitherWay(bool open)
    {
        // Arrange
        ThrowWorldRig rig = Standing();
        rig.InPhase(() => rig.Mail.SetDeliveriesState(open, 0, 1));
        EntryId entry = rig.PlaceMail(0, Index, "letter", 7);

        // Act
        (Reply reply, _, uint t) = rig.Release(0, Release(entry, 1));

        // Assert
        Assert.True(reply.IsOk);
        Assert.Equal((t, open), (rig.Flights.ReleaseTick(0), rig.Flights.OpenAtRelease(0)));
    }

    // ---------------------------------------------------------------- Claimed origin

    [Theory]
    [Trait("Id", "TC-T4-1")]
    [InlineData(0, false)]       // corroborated: the claim
    [InlineData(2_000, true)]    // beyond claimToleranceMm: the newest sample, bit 0
    public void ClaimedFeet_LaunchFromTheClaimWhenCorroborated_ElseFromTheNewestSampleWithTheClampedFlag(int offX, bool clamped)
    {
        // Arrange
        ThrowWorldRig rig = Standing();
        EntryId entry = rig.PlaceMail(0, Index, "letter", 7);
        var claim = new Vec3Mm(_feet.X + offX, _feet.Y, _feet.Z);

        // Act
        (Reply reply, EventEntry[] log, _) = rig.Release(0, ThrowWorldRig.Payload(entry, 1, claim, 0, DirY, DirZ));

        // Assert: FX-T's hand offset (+250, +1200, -300) from the feet used
        Assert.True(reply.IsOk);
        Vec3Mm used = clamped ? _feet : claim;
        Assert.Equal(new Vec3Mm(used.X + 250, used.Y + 1_200, used.Z - 300), rig.Flights.Origin(0));
        Assert.Equal(clamped, rig.Flights.OriginClamped(0));
        Assert.Equal(clamped ? ThrowLaunched.OriginClampedBit : 0, Assert.Single(log, e => e.Kind == ThrowEventKinds.ThrowLaunched).PayloadAs<ThrowLaunched>().Flags);
    }

    // ---------------------------------------------------------------- TC-N-1 ordering

    [Fact]
    [Trait("Id", "TC-N-1")]
    public void OneLaunch_LogsGridRemovedAndSealThenThrowLaunchedThenOk_ForTheTransportActorsSlot()
    {
        // Arrange
        ThrowWorldRig rig = Standing();
        EntryId entry = rig.PlaceMail(0, Index, "letter", 7, 8);

        // Act
        (Reply reply, EventEntry[] log, _) = rig.Release(0, Release(entry, 2));

        // Assert
        EventKind[] order = log
            .Where(e => e.Kind is InvEventKinds.Removed or InvEventKinds.Version or ThrowEventKinds.ThrowLaunched or EventKind.OpReply)
            .Select(e => e.Kind)
            .ToArray();
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Version, ThrowEventKinds.ThrowLaunched, EventKind.OpReply }, order);
        Assert.True(reply.IsOk);
        EventEntry launched = Assert.Single(log, e => e.Kind == ThrowEventKinds.ThrowLaunched);
        Assert.Equal(0, launched.PayloadAs<ThrowLaunched>().Thrower);
        Assert.Equal(rig.Host.Services.Slots.ConnectedMask, launched.Audience);
    }

    // ---------------------------------------------------------------- Cooldown carried (M-1a)

    [Fact]
    [Trait("Id", "TC-T4-1")]
    public void TwoReleasesAtTicks100And101_BothLaunchThroughTheOpRing_TatCarriedUnchanged_NoCooldownReply()
    {
        // Arrange
        ThrowWorldRig rig = Standing();
        EntryId first = rig.PlaceMail(0, Index, "letter", 7);
        EntryId second = rig.PlaceMail(0, Index + 1, "letter", 8);
        uint tat = rig.Throwing.Tat(ThrowWorldRig.Slot(0));
        rig.TickTo(100);

        // Act
        (Reply a, _, uint ta) = rig.Release(0, Release(first, 1));
        rig.Selection[0] = Index + 1;
        (Reply b, _, uint tb) = rig.Release(0, Release(second, 1));

        // Assert
        Assert.Equal((100u, 101u), (ta, tb));
        Assert.True(a.IsOk);
        Assert.True(b.IsOk);
        Assert.Equal(2, rig.Flights.LiveFlights);
        Assert.Equal(tat, rig.Throwing.Tat(ThrowWorldRig.Slot(0)));
    }

    // ---------------------------------------------------------------- Commit never fails

    [Fact]
    [Trait("Id", "TC-T4-1")]
    public void DetachRefusedAfterAPassingValidation_RecordsACommitFault_RepliesBadRequest_NoFlightNoEvent()
    {
        // Arrange: Throwing's inventory refuses every detach; its check (CheckDetachForThrow) still passes
        ThrowWorldRig rig = Standing(inner => new RefusingDetach(inner));
        EntryId entry = rig.PlaceMail(0, Index, "letter", 7);

        // Act
        (Reply reply, EventEntry[] log, _) = rig.Release(0, Release(entry, 1));

        // Assert
        Assert.Equal(ThrowReasons.BadRequest, reply.Reason);
        Assert.Equal(0, rig.Flights.LiveFlights);
        Assert.DoesNotContain(log, e => e.Kind == ThrowEventKinds.ThrowLaunched);
        Assert.Equal(1L, rig.Sim.Counters[CounterId.ThrowingCommitFaults]);
        Assert.Contains(Enumerable.Range(0, rig.Sim.CheckRing.Count), i => rig.Sim.CheckRing[i].Check == CheckCode.ThrowingCommitFault && rig.Sim.CheckRing[i].A == 3);
    }

    // ---------------------------------------------------------------- Owner: the production binding

    [Fact]
    [Trait("Id", "TC-T4-1")]
    public void ShippedComposition_BindsTheThrowReleaseHandler_AndDeclaresBothThrowingEventKinds()
    {
        // Arrange: the M-1a greybox profile as shipped (no hotbar selection owner yet, so the release cannot pass check 2)
        var sink = new CaptureSink();
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, BuiltInContent.CreateM1a(), 7) { World = WorldTablesFixture.Pad(), EventSink = sink });
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 1 };
        join.SetPayload(new PlayerJoinPayload { Slot = 0 });
        Assert.True(host.World.EnqueueOp(ActorId.System, in join));
        host.World.Tick();
        var op = new Op { Kind = ThrowOpKinds.ThrowRelease, ReqId = 2 };
        op.SetPayload(ThrowWorldRig.Payload(EntryId.Real(1), 1, default, 0, DirY, DirZ));

        // Act
        Assert.True(host.World.EnqueueOp(ActorId.FromEntity(host.Services.Slots.Entry(ThrowWorldRig.Slot(0)).PlayerId), in op));
        host.World.Tick();

        // Assert: the handler's chain answered (not core's unknown_kind), and both kinds are declared
        Reply reply = Assert.Single(sink.Last, e => e.Kind == EventKind.OpReply).PayloadAs<Reply>();
        Assert.Equal(ThrowReasons.BadRequest, reply.Reason);
        Assert.Contains(host.World.OpDescriptors.ToArray(), d => d.Kind == ThrowOpKinds.ThrowRelease);
        EventKind[] kinds = host.World.EventDescriptors.ToArray().Select(d => d.Kind).ToArray();
        Assert.Contains(ThrowEventKinds.ThrowLaunched, kinds);
        Assert.Contains(ThrowEventKinds.FlightResolved, kinds);
    }

    // ---------------------------------------------------------------- Owner: the two Bind event declarations are guarded

    [Fact]
    [Trait("Id", "TC-N-1")]
    public void ThrowLaunchedAndFlightResolvedFromInsideATick_FlushReplicated_WithCodecIds4And5()
    {
        // Arrange: a world built through SimComposition.Build with the shipped ThrowingSystem.Bind
        ThrowWorldRig rig = Standing();
        EntryId entry = rig.PlaceMail(0, Index, "letter", 7);

        // Act: the launch, then the ticks until it lands
        Assert.True(rig.Release(0, Release(entry, 1)).Reply.IsOk);
        rig.TickEmpty(rig.Throwing.Settings.MaxFlightTicks);

        // Assert: a kind without a descriptor would flush as Internal and never reach a peer
        EventEntry[] all = rig.Ticks.SelectMany(t => t).ToArray();
        EventEntry launched = Assert.Single(all, e => e.Kind == ThrowEventKinds.ThrowLaunched);
        EventEntry resolved = Assert.Single(all, e => e.Kind == ThrowEventKinds.FlightResolved);
        Assert.Equal((ReplicationClass.Replicated, ReplicationClass.Replicated), (launched.Class, resolved.Class));
        EventDescriptor[] declared = rig.Sim.EventDescriptors.ToArray();
        Assert.Equal(ThrowEventDescriptors.ThrowLaunchedCodecId, Assert.Single(declared, d => d.Kind == ThrowEventKinds.ThrowLaunched).WireCodecId);
        Assert.Equal(ThrowEventDescriptors.FlightResolvedCodecId, Assert.Single(declared, d => d.Kind == ThrowEventKinds.FlightResolved).WireCodecId);
    }

    // ---------------------------------------------------------------- helpers

    private static void Push(ThrowWorldRig rig, ushort id, int mulPm)
    {
        var src = new ModifierSource(SourceKind.Perk, DefIndex.FromValue(id), 0, ModifierOwner.ForSlot(ThrowWorldRig.Slot(0)), 1, ModifierLifetime.Run);
        Modifier m = new(src.Kind, src.Id, src.StackIndex, src.Owner.Slot, Stat.ThrownCorrectPayMultPm, StatSheet.NoQualifier, ModifierOp.Mul, mulPm, src.Lifetime);
        Assert.True(rig.Host.Services.Stats.Push(in src, [m]));
    }

    /// <summary>The real inventory, except that every <see cref="IInventoryApi.DetachForThrow"/> is refused (a commit that cannot detach).</summary>
    private sealed class RefusingDetach : IInventoryApi
    {
        private readonly IInventoryApi _inner;

        public RefusingDetach(IInventoryApi inner) => _inner = inner;

        public int BacklogUnits => _inner.BacklogUnits;

        public int BacklogEntries => _inner.BacklogEntries;

        public ContainerId IntakeContainer => _inner.IntakeContainer;

        public DetachResult DetachForThrow(PlayerSlot slot, EntryId entryId, byte units) => new(InvReason.StaleEntry, default, 0);

        public DetachCheck CheckDetachForThrow(PlayerSlot slot, uint rawEntryId, byte units, out byte hotbarIndex) => _inner.CheckDetachForThrow(slot, rawEntryId, units, out hotbarIndex);

        public bool TryReadThrowable(EntryId entry, out ThrowableEntry t) => _inner.TryReadThrowable(entry, out t);

        public SpawnResult Spawn(ContainerId c, DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids, MergePolicy p) => _inner.Spawn(c, mailKind, addressId, flags, ids, p);

        public bool PeekOutfeed(ContainerId c, Selector s, out UnitRef u) => _inner.PeekOutfeed(c, s, out u);

        public bool TakeUnit(ContainerId c, Selector s, ActorId actor, out UnitRef u) => _inner.TakeUnit(c, s, actor, out u);

        public EntityId CreateWorldItem(EntryId entry, Vec3Mm posMm, WorldItemOrigin origin, bool onWater, uint createdTick) => _inner.CreateWorldItem(entry, posMm, origin, onWater, createdTick);

        public bool ConsumeUnit(EntryRef entry, ConsumeReason reason, ActorId actor) => _inner.ConsumeUnit(entry, reason, actor);

        public bool TryReadEntry(EntryRef entry, out EntryView v) => _inner.TryReadEntry(entry, out v);

        public CatchResult TryCatch(PlayerSlot slot, in FlightPayload flight) => _inner.TryCatch(slot, in flight);

        public bool TryInsert(ContainerId c, in UnitRef u, ActorId actor) => _inner.TryInsert(c, in u, actor);

        public int CountItem(ReadOnlySpan<ContainerId> cs, DefIndex itemId) => _inner.CountItem(cs, itemId);

        public short WeightPoints(PlayerSlot slot) => _inner.WeightPoints(slot);

        public int SpeedPermille(PlayerSlot slot) => _inner.SpeedPermille(slot);

        public bool AppendBacklog(in BacklogEntry e) => _inner.AppendBacklog(in e);
    }
}
