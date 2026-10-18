using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The identity of an interaction anchor: the owning entity plus a slot byte (ADR-0014 section 1). Totally ordered by
/// <see cref="EntityId.Value"/> then <see cref="Slot"/>, which is the focus tie-break (lowest entity, then lowest slot).
/// </summary>
/// <param name="Entity">The owning entity (a destination, container, seat...).</param>
/// <param name="Slot">Which anchor of that entity (0 for the first).</param>
/// <example>
/// <code>
/// var a = new AnchorRef(new EntityId(16_777_217u), 0);
/// int order = a.CompareTo(new AnchorRef(new EntityId(16_777_217u), 1));   // -1
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AnchorRef(EntityId Entity, byte Slot) : IComparable<AnchorRef>
{
    /// <summary>The "no anchor" value (<c>default</c>).</summary>
    public static AnchorRef None => default;

    /// <summary>True when <see cref="Entity"/> is <see cref="EntityId.None"/>; such a ref cannot be registered.</summary>
    public bool IsNone => Entity.IsNone;

    /// <summary>Orders by entity value, then slot.</summary>
    /// <param name="other">The other ref.</param>
    /// <returns>Negative, zero or positive.</returns>
    /// <example><code>int c = a.CompareTo(b);</code></example>
    public int CompareTo(AnchorRef other)
    {
        int c = Entity.CompareTo(other.Entity);
        return c != 0 ? c : Slot.CompareTo(other.Slot);
    }
}
