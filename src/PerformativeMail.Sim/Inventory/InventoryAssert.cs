using System.Diagnostics;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// Grid Inventory's debug-time invariant checks (story grid-inventory/002). <c>[Conditional("INVENTORY_ASSERT")]</c>, which
/// <c>Directory.Build.targets</c> defines together with <c>SIM_ASSERT</c>, so a trip throws the same <see cref="SimAssertException"/>
/// with a <see cref="SimAssertCode"/> and Release builds compile the call (and its argument evaluation) out. Arguments must be pure.
/// Stateless.
/// </summary>
/// <example>
/// <code>
/// InventoryAssert.That(count >= 1, SimAssertCode.InventoryCountZero);
/// </code>
/// </example>
internal static class InventoryAssert
{
    /// <summary>Throws <see cref="SimAssertException"/> if <paramref name="condition"/> is false (INVENTORY_ASSERT builds only).</summary>
    /// <param name="condition">The invariant; side-effect free.</param>
    /// <param name="code">Assert code carried by the exception.</param>
    /// <exception cref="SimAssertException"><paramref name="condition"/> is false.</exception>
    [Conditional("INVENTORY_ASSERT")]
    public static void That(bool condition, ushort code)
    {
        if (!condition)
        {
            throw new SimAssertException(code);
        }
    }
}
