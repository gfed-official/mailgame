using System.Reflection;
using System.Text.RegularExpressions;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Throwing story 001 architecture guards (ADR-0015 section 2 and Risks, ADR-0005 section 2, ADR-0006 sections 1 and 3;
/// TR-throwing-001, TR-throwing-002, TR-throwing-004): <c>src/PerformativeMail.Sim/Throwing/</c> (namespace
/// <c>PerformativeMail.Sim.ThrowingCatching</c>) references no RNG type (the pure hash <c>SplitMix.Mix64</c> is allowed) and
/// no <c>Fix.MulDivRH</c>, mentions no
/// <c>float</c>/<c>double</c>, rounds only through <see cref="Throw.RH"/> (no <c>/</c> or <c>%</c> operator anywhere in its
/// code), declares no mutable static, and <see cref="Ballistic"/> holds no state. Checked both on the source text (comments
/// stripped) and on the compiled IL.
/// </summary>
public class ThrowingRulesTests
{
    private const string Namespace = "PerformativeMail.Sim.ThrowingCatching";

    private const BindingFlags AllDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // ADR-0015 Risks allow Fix.MulDivRH in the F7 label code only; story 001 has no F7 code, so the ban is folder-wide.
    // The F7 story must exempt its label file here and in ThrowingIl_CallsNoFixHelperOtherThanFloorDivAndISqrt.
    // SplitMix is deliberately absent from both lists: ADR-0015/ADR-0005 treat SplitMix.Mix64 as a pure hash and require it
    // for the section 4 catch tie-break (TieHash); the guard bans RNG streams only.
    private static readonly Regex _forbiddenTokens = new(
        @"\b(Random|Pcg32|IRngStreams|RngNames|RngOwnerView|RngRegistry|StreamName|MulDivRH|MulDiv|float|double|decimal|Half|Single|Double|Math|MathF)\b",
        RegexOptions.CultureInvariant);

    private static readonly string[] _rngTypeNames = { "Random", "Pcg32", "IRngStreams", "RngOwnerView", "RngRegistry", "StreamName", "RngNames" };

    private static Type[] ThrowingTypes() =>
        typeof(Throw).Assembly.GetTypes().Where(t => t.Namespace == Namespace || (t.Namespace?.StartsWith(Namespace + ".", StringComparison.Ordinal) ?? false)).ToArray();

    [Fact]
    [Trait("Id", "TR-throwing-001")]
    public void ThrowingFolder_Exists_AndEveryFileUsesTheThrowingNamespace()
    {
        // Arrange
        string[] files = SourceFiles();

        // Act
        string[] offNamespace = files.Where(f => !File.ReadAllText(f).Contains("namespace " + Namespace + ";", StringComparison.Ordinal)).ToArray();

        // Assert
        Assert.True(files.Length >= 4, "found only " + files.Length + " files");
        Assert.Empty(offNamespace);
    }

    [Fact]
    [Trait("Id", "TR-throwing-002")]
    public void ThrowingSource_NamesNoRngTypeMulDivRhOrFloatingPoint()
    {
        // Arrange
        List<string> hits = [];

        // Act
        foreach (string file in SourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in _forbiddenTokens.Matches(StripComment(lines[i])))
                {
                    hits.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Value}");
                }
            }
        }

        // Assert
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    [Trait("Id", "TR-throwing-004")]
    public void ThrowingSource_HasNoDivisionOrRemainderOperator_SoEveryRoundingIsThrowRH()
    {
        // Arrange: code only (comments stripped); Throw.RH itself divides through Fix.FloorDiv, so no file needs '/' or '%'
        List<string> hits = [];

        // Act
        foreach (string file in SourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComment(lines[i]);
                if (code.Contains('/', StringComparison.Ordinal) || code.Contains('%', StringComparison.Ordinal))
                {
                    hits.Add($"{Path.GetFileName(file)}:{i + 1}: {code.Trim()}");
                }
            }
        }

        // Assert
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    [Trait("Id", "TR-throwing-004")]
    public void ThrowingIl_CallsNoFixHelperOtherThanFloorDivAndISqrt()
    {
        // Arrange
        string[] allowedFix = { nameof(Fix.FloorDiv), nameof(Fix.ISqrt) };
        List<string> bad = [];

        // Act
        foreach (Type t in ThrowingTypes())
        {
            foreach (MethodBase m in ArchitectureScan.MethodsWithBodies(t))
            {
                foreach (MemberInfo r in ArchitectureScan.BodyReferences(m))
                {
                    if (r is MethodBase called && called.DeclaringType == typeof(Fix) && !allowedFix.Contains(called.Name))
                    {
                        bad.Add($"{t.Name}.{m.Name} calls Fix.{called.Name}");
                    }
                }
            }
        }

        // Assert
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    [Trait("Id", "TR-throwing-002")]
    public void ThrowingIl_ReferencesNoRngOrFloatingPointType()
    {
        // Arrange
        List<string> bad = [];

        // Act
        foreach (Type t in ThrowingTypes())
        {
            foreach (MethodBase m in ArchitectureScan.MethodsWithBodies(t))
            {
                foreach (Type named in ArchitectureScan.BodyReferences(m).SelectMany(ArchitectureScan.TypesNamedBy))
                {
                    if (named == typeof(float) || named == typeof(double) || named == typeof(decimal) || named == typeof(Half)
                        || named == typeof(Random) || _rngTypeNames.Contains(named.Name))
                    {
                        bad.Add($"{t.Name}.{m.Name} references {named.FullName}");
                    }
                }
            }

            foreach ((string where, Type st) in ArchitectureScan.SignatureTypes(t))
            {
                if (st == typeof(float) || st == typeof(double) || st == typeof(decimal) || st == typeof(Half) || _rngTypeNames.Contains(st.Name))
                {
                    bad.Add($"{t.Name} {where}: {st.FullName}");
                }
            }
        }

        // Assert
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void ThrowingTypes_DeclareNoMutableStatic()
    {
        // Arrange
        Type[] types = ThrowingTypes();

        // Act
        List<ArchViolation> violations = StaticStateRules.Check(types);

        // Assert
        Assert.NotEmpty(types);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void BallisticLaunchMathAndThrow_AreStaticClassesWithNoFieldOtherThanConstants()
    {
        foreach (Type t in new[] { typeof(Ballistic), typeof(LaunchMath), typeof(Throw) })
        {
            // Act
            FieldInfo[] fields = t.GetFields(AllDeclared).Where(f => !f.IsLiteral).ToArray();

            // Assert
            Assert.True(t.IsAbstract && t.IsSealed, $"{t.Name} is not a static class");
            Assert.True(fields.Length == 0, $"{t.Name} declares {string.Join(", ", fields.Select(f => f.Name))}");
        }
    }

    [Fact]
    [Trait("Id", "TR-throwing-001")]
    public void KeyInterfaces_KeepTheAdr0015Shapes()
    {
        // Act
        MethodInfo rh = typeof(Throw).GetMethod(nameof(Throw.RH))!;
        MethodInfo compute = typeof(LaunchMath).GetMethod(nameof(LaunchMath.Compute))!;
        MethodInfo position = typeof(Ballistic).GetMethod(nameof(Ballistic.PositionAt))!;
        string[] launchFields = typeof(Launch).GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name).Order(StringComparer.Ordinal).ToArray();

        // Assert
        Assert.Equal(typeof(long), rh.ReturnType);
        Assert.Equal(new[] { typeof(long), typeof(long) }, rh.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(typeof(bool), compute.ReturnType);
        ParameterInfo[] cp = compute.GetParameters();
        Assert.Equal(3, cp.Length);
        Assert.True(cp[0].IsIn && cp[0].ParameterType == typeof(LaunchInput).MakeByRefType());
        Assert.True(cp[1].IsOut && cp[1].ParameterType == typeof(Launch).MakeByRefType());
        Assert.True(cp[2].IsOut && cp[2].ParameterType == typeof(bool).MakeByRefType());
        Assert.Equal(typeof(Vec3Mm), position.ReturnType);
        Assert.True(position.GetParameters()[0].IsIn);
        Assert.Equal(typeof(int), position.GetParameters()[1].ParameterType);
        Assert.Equal(new[] { "G", "Origin", "Vel" }, launchFields);   // GetFields order is unspecified: compare sorted
        Assert.All(typeof(Launch).GetFields(BindingFlags.Instance | BindingFlags.Public), f => Assert.True(f.IsInitOnly));
    }

    private static string StripComment(string line)
    {
        int at = line.IndexOf("//", StringComparison.Ordinal);
        return at < 0 ? line : line[..at];
    }

    private static string[] SourceFiles()
    {
        string dir = Path.Combine(FindRepoRoot(), "src", "PerformativeMail.Sim", "Throwing");
        Assert.True(Directory.Exists(dir), dir + " is missing");
        return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }
}
