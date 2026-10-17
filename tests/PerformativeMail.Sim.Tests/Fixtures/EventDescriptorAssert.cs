using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests;

/// <summary>
/// Shared event-descriptor check (ADR-0008 sections 1 and 3), reused by every system's event descriptor test.
/// <see cref="Validate"/> checks one <see cref="EventDescriptor"/>: the kind is non-zero and inside its owner's
/// <see cref="OpKindBlock"/> (<see cref="EventKind"/> mirrors those ranges); the class is defined; the resync scope is at most
/// 63 (a shift by 64 would alias bit 0); and a declared payload type passes the same plain-integer, at most 48 B, padding-free
/// checks as op payloads (<see cref="DescriptorAssert.CheckPayload"/>). <see cref="FindUndeclared(EventLog)"/> enumerates every
/// <see cref="EventKind"/> member and reports the ones with no descriptor bound in a world. Test-side only (uses reflection).
/// </summary>
/// <example>
/// <code>
/// EventDescriptorAssert.Check(CoreEventDescriptors.OpReply, OpKindBlock.Core);
/// EventDescriptorAssert.AssertAllDeclared(world.Events);
/// </code>
/// </example>
internal static class EventDescriptorAssert
{
    /// <summary>Lists every rule the descriptor breaks (empty when valid).</summary>
    /// <param name="descriptor">The descriptor to check.</param>
    /// <param name="ownerBlock">The block the declaring system may use.</param>
    /// <returns>Human-readable violations.</returns>
    public static IReadOnlyList<string> Validate(EventDescriptor descriptor, OpKindBlock ownerBlock)
    {
        var violations = new List<string>();
        var asOpKind = (OpKind)(ushort)descriptor.Kind;
        if (descriptor.Kind == EventKind.None)
        {
            violations.Add("kind is None (0 is invalid)");
        }
        else if (!ownerBlock.Contains(asOpKind))
        {
            violations.Add($"kind 0x{(ushort)descriptor.Kind:X4} is outside the {ownerBlock.Name} block");
        }

        if (!Enum.IsDefined(descriptor.Class))
        {
            violations.Add($"class {(byte)descriptor.Class} is not a ReplicationClass");
        }

        if (descriptor.ResyncScope > 63)
        {
            violations.Add($"ResyncScope {descriptor.ResyncScope} is over 63");
        }

        if (descriptor.PayloadType is null)
        {
            if (descriptor.PayloadLength != 0)
            {
                violations.Add($"payload-free kind declares PayloadLength {descriptor.PayloadLength}");
            }
        }
        else
        {
            DescriptorAssert.CheckPayload(descriptor.PayloadType, descriptor.PayloadLength, violations);
        }

        return violations;
    }

    /// <summary>Fails the current test with every violation of <see cref="Validate"/>, if there are any.</summary>
    /// <param name="descriptor">The descriptor to check.</param>
    /// <param name="ownerBlock">The block the declaring system may use.</param>
    public static void Check(EventDescriptor descriptor, OpKindBlock ownerBlock)
    {
        IReadOnlyList<string> violations = Validate(descriptor, ownerBlock);
        if (violations.Count > 0)
        {
            Assert.Fail($"Event descriptor for {descriptor.Kind} is invalid: " + string.Join("; ", violations));
        }
    }

    /// <summary>The kinds in <paramref name="kinds"/> that have no descriptor bound in <paramref name="log"/> (<see cref="EventKind.None"/> is skipped).</summary>
    /// <param name="log">The log of the world under test.</param>
    /// <param name="kinds">The kinds to look up.</param>
    /// <returns>The undeclared kinds, in the order given.</returns>
    public static IReadOnlyList<EventKind> FindUndeclared(EventLog log, IEnumerable<EventKind> kinds)
    {
        var missing = new List<EventKind>();
        foreach (EventKind kind in kinds)
        {
            if (kind != EventKind.None && log.DescriptorFor(kind) is null)
            {
                missing.Add(kind);
            }
        }

        return missing;
    }

    /// <summary>The kinds among every declared <see cref="EventKind"/> enum member with no descriptor bound in <paramref name="log"/>.</summary>
    /// <param name="log">The log of the world under test.</param>
    /// <returns>The undeclared kinds.</returns>
    public static IReadOnlyList<EventKind> FindUndeclared(EventLog log) => FindUndeclared(log, Enum.GetValues<EventKind>());

    /// <summary>Fails the current test naming every declared <see cref="EventKind"/> member that has no descriptor bound in <paramref name="log"/>.</summary>
    /// <param name="log">The log of a full-profile world.</param>
    public static void AssertAllDeclared(EventLog log)
    {
        IReadOnlyList<EventKind> missing = FindUndeclared(log);
        if (missing.Count > 0)
        {
            Assert.Fail("EventKind members with no EventDescriptor bound: " + string.Join(", ", missing));
        }
    }
}
