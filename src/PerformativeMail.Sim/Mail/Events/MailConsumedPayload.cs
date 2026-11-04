using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// <see cref="MailEventKinds.MailConsumed"/> (16 B, Internal; ADR-0017 section 9; ADR-0008 padding-free). TD-208 item 6, approved by Evan
/// 2026-11-04 9:23 AM PT by card click (card t271s1234, listing t271s1233), in the listing's words: "an internal note for each letter.
/// It's never sent over the network. The message id is 0x0102, and its 16 bytes hold the letter id, cents, result, whether it was late,
/// whether it was dead, and the player." The struct, field names and order are uncredited (Mail Lead planning detail, TD-208 item 6).
/// </summary>
/// <example><code>var p = entry.PayloadAs&lt;MailConsumedPayload&gt;(); long cents = p.Cents;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MailConsumedPayload
{
    /// <summary>The unit's <c>MailId</c> value.</summary>
    public uint MailId;

    /// <summary>Pay (positive) or penalty (negative), cents.</summary>
    public long Cents;

    /// <summary>The <see cref="EvalResult"/> value.</summary>
    public byte Result;

    /// <summary>1 when the unit was late (not dead), else 0.</summary>
    public byte Late;

    /// <summary>1 when the unit was dead, else 0.</summary>
    public byte Dead;

    /// <summary>The acting player's <c>PlayerSlot</c> value.</summary>
    public byte Actor;
}
