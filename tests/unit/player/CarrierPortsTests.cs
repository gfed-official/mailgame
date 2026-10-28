using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 002: the tick-time ports, <see cref="CarrierInput"/>, <see cref="CarrierEnv"/>, <see cref="CarrierScratch"/>
/// and <see cref="CarrierCounterFlush"/> (ADR-0013 sections 1, 4 and 7; TR-movement-004, TR-movement-001).
/// </summary>
public class CarrierPortsTests
{
    private static RemoteCapsule Cap(uint id, byte slot, int x, int z, bool solid = true) =>
        new(new EntityId(id), PlayerSlot.FromValue(slot), x, 0, z, solid);

    private static VehiclePose Veh(uint id, int x) => new(new EntityId(id), x, 500, -x, 16384, 1000, 500, 2000, 1000);

    // AC-1: the ports exist exactly as declared.
    [Fact]
    public void Ports_ExposeTheDeclaredShape()
    {
        var capsule = new RemoteCapsule(new EntityId(7), PlayerSlot.FromValue(3), 1, 2, 3, true);
        Assert.Equal(7u, capsule.Id.Value);
        Assert.Equal((byte)3, capsule.Slot.Value);
        Assert.Equal((1, 2, 3, true), (capsule.X, capsule.Y, capsule.Z, capsule.Solid));

        var pose = new VehiclePose(new EntityId(9), 10, 20, 30, 4096, 100, 200, 300, 400);
        Assert.Equal((10, 20, 30, (ushort)4096), (pose.X, pose.Y, pose.Z, pose.YawBam));
        Assert.Equal((100, 200, 300, 400), (pose.HalfX, pose.HalfY, pose.HalfZ, pose.RoofY));

        var remotes = new ScriptedRemoteCapsuleSource([[Cap(1, 0, 5, 6)]]);
        remotes.SetTick(0);
        IRemoteCapsuleSource asPort = remotes;
        Assert.Equal(1, asPort.Capsules.Length);

        var supports = new ScriptedSupportSource([[Veh(2, 100)]], [[new ScriptedBelt(4, 5, 100, -200, 77u)]]);
        supports.SetTick(0);
        ISupportTransformSource asSupport = supports;
        Assert.Equal(1, asSupport.Vehicles.Length);
        Assert.True(asSupport.TryGetBeltVelocity(4, 5, out int vx, out int vz, out uint seg));
        Assert.Equal((100, -200, 77u), (vx, vz, seg));
        Assert.False(asSupport.TryGetBeltVelocity(0, 0, out vx, out vz, out seg));
        Assert.Equal((0, 0, 0u), (vx, vz, seg));
    }

    // AC-2: empty M-1a sources, and they are the CarrierEnv defaults for the M1aGreybox profile.
    [Fact]
    public void EmptySources_ReturnNothing()
    {
        IRemoteCapsuleSource remotes = new EmptyRemoteCapsuleSource();
        ISupportTransformSource supports = new EmptySupportTransformSource();

        Assert.Equal(0, remotes.Capsules.Length);
        Assert.Equal(0, supports.Vehicles.Length);
        Assert.False(supports.TryGetBeltVelocity(3, 4, out int vx, out int vz, out uint seg));
        Assert.Equal((0, 0, 0u), (vx, vz, seg));
    }

    [Fact]
    public void PlayersSystem_CreateCarrierEnv_UsesTheEmptyPortsAndItsOwnWorldAndSettings()
    {
        PlayersRig rig = PlayersRig.Create();
        var self = new EntityId(0x0100_0001);

        CarrierEnv env = rig.Players.CreateCarrierEnv(self);

        Assert.IsType<EmptyRemoteCapsuleSource>(env.Remotes);
        Assert.IsType<EmptySupportTransformSource>(env.Supports);
        Assert.Equal(0, env.Remotes.Capsules.Length);
        Assert.Equal(0, env.Supports.Vehicles.Length);
        Assert.Same(rig.Players.Settings, env.Settings);
        Assert.Same(rig.World, env.World);
        Assert.Equal(self, env.Self);

        // A second call shares the same empty port instances.
        CarrierEnv other = rig.Players.CreateCarrierEnv(new EntityId(0x0100_0002));
        Assert.Same(env.Remotes, other.Remotes);
        Assert.Same(env.Supports, other.Supports);
    }

    // AC-3: CarrierInput carries the listed fields; Idle is full speed, default is speed 0.
    [Fact]
    public void CarrierInput_CarriesTheDeclaredFields()
    {
        var i = new CarrierInput
        {
            MoveX = -32768, MoveY = 23170, LookYaw = 65535, SprintHeld = true, JumpPressed = true, Aiming = true,
            ContextFlags = CarrierContextFlags.Panel, HostAuthored = true, SpeedPermille = 1250, BuffAddPm = 100, ExtraJumps = 2,
        };

        Assert.Equal((-32768, 23170, (ushort)65535), (i.MoveX, i.MoveY, i.LookYaw));
        Assert.True(i.SprintHeld && i.JumpPressed && i.Aiming && i.HostAuthored);
        Assert.Equal(CarrierContextFlags.Panel, i.ContextFlags);
        Assert.Equal((1250, 100, 2), (i.SpeedPermille, i.BuffAddPm, i.ExtraJumps));
        Assert.Equal(1000, CarrierInput.Idle.SpeedPermille);
        Assert.Equal(0, default(CarrierInput).SpeedPermille);
    }

    // AC-4: fixed capacity, overflow counted, never grows, never throws.
    [Fact]
    public void Scratch_65thSolid_LeavesLengthUnchangedAndCountsOverflow()
    {
        var scratch = new CarrierScratch(64);

        for (ushort k = 0; k < 64; k++)
        {
            Assert.True(scratch.TryAddSolid(k));
        }

        Assert.Equal(64, scratch.Solids.Length);
        bool kept = true;
        Exception? thrown = Record.Exception(() => kept = scratch.TryAddSolid(999));
        Assert.Null(thrown);
        Assert.False(kept);
        Assert.Equal(64, scratch.Solids.Length);
        Assert.Equal(1, scratch.CandidateOverflow);
        Assert.Equal((ushort)63, scratch.Solids[63]);

        _ = scratch.TryAddSolid(1000);
        Assert.Equal(2, scratch.CandidateOverflow);
    }

    [Fact]
    public void Scratch_SettingsLimitBelowCapacity_IsRespected_AndAboveCapacityIsClamped()
    {
        var small = new CarrierScratch(3);
        for (ushort k = 0; k < 5; k++)
        {
            _ = small.TryAddSolid(k);
        }

        Assert.Equal(3, small.SolidCount);
        Assert.Equal(2, small.CandidateOverflow);

        var big = new CarrierScratch(10_000);
        for (ushort k = 0; k < 70; k++)
        {
            _ = big.TryAddSolid(k);
        }

        Assert.Equal(CarrierScratch.SolidCapacity, big.SolidCount);
        Assert.Equal(6, big.CandidateOverflow);
    }

    [Fact]
    public void Scratch_BoxesAndVehicles_AreBoundedAndCounted()
    {
        var scratch = new CarrierScratch(64);
        var box = new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(1, 1, 1));

        for (int k = 0; k < CarrierScratch.BoxCapacity + 2; k++)
        {
            _ = scratch.TryAddBox(in box);
        }

        for (int k = 0; k < CarrierScratch.VehicleCapacity + 1; k++)
        {
            _ = scratch.TryAddVehicle((ushort)k);
        }

        Assert.Equal(CarrierScratch.BoxCapacity, scratch.Boxes.Length);
        Assert.Equal(CarrierScratch.VehicleCapacity, scratch.Vehicles.Length);
        Assert.Equal(3, scratch.CandidateOverflow);
    }

    [Fact]
    public void Scratch_Clear_EmptiesCandidatesAndCounters_ButKeepsLimit()
    {
        var scratch = new CarrierScratch(2);
        _ = scratch.TryAddSolid(1);
        _ = scratch.TryAddSolid(2);
        _ = scratch.TryAddSolid(3);
        scratch.SubStepsMaxThisTick = 4;
        scratch.SlideIterationsMax = 3;
        scratch.StepClamped = 1;
        scratch.Depenetrations = 2;

        scratch.Clear();

        Assert.Equal(0, scratch.SolidCount);
        Assert.Equal((0, 0, 0, 0, 0), (scratch.SubStepsMaxThisTick, scratch.SlideIterationsMax, scratch.StepClamped, scratch.Depenetrations, scratch.CandidateOverflow));
        Assert.Equal(2, scratch.Limit);
        Assert.True(scratch.TryAddSolid(9));
    }

    // AC-5: flush semantics.
    [Fact]
    public void Flush_MaxKeysKeepTheMaximum_AddKeysAccumulate()
    {
        var counters = new SimCounters();
        var scratch = new CarrierScratch(64);
        scratch.SubStepsMaxThisTick = 3;
        scratch.SlideIterationsMax = 2;
        scratch.StepClamped = 1;
        scratch.Depenetrations = 2;
        scratch.CandidateOverflow = 5;

        CarrierCounterFlush.Flush(counters, ref scratch);

        Assert.Equal(3L, counters[CounterId.CarrierSubSteps]);
        Assert.Equal(2L, counters[CounterId.CarrierSlideIterations]);
        Assert.Equal(1L, counters[CounterId.CarrierStepClamped]);
        Assert.Equal(2L, counters[CounterId.CarrierDepenetrations]);
        Assert.Equal(5L, counters[CounterId.CarrierCandidateOverflow]);

        // A smaller maximum does not lower the key; the add keys accumulate.
        scratch.SubStepsMaxThisTick = 1;
        scratch.SlideIterationsMax = 3;
        scratch.StepClamped = 1;
        scratch.CandidateOverflow = 2;
        CarrierCounterFlush.Flush(counters, ref scratch);

        Assert.Equal(3L, counters[CounterId.CarrierSubSteps]);
        Assert.Equal(3L, counters[CounterId.CarrierSlideIterations]);
        Assert.Equal(2L, counters[CounterId.CarrierStepClamped]);
        Assert.Equal(2L, counters[CounterId.CarrierDepenetrations]);
        Assert.Equal(7L, counters[CounterId.CarrierCandidateOverflow]);
    }

    [Fact]
    public void Flush_ZeroesTheScratchCounters_SoARepeatAddsNothing_AndTouchesNoOtherKey()
    {
        var counters = new SimCounters();
        var scratch = new CarrierScratch(64) { SubStepsMaxThisTick = 4, StepClamped = 2 };

        CarrierCounterFlush.Flush(counters, ref scratch);
        CarrierCounterFlush.Flush(counters, ref scratch);

        Assert.Equal(4L, counters[CounterId.CarrierSubSteps]);
        Assert.Equal(2L, counters[CounterId.CarrierStepClamped]);
        Assert.Equal(0, scratch.StepClamped);
        Assert.Equal(0L, counters[CounterId.BadTick]);
        Assert.Equal(0L, counters[CounterId.InputEdgeOverflow]);
    }

    // AC-6: scripted doubles replay bit-identically.
    [Fact]
    public void ScriptedSources_TwoRuns_ProduceIdenticalReadsPerTick()
    {
        RemoteCapsule[][] caps =
        [
            [Cap(1, 0, 100, 200), Cap(2, 1, -50, 75, solid: false)],
            [],
            [Cap(1, 0, 130, 210)],
            [Cap(1, 0, 160, 220), Cap(3, 2, 1, 1), Cap(4, 3, 2, 2)],
        ];
        VehiclePose[][] vehs = [[Veh(10, 1000)], [Veh(10, 1010)], [], [Veh(10, 1030), Veh(11, 5)]];
        ScriptedBelt[][] belts = [[new ScriptedBelt(1, 2, 300, 0, 5u)], [], [new ScriptedBelt(1, 2, -300, 40, 6u)], []];

        ulong a = Replay(caps, vehs, belts);
        ulong b = Replay(caps, vehs, belts);

        Assert.Equal(a, b);
        Assert.NotEqual(0UL, a);
        Assert.NotEqual(a, Replay(caps, [[Veh(10, 1000)], [Veh(10, 1011)], [], [Veh(10, 1030), Veh(11, 5)]], belts));
    }

    [Fact]
    public void ScriptedSources_SetTick_CopiesIntoBackingStorage()
    {
        RemoteCapsule[][] caps = [[Cap(1, 0, 100, 200)], [Cap(1, 0, 300, 400)]];
        var source = new ScriptedRemoteCapsuleSource(caps);

        source.SetTick(0);
        caps[0][0] = Cap(9, 5, -1, -1);          // mutating the script after SetTick must not change what the tick saw
        Assert.Equal(100, source.Capsules[0].X);
        Assert.Equal(1u, source.Capsules[0].Id.Value);

        source.SetTick(1);
        Assert.Equal(300, source.Capsules[0].X);
        source.SetTick(2);                       // beyond the script: empty
        Assert.Equal(0, source.Capsules.Length);
        source.SetTick(-1);
        Assert.Equal(0, source.Capsules.Length);
    }

    [Fact]
    public void ScriptedSupportSource_SetTick_CopiesAndBeyondScriptIsEmpty()
    {
        VehiclePose[][] vehs = [[Veh(1, 10)], [Veh(2, 20)]];
        ScriptedBelt[][] belts = [[new ScriptedBelt(1, 1, 5, 6, 7u)], []];
        var source = new ScriptedSupportSource(vehs, belts);

        source.SetTick(0);
        vehs[0][0] = Veh(99, 999);
        belts[0][0] = new ScriptedBelt(1, 1, -1, -1, 0u);
        Assert.Equal(1u, source.Vehicles[0].Id.Value);
        Assert.Equal(10, source.Vehicles[0].X);
        Assert.True(source.TryGetBeltVelocity(1, 1, out int vx, out int vz, out uint seg));
        Assert.Equal((5, 6, 7u), (vx, vz, seg));

        foreach (int tick in new[] { 2, -1, 1000 })
        {
            source.SetTick(tick);
            Assert.Equal(0, source.Vehicles.Length);
            Assert.False(source.TryGetBeltVelocity(1, 1, out _, out _, out _));
        }
    }

    [Fact]
    public void ScriptedSources_RowLongerThanCapacity_TruncatesToCapacity()
    {
        RemoteCapsule[] row = Enumerable.Range(0, 9).Select(i => Cap((uint)(i + 1), 0, i, i)).ToArray();
        VehiclePose[] vrow = Enumerable.Range(0, 9).Select(i => Veh((uint)(i + 1), i)).ToArray();
        var remotes = new ScriptedRemoteCapsuleSource([row]);
        var supports = new ScriptedSupportSource([vrow]);

        remotes.SetTick(0);
        supports.SetTick(0);

        Assert.Equal(8, remotes.Capsules.Length);
        Assert.Equal(8, supports.Vehicles.Length);
        Assert.Equal(8u, remotes.Capsules[7].Id.Value);
        Assert.Equal(8u, supports.Vehicles[7].Id.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Scratch_NonPositiveLimit_KeepsNothingAndCountsEveryAdd(int limit)
    {
        var scratch = new CarrierScratch(limit);
        var box = new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(1, 1, 1));

        Assert.False(scratch.TryAddSolid(1));
        Assert.False(scratch.TryAddBox(in box));
        Assert.False(scratch.TryAddVehicle(1));

        Assert.Equal((0, 0, 0), (scratch.SolidCount, scratch.BoxCount, scratch.VehicleCount));
        Assert.Equal(3, scratch.CandidateOverflow);
        Assert.Equal(0, scratch.Limit);
    }

    [Fact]
    public void Scratch_DefaultHasLimitZero()
    {
        CarrierScratch scratch = default;

        Assert.False(scratch.TryAddSolid(1));
        Assert.Equal(1, scratch.CandidateOverflow);
    }

    [Fact]
    public void Scratch_Limit3_BoundsAllBuffersAndKeepsTheFirstNInOrder()
    {
        var scratch = new CarrierScratch(3);
        for (int k = 0; k < 5; k++)
        {
            var box = new AabbMm(new Vec3Mm(k, 0, 0), new Vec3Mm(k + 1, 1, 1));
            _ = scratch.TryAddSolid((ushort)(10 + k));
            _ = scratch.TryAddBox(in box);
            _ = scratch.TryAddVehicle((ushort)(20 + k));
        }

        Assert.Equal(new ushort[] { 10, 11, 12 }, scratch.Solids.ToArray());
        Assert.Equal(new ushort[] { 20, 21, 22 }, scratch.Vehicles.ToArray());
        Assert.Equal(3, scratch.Boxes.Length);
        Assert.Equal(new[] { 0, 1, 2 }, scratch.Boxes.ToArray().Select(b => b.Min.X).ToArray());
        Assert.Equal(6, scratch.CandidateOverflow);
    }

    [Fact]
    public void MaxCarrierCandidatesKnob_HardMaxEqualsScratchSolidCapacity()
    {
        BalanceRow row = BalanceKeys.Player.Rows.Single(r => r.Key == BalanceKeys.Player.MaxCarrierCandidatesKey);

        Assert.Equal(CarrierScratch.SolidCapacity, row.HardMax);
        Assert.Equal(1L, row.HardMin);
    }

    [Fact]
    public void CarrierEnv_NullWorldSupportsOrSettings_Throw()
    {
        PlayersRig rig = PlayersRig.Create();
        var remotes = new EmptyRemoteCapsuleSource();
        var supports = new EmptySupportTransformSource();
        PlayerSettings settings = rig.Players.Settings;

        Assert.Throws<ArgumentNullException>(() => new CarrierEnv(null!, remotes, supports, settings, default));
        Assert.Throws<ArgumentNullException>(() => new CarrierEnv(rig.World, null!, supports, settings, default));
        Assert.Throws<ArgumentNullException>(() => new CarrierEnv(rig.World, remotes, null!, settings, default));
        Assert.Throws<ArgumentNullException>(() => new CarrierEnv(rig.World, remotes, supports, null!, default));
    }

    private static ulong Replay(RemoteCapsule[][] caps, VehiclePose[][] vehs, ScriptedBelt[][] belts)
    {
        var remotes = new ScriptedRemoteCapsuleSource(caps);
        var supports = new ScriptedSupportSource(vehs, belts);
        ulong h = 14695981039346656037UL;
        for (int tick = 0; tick < caps.Length; tick++)
        {
            remotes.SetTick(tick);
            supports.SetTick(tick);
            foreach (RemoteCapsule c in remotes.Capsules)
            {
                h = Mix(h, c.Id.Value, c.Slot.Value, (uint)c.X, (uint)c.Y, (uint)c.Z, c.Solid ? 1u : 0u);
            }

            foreach (VehiclePose v in supports.Vehicles)
            {
                h = Mix(h, v.Id.Value, (uint)v.X, (uint)v.Y, (uint)v.Z, v.YawBam, (uint)(v.HalfX ^ v.HalfY ^ v.HalfZ ^ v.RoofY));
            }

            bool hit = supports.TryGetBeltVelocity(1, 2, out int vx, out int vz, out uint seg);
            h = Mix(h, hit ? 1u : 0u, (uint)vx, (uint)vz, seg, (uint)tick, 0u);
        }

        return h;
    }

    private static ulong Mix(ulong h, uint a, uint b, uint c, uint d, uint e, uint f)
    {
        foreach (uint v in new[] { a, b, c, d, e, f })
        {
            h = (h ^ v) * 1099511628211UL;
        }

        return h;
    }
}

/// <summary>Zero-allocation guardrail for the scratch and flush (story 002 AC-7), in the non-parallel collection.</summary>
[Collection(NonParallelCollection.Name)]
public class CarrierScratchAllocationTests
{
    [Fact]
    public void ConstructClearAndFlush_10000Times_AllocateNothing()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(CarrierScratch).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var counters = new SimCounters();
        PlayersRig rig = PlayersRig.Create();
        var empty1 = new EmptyRemoteCapsuleSource();
        var empty2 = new EmptySupportTransformSource();
        var self = new EntityId(5);
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("10,000 construct + clear + add + flush cycles", () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                var scratch = new CarrierScratch(64);
                scratch.Clear();
                _ = scratch.TryAddSolid((ushort)i);
                scratch.SubStepsMaxThisTick = i & 3;
                scratch.CandidateOverflow = 1;
                sink += scratch.Solids.Length;
                CarrierCounterFlush.Flush(counters, ref scratch);
                CarrierEnv env = rig.Players.CreateCarrierEnv(self);
                sink += env.Remotes.Capsules.Length + env.Supports.Vehicles.Length;
                sink += empty1.Capsules.Length + empty2.Vehicles.Length;
                sink += empty2.TryGetBeltVelocity(i, i, out int vx, out int vz, out uint seg) ? 1 : vx + vz + (int)seg;
            }
        });

        Assert.NotEqual(long.MinValue, sink);
    }
}
