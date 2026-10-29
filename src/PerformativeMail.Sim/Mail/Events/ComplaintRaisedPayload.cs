using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// <see cref="MailEventKinds.ComplaintRaised"/> (8 B, ADR-0017 section 9; ADR-0008 padding-free): the door the complaint is filed at
/// (the GDD's <c>mailboxId</c>), the amount in permille and its <see cref="ComplaintSource"/>. Never raised with an amount of 0
/// (M-R9-2a). Evan approved 2026-10-29 (TD-187 item 1).
/// </summary>
/// <example><code>var p = entry.PayloadAs&lt;ComplaintRaisedPayload&gt;(); int pm = p.AmountPermille;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ComplaintRaisedPayload
{
    /// <summary>The destination's <c>EntityId</c> value.</summary>
    public uint Destination;

    /// <summary>The complaint raised, in permille (1 to 65,535).</summary>
    public ushort AmountPermille;

    /// <summary>The <see cref="ComplaintSource"/> value.</summary>
    public byte Source;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;
}
