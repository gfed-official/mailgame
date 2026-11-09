using System.Reflection;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Presentation.Delivery;
using PerformativeMail.Sim.Players;
using EventCursor = PerformativeMail.Client.Core.Presentation.EventCursor;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// ADR-0016 section 5 Enforcement, manual-delivery story 006 AC-4 (TR-delivery-018): every PerformativeMail.Client.Core type tagged
/// <see cref="OutcomeBeatAttribute"/> receives its data only from <see cref="PresentationEventStream"/> cursors. Checked from compiled
/// metadata and IL (<see cref="ArchitectureScan"/>, nested and compiler-generated types included): the type holds an
/// <see cref="EventCursor"/> field, a constructor opens it through <see cref="PresentationEventStream.OpenCursor"/>, a method reads it
/// through <see cref="EventCursor.TryRead"/>, and no signature or body names a predictive input, i.e. the ClientLayer's input and
/// preview types (<c>PerformativeMail.Sim.ClientLayer</c>, <c>PerformativeMail.Sim.&lt;System&gt;.Client</c>: <c>IDeliverPreviewRead</c>,
/// <c>DeliverPreviewState.CanStartHoldNext</c>, <c>InputEdge</c>, ...), the published <see cref="InteractionSnapshot"/> or the
/// predictive <see cref="ReachEcho"/>.
/// </summary>
[Trait("Id", "TR-delivery-018")]
public class OutcomeBeatRulesTests
{
    private static readonly Assembly _clientCore = typeof(LoopbackTransport).Assembly;

    [Fact]
    public void ClientCoreAssembly_OutcomeBeatTypes_ReadOnlyFromPresentationEventStreamCursors()
    {
        // Arrange
        Type[] beats = _clientCore.GetTypes().Where(t => t.IsDefined(typeof(OutcomeBeatAttribute), false)).ToArray();

        // Act
        List<ArchViolation> violations = beats.SelectMany(OutcomeBeatRules.Check).ToList();

        // Assert
        Assert.Contains(typeof(HandBeatController), beats);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_ReachEcho_IsNotAnOutcomeBeat_AndIsReportedAsPredictive()
    {
        // Act: the predictive reach echo, checked as if it were tagged.
        List<ArchViolation> violations = OutcomeBeatRules.Check(typeof(ReachEcho));

        // Assert
        Assert.False(typeof(ReachEcho).IsDefined(typeof(OutcomeBeatAttribute), false));
        Assert.Equal(
            new[]
            {
                "ReachEcho: DeliverPreviewState",
                "ReachEcho: IDeliverPreviewRead",
                "ReachEcho: no EventCursor field",
                "ReachEcho: no OpenCursor in a constructor",
                "ReachEcho: no EventCursor.TryRead",
            }.Order(StringComparer.Ordinal),
            violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));
    }
}

/// <summary>The <see cref="OutcomeBeatRulesTests"/> rule: an outcome beat's data comes only from its <see cref="EventCursor"/>.</summary>
internal static class OutcomeBeatRules
{
    private const string ClientLayerNamespace = "PerformativeMail.Sim.ClientLayer";
    private const string SimRoot = "PerformativeMail.Sim.";

    private static readonly MethodInfo _openCursor = typeof(PresentationEventStream).GetMethod(nameof(PresentationEventStream.OpenCursor))!;
    private static readonly MethodInfo _tryRead = typeof(EventCursor).GetMethod(nameof(EventCursor.TryRead))!;

    /// <summary>The violations of <paramref name="beat"/> (and its nested types) against the outcome-beat rule.</summary>
    public static List<ArchViolation> Check(Type beat)
    {
        string name = ArchitectureScan.NameOf(beat);
        Type[] types = ArchitectureScan.WithNested(beat).ToArray();
        var violations = new List<ArchViolation>();

        if (!beat.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Any(f => f.FieldType == typeof(EventCursor)))
        {
            violations.Add(new ArchViolation(name, "no EventCursor field", "fields"));
        }

        if (!beat.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Any(c => ArchitectureScan.BodyReferences(c).Contains(_openCursor)))
        {
            violations.Add(new ArchViolation(name, "no OpenCursor in a constructor", "constructors"));
        }

        if (!types.SelectMany(ArchitectureScan.MethodsWithBodies).Any(m => ArchitectureScan.BodyReferences(m).Contains(_tryRead)))
        {
            violations.Add(new ArchViolation(name, "no EventCursor.TryRead", "method bodies"));
        }

        foreach (Type t in types)
        {
            string owner = ArchitectureScan.NameOf(ArchitectureScan.OwnerOf(t));
            foreach ((string where, Type type) in ArchitectureScan.SignatureTypes(t))
            {
                violations.AddRange(ArchitectureScan.Constituents(type).Where(p => p != beat && IsPredictive(p)).Select(p => new ArchViolation(owner, p.Name, where)));
            }

            foreach (MethodBase body in ArchitectureScan.MethodsWithBodies(t))
            {
                violations.AddRange(ArchitectureScan.BodyReferences(body)
                    .SelectMany(ArchitectureScan.TypesNamedBy)
                    .Where(p => p != beat && IsPredictive(p))
                    .Select(p => new ArchViolation(owner, p.Name, "body of " + body.Name)));
            }
        }

        return violations;
    }

    // The predictive inputs ADR-0016 section 5 keeps away from outcome beats: ClientLayer input and previews, the per-system client
    // steps and their published states, the published interaction snapshot, and the reach echo.
    private static bool IsPredictive(Type t)
    {
        if (t.HasElementType || t.Namespace is not { } ns)
        {
            return false;
        }

        return t == typeof(InteractionSnapshot)
            || t == typeof(ReachEcho)
            || ns == ClientLayerNamespace
            || ns.StartsWith(ClientLayerNamespace + ".", StringComparison.Ordinal)
            || (ns.StartsWith(SimRoot, StringComparison.Ordinal) && ns.EndsWith(".Client", StringComparison.Ordinal));
    }
}
