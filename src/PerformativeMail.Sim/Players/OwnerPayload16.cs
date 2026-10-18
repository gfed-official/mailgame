using System.Runtime.CompilerServices;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The 16 opaque bytes an anchor's owner stores in the <see cref="InteractionLatch"/> at press (for example Manual Delivery's
/// branch kind, <c>selectedEntryId</c> and slot). Players never reads them. A fixed inline array: no allocation, blittable.
/// </summary>
/// <example><code>latch.OwnerPayload[0] = 3;   // the owner's branch kind</code></example>
[InlineArray(Size)]
public struct OwnerPayload16
{
    /// <summary>The size in bytes.</summary>
    public const int Size = 16;

#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private byte _e0;
#pragma warning restore CS0169
}
