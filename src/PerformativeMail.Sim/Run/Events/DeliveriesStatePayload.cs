// M-1a STUB — replaced by Run Structure at M1 (no GDD)
using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Run.Events;

/// <summary>
/// <see cref="RunEventKinds.DeliveriesState"/> (24 B, padded to 8-byte alignment): the shift stub's state after a change. Named
/// <c>…Payload</c> because Mail already has a <c>DeliveriesState</c> store class.
/// </summary>
/// <example><code>var p = entry.PayloadAs&lt;DeliveriesStatePayload&gt;(); bool open = p.Open != 0;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DeliveriesStatePayload
{
    /// <summary>The tick deliveries close (or closed).</summary>
    public uint CloseTick;

    /// <summary>The tick the Delivery phase ends, set at Delivery start (0 before).</summary>
    public uint DeadlineTick;

    /// <summary>The unlocked-district mask.</summary>
    public ulong UnlockedMask;

    /// <summary>The <see cref="ShiftPhase"/> value.</summary>
    public byte Phase;

    /// <summary>1 when deliveries are open.</summary>
    public byte Open;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad0;

    /// <summary>Explicit padding, zero.</summary>
    public uint Pad1;
}
