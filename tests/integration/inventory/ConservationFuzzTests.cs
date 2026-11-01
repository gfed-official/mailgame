using System.Diagnostics;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Integration.Inventory.Fuzz;

namespace PerformativeMail.Sim.Tests.Integration.Inventory;

/// <summary>
/// Runs the H-R19-1 M-1a fuzz once (story grid-inventory/018; GDD R19 l.763): a seeded world with 8 players, the Intake and the two
/// fixture chests (<see cref="InventoryFuzzWorld"/>), pre-populated with 400 mail units (60 addresses, 3 kinds, 10 % Cursed) and 200
/// item units, then <see cref="Ticks"/> ticks of 4 ops per player per tick through the real op FIFO and the automation stand-in. The
/// <see cref="InventoryInvariantChecker"/> asserts the counters after every op and the full check every 100 ops, so a broken
/// invariant fails the run itself with its seed, op index and letter. Shared by the tests of <see cref="ConservationFuzzTests"/>.
/// </summary>
public sealed class ConservationFuzzRun
{
    /// <summary>
    /// Fuzz ticks. Release runs the AC's 18 000 (576 000 ops). The unoptimised Debug legs, where the Sim also runs its
    /// <c>INVENTORY_ASSERT</c> scan every tick, run a fifth with the same seed, ops per tick and checks, so every CI leg still runs
    /// the AC evidence inside the suite's time (story 018 Completion Notes).
    /// </summary>
    // Mail Dev default, pending Evan (TD-206 item 6): Debug runs one fifth of the AC numbers (3 600 ticks) with the same seeds and checks; Release runs the AC numbers.
#if DEBUG
    public const int Ticks = 3_600;
#else
    public const int Ticks = 18_000;
#endif

    /// <summary>Ops per player per tick.</summary>
    public const int OpsPerSlotPerTick = 4;

    /// <summary>The generator seed (fixed in CI; printed by every failure).</summary>
    public const ulong Seed = 0x0018_F022_5EED_0001UL;

    /// <summary>The world's run seed.</summary>
    public const ulong RunSeed = 0x5EED_0018UL;

    public ConservationFuzzRun()
    {
        var clock = Stopwatch.StartNew();
        Checker = new InventoryInvariantChecker(Seed);
        World = InventoryFuzzWorld.Build(RunSeed, recorder: Checker, sink: Checker);
        Checker.Attach(World);
        Generator = new InventoryFuzzGenerator(Seed, World);

        TickWith(Generator.JoinOps());
        List<Op> prepopulation = Generator.PrepopulationOps();
        for (int i = 0; i < prepopulation.Count; i += 32)
        {
            TickWith(prepopulation.GetRange(i, Math.Min(32, prepopulation.Count - i)));
        }

        PrepopulatedMail = Checker.MailUnits;
        PrepopulatedItems = ItemUnitsLive();
        for (int t = 0; t < Ticks; t++)
        {
            Generator.Snapshot();
            for (int k = 0; k < OpsPerSlotPerTick; k++)
            {
                for (int slot = 0; slot < PlayerSlot.Count; slot++)
                {
                    Enqueue(Generator.Next(slot));
                }
            }

            Tick();
        }

        Checker.FullCheck();
        Elapsed = clock.Elapsed;
    }

    internal InventoryInvariantChecker Checker { get; }

    internal InventoryFuzzWorld World { get; }

    internal InventoryFuzzGenerator Generator { get; }

    /// <summary>Mail units in custody after the pre-population.</summary>
    public int PrepopulatedMail { get; }

    /// <summary>Item units in custody after the pre-population.</summary>
    public long PrepopulatedItems { get; }

    /// <summary>Wall time of the whole run (advisory budget 60 s).</summary>
    public TimeSpan Elapsed { get; }

    private long ItemUnitsLive()
    {
        UnitLedger ledger = World.Inventory.Ledger;
        long units = 0;
        for (int row = 1; row < ledger.Rows; row++)
        {
            units += ledger.LiveSum(row);
        }

        return units;
    }

    private void TickWith(List<Op> ops)
    {
        foreach (Op op in ops)
        {
            Enqueue(op);
        }

        Tick();
    }

    private void Enqueue(Op op) => Assert.True(World.World.EnqueueOp(op.Actor, in op), $"H-R19-1 seed=0x{Seed:X16} op={Checker.OpIndex}: enqueue refused");

    private void Tick()
    {
        try
        {
            World.World.Tick();
        }
        catch (SimAssertException ex)
        {
            ScanFindingRecord f = World.Inventory.ConservationCheck.ScanFinding;
            char letter = f.Kind == ScanFindingKind.Recount && f.A != UnitLedger.MailRow ? 'b' : 'a';
            Assert.Fail($"H-R19-1 seed=0x{Seed:X16} op={Checker.OpIndex} tick={World.World.CurrentTick} invariant {letter} (INVENTORY_ASSERT scan {f.Kind} {f.A}/{f.B}): {ex.Message}");
        }

        if (Checker.FirstFailure is { } failure)
        {
            Assert.Fail(failure); // raised inside the tick (Release: swallowed by Tick's fault path)
        }

        Assert.True(World.World.State != SimState.Faulted, $"H-R19-1 seed=0x{Seed:X16} op={Checker.OpIndex} tick={World.World.CurrentTick}: the world faulted");
        Checker.AfterTick();
    }
}

/// <summary>
/// H-R19-1, M-1a subset (story grid-inventory/018; TR-inventory-012; ADR-0009 sections 5-6): the fuzz run of
/// <see cref="ConservationFuzzRun"/> keeps the conservation, bounds, no-overlap, in-bounds and index invariants (a-e, h) after every op
/// and every 100 ops, covers every well-formed M-1a op type (i), and keeps every mail-id arena class below half its blocks.
/// </summary>
[Trait("Id", "H-R19-1")]
public class ConservationFuzzTests : IClassFixture<ConservationFuzzRun>
{
    private readonly ConservationFuzzRun _run;
    private readonly ITestOutputHelper _output;

    public ConservationFuzzTests(ConservationFuzzRun run, ITestOutputHelper output)
    {
        _run = run;
        _output = output;
    }

    [Fact]
    public void FuzzRun_KeepsInvariantsAToEAndH_AfterEveryOpAndEveryHundredOps()
    {
        // Arrange: the run (in the fixture) asserted every invariant after every op and every 100 ops; a failure there fails this
        // test with its seed, op index and letter
        _output.WriteLine(_run.Checker.Report(_run.Generator));
        _output.WriteLine($"{ConservationFuzzRun.Ticks} ticks in {_run.Elapsed.TotalSeconds:F1} s (advisory budget 60 s)");

        // Assert: it started from the AC's pre-population (400 mail units, 200 item units) and recorded no failure
        Assert.Equal(InventoryFuzzGenerator.PrepopulatedMail, _run.PrepopulatedMail);
        Assert.Equal(200, _run.PrepopulatedItems);
        Assert.Null(_run.Checker.FirstFailure);
    }

    [Fact]
    public void FuzzRun_AcceptsAtLeastFivePercentOfEachWellFormedOpType_AndAimsHalfItsOpsAtTheSharedChest()
    {
        _output.WriteLine(_run.Checker.Report(_run.Generator));

        _run.Checker.CheckCoverage(_run.Generator);
    }

    [Fact]
    public void FuzzRun_KeepsEveryMailIdArenaClassBelowHalfItsBlocks()
    {
        // Arrange
        MailIdArena arena = _run.World.Inventory.MailIds;

        // Act
        var peaks = new (int Peak, int Blocks)[MailIdArena.ClassCount];
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            peaks[c] = (arena.PeakLiveBlocks(c), arena.BlocksPerClass(c));
            _output.WriteLine($"arena class {c} ({MailIdArena.ClassSize(c)} units): peak {peaks[c].Peak} of {peaks[c].Blocks} blocks ({100.0 * peaks[c].Peak / peaks[c].Blocks:F1} %)");
        }

        // Assert (ADR-0009 section 5)
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            Assert.True(peaks[c].Peak * 2 < peaks[c].Blocks, $"H-R19-1 seed=0x{ConservationFuzzRun.Seed:X16}: arena class {c} peaked at {peaks[c].Peak} of {peaks[c].Blocks} blocks (needs < 50 %)");
        }
    }
}
