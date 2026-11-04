using System.Runtime.ExceptionServices;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Core.Replay;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Integration.Inventory.Fuzz;
using PerformativeMail.Sim.Tests.Integration.Sim;

namespace PerformativeMail.Sim.Tests.Integration.Inventory;

/// <summary>
/// Records the H-DET-1 log once (story grid-inventory/018): world R, driven one op per tick by the H-R19-1 generator (joins,
/// pre-population, then the fuzz op set), records <see cref="Ops"/> ops with an <see cref="InMemoryRecorder"/>. Worlds A and B (same
/// content and run seed) and C (another run seed) are then each built and replayed on their own thread through SC-DET-1's
/// <see cref="DeterminismRun.ReplayInto"/>, which hashes the whole canonical dump with SHA-256 before the first tick and after every
/// tick. Shared by the tests of <see cref="InventoryDeterminismTests"/>.
/// </summary>
public sealed class InventoryDeterminismRun
{
    /// <summary>
    /// Ops in the log, one per tick. Release runs the AC's 50 000. The unoptimised Debug legs replay a fifth with the same generator
    /// and seeds, so every CI leg still runs the AC evidence inside the suite's time (story 018 Completion Notes).
    /// </summary>
    // Mail Dev default, approved by Evan 2026-11-01 1:48 PM PT by card click (TD-206 item 6): Debug runs one fifth of the AC numbers (10 000 ops) with the same seeds and checks; Release runs the AC numbers.
#if DEBUG
    public const int Ops = 10_000;
#else
    public const int Ops = 50_000;
#endif

    /// <summary>Run seed of worlds R, A and B.</summary>
    public const ulong Seed = 0x5EED_0018UL;

    /// <summary>Run seed of world C.</summary>
    public const ulong OtherSeed = 0x5EED_0019UL;

    /// <summary>The generator seed.</summary>
    public const ulong GeneratorSeed = 0x0018_DE71_5EED_0001UL;

    public InventoryDeterminismRun()
    {
        Content = BuiltInContent.CreateM1a();
        Record();
        Input = new ReplayInput(Recorder!.Ops.ToArray(), Recorder.Samples.ToArray());
        HashesA = new byte[(Ops + 1) * DeterminismRun.HashSize];
        HashesB = new byte[(Ops + 1) * DeterminismRun.HashSize];
        HashesC = new byte[(Ops + 1) * DeterminismRun.HashSize];
        var finals = new string[3];
        var errors = new Exception?[3];
        Thread[] threads =
        [
            Start(0, Seed, HashesA, finals, errors),
            Start(1, Seed, HashesB, finals, errors),
            Start(2, OtherSeed, HashesC, finals, errors),
        ];
        foreach (Thread t in threads)
        {
            t.Join();
        }

        foreach (Exception? e in errors)
        {
            if (e is not null)
            {
                ExceptionDispatchInfo.Capture(e).Throw();
            }
        }

        FinalA = finals[0];
        FinalB = finals[1];
    }

    internal ContentSet Content { get; }

    internal InMemoryRecorder? Recorder { get; private set; }

    internal InventoryFuzzWorld? RecordedWorld { get; private set; }

    internal ReplayInput Input { get; }

    /// <summary>World R's dump hash after its last tick (<see cref="DumpHash.Sha256"/>).</summary>
    public string FinalRecorded { get; private set; } = "";

    /// <summary>World A's hashes: entry <c>i</c> is the hash after <c>i</c> ticks (entry 0 = before the first).</summary>
    public byte[] HashesA { get; }

    /// <summary>World B's hashes (same layout).</summary>
    public byte[] HashesB { get; }

    /// <summary>World C's hashes (same layout).</summary>
    public byte[] HashesC { get; }

    /// <summary>World A's <see cref="DumpHash.Sha256"/> after its last tick.</summary>
    public string FinalA { get; }

    /// <summary>World B's <see cref="DumpHash.Sha256"/> after its last tick.</summary>
    public string FinalB { get; }

    private void Record()
    {
        var recorder = new InMemoryRecorder(RecordingHeader.Create(Content, Seed), new RecorderCapacity(Ops + 16, Ops + 16, 16, 16, 16, 64));
        InventoryFuzzWorld world = InventoryFuzzWorld.Build(Seed, Content, recorder);
        var generator = new InventoryFuzzGenerator(GeneratorSeed, world);
        var script = new Queue<Op>(generator.JoinOps());
        bool prepopulated = false;
        for (int t = 0; t < Ops; t++)
        {
            if (script.Count == 0 && !prepopulated)
            {
                prepopulated = true;
                foreach (Op p in generator.PrepopulationOps())
                {
                    script.Enqueue(p);
                }
            }

            Op op;
            if (script.Count > 0)
            {
                op = script.Dequeue();
            }
            else
            {
                generator.Snapshot();
                op = generator.Next(t % PlayerSlot.Count);
            }

            Assert.True(world.World.EnqueueOp(op.Actor, in op), $"H-DET-1 seed=0x{GeneratorSeed:X16} op={t}: enqueue refused");
            world.World.Tick();
            recorder.Flush(world.World.CurrentTick);
        }

        Recorder = recorder;
        RecordedWorld = world;
        FinalRecorded = DumpHash.Sha256(world.World);
    }

    private Thread Start(int index, ulong seed, byte[] hashes, string[] finals, Exception?[] errors)
    {
        var thread = new Thread(() =>
        {
            try
            {
                InventoryFuzzWorld world = InventoryFuzzWorld.Build(seed, Content);
                DeterminismRun.ReplayInto(world.World, Input, Ops, hashes);
                finals[index] = DumpHash.Sha256(world.World);
            }
            catch (Exception ex)
            {
                errors[index] = ex;
            }
        })
        {
            Name = $"inventory-det-{index}",
        };
        thread.Start();
        return thread;
    }
}

/// <summary>
/// H-DET-1 (story grid-inventory/018; TR-inventory-028; ADR-0006 sections 4-5): two worlds from the same content and run seed, fed
/// the same 50 000-op log from the H-R19-1 generator one op per tick, have the same SHA-256 of the whole canonical dump after every
/// op; a world with another run seed diverges at the first tick whose state depends on the seed. Known limit: the open set is not in
/// the dump yet (TD-146), so an open-set divergence is invisible here until TD-146 closes.
/// </summary>
[Trait("Id", "H-DET-1")]
public class InventoryDeterminismTests : IClassFixture<InventoryDeterminismRun>
{
    private readonly InventoryDeterminismRun _run;

    public InventoryDeterminismTests(InventoryDeterminismRun run)
    {
        _run = run;
    }

    [Fact]
    public void TwoWorlds_SameContentSeedAndLog_HaveEqualWholeDumpHashAfterEveryOp()
    {
        // Arrange: the log is what the generator wrote, one op per tick, and it exercises every well-formed M-1a op type
        InMemoryRecorder rec = _run.Recorder!;
        long[] accepted = AcceptedPerType(rec, _run.RecordedWorld!.Automation);

        // Act
        int ab = DeterminismRun.FirstMismatch(_run.HashesA, _run.HashesB, InventoryDeterminismRun.Ops + 1);

        // Assert
        Assert.True(rec.IsReproducible);
        Assert.Equal(InventoryDeterminismRun.Ops, rec.Ops.Count);
        Assert.Equal(-1, ab);
        Assert.Equal(_run.FinalA, _run.FinalB);
        Assert.Equal(_run.FinalRecorded, _run.FinalA);
        Assert.Equal(_run.FinalA, Convert.ToHexString(_run.HashesA.AsSpan(InventoryDeterminismRun.Ops * DeterminismRun.HashSize, DeterminismRun.HashSize)));
        for (var t = FuzzOpType.Pick; t < FuzzOpType.Malformed; t++)
        {
            Assert.True(accepted[(int)t] > 0, $"H-DET-1 seed=0x{InventoryDeterminismRun.GeneratorSeed:X16}: the log has no accepted {t}");
        }
    }

    [Fact]
    public void WorldWithAnotherRunSeed_DivergesAtTheFirstRngDraw()
    {
        // Arrange: the first Land command draws its position from the world's own RNG stream (nothing earlier depends on the run seed)
        uint firstDraw = _run.RecordedWorld!.Automation.FirstDrawTick;
        Assert.True(firstDraw < InventoryDeterminismRun.Ops);

        // Act
        int divergence = DeterminismRun.FirstMismatch(_run.HashesA, _run.HashesC, InventoryDeterminismRun.Ops + 1);

        // Assert: every hash up to that tick matches, the one after it differs
        Assert.Equal((int)firstDraw + 1, divergence);
    }

    private static long[] AcceptedPerType(InMemoryRecorder rec, FuzzAutomationSystem automation)
    {
        long[] accepted = (long[])automation.Accepted.Clone();
        for (int i = 0; i < rec.Drains.Count; i++)
        {
            ref readonly RecordedDrain d = ref rec.Drains[i];
            if (d.Op.Kind != FuzzAutomationSystem.CommandKind && d.Reply.IsOk)
            {
                accepted[d.Op.ReqId & InventoryFuzzGenerator.TypeMask]++;
            }
        }

        return accepted;
    }
}
