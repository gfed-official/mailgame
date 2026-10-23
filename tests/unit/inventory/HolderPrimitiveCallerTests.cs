using System.Reflection;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.World;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/017 (Mail Review r1 A2; the ADR-0009 §6 l.170 erratum of 2026-10-22, approved by Evan 6:41 PM PT; TD-160 item 1): the ledger's holder and custody primitives on
/// <see cref="InventoryMutator"/> (both <c>EnterHolder</c> and both <c>LeaveHolder</c> overloads, and <c>CountSpawned</c>) are called
/// in the Sim assembly only by <see cref="InventorySystem"/>, <see cref="WorldItems"/>, <see cref="PickupCore"/> and the mutator
/// itself (their nested and compiler-generated types included). The bodies are read from IL with the same
/// <see cref="ArchitectureScan"/> engine as the sole-writer architecture test (<c>LedgerWriterRulesTests</c>).
/// </summary>
public sealed class HolderPrimitiveCallerTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    private static readonly string[] _primitives = { "EnterHolder", "LeaveHolder", "CountSpawned" };

    [Fact]
    public void SimAssembly_OnlyTheAllowedTypesCallTheHolderPrimitives()
    {
        // Arrange: the ADR-0009 §6 l.170 erratum names PickupCore as a caller through WorldItems; it makes no direct call today, so its
        // entry also admits a future direct call, which the erratum's caller list (PickupCore through WorldItems) tolerates rather than
        // names (advisory fix, Mail Review PR #36 A9; wording, Mail Review PR #38 r1 advisory 7)
        Type[] allowed = { typeof(InventoryMutator), typeof(InventorySystem), typeof(WorldItems), typeof(PickupCore) };

        // Act
        List<ArchViolation> violations = Check(_sim.GetTypes(), allowed);

        // Assert
        Assert.True(violations.Count == 0, "InventoryMutator holder primitives called outside the allow-list (InventoryMutator, InventorySystem, WorldItems, PickupCore):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SimAssembly_WithOnlyTheMutatorAllowed_ReportsInventorySystemAndWorldItems()
    {
        // Act: the allow-list is not vacuous, the scan sees the known production callers
        string[] owners = Check(_sim.GetTypes(), typeof(InventoryMutator)).Select(v => v.Owner).Distinct().ToArray();

        // Assert
        Assert.Contains(nameof(InventorySystem), owners);
        Assert.Contains(nameof(WorldItems), owners);
    }

    [Fact]
    public void RuleEngine_BadFixture_NamesTheOffendingCallerOfEachPrimitive()
    {
        // Arrange
        Type[] universe = ArchitectureScan.WithNested(typeof(HolderPrimitiveBadFixture)).ToArray();

        // Act
        List<ArchViolation> violations = Check(universe, typeof(InventoryMutator), typeof(InventorySystem), typeof(WorldItems), typeof(PickupCore));

        // Assert: the lambda's call is owned by the fixture, not its compiler-generated closure
        Assert.Equal(
            new[] { "HolderPrimitiveBadFixture: InventoryMutator.CountSpawned", "HolderPrimitiveBadFixture: InventoryMutator.EnterHolder", "HolderPrimitiveBadFixture: InventoryMutator.LeaveHolder" },
            violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(4, violations.Count);
    }

    [Fact]
    public void Primitives_AreTheFiveInternalMethods()
    {
        // Act
        MethodInfo[] methods = Primitives().ToArray();

        // Assert
        Assert.Equal(5, methods.Length);
        Assert.All(methods, m => Assert.True(m.IsAssembly, $"InventoryMutator.{m.Name} is not internal"));
    }

    private static IEnumerable<MethodInfo> Primitives() =>
        typeof(InventoryMutator).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => Array.IndexOf(_primitives, m.Name) >= 0);

    /// <summary>Every body in <paramref name="types"/> outside the <paramref name="allowed"/> owners (nested types included) that calls a primitive.</summary>
    private static List<ArchViolation> Check(IReadOnlyList<Type> types, params Type[] allowed)
    {
        var primitives = new HashSet<MethodInfo>(Primitives());
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
                    if (used is MethodInfo mi && primitives.Contains(mi))
                    {
                        violations.Add(new ArchViolation(owner, "InventoryMutator." + mi.Name, $"body of {ArchitectureScan.NameOf(t)}.{m.Name}"));
                    }
                }
            }
        }

        return violations;
    }
}

/// <summary>Seen-failing fixture: a type outside the allow-list that calls each primitive (both LeaveHolder overloads, one inside a lambda).</summary>
internal sealed class HolderPrimitiveBadFixture
{
    public void Cross(InventoryMutator mutator, in UnitRef unit)
    {
        mutator.EnterHolder(LedgerState.Flight, 0);
        mutator.LeaveHolder(LedgerState.Belt, in unit);
        mutator.CountSpawned(0);
    }

    public Action Later(InventoryMutator mutator) => () => mutator.LeaveHolder(0, 1);
}
