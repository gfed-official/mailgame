using System.Diagnostics;
using System.Reflection;
using PerformativeMail.Sim.Composition;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 005 AC-2 (M-R7-2d; ADR-0017 section 8, TR-mail-007): no type in <c>PerformativeMail.Sim.MailDestinations*</c>
/// (folder <c>Sim/Mail/</c>; ADR-0017 erratum 2026-10-20) references a type in <c>PerformativeMail.Sim.Economy*</c>. The scan is keyed
/// on the real namespaces and first proves both sides are populated, so it cannot pass vacuously.
/// </summary>
[Trait("Id", "M-R7-2d")]
public class MailArchitectureTests
{
    [Fact]
    public void MailNamespace_ReferencesNoEconomyType_InSignaturesAttributesBaseTypesOrMethodBodies()
    {
        // Arrange
        Type[] mail = MailScan.TypesIn(MailScan.MailNamespace);
        Type[] economy = MailScan.TypesIn(MailScan.EconomyNamespace);

        // Act
        string[] violations = mail
            .SelectMany(t => MailScan.ReferencedTypes(t).Select(r => (Owner: t, r.Where, r.Type)))
            .Where(r => MailScan.IsIn(r.Type, MailScan.EconomyNamespace))
            .Select(r => $"{r.Owner.FullName}: {r.Type.FullName} in {r.Where}")
            .Distinct()
            .ToArray();

        // Assert
        Assert.NotEmpty(mail);
        Assert.NotEmpty(economy);
        Assert.Contains(mail, t => t.Name == "MailSystem");
        Assert.Contains(economy, t => t.Name == "StubWallet");
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }
}

/// <summary>Reflection over the Sim assembly for the Mail and Economy namespaces (stories 005 AC-2 and AC-4).</summary>
internal static class MailScan
{
    /// <summary>Mail &amp; Destinations' namespace root (folder <c>Sim/Mail/</c>).</summary>
    public const string MailNamespace = "PerformativeMail.Sim.MailDestinations";

    /// <summary>The Economy's namespace root (folder <c>Sim/Economy/</c>).</summary>
    public const string EconomyNamespace = "PerformativeMail.Sim.Economy";

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    /// <summary>True when <paramref name="t"/>'s namespace is <paramref name="root"/> or below it.</summary>
    public static bool IsIn(Type t, string root) =>
        t.Namespace is { } ns && (ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal));

    /// <summary>Every Sim type (nested and compiler-generated ones included) in <paramref name="root"/> or below.</summary>
    public static Type[] TypesIn(string root) => _sim.GetTypes().Where(t => IsIn(t, root)).ToArray();

    /// <summary>
    /// Every type <paramref name="t"/> names: base type, interfaces, attributes (on the type, its members and their parameters),
    /// member signatures, method-body locals and the IL type, field and method references of its bodies, constituents unwrapped.
    /// </summary>
    public static IEnumerable<(string Where, Type Type)> ReferencedTypes(Type t)
    {
        var roots = new List<(string, Type)>();
        if (t.BaseType is { } b)
        {
            roots.Add(("base type", b));
        }

        roots.AddRange(t.GetInterfaces().Select(i => ("interface", i)));
        roots.AddRange(t.GetCustomAttributesData().Select(a => ("attribute", a.AttributeType)));
        foreach (MemberInfo m in t.GetMembers(Declared))
        {
            roots.AddRange(m.GetCustomAttributesData().Select(a => ($"attribute of {m.Name}", a.AttributeType)));
            if (m is MethodBase mb)
            {
                foreach (ParameterInfo p in mb.GetParameters())
                {
                    roots.AddRange(p.GetCustomAttributesData().Select(a => ($"attribute of {m.Name}({p.Name})", a.AttributeType)));
                }
            }
        }

        roots.AddRange(ArchitectureScan.SignatureTypes(t));
        foreach (MethodBase body in ArchitectureScan.MethodsWithBodies(t))
        {
            roots.AddRange(body.GetMethodBody()!.LocalVariables.Select(l => ($"local of {body.Name}", l.LocalType)));
            foreach (MemberInfo used in ArchitectureScan.BodyReferences(body))
            {
                roots.AddRange(ArchitectureScan.TypesNamedBy(used).Select(r => ($"body of {body.Name}", r)));
            }
        }

        return roots.SelectMany(r => ArchitectureScan.Constituents(r.Item2).Select(c => (r.Item1, c)));
    }

    /// <summary>
    /// The wall-clock reads (<see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="Stopwatch"/>, <c>Environment.TickCount</c>
    /// and <c>TickCount64</c>) named by any type in <paramref name="root"/>; empty when there are none (story 005 AC-4, M-E-1).
    /// </summary>
    public static string[] WallClockReads(string root)
    {
        Type[] banned = { typeof(DateTime), typeof(DateTimeOffset), typeof(Stopwatch) };
        var found = new List<string>();
        foreach (Type t in TypesIn(root))
        {
            found.AddRange(ReferencedTypes(t).Where(r => banned.Contains(r.Type)).Select(r => $"{t.FullName}: {r.Type.Name} in {r.Where}"));
            foreach (MethodBase body in ArchitectureScan.MethodsWithBodies(t))
            {
                found.AddRange(ArchitectureScan.BodyReferences(body)
                    .Where(m => m.DeclaringType == typeof(Environment) && m.Name is "get_TickCount" or "get_TickCount64")
                    .Select(m => $"{t.FullName}: Environment.{m.Name} in body of {body.Name}"));
            }
        }

        return found.Distinct().ToArray();
    }
}
