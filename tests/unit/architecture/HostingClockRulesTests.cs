using System.Diagnostics;
using System.Reflection;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// SC-ARCH-1 row (d), ADR-0002 section 1 (TR-sim-042 clock half): no type in <c>PerformativeMail.Sim.Hosting</c> (nested and
/// compiler-generated types included) references <see cref="Stopwatch"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
/// <see cref="TimeProvider"/> or <c>Environment.TickCount*</c> — in a signature or a method body — and only
/// <see cref="FrameRunner"/> reads <see cref="IMonotonicClock.Timestamp"/> ("TickDriver never reads a clock").
/// Story 004's analyzer bans the clock types Sim-wide; this rule adds the Hosting-specific <c>Timestamp</c> ban.
/// </summary>
[Trait("Id", "SC-ARCH-1")]
public class HostingClockRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    [Fact]
    public void SimAssembly_HostingTypes_ReadNoClockOutsideFrameRunner()
    {
        // Arrange
        Type[] hosting = _sim.GetTypes()
            .Where(t => t.Namespace is { } ns && (ns == HostingClockRules.Namespace || ns.StartsWith(HostingClockRules.Namespace + ".", StringComparison.Ordinal)))
            .ToArray();

        // Act
        List<ArchViolation> violations = HostingClockRules.Check(hosting, typeof(FrameRunner));

        // Assert
        Assert.Contains(typeof(TickDriver), hosting);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SimAssembly_FrameRunner_IsTheTimestampReader()
    {
        // Act: with no exempt reader, FrameRunner's one Timestamp read must be reported (the exemption is not vacuous).
        List<ArchViolation> violations = HostingClockRules.Check(new[] { typeof(FrameRunner) }, timestampReader: null);

        // Assert
        Assert.Equal(new[] { "FrameRunner: IMonotonicClock.Timestamp" }, violations.Select(v => v.Key).Distinct());
    }

    [Fact]
    public void RuleEngine_FixtureFrameRunnerReadingTimestamp_IsClean()
    {
        // Act
        List<ArchViolation> violations = HostingClockRules.Check(ArchitectureScan.WithNested(typeof(HostingFrameRunnerFixture)).ToArray(), typeof(HostingFrameRunnerFixture));

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_BadFixture_ReportsExactlyTheExpectedViolations()
    {
        // Arrange
        string[] expected =
        {
            "HostingClockBadFixture: Stopwatch",
            "HostingClockBadFixture: DateTime",
            "HostingClockBadFixture: DateTimeOffset",
            "HostingClockBadFixture: TimeProvider",
            "HostingClockBadFixture: Environment.TickCount64",
            "HostingClockBadFixture: IMonotonicClock.Timestamp",
            "HostingGenericClockFixture: Stopwatch",
        };
        Type[] universe = new[] { typeof(HostingClockBadFixture), typeof(HostingGenericClockFixture) }.SelectMany(ArchitectureScan.WithNested).ToArray();

        // Act
        List<ArchViolation> violations = HostingClockRules.Check(universe, typeof(HostingFrameRunnerFixture));

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));
    }
}

/// <summary>The SC-ARCH-1 row (d) engine.</summary>
internal static class HostingClockRules
{
    /// <summary>The hosting namespace (sub-namespaces included by the positive scan).</summary>
    public const string Namespace = "PerformativeMail.Sim.Hosting";

    private static readonly Type[] _clockTypes = { typeof(Stopwatch), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeProvider) };

    /// <summary>
    /// Checks each given type. <paramref name="timestampReader"/> (and types nested in it) may read
    /// <see cref="IMonotonicClock.Timestamp"/>; <c>null</c> exempts nothing.
    /// </summary>
    public static List<ArchViolation> Check(IReadOnlyList<Type> types, Type? timestampReader)
    {
        var violations = new List<ArchViolation>();
        foreach (Type t in types)
        {
            string owner = ArchitectureScan.NameOf(ArchitectureScan.OwnerOf(t));
            bool mayReadTimestamp = false;
            for (Type? c = t; c is not null; c = c.DeclaringType)
            {
                mayReadTimestamp |= c == timestampReader;
            }

            foreach ((string where, Type type) in ArchitectureScan.SignatureTypes(t))
            {
                foreach (Type c in ArchitectureScan.Constituents(type).Where(c => _clockTypes.Contains(c)))
                {
                    violations.Add(new ArchViolation(owner, c.Name, $"{ArchitectureScan.NameOf(t)} {where}"));
                }
            }

            foreach (MethodBase m in ArchitectureScan.MethodsWithBodies(t))
            {
                foreach (MemberInfo used in ArchitectureScan.BodyReferences(m))
                {
                    string where = $"body of {ArchitectureScan.NameOf(t)}.{m.Name} ({used.Name})";
                    foreach (Type c in ArchitectureScan.TypesNamedBy(used).Where(c => _clockTypes.Contains(c)).Distinct())
                    {
                        violations.Add(new ArchViolation(owner, c.Name, where));
                    }

                    if (used is MethodInfo mi && mi.DeclaringType == typeof(Environment) && mi.Name.StartsWith("get_TickCount", StringComparison.Ordinal))
                    {
                        violations.Add(new ArchViolation(owner, "Environment." + mi.Name[4..], where));
                    }

                    if (!mayReadTimestamp && used is MethodInfo { Name: "get_Timestamp" } get && typeof(IMonotonicClock).IsAssignableFrom(get.DeclaringType))
                    {
                        violations.Add(new ArchViolation(owner, "IMonotonicClock.Timestamp", where));
                    }
                }
            }
        }

        return violations;
    }
}

/// <summary>Seen-passing fixture: stands in for <see cref="FrameRunner"/>, the one exempt Timestamp reader.</summary>
internal sealed class HostingFrameRunnerFixture
{
    private readonly IMonotonicClock _clock;

    public HostingFrameRunnerFixture(IMonotonicClock clock)
    {
        _clock = clock;
    }

    public long Now() => _clock.Timestamp;

    public long Rate() => _clock.Frequency;
}

/// <summary>Seen-failing fixture: a hosting-like type that reads every banned clock (some only inside a lambda).</summary>
internal sealed class HostingClockBadFixture
{
    private readonly IMonotonicClock _clock;
    private readonly TimeProvider? _provider;

    public HostingClockBadFixture(IMonotonicClock clock, TimeProvider? provider)
    {
        _clock = clock;
        _provider = provider;
    }

    public long Stamp() => Stopwatch.GetTimestamp();

    public long Wall() => DateTime.UtcNow.Ticks;

    public Func<long> Later() => () => DateTimeOffset.UtcNow.Ticks + Environment.TickCount64 + _clock.Timestamp;

    public bool HasProvider => _provider is not null;
}

/// <summary>Seen-failing fixture: names a clock type only as a generic method argument (no signature mentions it).</summary>
internal static class HostingGenericClockFixture
{
    public static int Count<T>() => 0;

    public static int Use() => Count<Stopwatch>();
}
