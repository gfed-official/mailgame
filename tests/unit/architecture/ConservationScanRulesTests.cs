using System.Diagnostics;
using System.Reflection;
using PerformativeMail.Sim.Inventory.Ledger;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Story grid-inventory/017 AC "<c>INVENTORY_ASSERT</c> full scan" (ADR-0009 section 6: "the full multiset scan runs behind
/// <c>[Conditional("INVENTORY_ASSERT")]</c>"; ADR-0001 section 4): the scan is a <see cref="ConditionalAttribute"/> method of
/// <see cref="InventoryConservationCheck"/>, and the compiled <c>Check</c> body calls it in an <c>INVENTORY_ASSERT</c> build (Debug)
/// and not in Release. Runs unskipped in all four legs: the expectation follows <see cref="TestBuildExpectation.SimAssertsExpected"/>
/// (the Sim defines <c>SIM_ASSERT</c> and <c>INVENTORY_ASSERT</c> together, <c>Directory.Build.targets</c>).
/// </summary>
public class ConservationScanRulesTests
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

    [Fact]
    public void Scan_IsConditionalOnInventoryAssert()
    {
        // Arrange
        MethodInfo scan = ScanMethod();

        // Act
        string[] conditions = scan.GetCustomAttributes<ConditionalAttribute>().Select(a => a.ConditionString).ToArray();

        // Assert
        Assert.Equal(new[] { "INVENTORY_ASSERT" }, conditions);
    }

    [Fact]
    public void CheckBody_ReferencesTheScan_InAnInventoryAssertBuildOnly()
    {
        // Arrange
        MethodInfo check = typeof(InventoryConservationCheck).GetMethod(nameof(InventoryConservationCheck.Check), Declared)!;
        MethodInfo scan = ScanMethod();

        // Act
        bool callsScan = ArchitectureScan.BodyReferences(check).Contains(scan);

        // Assert
        Assert.Equal(TestBuildExpectation.SimAssertsExpected, callsScan);
    }

    private static MethodInfo ScanMethod() => Assert.Single(typeof(InventoryConservationCheck).GetMethods(Declared), m => m.Name == "Scan");
}
