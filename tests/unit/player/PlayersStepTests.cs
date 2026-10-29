using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 019: phase 3 <see cref="PlayersStep"/> on a composed world (ADR-0003 section 2, ADR-0013 section 1; GDD P2):
/// the latched samples of Alive players become the host position and are never stepped; Dead and Frozen bodies are host-authored and
/// their owners' samples ignored; <c>PlayerJoin</c> seeds the host position at the spawn point.
/// </summary>
public sealed class PlayersStepTests
{
    private static PlayersRig Joined(params byte[] slots)
    {
        var rig = PlayersRig.Create();
        rig.TickEmpty(1);
        foreach (byte s in slots)
        {
            rig.JoinAndTick(s);
        }

        return rig;
    }

    private static Vec3Mm Offset(Vec3Mm p, int dx, int dy = 0, int dz = 0) => new(p.X + dx, p.Y + dy, p.Z + dz);

    // ---------------------------------------------------------------- PCM-P2-2

    [Fact]
    [Trait("Id", "PCM-P2-2")]
    public void Step_AliveSamples_NewestPerSlotBecomesTheHostPosition_StampedWithTheLatchTick()
    {
        // Arrange: slot 0 reports twice in one tick (the second is newest, swimming), slot 1 once.
        PlayersRig rig = Joined(0, 1);
        Vec3Mm a = Offset(rig.Spawn(0), 300);
        Vec3Mm b = Offset(rig.Spawn(0), 700, 0, -200);
        Vec3Mm c = Offset(rig.Spawn(1), -400);
        rig.Report(1, c, yaw: 0x2000);
        rig.Report(0, a, yaw: 0x1000);
        rig.Report(0, b, SampleFlags.Swimming, yaw: 0x4000);

        // Act
        rig.Sim.Tick();

        // Assert
        uint received = rig.Sim.Samples.ReceivedTick;
        Assert.Equal(rig.Sim.CurrentTick - 1, received);
        HostPosition p0 = rig.HostPosition(0);
        Assert.Equal((b, (ushort)0x4000, SampleFlags.Swimming, received), (p0.Feet, p0.Yaw, p0.Flags, p0.LastReceivedTick));
        HostPosition p1 = rig.HostPosition(1);
        Assert.Equal((c, (ushort)0x2000, SampleFlags.None, received), (p1.Feet, p1.Yaw, p1.Flags, p1.LastReceivedTick));
        Assert.Equal(3L, rig.Sim.Counters[CounterId.SamplesIngested]);
    }

    [Fact]
    [Trait("Id", "PCM-P2-2")]
    public void Step_100TicksOfAliveReports_NeverStepsTheCarrierOnTheHost()
    {
        // Arrange
        PlayersRig rig = Joined(0);
        CarrierState before = rig.Record(0).Carrier;
        Vec3Mm feet = rig.Spawn(0);

        // Act: the owner walks 30 mm per tick.
        for (int i = 1; i <= 100; i++)
        {
            rig.Report(0, Offset(feet, 30 * i));
            rig.Sim.Tick();
        }

        // Assert
        Assert.Equal(0L, rig.Sim.Counters[CounterId.HostCarrierSteps]);
        Assert.Equal(100L, rig.Sim.Counters[CounterId.SamplesIngested]);
        Assert.Equal((before.X, before.Y, before.Z), (rig.Record(0).Carrier.X, rig.Record(0).Carrier.Y, rig.Record(0).Carrier.Z));
        Assert.Equal(Offset(feet, 3000), rig.HostPosition(0).Feet);
    }

    [Theory]
    [Trait("Id", "PCM-P2-2")]
    [InlineData(LifeState.Dead)]
    [InlineData(LifeState.Frozen)]
    public void Step_DeadOrFrozenBody_IsHostAuthored_GroundDropped_AndIgnoresOwnerSamples(LifeState life)
    {
        // Arrange: the body hangs 2 m above the ground; the owner keeps reporting a far position.
        PlayersRig rig = Joined(0);
        ref PlayerRecord r = ref rig.Record(0);
        r.Life = life;
        r.Carrier.Y += 2000;
        r.Carrier.Loco = LocoState.Airborne;
        Vec3Mm far = Offset(rig.Spawn(0), 5000);

        // Act
        for (int i = 0; i < 60; i++)
        {
            rig.Report(0, far);
            rig.Sim.Tick();
        }

        // Assert: feet Y is the terrain height under (X, Z); the stored position is the authored one, not the owner's.
        CarrierState c = rig.Record(0).Carrier;
        Assert.Equal(rig.World.TerrainHeightMm(c.X, c.Z), (long)c.Y);
        Assert.Equal(new Vec3Mm(c.X, c.Y, c.Z), rig.HostPosition(0).Feet);
        Assert.Equal(60L, rig.Sim.Counters[CounterId.HostCarrierSteps]);
        Assert.Equal(60L, rig.Sim.Counters[CounterId.SamplesIgnoredAuthored]);
        Assert.Equal(0L, rig.Sim.Counters[CounterId.SamplesIngested]);
    }

    // ---------------------------------------------------------------- Seeding

    [Theory]
    [Trait("Id", "PC019-SEED-1")]
    [InlineData(1u, InteractionCheckKind.Ok)]              // the tick after the join
    [InlineData(31u, InteractionCheckKind.PositionStale)]  // 31 ticks with no transform since the join
    public void Join_SeedsTheHostPositionAtTheSpawnPoint_ReceivedOnTheJoinTick(uint age, InteractionCheckKind expected)
    {
        // Arrange
        PlayersRig rig = Joined(0);
        Vec3Mm spawn = rig.Spawn(0);
        var anchor = new AnchorRef(new EntityId(16_777_400u), 0);
        AnchorDef def = AnchorDef.For(anchor, new Vec3Mm(spawn.X + 1000, spawn.Y + 1000, spawn.Z), Verb.Open);
        Assert.Equal(RegisterResult.Ok, rig.Players.RegisterAnchor(in def, new CountingInteractable()));
        Assert.Equal(rig.Sim.CurrentTick - 1, rig.HostPosition(0).LastReceivedTick);
        rig.AgeTo(0, age);

        // Act
        InteractionCheck r = rig.Players.CheckInteraction(rig.Actor(0), anchor, new ClaimedPosition(spawn.X, spawn.Y, spawn.Z));

        // Assert
        Assert.Equal(expected, r.Kind);
    }
}

/// <summary>
/// Story 019 Counters / budget, ADVISORY (E2; TR-movement-021 has no ADR coverage): <see cref="PlayersStep"/> for 8 players with 2 000
/// registered anchors (none stepped) against the 0.20 ms p99 <c>players.hostStep</c> target, and <c>CheckInteraction</c>'s bytes against
/// 0 B. Both are written to the output and never asserted until M0 (Evan approved 2026-10-29 (TD-188 item 6)).
/// </summary>
[Collection(NonParallelCollection.Name)]
[Trait("Category", "Perf")]
public sealed class PlayersStepPerfTests
{
    private static Vec3Mm Offset(Vec3Mm p, int dx) => new(p.X + dx, p.Y, p.Z);

    [Fact]
    public void Step_8PlayersAnd2000Anchors_P99AndCheckInteractionAllocation_AreReported_Advisory()
    {
        // Arrange: 8 Alive players reporting every tick, 2 000 registered anchors (none stepped).
        const int Ticks = 2000;
        var rig = PlayersRig.Create();
        rig.TickEmpty(1);
        for (byte s = 0; s < PlayerSlot.Count; s++)
        {
            rig.JoinAndTick(s);
        }

        Vec3Mm spawn = rig.Spawn(0);
        for (int i = 0; i < 2000; i++)
        {
            var def = AnchorDef.For(new AnchorRef(new EntityId(16_777_400u + (uint)i), 0), new Vec3Mm(1000 + (7 * i), 1000, 1000 + (5 * i)), Verb.Open);
            Assert.Equal(RegisterResult.Ok, rig.Players.RegisterAnchor(in def, new CountingInteractable()));
        }

        var probe = new PlayersStep(rig.Players, rig.Sim.Samples, rig.Host.Services.Slots, new SimCounters());
        var samples = new long[Ticks];

        // Act: the world's tick latches 8 samples; the probe re-runs phase 3 over that latch, timed.
        for (int t = 0; t < Ticks; t++)
        {
            for (byte s = 0; s < PlayerSlot.Count; s++)
            {
                rig.Report(s, Offset(rig.Spawn(s), t % 100));
            }

            rig.Sim.Tick();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            probe.Run(new TickContext(rig.Sim.CurrentTick - 1, PhaseId.PlayersStep));
            samples[t] = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }

        var anchor = new AnchorRef(new EntityId(16_777_400u + 1999u), 0);
        var claim = new ClaimedPosition(spawn.X, spawn.Y, spawn.Z);
        ActorId actor = rig.Actor(0);
        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                _ = rig.Players.CheckInteraction(actor, anchor, in claim);
            }
        });

        // Report (advisory: no assertion on time or bytes until M0, E2; TR-movement-021 has no ADR coverage).
        Array.Sort(samples);
        double p99Ms = samples[(Ticks * 99 / 100) - 1] * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"players.hostStep (advisory) 8 players, 2000 anchors, {Ticks} ticks: p99 {p99Ms:F4} ms (target 0.20 ms); "
            + $"CheckInteraction x 1000: {bytes} B over {runs} runs (target 0 B)");
    }
}
