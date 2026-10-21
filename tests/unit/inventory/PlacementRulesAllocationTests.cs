using PerformativeMail.Sim.Inventory.Rules;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/006 (ADR-0009 Performance Implications; ADR-0006 0 B per tick): <see cref="PlacementRules"/>,
/// <see cref="StackKey"/>, <see cref="PlacementSubject"/> and <see cref="InsertionWhitelist"/> allocate nothing. The fixture is built
/// before the measured window. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class PlacementRulesAllocationTests
{
    [Fact]
    [Trait("Id", "TR-inventory-004")]
    public void ValidateStackKeyAndWhitelist_Over10000Calls_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var f = new PlacementFixture();
        ushort intake = f.CreateContainer("po_intake");
        f.AddItem(f.Chest, "log", 2, 0, count: 3);
        var defIntake = f.ContainerDef("po_intake");
        int okCount = 0;

        // Act
        AllocAssert.Zero("PlacementRules x10000", () =>
        {
            int local = 0;
            for (int i = 0; i < 10_000; i++)
            {
                PlacementSubject letter = PlacementSubject.Of(f.Content, f.Sheet, in f.Entries[f.Letters]);
                PlacementSubject plank = f.Item("plank");
                local += f.Validate(f.Chest, in letter, 0, 0).IsOk ? 1 : 0;            // merge
                local += f.Validate(f.Chest, in letter, 4, 1).IsOk ? 1 : 0;            // free
                local += f.Validate(f.Chest, f.Mail("large", 5u), 5, 1).IsOk ? 1 : 0;  // out_of_bounds
                local += f.Validate(f.Chest, f.Item("log"), 2, 1).IsOk ? 1 : 0;       // partial overlap
                local += f.Validate(intake, in plank, 0, 0).IsOk ? 1 : 0;              // not_allowed
                local += InsertionWhitelist.Check(defIntake, in letter) == 0 ? 1 : 0;
                local += StackKey.Of(in f.Entries[f.Letters]) == letter.Key ? 1 : 0;
            }

            okCount = local;
        });

        // Assert
        Assert.Equal(40_000, okCount);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(PlacementRules).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }
}
