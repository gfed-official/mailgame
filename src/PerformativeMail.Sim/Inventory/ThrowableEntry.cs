using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// What Throwing's <c>ThrowRelease</c> commit reads of the entry it launches (throwing-catching story 010; GDD T2 "what launches"):
/// whether it is mail, its def ordinal (the mail kind's or the item's, <c>ThrowLaunched.kindDef</c>), its weight class (F1's
/// <c>baseSpeed[class]</c>) and that class's weight points (<c>classPts</c>, <c>inventory.weightPoints.*</c>: 1 / 3 / 8 shipped).
/// Returned by <see cref="IInventoryApi.TryReadThrowable"/>; <see cref="DetachResult"/> stays the three-field shape its callers compare.
/// </summary>
/// <param name="IsMail">True for a mail entry, false for an item.</param>
/// <param name="KindDef">The entry's mail-kind or item def ordinal.</param>
/// <param name="WeightClass">The def's weight class.</param>
/// <param name="ClassPts">The weight points of <paramref name="WeightClass"/>.</param>
/// <example><code>if (inventory.TryReadThrowable(entry, out ThrowableEntry t)) { int pts = t.ClassPts; }</code></example>
public readonly record struct ThrowableEntry(bool IsMail, ushort KindDef, WeightClass WeightClass, int ClassPts);
