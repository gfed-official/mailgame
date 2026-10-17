using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Architecture;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 001 (PCM-P1-1): Players binds Sim Core's join seam, holds a record per slot with a <c>player</c> id,
/// the settings-driven capsule, separate body yaw and aim, spawns on the pad ring slot, models life state, and removes the
/// record on leave (a rejoin from T + 1 is a fresh record).
/// </summary>
[Trait("Id", "PCM-P1-1")]
public class PlayerJoinBindingTests
{
    private static readonly PlayerSlot _slot0 = PlayerSlot.FromValue(0);

    // ---------------------------------------------------------------- PCM-P1-1: the record

    [Fact]
    public void Join_Slot0_RecordHoldsThePlayerIdOfTheSlotTable_OfClassPlayer()
    {
        // Arrange
        var rig = PlayersRig.Create();

        // Act
        rig.JoinAndTick(0);

        // Assert
        EntityId inTable = rig.Host.Services.Slots.Entry(_slot0).PlayerId;
        PlayerRecord r = rig.Record(0);
        Assert.False(r.IsEmpty);
        Assert.Equal(inTable, r.PlayerId);
        Assert.Equal(EntityClass.Player, r.PlayerId.Class);
        Assert.Equal(16_777_217u, r.PlayerId.Value);
    }

    [Fact]
    public void Join_Slot0_CapsuleIs300By1400_FromPlayerSettings()
    {
        // Arrange
        var rig = PlayersRig.Create();

        // Act
        rig.JoinAndTick(0);

        // Assert
        Assert.Equal(300, rig.Record(0).CapsuleRadiusMm);
        Assert.Equal(1400, rig.Record(0).CapsuleHeightMm);
        Assert.Equal(rig.Players.Settings.CapsuleRadiusMm, rig.Record(0).CapsuleRadiusMm);
        Assert.Equal(rig.Players.Settings.CapsuleHeightMm, rig.Record(0).CapsuleHeightMm);
    }

    [Fact]
    public void Join_CapsuleFollowsTheContent_NotALiteral()
    {
        // Arrange: a content set with a different capsule.
        ContentSet content = BuiltInContent.CreateM1a();
        PlayerSettings custom = PlayerSettings.FromBalance(content.Player with { CapsuleRadiusMm = 250, CapsuleHeightMm = 1500 });
        var players = new PlayersSystem(WorldTablesFixture.Pad(), custom);

        // Act
        ((IPlayerJoinListener)players).OnPlayerJoined(_slot0, new EntityId(16_777_217u), 4);

        // Assert
        Assert.Equal((250, 1500), (players.RecordRef(_slot0).CapsuleRadiusMm, players.RecordRef(_slot0).CapsuleHeightMm));
    }

    [Fact]
    public void BodyYaw_AndAim_AreSeparateFields_WritingOneLeavesTheOtherUnchanged()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        ref PlayerRecord r = ref rig.Record(0);
        ushort aimYawBefore = r.LookYaw;
        short aimPitchBefore = r.LookPitch;
        ushort bodyBefore = r.Carrier.BodyYaw;

        // Act + Assert: body yaw does not move the aim.
        r.Carrier.BodyYaw = 12_345;
        Assert.Equal(aimYawBefore, r.LookYaw);
        Assert.Equal(aimPitchBefore, r.LookPitch);

        // Act + Assert: aim does not move the body yaw.
        r.Carrier.BodyYaw = bodyBefore;
        r.LookYaw = 40_000;
        r.LookPitch = -1500;
        Assert.Equal(bodyBefore, r.Carrier.BodyYaw);
        Assert.Equal(40_000, r.LookYaw);
        Assert.Equal(-1500, r.LookPitch);
    }

    // ---------------------------------------------------------------- Spawn

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)3)]
    [InlineData((byte)7)]
    public void Spawn_FeetAreOnPadRingSlotK_OnTerrain_GroundedAlive_Unprotected(byte slot)
    {
        // Arrange
        var rig = PlayersRig.Create();

        // Act
        rig.JoinAndTick(slot);

        // Assert
        Vec3Mm expected = PadSlots.Position(rig.World, rig.World.PostOffice, slot);
        PlayerRecord r = rig.Record(slot);
        Assert.Equal((expected.X, expected.Y, expected.Z), (r.Carrier.X, r.Carrier.Y, r.Carrier.Z));
        Assert.Equal((int)rig.World.TerrainHeightMm(r.Carrier.X, r.Carrier.Z), r.Carrier.Y);
        Assert.Equal(LocoState.Grounded, r.Carrier.Loco);
        Assert.Equal(LifeState.Alive, r.Life);
        Assert.Equal(0u, r.ProtectedUntilTick);
        Assert.Equal(0u, r.FrozenUntilTick);
        Assert.Equal((byte)0, r.TeleportSeq);
        Assert.Equal(CarrierFlags.None, r.Carrier.Flags);
        Assert.Equal((0, 0, 0), (r.Carrier.Vx, r.Carrier.Vy, r.Carrier.Vz));
    }

    [Fact]
    public void Spawn_Slot0_IsMinus1200OnZ_RelativeToThePadCentre()
    {
        // Arrange
        var rig = PlayersRig.Create();

        // Act
        rig.JoinAndTick(0);

        // Assert: ADR-0013 axis convention (slot 0 is at (0, -R)); the pad centre is pad-relative, never (0, 0).
        Vec3Mm centre = rig.World.PostOffice.PadCentre;
        Assert.Equal(centre.X, rig.Record(0).Carrier.X);
        Assert.Equal(centre.Z - 1200, rig.Record(0).Carrier.Z);
    }

    [Fact]
    public void Spawn_OnTheM1aStreet_UsesItsPostOfficePad()
    {
        // Arrange
        WorldTables street = BuildStreet();
        var rig = PlayersRig.Create(street);

        // Act
        rig.JoinAndTick(2);

        // Assert
        Vec3Mm expected = PadSlots.Position(street, street.PostOffice, 2);
        Assert.Equal((expected.X, expected.Y, expected.Z), (rig.Record(2).Carrier.X, rig.Record(2).Carrier.Y, rig.Record(2).Carrier.Z));
        Assert.Equal(street.PostOffice.PadCentre.X - 1200, rig.Record(2).Carrier.X);
    }

    [Fact]
    public void Spawn_FacesOutwardLikeThePadSlot_AndTheCameraStartsOnTheSameHeading()
    {
        // Arrange
        var rig = PlayersRig.Create();

        // Act
        rig.JoinAndTick(3);

        // Assert: PadSlots documents yaw k * 8192 as facing outward.
        Assert.Equal((ushort)(3 * PadSlots.SlotStepBam), rig.Record(3).Carrier.BodyYaw);
        Assert.Equal(rig.Record(3).Carrier.BodyYaw, rig.Record(3).LookYaw);
        Assert.Equal((short)0, rig.Record(3).LookPitch);
    }

    [Fact]
    public void Spawn_ClockInTick_IsTheTickTheJoinWasApplied()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.TickEmpty(3);

        // Act
        rig.JoinAndTick(0);

        // Assert: ticks 0, 1, 2 were empty; the join ran on tick 3 (TD-052 item 3).
        Assert.Equal(3u, rig.Record(0).ClockInTick);
    }

    // ---------------------------------------------------------------- Life-state model

    [Fact]
    public void LifeState_HasTheSixStatesInTheDocumentedOrder()
    {
        Assert.Equal(
            new[] { LifeState.Joining, LifeState.Alive, LifeState.Dead, LifeState.DeadAway, LifeState.Frozen, LifeState.Despawned },
            Enum.GetValues<LifeState>().OrderBy(v => (byte)v).ToArray());
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5 }, Enum.GetValues<LifeState>().Select(v => (byte)v).OrderBy(b => b).ToArray());
    }

    [Fact]
    public void GetLifeState_EmptySlot_AndNoneSlot_AreDespawned_AfterJoinAlive_AfterLeaveDespawned()
    {
        // Arrange
        var rig = PlayersRig.Create();
        Assert.Equal(LifeState.Despawned, rig.Players.GetLifeState(_slot0));
        Assert.Equal(LifeState.Despawned, rig.Players.GetLifeState(PlayerSlot.None));

        // Act + Assert
        rig.JoinAndTick(0);
        Assert.Equal(LifeState.Alive, rig.Players.GetLifeState(_slot0));
        Assert.Equal(LifeState.Despawned, rig.Players.GetLifeState(PlayerSlot.FromValue(1)));
        rig.LeaveAndTick(0);
        Assert.Equal(LifeState.Despawned, rig.Players.GetLifeState(_slot0));
    }

    [Fact]
    public void GetLifeState_ReadsTheRecordsLifeField()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(4);

        // Act
        rig.Record(4).Life = LifeState.Frozen;

        // Assert
        Assert.Equal(LifeState.Frozen, rig.Players.GetLifeState(PlayerSlot.FromValue(4)));
        Assert.IsAssignableFrom<IPlayersApi>(rig.Players);
    }

    [Fact]
    public void Transitions_OnlyJoiningToAlive_IsAnEdge_AtM1a()
    {
        // Arrange
        LifeState[] all = Enum.GetValues<LifeState>();

        // Act
        (LifeState From, LifeState To)[] edges = all.SelectMany(f => all.Select(t => (From: f, To: t))).Where(p => LifeStates.CanTransition(p.From, p.To)).ToArray();

        // Assert
        Assert.Equal(new[] { (LifeState.Joining, LifeState.Alive) }, edges);
    }

    [Fact]
    public void TryTransition_RefusesIllegalEdgesAndEmptySlots_AndLeavesTheLifeUnchanged()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);

        // Act + Assert
        Assert.False(rig.Players.TryTransition(_slot0, LifeState.Dead));
        Assert.Equal(LifeState.Alive, rig.Record(0).Life);
        Assert.False(rig.Players.TryTransition(PlayerSlot.FromValue(5), LifeState.Alive));
        Assert.False(rig.Players.TryTransition(PlayerSlot.None, LifeState.Alive));

        // Act + Assert: Joining to Alive is taken.
        rig.Record(0).Life = LifeState.Joining;
        Assert.True(rig.Players.TryTransition(_slot0, LifeState.Alive));
        Assert.Equal(LifeState.Alive, rig.Record(0).Life);
    }

    [Fact]
    public void RecordRef_NoneSlot_Throws()
    {
        var rig = PlayersRig.Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Players.RecordRef(PlayerSlot.None));
    }

    [Fact]
    public void LifeStateTimers_AreAbsoluteTicks_NothingDecrementsThem()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        rig.Record(0).ProtectedUntilTick = 500;
        rig.Record(0).FrozenUntilTick = 900;

        // Act
        rig.TickEmpty(40);

        // Assert
        Assert.Equal((500u, 900u), (rig.Record(0).ProtectedUntilTick, rig.Record(0).FrozenUntilTick));
        Assert.Equal(0u, rig.Record(0).ClockInTick);
    }

    // ---------------------------------------------------------------- Leave

    [Fact]
    public void Leave_RemovesTheRecord_AndTouchesNoOtherSlot()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        rig.JoinAndTick(3);
        PlayerRecord other = rig.Record(3);

        // Act
        rig.LeaveAndTick(0);

        // Assert
        Assert.True(rig.Record(0).IsEmpty);
        Assert.Equal(default(CarrierState).X, rig.Record(0).Carrier.X);
        Assert.Equal(other.PlayerId, rig.Record(3).PlayerId);
        Assert.Equal(other.Carrier.X, rig.Record(3).Carrier.X);
        Assert.False(rig.Host.Services.Slots.Entry(_slot0).Occupied);
        Assert.Equal(LifeState.Alive, rig.Players.GetLifeState(PlayerSlot.FromValue(3)));
    }

    [Fact]
    public void Rejoin_SameSlotAtTPlus1_CreatesAFreshRecord()
    {
        // Arrange: join on tick 0, dirty the record, leave on tick 1 (T = 1).
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        EntityId first = rig.Record(0).PlayerId;
        ref PlayerRecord r = ref rig.Record(0);
        r.Carrier.Vx = 4000;
        r.Carrier.Flags = CarrierFlags.Sprinting;
        r.Carrier.Pair[2].OverlapTicks = 9;
        r.LookYaw = 777;
        r.ProtectedUntilTick = 99;
        r.TeleportSeq = 5;
        rig.LeaveAndTick(0);

        // Act: tick 2 = T + 1.
        rig.JoinAndTick(0);

        // Assert
        PlayerRecord fresh = rig.Record(0);
        Assert.Equal(2u, fresh.ClockInTick);
        Assert.NotEqual(first, fresh.PlayerId);
        Assert.Equal(EntityClass.Player, fresh.PlayerId.Class);
        Assert.Equal(rig.Host.Services.Slots.Entry(_slot0).PlayerId, fresh.PlayerId);
        Assert.Equal(0, fresh.Carrier.Vx);
        Assert.Equal(CarrierFlags.None, fresh.Carrier.Flags);
        Assert.Equal((ushort)0, fresh.Carrier.Pair[2].OverlapTicks);
        Assert.Equal((ushort)0, fresh.LookYaw);
        Assert.Equal((0u, (byte)0), (fresh.ProtectedUntilTick, fresh.TeleportSeq));
        Assert.Equal(LifeState.Alive, fresh.Life);
    }

    [Fact]
    public void Rejoin_InTheTickOfTheLeave_IsSlotBusy_AndCreatesNoRecord()
    {
        // Arrange: leave and join queued for the same tick; the slot is freed this tick, so the join is refused (Sim Core).
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        var leave = new Op { Kind = OpKind.PlayerLeave, ReqId = 50 };
        leave.SetPayload(new PlayerLeavePayload { PlayerId = rig.Record(0).PlayerId.Value });
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 51 };
        join.SetPayload(new PlayerJoinPayload { Slot = 0 });
        Assert.True(rig.Sim.EnqueueOp(ActorId.System, in leave));
        Assert.True(rig.Sim.EnqueueOp(ActorId.System, in join));

        // Act
        rig.Sim.Tick();

        // Assert
        Assert.True(rig.Record(0).IsEmpty);
    }

    // ---------------------------------------------------------------- listener, binding and construction

    [Fact]
    public void Listener_JoinAndLeaveCalledDirectly_CreateAndRemoveTheRecord()
    {
        // Arrange
        var players = new PlayersSystem(WorldTablesFixture.Pad(), BuiltInContent.CreateM1a().Player);
        var listener = (IPlayerJoinListener)players;
        var id = new EntityId(16_777_300u);

        // Act
        listener.OnPlayerJoined(PlayerSlot.FromValue(6), id, 12);

        // Assert
        Assert.Equal(id, players.RecordRef(PlayerSlot.FromValue(6)).PlayerId);
        Assert.Equal(12u, players.RecordRef(PlayerSlot.FromValue(6)).ClockInTick);
        listener.OnPlayerLeft(PlayerSlot.FromValue(6), id);
        Assert.Equal(LifeState.Despawned, players.GetLifeState(PlayerSlot.FromValue(6)));
    }

    [Fact]
    public void Bind_BindsTheJoinListenerOnce_AndBindsNoPhase()
    {
        // Arrange
        var counters = new SimCounters();
        var ring = new SimCheckRing();
        var builder = new SimWorldBuilder(new SimCoreSettings(), counters, ring);
        var players = new PlayersSystem(WorldTablesFixture.Pad(), BuiltInContent.CreateM1a().Player);

        // Act
        players.Bind(builder);

        // Assert: a second listener bind is refused, and the world freezes and ticks with no phase from Players.
        SimBuildException ex = Assert.Throws<SimBuildException>(() => builder.BindPlayerJoinListener(NullPlayerJoinListener.Instance));
        Assert.Equal(SimBuildErrorCode.PlayerJoinListenerAlreadyBound, ex.Code);
        SimWorld world = builder.Freeze();
        world.Tick();
        Assert.Equal(1u, world.CurrentTick);
    }

    [Fact]
    public void Bind_Null_Throws_AndConstructorRejectsNullAndAWorldWithoutAPostOffice()
    {
        // Arrange
        PlayerBalance balance = BuiltInContent.CreateM1a().Player;
        var players = new PlayersSystem(WorldTablesFixture.Pad(), balance);
        WorldTables noPad = WorldTablesFixture.Dry();

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => players.Bind(null!));
        Assert.Throws<ArgumentNullException>(() => new PlayersSystem(null!, balance));
        Assert.Throws<ArgumentNullException>(() => new PlayersSystem(WorldTablesFixture.Pad(), (PlayerBalance)null!));
        Assert.Throws<ArgumentNullException>(() => new PlayersSystem(WorldTablesFixture.Pad(), (PlayerSettings)null!));
        SimBuildException ex = Assert.Throws<SimBuildException>(() => new PlayersSystem(noPad, balance));
        Assert.Equal(SimBuildErrorCode.PlayersWorldHasNoPostOffice, ex.Code);
    }

    // ---------------------------------------------------------------- Composition

    [Fact]
    public void Players_IsMarkedSimSystemPlayerCharacter_WithOnePublicConstructor()
    {
        // Arrange
        Type t = typeof(PlayersSystem);

        // Assert
        Assert.Equal(SystemId.PlayerCharacter, ((SimSystemAttribute)Attribute.GetCustomAttribute(t, typeof(SimSystemAttribute))!).Id);
        Assert.Single(t.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
        Assert.True(typeof(ISimSystem).IsAssignableFrom(t));
    }

    [Fact]
    public void CompositionRules_PlayersSystem_Passes_AndTheWholeSimAssemblyStillDoes()
    {
        // Act
        List<string> own = CompositionRules.Check(new[] { typeof(PlayersSystem) }, SystemGraph.Shipped);
        List<string> all = CompositionRules.Check(typeof(SimAssembly).Assembly.GetTypes(), SystemGraph.Shipped);

        // Assert
        Assert.True(own.Count == 0, string.Join(Environment.NewLine, own));
        Assert.True(all.Count == 0, string.Join(Environment.NewLine, all));
    }

    [Fact]
    public void SystemGraph_PlayerCharacter_DeclaresTheWorldAsItsDependency_AndTheOrderIsStillTopological()
    {
        Assert.True(SystemGraph.Shipped.Declares(SystemId.PlayerCharacter, SystemId.WorldGeneration));
        Assert.True(SystemGraph.Shipped.IsTopologicalOrder(SimComposition.ShippedOrder, out _));
    }

    [Fact]
    public void Compose_WithAWorld_BuildsExactlyOnePlayersSystem_WithoutOne_LeavesItUnbuilt()
    {
        // Arrange
        ContentSet content = BuiltInContent.CreateM1a();
        var withWorld = new SimBuildArgs(SimProfile.M1aGreybox, content, 1) { World = WorldTablesFixture.Pad() };
        var without = new SimBuildArgs(SimProfile.M1aGreybox, content, 1);

        // Act
        SimHost a = SimComposition.Build(in withWorld);
        SimHost b = SimComposition.Build(in without);

        // Assert
        Assert.IsType<PlayersSystem>(Assert.Single(a.Systems));
        Assert.Empty(b.Systems);
    }

    [Fact]
    public void Compose_PlayersReadsItsSettingsFromTheContentSet()
    {
        // Arrange + Act
        var rig = PlayersRig.Create();

        // Assert
        Assert.Equal(PlayerSettings.FromBalance(rig.Host.Content.Player), rig.Players.Settings);
    }

    [Fact]
    public void CarrierState_IsBlittable_AndExactly148Bytes_PushPairIs12()
    {
        // Assert: ADR-0013 section 2 (about 160 B budget); the exact size is pinned so a layout change is deliberate.
        Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<CarrierState>());
        Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<PlayerRecord>());
        Assert.True(System.Runtime.CompilerServices.Unsafe.SizeOf<CarrierState>() <= 160);
        Assert.Equal(148, System.Runtime.CompilerServices.Unsafe.SizeOf<CarrierState>());
        Assert.Equal(12, System.Runtime.CompilerServices.Unsafe.SizeOf<PushPairState>());
        Assert.Equal(7, PushPairArray.Length);
        Assert.Equal(7 * 12, System.Runtime.CompilerServices.Unsafe.SizeOf<PushPairArray>());
    }

    [Fact]
    public void Compose_WithoutAWorld_BuildsNoPlayersSystem_ButKeepsItInTheConstructionOrder()
    {
        // Arrange: documented interim behaviour - Players is left unbuilt when SimBuildArgs.World is null.
        var args = new SimBuildArgs(SimProfile.M1aGreybox, BuiltInContent.CreateM1a(), 1);
        Assert.Null(args.World);

        // Act
        SimHost host = SimComposition.Build(in args);

        // Assert
        Assert.Empty(host.Systems);
        Assert.DoesNotContain(host.Systems, s => s is PlayersSystem);
        Assert.Contains(SystemId.PlayerCharacter, host.Order);
    }

    [Fact]
    public void Join_ListenerCalledForAnOccupiedSlot_RaisesPlayersJoinSlotOccupied()
    {
        // Arrange
        var players = new PlayersSystem(WorldTablesFixture.Pad(), BuiltInContent.CreateM1a().Player);
        var listener = (IPlayerJoinListener)players;
        listener.OnPlayerJoined(_slot0, new EntityId(16_777_217u), 1);
        var second = new EntityId(16_777_218u);

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => listener.OnPlayerJoined(_slot0, second, 2));
            Assert.Equal(SimAssertCode.PlayersJoinSlotOccupied, ex.Code);
        }
        else
        {
            listener.OnPlayerJoined(_slot0, second, 2);
            Assert.Equal(second, players.RecordRef(_slot0).PlayerId);
            Assert.Equal(2u, players.RecordRef(_slot0).ClockInTick);
        }
    }

    [Fact]
    public void Leave_ListenerCalledWithAnotherPlayersId_RaisesPlayersLeaveMismatch()
    {
        // Arrange
        var players = new PlayersSystem(WorldTablesFixture.Pad(), BuiltInContent.CreateM1a().Player);
        var listener = (IPlayerJoinListener)players;
        listener.OnPlayerJoined(_slot0, new EntityId(16_777_217u), 1);
        var other = new EntityId(16_777_999u);

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => listener.OnPlayerLeft(_slot0, other));
            Assert.Equal(SimAssertCode.PlayersLeaveMismatch, ex.Code);
            Assert.False(players.RecordRef(_slot0).IsEmpty);
        }
        else
        {
            listener.OnPlayerLeft(_slot0, other);
            Assert.True(players.RecordRef(_slot0).IsEmpty);
        }
    }

    private static WorldTables BuildStreet()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }
}

/// <summary>Players' join and leave path allocates 0 B once warm (optimised builds only, TD-008).</summary>
[Collection(NonParallelCollection.Name)]
public class PlayersAllocationTests
{
    [Fact]
    [Trait("Id", "PCM-P1-1")]
    public void JoinAndLeave_AllocateNothing_InOptimisedBuilds()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        var players = new PlayersSystem(WorldTablesFixture.Pad(), BuiltInContent.CreateM1a().Player);
        var listener = (IPlayerJoinListener)players;
        var slot = PlayerSlot.FromValue(5);
        var id = new EntityId(16_777_217u);

        AllocAssert.Zero(
            "join then leave",
            () =>
            {
                listener.OnPlayerJoined(slot, id, 3);
                listener.OnPlayerLeft(slot, id);
            });
    }
}
