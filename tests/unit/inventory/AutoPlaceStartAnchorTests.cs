using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/021 review round 1 A5 (b): <see cref="AutoPlace.BeginStartAnchors"/>'s per-oriented-shape start anchors
/// change only the probe count, never a placement. One pass of mixed, rotatable footprints (more distinct oriented shapes than
/// <see cref="AutoPlace.MaxStartAnchors"/>, so the overflow scans from (0, 0)), some planned and not committed, gives the same
/// placements with anchors as without; a shape that found no fit is not scanned again in the same pass.
/// </summary>
public sealed class AutoPlaceStartAnchorTests
{
    private const uint Address4 = 4u;

    // 20 unrotated footprints 1..5 x 1..4; with PASS R's rotations, 25 distinct oriented shapes (> MaxStartAnchors = 16)
    private static readonly (byte W, byte H)[] _shapes =
        Enumerable.Range(1, 5).SelectMany(w => Enumerable.Range(1, 4).Select(h => ((byte)w, (byte)h))).ToArray();

    [Fact]
    public void StartAnchors_AMixedRotatedPassOfMoreShapesThanTheAnchorTable_GivesTheSamePlacementsAsUnanchoredScans_WithFewerProbes()
    {
        // Arrange + Act: two identical Intakes; the same 160-plan pass with and without start anchors
        (List<(ushort Reason, byte X, byte Y, bool Rotated)> anchored, long anchoredProbes) = RunPass(anchors: true);
        (List<(ushort Reason, byte X, byte Y, bool Rotated)> plain, long plainProbes) = RunPass(anchors: false);

        // Assert: identical results plan by plan; the pass both placed and refused, rotated some, and anchors saved probes
        Assert.Equal(plain, anchored);
        Assert.Contains(plain, p => p.Reason == 0);
        Assert.Contains(plain, p => p.Reason == (ushort)InvReason.NoRoom);
        Assert.Contains(plain, p => p.Reason == 0 && p.Rotated);
        Assert.Contains(plain, p => p.Reason == 0 && !p.Rotated);
        Assert.True(anchoredProbes < plainProbes, $"anchored {anchoredProbes} >= unanchored {plainProbes}");
    }

    [Fact]
    public void StartAnchors_AShapeThatFoundNoFit_IsNotScannedAgainInTheSamePass()
    {
        // Arrange: a full Intake but one cell; a 2 x 2 (square: PASS U only) cannot fit
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 319, 1_000);
        PlacementSubject square = Shape(rig, 2, 2);
        long first = 0;
        long second = 0;

        // Act: two plans of the same shape inside one anchored pass (no no-fit fact is recorded between them)
        Reply r = rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) =>
            {
                rig.Inventory.AutoPlace.BeginStartAnchors(rig.Intake);
                first = Probes(rig, () => PlanOnly(rig, in square));
                second = Probes(rig, () => PlanOnly(rig, in square));
                rig.Inventory.AutoPlace.EndStartAnchors();
            });

        // Assert: the first scan swept every anchor (19 x 15 x 4 cells); the second, from the exhausted anchor, probed nothing
        Assert.True(r.IsOk);
        Assert.Equal(19 * 15 * 4, first);
        Assert.Equal(0, second);
    }

    /// <summary>
    /// The Intake filled, then about 60% of its cells freed in a scattered pattern and rows 12-15 freed whole; then, in one fixture
    /// op, 160 plans of shapes cycling through <see cref="_shapes"/> (PassFirst, NoMerge), every third planned but not committed (20
    /// shapes, so each shape recurs both committed and not).
    /// </summary>
    private static (List<(ushort Reason, byte X, byte Y, bool Rotated)> Results, long Probes) RunPass(bool anchors)
    {
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        var freed = new List<ushort>();
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (y >= 12 || ((x * 7) + (y * 13)) % 5 < 3)
                {
                    freed.Add(rig.EntryAt(rig.Intake, x, y));
                }
            }
        }

        Assert.True(rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) =>
            {
                foreach (ushort e in freed)
                {
                    rig.Inventory.Mutator.Remove(ref p, e);
                }
            }).IsOk);

        const int Plans = 160;
        var entries = new ushort[Plans];
        for (int i = 0; i < Plans; i++)
        {
            (byte w, byte h) = _shapes[(i * 7) % _shapes.Length];
            entries[i] = rig.DetachedMail("letter", Address4, new[] { 50_000u + (uint)i });
            rig.Entries[entries[i]].W = w;
            rig.Entries[entries[i]].H = h;
        }

        var results = new List<(ushort, byte, byte, bool)>();
        long probes = rig.Counters[CounterId.InventoryPlacementProbes];
        Reply r = rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) =>
            {
                if (anchors)
                {
                    rig.Inventory.AutoPlace.BeginStartAnchors(rig.Intake);
                }

                for (int i = 0; i < Plans; i++)
                {
                    PlacementSubject s = Shape(rig, rig.Entries[entries[i]].W, rig.Entries[entries[i]].H);
                    var entryPlan = new InvPlan();
                    ushort reason = rig.Inventory.AutoPlace.Plan(
                        ref entryPlan, in s, 1, entries[i], new[] { rig.Intake }, MergePolicy.NoMerge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
                    results.Add((reason, ap.Placement.X, ap.Placement.Y, ap.Placement.Rotated));
                    if (reason == 0 && i % 3 != 2)
                    {
                        rig.Inventory.AutoPlace.Commit(ref p, in ap);
                    }
                }

                rig.Inventory.AutoPlace.EndStartAnchors();
            });
        Assert.True(r.IsOk);
        return (results, rig.Counters[CounterId.InventoryPlacementProbes] - probes);
    }

    /// <summary>A letter-keyed mail subject of footprint <paramref name="w"/> x <paramref name="h"/>.</summary>
    private static PlacementSubject Shape(InvAutoPlaceRig rig, byte w, byte h)
    {
        PlacementSubject letter = rig.Mail("letter", Address4);
        return new PlacementSubject(letter.Key, w, h, letter.Category, letter.CarryByHand, letter.WeightClass, letter.EffMax);
    }

    private static ushort PlanOnly(InvAutoPlaceRig rig, in PlacementSubject s)
    {
        var plan = new InvPlan();
        return rig.Inventory.AutoPlace.Plan(ref plan, in s, 1, AutoPlace.NoSource, new[] { rig.Intake }, MergePolicy.NoMerge, PlaceOrder.PassFirst, out _);
    }

    private static long Probes(InvAutoPlaceRig rig, Func<ushort> plan)
    {
        long before = rig.Counters[CounterId.InventoryPlacementProbes];
        Assert.Equal((ushort)InvReason.NoRoom, plan());
        return rig.Counters[CounterId.InventoryPlacementProbes] - before;
    }
}
