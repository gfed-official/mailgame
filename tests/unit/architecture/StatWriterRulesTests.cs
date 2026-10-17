using System.Reflection;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Content-data story 015 architecture assertions (TR-content-016, ADR-0010 section 7, ADR-0003 section 4, CDD-STAT-4 M-1a
/// form): <see cref="IStatWriter"/> appears in no <c>[SimSystem]</c> constructor other than Perks and Stamps &amp; Run Events
/// (at M-1a: none), nothing but <see cref="StatSheetWriter"/> implements it, <see cref="StatSheet.Build"/> takes no profile,
/// rank or meta input, and the only <see cref="SourceKind"/> members are Perk, Stamp and RunEvent. The rule engine is checked
/// against deliberately bad fixtures that live in this test assembly, never in the Sim.
/// </summary>
[Trait("Id", "CDD-STAT-4")]
public class StatWriterRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    // ---------------------------------------------------------------- fixtures

    public abstract class FixtureSystem : ISimSystem
    {
        public void Bind(SimWorldBuilder builder)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class ReaderTakesSheet : FixtureSystem
    {
        public ReaderTakesSheet(IStatSheet sheet)
        {
        }
    }

    [SimSystem(SystemId.Perks)]
    public sealed class PerksTakesWriter : FixtureSystem
    {
        public PerksTakesWriter(IStatWriter writer, IStatSheet sheet)
        {
        }
    }

    [SimSystem(SystemId.StampsRunEvents)]
    public sealed class StampsTakesWriter : FixtureSystem
    {
        public StampsTakesWriter(IStatWriter writer)
        {
        }
    }

    [SimSystem(SystemId.GridInventory)]
    public sealed class GridTakesWriter : FixtureSystem
    {
        public GridTakesWriter(IStatWriter writer)
        {
        }
    }

    [SimSystem(SystemId.Economy)]
    public sealed class EconomyTakesConcreteWriter : FixtureSystem
    {
        public EconomyTakesConcreteWriter(StatSheetWriter writer)
        {
        }
    }

    // Not a [SimSystem]: a "meta-progression" helper that tries to get a writer.
    public sealed class MetaProgressionHelper
    {
        public MetaProgressionHelper(IStatWriter writer)
        {
        }
    }

    // ---------------------------------------------------------------- the rule under test

    /// <summary>Every constructor (any visibility) of <paramref name="universe"/> that takes the writer, unless it is Perks or Stamps &amp; Run Events.</summary>
    private static List<string> WriterInjectionViolations(IEnumerable<Type> universe)
    {
        var violations = new List<string>();
        foreach (Type t in universe)
        {
            ConstructorInfo[] ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            bool takesWriter = ctors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IStatWriter) || p.ParameterType == typeof(StatSheetWriter)));
            if (!takesWriter)
            {
                continue;
            }

            SystemId? id = t.GetCustomAttribute<SimSystemAttribute>()?.Id;
            if (id is null && t.Namespace == typeof(SimComposition).Namespace && t.Name == "SimCoreServices")
            {
                continue; // the composition root's own service record holds the per-world writer it creates
            }

            if (id != SystemId.Perks && id != SystemId.StampsRunEvents)
            {
                violations.Add($"{t.Name} takes IStatWriter but is {(id is null ? "not a [SimSystem]" : id.ToString())}.");
            }
        }

        return violations;
    }

    // ---------------------------------------------------------------- IStatWriter reaches only Perks and Stamps

    [Fact]
    public void SimAssembly_NoTypeTakesTheWriter_AtM1a_ExceptPerksAndStamps()
    {
        // Arrange / Act
        List<string> violations = WriterInjectionViolations(_sim.GetTypes());
        string[] writerTakers = _sim.GetTypes()
            .Where(t => t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IStatWriter))))
            .Select(t => t.Name)
            .ToArray();

        // Assert: at M-1a there is no Perks or Stamps system, so nothing takes the writer
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.Empty(writerTakers);
    }

    [Fact]
    public void WriterRule_FlagsEverySystemButPerksAndStamps_AndNonSystems()
    {
        // Arrange
        Type[] universe =
        [
            typeof(ReaderTakesSheet), typeof(PerksTakesWriter), typeof(StampsTakesWriter), typeof(GridTakesWriter),
            typeof(EconomyTakesConcreteWriter), typeof(MetaProgressionHelper),
        ];

        // Act
        List<string> violations = WriterInjectionViolations(universe);

        // Assert
        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.StartsWith("GridTakesWriter", StringComparison.Ordinal) && v.Contains("GridInventory", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith("EconomyTakesConcreteWriter", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith("MetaProgressionHelper", StringComparison.Ordinal) && v.Contains("not a [SimSystem]", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectableListRules_AllowTheSheetAnywhere_AndTheWriterOnlyToPerksAndStamps()
    {
        // Arrange: the real interfaces, through the composition rule engine of ADR-0003 section 4
        Type[] good = [typeof(ReaderTakesSheet), typeof(PerksTakesWriter), typeof(StampsTakesWriter)];
        Type[] bad = [typeof(GridTakesWriter)];

        // Act
        List<string> goodViolations = CompositionRules.Check(good, SystemGraph.Shipped);
        List<string> badViolations = CompositionRules.Check(bad, SystemGraph.Shipped);

        // Assert
        Assert.True(goodViolations.Count == 0, string.Join(Environment.NewLine, goodViolations));
        string violation = Assert.Single(badViolations);
        Assert.Contains("IStatWriter", violation, StringComparison.Ordinal);
        Assert.Contains("GridInventory", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheStatSheetWriter_ImplementsTheWriter_AndTheFaceIsPushAndRemoveOnly()
    {
        // Arrange / Act
        string[] implementers = _sim.GetTypes().Where(t => t.IsClass && typeof(IStatWriter).IsAssignableFrom(t)).Select(t => t.Name).ToArray();
        string[] members = typeof(IStatWriter).GetMembers().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // Assert: the sheet read face has no writing member and the writer face has no read member
        Assert.Equal(new[] { nameof(StatSheetWriter) }, implementers);
        Assert.Equal(new[] { "Push", "Remove" }, members);
        Assert.False(typeof(IStatSheet).IsAssignableFrom(typeof(IStatWriter)));
        Assert.False(typeof(IStatWriter).IsAssignableFrom(typeof(IStatSheet)));
        Assert.False(typeof(IStatWriter).IsAssignableFrom(_sim.GetType("PerformativeMail.Sim.Content.Stats.LiveStatSheet")!));
    }

    [Fact]
    public void TheWriterAccessorOnTheCompositionContext_IsTheOnlyPublicPathToTheWriter()
    {
        // Arrange / Act: every public or internal member of the Sim, outside StatSheetWriter itself, that exposes the writer
        var exposures = new List<string>();
        foreach (Type t in _sim.GetTypes().Where(t => t != typeof(StatSheetWriter)))
        {
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (p.PropertyType == typeof(IStatWriter) || p.PropertyType == typeof(StatSheetWriter))
                {
                    exposures.Add(t.Name + "." + p.Name);
                }
            }
        }

        // Assert: the context hands IStatWriter out (Perks and Stamps only, checked at Build); the host and services keep the concrete writer internal
        Assert.Equal(new[] { "SimCoreServices.Stats", "SimSystemContext.StatWriter" }, exposures.OrderBy(e => e, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(typeof(SimHost).GetProperties(BindingFlags.Instance | BindingFlags.Public), p => p.PropertyType == typeof(IStatWriter) || p.PropertyType == typeof(StatSheetWriter));
    }

    // ---------------------------------------------------------------- StatSheet.Build input

    [Fact]
    public void StatSheetBuild_TakesOnlyTheContentSetAndTheSourceList_NoProfileRankOrMetaInput()
    {
        // Arrange
        MethodInfo[] builds = typeof(StatSheet).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == nameof(StatSheet.Build)).ToArray();

        // Act
        Type[] parameters = builds.Single().GetParameters().Select(p => p.ParameterType).ToArray();
        string[] names = builds.Single().GetParameters().Select(p => p.Name!).ToArray();

        // Assert
        Assert.Equal(new[] { typeof(ContentSet), typeof(StatSources) }, parameters);
        Assert.Equal(new[] { "content", "sources" }, names);
        string[] forbidden = ["profile", "rank", "meta", "progression", "unlock"];
        Assert.DoesNotContain(names, n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void TheSourceList_AndTheSheetFace_ExposeNoProfileRankOrMetaMember()
    {
        // Arrange: the profile-keyed remap is a static factory producing a slot-keyed list; nothing instance-side takes a profile
        string[] forbidden = ["Profile", "Rank", "Meta"];
        Type[] faces = [typeof(IStatSheet), typeof(IStatWriter), typeof(StatSources), typeof(StatSheet), typeof(ModifierSource), typeof(Modifier)];

        // Act
        var offenders = new List<string>();
        foreach (Type t in faces)
        {
            foreach (MethodBase m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)))
            {
                bool remap = t == typeof(StatSources) && m.Name == nameof(StatSources.FromProfilePicks);
                foreach (ParameterInfo p in m.GetParameters())
                {
                    if (!remap && forbidden.Any(f => p.ParameterType.Name.Contains(f, StringComparison.Ordinal) || (p.Name ?? string.Empty).Contains(f, StringComparison.OrdinalIgnoreCase)))
                    {
                        offenders.Add($"{t.Name}.{m.Name}({p.ParameterType.Name} {p.Name})");
                    }
                }
            }

            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (forbidden.Any(f => p.Name.Contains(f, StringComparison.Ordinal) || p.PropertyType.Name.Contains(f, StringComparison.Ordinal)))
                {
                    offenders.Add($"{t.Name}.{p.Name}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    // ---------------------------------------------------------------- SourceKind

    [Fact]
    public void SourceKind_HasExactlyPerkStampAndRunEvent()
    {
        // Arrange / Act
        string[] names = Enum.GetNames<SourceKind>().OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // Assert: no Meta, Rank, Unlock or Profile kind, ever (meta-progression never pushes modifiers)
        Assert.Equal(new[] { "Perk", "RunEvent", "Stamp" }, names);
        Assert.Equal(1, (int)SourceKind.Perk);
        Assert.Equal(2, (int)SourceKind.Stamp);
        Assert.Equal(3, (int)SourceKind.RunEvent);
    }
}
