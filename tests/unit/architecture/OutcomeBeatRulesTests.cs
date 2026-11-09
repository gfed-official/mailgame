using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Presentation.Delivery;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Delivery.Client;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using EventCursor = PerformativeMail.Client.Core.Presentation.EventCursor;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// ADR-0016 section 5 Enforcement, manual-delivery story 006 AC-4 (TR-delivery-018): every PerformativeMail.Client.Core type tagged
/// <see cref="OutcomeBeatAttribute"/> receives its data only from <see cref="PresentationEventStream"/> cursors. Checked from compiled
/// metadata and IL (<see cref="ArchitectureScan"/>, nested and compiler-generated types included): the type holds an
/// <see cref="EventCursor"/> field, a constructor opens it through <see cref="PresentationEventStream.OpenCursor"/>, a method reads it
/// through <see cref="EventCursor.TryRead"/>, every field and parameter type is on <see cref="OutcomeBeatRules"/>' cursor-data
/// allow-list, and no body names a frame port (<see cref="IRenderClock"/>, <see cref="IDeliverReachRead"/>), the stamped
/// <see cref="HandBeatStart"/> or a predictive input (ClientLayer and <c>PerformativeMail.Sim.&lt;System&gt;.Client</c> types such as
/// <see cref="IDeliverPreviewRead"/>, <see cref="InteractionSnapshot"/>, <see cref="ReachEcho"/>).
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
        string[] keys = violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Contains("ReachEcho: IDeliverPreviewRead", keys);
        Assert.Contains("ReachEcho: IRenderClock", keys);
        Assert.Contains("ReachEcho: no EventCursor field", keys);
        Assert.Contains("ReachEcho: no OpenCursor in a constructor", keys);
        Assert.Contains("ReachEcho: no EventCursor.TryRead", keys);
        // Allow-list / body bans also report other non-cursor types (e.g. ReachEchoStart, DeliverPreviewState, ReachEcho).
        Assert.True(keys.Length >= 5, string.Join(Environment.NewLine, keys));
    }

    [Fact]
    public void RuleEngine_BeatWithClockOrReachArgument_IsReported()
    {
        // Act: a cursor-reading beat that also takes the render clock and the reach port.
        List<ArchViolation> violations = OutcomeBeatRules.Check(typeof(ClockedBeat));

        // Assert
        Assert.Equal(
            new[] { "OutcomeBeatRulesTests+ClockedBeat: IDeliverReachRead", "OutcomeBeatRulesTests+ClockedBeat: IRenderClock" },
            violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));
    }

    [OutcomeBeat]
    private sealed class ClockedBeat
    {
        private readonly EventCursor _cursor;
        private readonly IRenderClock _clock;

        public ClockedBeat(PresentationEventStream events, IRenderClock clock)
        {
            _cursor = events.OpenCursor(EventKindSet.Of(EventKind.OpReply));
            _clock = clock;
        }

        public long Update(IDeliverReachRead reach)
        {
            bool attached = false;
            while (_cursor.TryRead(out PresentedEvent e))
            {
                attached |= reach.IsWithinHoldReach(new EntityId(e.SourceTick));
            }

            return attached ? _clock.RenderSimTimeMs : 0;
        }
    }
}

/// <summary>
/// The <see cref="OutcomeBeatRulesTests"/> rule: an outcome beat's data comes only from its <see cref="EventCursor"/>. Allow-list:
/// every instance field, constructor parameter and method parameter type of the beat and its nested types (array, by-ref,
/// <see cref="Nullable{T}"/> and generic arguments unwrapped) is cursor data, a primitive the event payloads use, a container of
/// those, or the beat and its own nested types. Bodies may not name the frame ports or the predictive inputs.
/// </summary>
internal static class OutcomeBeatRules
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private const string ClientLayerNamespace = "PerformativeMail.Sim.ClientLayer";
    private const string SimRoot = "PerformativeMail.Sim.";

    private static readonly MethodInfo _openCursor = typeof(PresentationEventStream).GetMethod(nameof(PresentationEventStream.OpenCursor))!;
    private static readonly MethodInfo _tryRead = typeof(EventCursor).GetMethod(nameof(EventCursor.TryRead))!;

    // Cursor data, the event payloads and replies the hand beat reads, the beat's own descriptors, and the payloads' primitives.
    private static readonly HashSet<Type> _allowed =
    [
        typeof(PresentationEventStream), typeof(EventCursor), typeof(PresentedEvent), typeof(EventKind), typeof(EventKindSet),
        typeof(HandBeatOutcome), typeof(HandResponse), typeof(HandBeat), typeof(RiffleTiming),
        typeof(DeliveryOutcomePayload), typeof(Reply), typeof(InvReplyExtra), typeof(DeliverBranchKind), typeof(EvalResult), typeof(Op), typeof(OpKind),
        typeof(PlayerSlot), typeof(EntityId), typeof(StringId), typeof(bool), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
    ];

    // Containers, allowed only around allowed types (their arguments are checked as constituents).
    private static readonly HashSet<Type> _containers = [typeof(List<>), typeof(IReadOnlyList<>), typeof(Nullable<>)];

    // Frame ports, the stamped hand beat and the predictive types no outcome-beat body may name.
    private static readonly HashSet<Type> _bannedInBodies =
    [
        typeof(IRenderClock), typeof(IDeliverReachRead), typeof(IDeliverPreviewRead), typeof(HandBeatStart), typeof(ReachEcho), typeof(InteractionSnapshot),
    ];

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
            foreach ((string where, Type type) in Inputs(t))
            {
                violations.AddRange(ArchitectureScan.Constituents(type).Where(p => !IsAllowed(p, types)).Select(p => new ArchViolation(owner, p.Name, where)));
            }

            foreach (MethodBase body in ArchitectureScan.MethodsWithBodies(t))
            {
                violations.AddRange(ArchitectureScan.BodyReferences(body)
                    .SelectMany(ArchitectureScan.TypesNamedBy)
                    .Where(IsBannedInBody)
                    .Distinct()
                    .Select(p => new ArchViolation(owner, p.Name, "body of " + body.Name)));
            }
        }

        return violations;
    }

    // Where data enters a type: instance fields, constructor parameters and method parameters (return types are outputs). Synthesized
    // members (a record's Equals(object), PrintMembers(StringBuilder), lambdas) are skipped here; their bodies are still scanned.
    private static IEnumerable<(string Where, Type Type)> Inputs(Type t)
    {
        foreach (FieldInfo f in t.GetFields(Declared).Where(f => !f.IsStatic))
        {
            yield return ($"field {f.Name}", f.FieldType);
        }

        foreach (MethodBase m in t.GetConstructors(Declared).Cast<MethodBase>().Concat(t.GetMethods(Declared))
                     .Where(m => !m.IsDefined(typeof(CompilerGeneratedAttribute), false)))
        {
            foreach (ParameterInfo p in m.GetParameters())
            {
                yield return ($"parameter {p.Name} of {m.Name}", p.ParameterType);
            }
        }
    }

    // Arrays and by-refs are allowed by their element (a constituent); generics by their definition and arguments.
    private static bool IsAllowed(Type t, Type[] own)
        => t.HasElementType
            || _allowed.Contains(t)
            || own.Contains(t)
            || (t.IsGenericType && _containers.Contains(t.GetGenericTypeDefinition()));

    // The frame ports and the predictive inputs ADR-0016 section 5 keeps away from outcome beats: ClientLayer input and previews,
    // the per-system client steps and their published states.
    private static bool IsBannedInBody(Type t)
    {
        if (_bannedInBodies.Contains(t))
        {
            return true;
        }

        return !t.HasElementType
            && t.Namespace is { } ns
            && (ns == ClientLayerNamespace
                || ns.StartsWith(ClientLayerNamespace + ".", StringComparison.Ordinal)
                || (ns.StartsWith(SimRoot, StringComparison.Ordinal) && ns.EndsWith(".Client", StringComparison.Ordinal)));
    }
}
