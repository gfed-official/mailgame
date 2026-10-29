using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// The constructor reflection test of ADR-0003 section 4 and ADR-0001 section 8 (Story 025 AC-3, TR-sim-013): every type marked
/// <see cref="SimSystemAttribute"/> has exactly one public constructor, and every parameter type is on the closed injectable list
/// for that system; every <c>I…Api</c> parameter is a declared dependency in <see cref="SystemGraph"/>. The rule table lives here
/// (<see cref="CompositionRules"/>); deliberately bad systems live in this test assembly, never in the Sim, and must be reported.
/// </summary>
[Trait("Id", "SC-R4-1")]
[Trait("Id", "SC-ARCH-1")]
public class CompositionRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    // ---------------------------------------------------------------- the Sim itself

    [Fact]
    public void SimAssembly_EverySimSystemType_PassesTheCompositionRules()
    {
        // Arrange
        Type[] universe = _sim.GetTypes();

        // Act
        List<string> violations = CompositionRules.Check(universe, SystemGraph.Shipped);

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SimAssembly_EveryConcreteISimSystem_IsMarkedSimSystem()
    {
        // Arrange
        Type[] systems = _sim.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ISimSystem).IsAssignableFrom(t))
            .ToArray();

        // Act
        string[] unmarked = systems
            .Where(t => t.GetCustomAttribute<SimSystemAttribute>() is null)
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        // Assert: an unmarked system would escape the constructor rules.
        Assert.Empty(unmarked);
    }

    [Fact]
    public void CompositionNamespace_HasNoStaticFields()
    {
        // Arrange: ADR-0003 section 4 "no statics" for the composition root (the full SC-ARCH-1 rule is Story 028).
        Type[] types = _sim.GetTypes()
            .Where(t => t.Namespace == "PerformativeMail.Sim.Composition" && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .ToArray();

        // Act
        string[] statics = types
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral && f.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Select(f => f.DeclaringType!.Name + "." + f.Name)
            .ToArray();

        // Assert
        Assert.NotEmpty(types);
        Assert.Empty(statics);
    }

    // ---------------------------------------------------------------- the rule engine on test fixtures

    [Fact]
    public void RuleEngine_GoodFixtures_UsingEveryInjectable_AreClean()
    {
        // Arrange
        Type[] universe =
        {
            typeof(GoodGrid), typeof(GoodMail), typeof(GoodRun), typeof(GoodPerks), typeof(GoodWorldGen), typeof(NotASystem),
        };

        // Act
        List<string> violations = CompositionRules.Check(universe, SystemGraph.Shipped);

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private const string NotOnList = " is not on the ADR-0003 section 4 injectable list.";

    private static string NotFor(string type, string row, SystemId id) => $"{type} is on the list as \"{row}\" but {id} may not receive it.";

    // Each bad fixture, the other types in its universe (clean ones only) and the exact reason of its single violation.
    // "on the list but not for this system" and "not on the list" are distinct reasons.
    public static TheoryData<Type, Type[], string> BadFixtures => new()
    {
        { typeof(BadTakesSimWorld), Array.Empty<Type>(), "BadTakesSimWorld(SimWorld world): SimWorld" + NotOnList },
        { typeof(BadThrowingTakesRunControl), Array.Empty<Type>(), "BadThrowingTakesRunControl(IRunControl stop): " + NotFor("IRunControl", "IRunControl (Run Structure only)", SystemId.Throwing) },
        { typeof(BadGridTakesMailIds), Array.Empty<Type>(), "BadGridTakesMailIds(MailIds mailIds): " + NotFor("MailIds", "MailIds (Mail only)", SystemId.GridInventory) },
        { typeof(BadThrowingTakesWalletGate), Array.Empty<Type>(), "BadThrowingTakesWalletGate(IWalletGate wallet): " + NotFor("IWalletGate", "IWalletGate (Mail only, ADR-0017 section 8)", SystemId.Throwing) },
        { typeof(BadThrowingTakesSampleLatch), Array.Empty<Type>(), "BadThrowingTakesSampleLatch(ISampleLatch samples): " + NotFor("ISampleLatch", "ISampleLatch (Player Character only)", SystemId.Throwing) },
        { typeof(BadThrowingTakesRng), Array.Empty<Type>(), "BadThrowingTakesRng(IRngStreams streams): " + NotFor("IRngStreams", "own IRngStreams view (stream owners only)", SystemId.Throwing) },
        { typeof(BadTakesRngRegistry), Array.Empty<Type>(), "BadTakesRngRegistry(RngRegistry registry): RngRegistry" + NotOnList },
        { typeof(BadTakesSlotTable), Array.Empty<Type>(), "BadTakesSlotTable(SlotTable slots): SlotTable" + NotOnList },
        { typeof(BadTakesBuilder), Array.Empty<Type>(), "BadTakesBuilder(SimWorldBuilder builder): SimWorldBuilder" + NotOnList },
        { typeof(BadTakesContext), Array.Empty<Type>(), "BadTakesContext(SimSystemContext context): SimSystemContext" + NotOnList },
        { typeof(BadTakesWholeSettings), Array.Empty<Type>(), "BadTakesWholeSettings(SimSettings settings): SimSettings" + NotOnList },
        { typeof(BadTakesCoreSettings), Array.Empty<Type>(), "BadTakesCoreSettings(SimCoreSettings settings): " + NotFor("SimCoreSettings", "own settings record", SystemId.GridInventory) },
        { typeof(BadGridTakesStatWriter), Array.Empty<Type>(), "BadGridTakesStatWriter(IStatWriter writer): " + NotFor("IStatWriter", "IStatWriter (Perks, Stamps only)", SystemId.GridInventory) },
        { typeof(BadTwoPublicConstructors), Array.Empty<Type>(), "BadTwoPublicConstructors: has 2 public constructors; a system needs exactly one public constructor." },
        { typeof(BadNoPublicConstructor), Array.Empty<Type>(), "BadNoPublicConstructor: has 0 public constructors; a system needs exactly one public constructor." },
        { typeof(BadNoneId), Array.Empty<Type>(), "BadNoneId: [SimSystem(None)] is not a defined system." },
        { typeof(BadGridTakesUndeclaredApi), new[] { typeof(MailApiProvider) }, "BadGridTakesUndeclaredApi(IFixtureMailApi mail): MailDestinations is not a declared dependency of GridInventory in SystemGraph." },
        { typeof(BadApiWithoutProvider), Array.Empty<Type>(), "BadApiWithoutProvider(IFixtureOrphanApi orphan): IFixtureOrphanApi has no [SimSystem] provider." },
        { typeof(BadSecondGrid), new[] { typeof(GoodGrid) }, "SystemId GridInventory has two [SimSystem] types: GoodGrid, BadSecondGrid." },
        { typeof(BadSelfApi), Array.Empty<Type>(), "BadSelfApi(IFixtureGridApi self): IFixtureGridApi is its own API." },
        { typeof(BadTakesInt), Array.Empty<Type>(), "BadTakesInt(Int32 count): Int32" + NotOnList },
    };

    [Theory]
    [MemberData(nameof(BadFixtures))]
    public void RuleEngine_BadFixture_IsReportedOnceWithItsReason(Type bad, Type[] others, string expectedViolation)
    {
        // Arrange
        Type[] universe = others.Append(bad).ToArray();

        // Act
        List<string> violations = CompositionRules.Check(universe, SystemGraph.Shipped);

        // Assert
        string only = Assert.Single(violations);
        Assert.Equal(expectedViolation, only);
    }

    [Fact]
    public void RuleEngine_EachCleanUniverseUsedByTheBadFixtures_HasNoViolationOfItsOwn()
    {
        // Arrange: the "others" sets above must not contribute violations, or the single-violation assertion would be vacuous.
        Type[][] universes = { new[] { typeof(MailApiProvider) }, new[] { typeof(GoodGrid) } };

        // Act + Assert
        foreach (Type[] universe in universes)
        {
            Assert.Empty(CompositionRules.Check(universe, SystemGraph.Shipped));
        }
    }

    [Fact]
    public void RuleEngine_ApiWithTwoProviders_IsReported()
    {
        // Arrange
        Type[] universe = { typeof(GoodGrid), typeof(SecondGridApiProvider), typeof(GoodMail) };

        // Act
        List<string> violations = CompositionRules.Check(universe, SystemGraph.Shipped);

        // Assert
        Assert.Contains(violations, v => v.Contains("2 providers", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- fixtures (test assembly only)

    public interface IFixtureGridApi
    {
    }

    public interface IFixtureMailApi
    {
    }

    public interface IFixtureOrphanApi
    {
    }

    public interface IFixtureWorldApi
    {
    }

    /// <summary>Stands in for the Content &amp; Data stat sheet until it exists (matched by name).</summary>
    public interface IStatSheet
    {
    }

    /// <summary>Stands in for the Content &amp; Data stat writer until it exists (matched by name).</summary>
    public interface IStatWriter
    {
    }

    public abstract class FixtureSystem : ISimSystem
    {
        public void Bind(SimWorldBuilder builder)
        {
        }
    }

    [SimSystem(SystemId.WorldGeneration)]
    public sealed class GoodWorldGen : FixtureSystem, IFixtureWorldApi
    {
        public GoodWorldGen(EventLogFacade events, EntityIds ids, IRngStreams ownStreams, ContentSet content)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class GoodGrid : FixtureSystem, IFixtureGridApi
    {
        public GoodGrid(
            EventLogFacade events,
            EntityIds ids,
            ISlotView slots,
            ITickReader tick,
            ContentSet content,
            InventoryBalance inventory,
            DefTable<ItemDef> items,
            SimCheckRing ring,
            SimCounters counters,
            IStatSheet stats)
        {
        }
    }

    [SimSystem(SystemId.MailDestinations)]
    public sealed class GoodMail : FixtureSystem, IFixtureMailApi
    {
        public GoodMail(EventLogFacade events, MailIds mailIds, IRngStreams ownStreams, IFixtureGridApi grid, IFixtureWorldApi world, MailBalance mail, DefTable<MailKindDef> kinds, IWalletGate wallet)
        {
        }
    }

    [SimSystem(SystemId.RunStructure)]
    public sealed class GoodRun : FixtureSystem
    {
        public GoodRun(IRunControl stop, IFixtureMailApi mail, ITickReader tick)
        {
        }
    }

    [SimSystem(SystemId.Perks)]
    public sealed class GoodPerks : FixtureSystem
    {
        public GoodPerks(IStatWriter writer, IStatSheet sheet, IRngStreams ownStreams)
        {
        }
    }

    /// <summary>Not marked: ignored by the rules (it may take anything).</summary>
    public sealed class NotASystem
    {
        public NotASystem(SimWorld world)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadTakesSimWorld : FixtureSystem
    {
        public BadTakesSimWorld(SimWorld world)
        {
        }
    }

    [SimSystem(SystemId.Throwing)]
    public sealed class BadThrowingTakesRunControl : FixtureSystem
    {
        public BadThrowingTakesRunControl(IRunControl stop)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadGridTakesMailIds : FixtureSystem
    {
        public BadGridTakesMailIds(MailIds mailIds)
        {
        }
    }

    [SimSystem(SystemId.Throwing)]
    public sealed class BadThrowingTakesRng : FixtureSystem
    {
        public BadThrowingTakesRng(IRngStreams streams)
        {
        }
    }

    [SimSystem(SystemId.MailDestinations)]
    public sealed class BadTakesRngRegistry : FixtureSystem
    {
        public BadTakesRngRegistry(RngRegistry registry)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadTakesSlotTable : FixtureSystem
    {
        public BadTakesSlotTable(SlotTable slots)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadTakesBuilder : FixtureSystem
    {
        public BadTakesBuilder(SimWorldBuilder builder)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    internal sealed class BadTakesContext : FixtureSystem
    {
        public BadTakesContext(SimSystemContext context)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadTakesWholeSettings : FixtureSystem
    {
        public BadTakesWholeSettings(SimSettings settings)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadTakesCoreSettings : FixtureSystem
    {
        public BadTakesCoreSettings(SimCoreSettings settings)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadGridTakesStatWriter : FixtureSystem
    {
        public BadGridTakesStatWriter(IStatWriter writer)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadTwoPublicConstructors : FixtureSystem
    {
        public BadTwoPublicConstructors()
        {
        }

        public BadTwoPublicConstructors(ITickReader tick)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadNoPublicConstructor : FixtureSystem
    {
        private BadNoPublicConstructor()
        {
        }
    }

    [SimSystem(SystemId.None)]
    public sealed class BadNoneId : FixtureSystem
    {
        public BadNoneId()
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadGridTakesUndeclaredApi : FixtureSystem
    {
        public BadGridTakesUndeclaredApi(IFixtureMailApi mail)
        {
        }
    }

    [SimSystem(SystemId.MailDestinations)]
    public sealed class BadApiWithoutProvider : FixtureSystem
    {
        public BadApiWithoutProvider(IFixtureOrphanApi orphan)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadSecondGrid : FixtureSystem
    {
        public BadSecondGrid(ITickReader tick)
        {
        }
    }

    [SimSystem(SystemId.MailDestinations)]
    public sealed class MailApiProvider : FixtureSystem, IFixtureMailApi
    {
        public MailApiProvider(ITickReader tick)
        {
        }
    }

    [SimSystem(SystemId.HealthDamage)]
    public sealed class SecondGridApiProvider : FixtureSystem, IFixtureGridApi
    {
        public SecondGridApiProvider(ITickReader tick)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadSelfApi : FixtureSystem, IFixtureGridApi
    {
        public BadSelfApi(IFixtureGridApi self)
        {
        }
    }

    [SimSystem(SystemId.Throwing)]
    public sealed class BadThrowingTakesWalletGate : FixtureSystem
    {
        public BadThrowingTakesWalletGate(IWalletGate wallet)
        {
        }
    }

    [SimSystem(SystemId.Throwing)]
    public sealed class BadThrowingTakesSampleLatch : FixtureSystem
    {
        public BadThrowingTakesSampleLatch(ISampleLatch samples)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class BadTakesInt : FixtureSystem
    {
        public BadTakesInt(int count)
        {
        }
    }
}

/// <summary>
/// The rule table of the closed injectable list (ADR-0003 section 4) and the engine that applies it to <c>[SimSystem]</c> types.
/// Each row: which parameter types it covers, and which systems may receive them. A parameter that matches no row is a violation.
/// </summary>
internal static class CompositionRules
{
    private sealed record Rule(string Name, Func<Type, bool> Matches, Func<SystemId, bool> MayReceive);

    private static bool Any(SystemId id) => true;

    private static bool Is<T>(Type t) => t == typeof(T);

    /// <summary>The table, in ADR-0003 section 4 order (the <c>I…Api</c> row is handled by <see cref="CheckApi"/>).</summary>
    private static List<Rule> Table()
    {
        HashSet<SystemId> streamOwners = new();
        for (int i = 0; i < RngNames.Count; i++)
        {
            streamOwners.Add(RngNames.ByOrdinal(i).Owner);
        }

        // ContentSet sub-tables: the types of its public properties, except the settings (which have their own row).
        HashSet<Type> subTables = typeof(ContentSet)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType != typeof(SimSettings))
            .Select(p => p.PropertyType)
            .ToHashSet();

        // SimSettings sub-records and their owning systems. Today none belongs to a composed system: SimCoreSettings is Sim
        // Core's own and StatsSettings is Content & Data's. A gameplay epic adds its record and owner here.
        Dictionary<Type, SystemId[]> settingsOwners = typeof(SimSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Where(t => t.IsClass && t != typeof(string))
            .ToDictionary(t => t, _ => Array.Empty<SystemId>());

        return new List<Rule>
        {
            new("EventLog writer facade", Is<EventLogFacade>, Any),
            new("EntityIds", Is<EntityIds>, Any),
            new("MailIds (Mail only)", Is<MailIds>, id => id == SystemId.MailDestinations),
            new("IWalletGate (Mail only, ADR-0017 section 8)", Is<IWalletGate>, id => id == SystemId.MailDestinations),
            new("ISlotView (read-only slot table)", Is<ISlotView>, Any),
            new("ISampleLatch (Player Character only)", Is<ISampleLatch>, id => id == SystemId.PlayerCharacter),   // Evan approved 2026-10-29 (ADR-0003 §4 erratum)
            new("own IRngStreams view (stream owners only)", Is<IRngStreams>, streamOwners.Contains),
            new("ITickReader", Is<ITickReader>, Any),
            new("IWorldQuery (read-only world, ADR-0026)", Is<IWorldQuery>, Any),
            new("IRunControl (Run Structure only)", Is<IRunControl>, id => id == SystemId.RunStructure),
            new("ContentSet", Is<ContentSet>, Any),
            new("ContentSet sub-table", subTables.Contains, Any),
            new("IStatSheet", t => t.IsInterface && t.Name == "IStatSheet", Any),
            new("IStatWriter (Perks, Stamps only)", t => t.IsInterface && t.Name == "IStatWriter", id => id == SystemId.Perks || id == SystemId.StampsRunEvents),
            new("SimCheckRing", Is<SimCheckRing>, Any),
            new("SimCounters", Is<SimCounters>, Any),
            new("own settings record", settingsOwners.ContainsKey, _ => false),
        };
    }

    /// <summary>Checks every <c>[SimSystem]</c> type in <paramref name="universe"/>; API providers are looked up in the same universe.</summary>
    public static List<string> Check(IReadOnlyList<Type> universe, SystemGraph graph)
    {
        var violations = new List<string>();
        List<Rule> table = Table();
        List<(Type Type, SystemId Id)> systems = universe
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<SimSystemAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => (x.Type, x.Attr!.Id))
            .ToList();

        foreach (IGrouping<SystemId, (Type Type, SystemId Id)> dup in systems.GroupBy(s => s.Id).Where(g => g.Count() > 1))
        {
            violations.Add($"SystemId {dup.Key} has two [SimSystem] types: {string.Join(", ", dup.Select(s => s.Type.Name))}.");
        }

        foreach ((Type type, SystemId id) in systems)
        {
            if (id == SystemId.None || !Enum.IsDefined(id))
            {
                violations.Add($"{type.Name}: [SimSystem({id})] is not a defined system.");
            }

            ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length != 1)
            {
                violations.Add($"{type.Name}: has {ctors.Length} public constructors; a system needs exactly one public constructor.");
                continue;
            }

            foreach (ParameterInfo p in ctors[0].GetParameters())
            {
                string? problem = CheckParameter(p.ParameterType, id, table, systems, graph);
                if (problem is not null)
                {
                    violations.Add($"{type.Name}({p.ParameterType.Name} {p.Name}): {problem}");
                }
            }
        }

        return violations;
    }

    private static string? CheckParameter(Type t, SystemId id, List<Rule> table, List<(Type Type, SystemId Id)> systems, SystemGraph graph)
    {
        foreach (Rule rule in table)
        {
            if (rule.Matches(t))
            {
                return rule.MayReceive(id) ? null : $"{t.Name} is on the list as \"{rule.Name}\" but {id} may not receive it.";
            }
        }

        if (t.IsInterface && t.Name.StartsWith('I') && t.Name.EndsWith("Api", StringComparison.Ordinal))
        {
            return CheckApi(t, id, systems, graph);
        }

        return $"{t.Name} is not on the ADR-0003 section 4 injectable list.";
    }

    private static string? CheckApi(Type api, SystemId id, List<(Type Type, SystemId Id)> systems, SystemGraph graph)
    {
        List<SystemId> providers = systems.Where(s => api.IsAssignableFrom(s.Type)).Select(s => s.Id).Distinct().ToList();
        if (providers.Count == 0)
        {
            return $"{api.Name} has no [SimSystem] provider.";
        }

        if (providers.Count > 1)
        {
            return $"{api.Name} has {providers.Count} providers ({string.Join(", ", providers)}).";
        }

        if (providers[0] == id)
        {
            return $"{api.Name} is its own API.";
        }

        return graph.Declares(id, providers[0]) ? null : $"{providers[0]} is not a declared dependency of {id} in SystemGraph.";
    }
}
