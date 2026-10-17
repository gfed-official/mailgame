namespace PerformativeMail.Sim.Core;

// Read-only enumeration of the frozen descriptor tables, for the network adapter (ADR-0011 section 3: the adapter reads the
// descriptors to build its codec registry and the protocol hash; the Sim never encodes or decodes). Additive: no handler and
// no mutable state is exposed.
public sealed partial class SimWorld
{
    private readonly EventDescriptor[] _declaredEventDescriptors;

    /// <summary>
    /// Every declared op descriptor in ascending <see cref="OpKind"/> order, fixed at <c>Freeze()</c>. The span is a read-only view
    /// of the frozen table (<see cref="OpDescriptor"/> is immutable); no handler is reachable through it. Allocation-free.
    /// </summary>
    /// <example><code>foreach (OpDescriptor d in world.OpDescriptors) { registry.Bind(d); }</code></example>
    public ReadOnlySpan<OpDescriptor> OpDescriptors => _opDescriptors;

    /// <summary>
    /// Every declared event descriptor in ascending <see cref="EventKind"/> order (undeclared kinds are skipped), fixed at
    /// <c>Freeze()</c>. A read-only view of the frozen table (<see cref="EventDescriptor"/> is an immutable record). Allocation-free.
    /// </summary>
    /// <example><code>foreach (EventDescriptor d in world.EventDescriptors) { hash.Add(d); }</code></example>
    public ReadOnlySpan<EventDescriptor> EventDescriptors => _declaredEventDescriptors;

    // The builder's lookup is indexed by (ushort)EventKind with null holes; the public view is the dense, ascending list.
    private static EventDescriptor[] CompactEventDescriptors(EventDescriptor?[] lookup)
    {
        int count = 0;
        for (int i = 0; i < lookup.Length; i++)
        {
            if (lookup[i] is not null)
            {
                count++;
            }
        }

        var dense = new EventDescriptor[count];
        int next = 0;
        for (int i = 0; i < lookup.Length; i++)
        {
            EventDescriptor? d = lookup[i];
            if (d is not null)
            {
                dense[next++] = d;
            }
        }

        return dense;
    }
}
