using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The typed table of one def type, stored in <see cref="DefIndex"/> order (the def at ordinal
/// <c>i</c> is <c>this[DefIndex.FromValue(i)]</c>). Per-tick code looks defs up by
/// <see cref="DefIndex"/>; there is deliberately no string-keyed member (string ids are resolved
/// to a <see cref="DefIndex"/> once, at load, outside the tick). Immutable and therefore safe to
/// read from any thread. A <see cref="DefIndex"/> is only meaningful against the table built
/// from the same content; it is never persisted.
/// </summary>
/// <typeparam name="T">The def type (for example an item def).</typeparam>
/// <example>
/// <code>
/// DefAssignment ids = DefIdAssigner.Assign(new[] { "small", "large" });   // large 0, small 1
/// var table = new DefTable&lt;string&gt;(ImmutableArray.Create("L", "S"));    // in DefIndex order
/// ref readonly string def = ref table[DefIndex.FromValue(1)];            // "S"
/// ReadOnlySpan&lt;string&gt; hot = table.AsSpan();                            // hot-path iteration
/// </code>
/// </example>
public sealed class DefTable<T>
{
    private readonly ImmutableArray<T> _items;

    /// <summary>Wraps <paramref name="itemsInDefIndexOrder"/> (not copied).</summary>
    /// <param name="itemsInDefIndexOrder">The defs, element <c>i</c> being the def with ordinal <c>i</c>. A default (uninitialised) array is treated as empty. Asserts (SIM_ASSERT) when longer than 65 534.</param>
    public DefTable(ImmutableArray<T> itemsInDefIndexOrder)
    {
        _items = itemsInDefIndexOrder.IsDefault ? ImmutableArray<T>.Empty : itemsInDefIndexOrder;
        SimAssert.That(_items.Length <= DefIdAssigner.MaxDefsPerType, ContentAssertCode.DefTableTooLarge);
    }

    /// <summary>The number of defs; valid ordinals are 0 to <c>Count - 1</c>.</summary>
    public int Count => _items.Length;

    /// <summary>
    /// The def at <paramref name="index"/>. Indexing with <see cref="DefIndex.None"/> is a
    /// SIM_ASSERT failure; without SIM_ASSERT, None (and any ordinal at or above <see cref="Count"/>)
    /// throws <see cref="IndexOutOfRangeException"/> rather than returning a wrong def.
    /// </summary>
    /// <param name="index">The ordinal of the def.</param>
    public ref readonly T this[DefIndex index]
    {
        get
        {
            SimAssert.That(!index.IsNone, ContentAssertCode.DefTableNoneIndex);
            return ref _items.ItemRef(index.Value);
        }
    }

    /// <summary>The defs in <see cref="DefIndex"/> order, for hot-path iteration. Allocation free.</summary>
    /// <returns>A read-only view over the table.</returns>
    public ReadOnlySpan<T> AsSpan() => _items.AsSpan();
}
