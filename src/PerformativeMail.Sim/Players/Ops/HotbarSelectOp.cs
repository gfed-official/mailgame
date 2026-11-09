using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Players.Ops;

/// <summary>
/// Payload of <see cref="PlayerOpKinds.HotbarSelect"/> (4 B): the hotbar slot index the client selected. Only 0 to 7 is valid (the hotbar
/// is 8 x 1; index 0 is the hands slot); any other value is <c>bad_request</c>.
/// </summary>
/// <example><code>var p = new HotbarSelectPayload { Index = 3 };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HotbarSelectPayload
{
    /// <summary>The selected hotbar index, 0 to 7.</summary>
    public uint Index;
}

/// <summary>The descriptor of <see cref="PlayerOpKinds.HotbarSelect"/>.</summary>
public static class HotbarSelectOp
{
    /// <summary>The number of hotbar slots: a valid index is below this.</summary>
    public const int SlotCount = 8;

    /// <summary>
    /// <see cref="PlayerOpKinds.HotbarSelect"/>: 4 B; its only reject is Core's <c>bad_request</c> (8), so it declares no reason.
    /// Fixed-length wire codec <see cref="CodecId"/> (WP-F: the client's hotbar keys send it).
    /// </summary>
    public static OpDescriptor Descriptor { get; } = OpDescriptor.For<HotbarSelectPayload>(PlayerOpKinds.HotbarSelect, false, CodecId, 4);

    /// <summary>Codec id of the <see cref="PlayerOpKinds.HotbarSelect"/> wire codec (WP-F).</summary>
    public const ushort CodecId = 13;
}
