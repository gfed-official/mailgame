using System.Reflection;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Story grid-inventory/017 AC "Sole writer" (ADR-0009 section 6, "UnitLedger touched only by the lowest mutator primitives"; Mail
/// Lead planning default pending Evan, TD-160 item 1): no method body in the Sim assembly (nested and compiler-generated types
/// included) calls a <see cref="UnitLedger"/> writer except <see cref="InventoryMutator"/> and <see cref="UnitLedger"/> itself (its
/// <c>Recount</c> and corruption test seams). A writer is every <see cref="UnitLedger"/> method that is not one of its named readers,
/// so a method added later counts as a writer until it is listed as a reader. The engine is checked against fixtures in this test
/// assembly, never in the Sim.
/// </summary>
public class LedgerWriterRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    [Fact]
    public void SimAssembly_OnlyInventoryMutatorAndUnitLedgerWriteTheLedger()
    {
        // Act
        List<ArchViolation> violations = LedgerWriterRules.Check(_sim.GetTypes(), typeof(InventoryMutator), typeof(UnitLedger));

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SimAssembly_InventoryMutator_IsTheLedgerWriter()
    {
        // Act: with only UnitLedger exempt, the mutator's writes must be reported (the exemption is not vacuous)
        List<ArchViolation> violations = LedgerWriterRules.Check(_sim.GetTypes(), typeof(UnitLedger));

        // Assert
        Assert.Equal(
            new[] { "InventoryMutator: UnitLedger.AddConsumed", "InventoryMutator: UnitLedger.AddDestroyed", "InventoryMutator: UnitLedger.AddLive", "InventoryMutator: UnitLedger.AddSpawned", "InventoryMutator: UnitLedger.SetHolder" },
            violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnitLedger_Writers_AreExactlyTheCrossingsAndTheTestSeams()
    {
        // Act
        string[] writers = LedgerWriterRules.Writers(typeof(UnitLedger)).Select(m => m.Name).Distinct().Order(StringComparer.Ordinal).ToArray();

        // Assert: a new UnitLedger method is a writer until it is listed as a reader in LedgerWriterRules
        Assert.Equal(new[] { "AddConsumed", "AddDestroyed", "AddLive", "AddSpawned", "CorruptSpawnedForTests", "Recount", "SetHolder" }, writers);
    }

    [Fact]
    public void RuleEngine_FixtureReader_IsClean()
    {
        // Act
        List<ArchViolation> violations = LedgerWriterRules.Check(ArchitectureScan.WithNested(typeof(LedgerReaderFixture)).ToArray(), typeof(InventoryMutator), typeof(UnitLedger));

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_BadFixture_ReportsExactlyTheExpectedViolations()
    {
        // Arrange
        string[] expected =
        {
            "LedgerWriterBadFixture: UnitLedger.AddLive",
            "LedgerWriterBadFixture: UnitLedger.SetHolder",
            "LedgerWriterBadFixture: UnitLedger.Recount",
            "LedgerWriterBadFixture: UnitLedger.CorruptSpawnedForTests",
        };
        Type[] universe = ArchitectureScan.WithNested(typeof(LedgerWriterBadFixture)).ToArray();

        // Act
        List<ArchViolation> violations = LedgerWriterRules.Check(universe, typeof(InventoryMutator), typeof(UnitLedger));

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));
    }
}

/// <summary>The story 017 sole-writer engine.</summary>
internal static class LedgerWriterRules
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // The UnitLedger members that only read; every other method of it is a writer.
    private static readonly string[] _readers =
    {
        "get_Rows", "RowOf", "Live", "LiveSum", "Consumed", "Destroyed", "Spawned", "Granted", "Expected", "HolderOf",
    };

    /// <summary>The methods of <paramref name="ledger"/> that write it: every declared method that is not a named reader (constructors excluded).</summary>
    public static IEnumerable<MethodInfo> Writers(Type ledger) =>
        ledger.GetMethods(Declared).Where(m => Array.IndexOf(_readers, m.Name) < 0);

    /// <summary>Every body in <paramref name="types"/> outside the <paramref name="allowed"/> owners (nested types included) that calls a ledger writer.</summary>
    public static List<ArchViolation> Check(IReadOnlyList<Type> types, params Type[] allowed)
    {
        var writers = new HashSet<MethodInfo>(Writers(typeof(UnitLedger)));
        var violations = new List<ArchViolation>();
        foreach (Type t in types)
        {
            bool exempt = false;
            for (Type? c = t; c is not null; c = c.DeclaringType)
            {
                exempt |= Array.IndexOf(allowed, c) >= 0;
            }

            if (exempt)
            {
                continue;
            }

            string owner = ArchitectureScan.NameOf(ArchitectureScan.OwnerOf(t));
            foreach (MethodBase m in ArchitectureScan.MethodsWithBodies(t))
            {
                foreach (MemberInfo used in ArchitectureScan.BodyReferences(m))
                {
                    if (used is MethodInfo mi && writers.Contains(mi))
                    {
                        violations.Add(new ArchViolation(owner, "UnitLedger." + mi.Name, $"body of {ArchitectureScan.NameOf(t)}.{m.Name}"));
                    }
                }
            }
        }

        return violations;
    }
}

/// <summary>Seen-passing fixture: reads every counter of the ledger and constructs one, writing nothing.</summary>
internal sealed class LedgerReaderFixture
{
    private readonly UnitLedger _ledger = new(1, 4);

    public long Read() => _ledger.Live(0, LedgerState.Placed) + _ledger.LiveSum(0) + _ledger.Consumed(0) + _ledger.Consumed(0, ConsumeReason.Delivered)
        + _ledger.Destroyed(0) + _ledger.Spawned(0) + _ledger.Granted(0) + _ledger.Expected(0) + _ledger.HolderOf(0) + _ledger.Rows;
}

/// <summary>Seen-failing fixture: a system-like type that writes the ledger directly (one write only inside a lambda) and calls its test seams.</summary>
internal sealed class LedgerWriterBadFixture
{
    private readonly UnitLedger _ledger = new(1, 4);

    public void Credit() => _ledger.AddLive(0, LedgerState.World, 1);

    public Action Later() => () => _ledger.SetHolder(0, UnitLedger.NoHolder);

    public void Seams(InventorySystem inventory)
    {
        _ledger.Recount(inventory);
        _ledger.CorruptSpawnedForTests(0, 1);
    }
}
