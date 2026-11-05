using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// <see cref="MailEventKinds.DeliveryOutcome"/> (34 B, ADR-0017 section 9; ADR-0008 padding-free, so <c>Pack = 1</c> in the ADR's field
/// order). TD-208 item 4, approved by Evan 2026-11-04 9:23 AM PT by card click (card t271s1234, listing t271s1233), in the listing's
/// words: "It's 34 bytes with 13 fields: number, house, player, how, result, mail kind, letters, cents, late letters, dead letters,
/// complaint, flags, and intended address. [...] Result 1 means the right house, and 2 means the wrong house. Flag 1 means it stopped at
/// the wallet floor, and flag 2 means cursed. Cents are positive pay at the right house and a negative penalty at the wrong one. The
/// intended address is filled in only for the wrong house, and is 0 otherwise." The struct and field names are uncredited (Mail Lead
/// planning detail, TD-208 item 4).
/// </summary>
/// <example><code>var p = entry.PayloadAs&lt;DeliveryOutcomePayload&gt;(); bool floor = (p.Flags &amp; DeliveryOutcomePayload.FlagStoppedAtFloor) != 0;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DeliveryOutcomePayload
{
    /// <summary><see cref="Flags"/> bit: the wallet floor stopped the misdelivery.</summary>
    public const byte FlagStoppedAtFloor = 1;

    /// <summary><see cref="Flags"/> bit: the entry was cursed.</summary>
    public const byte FlagCursed = 2;

    /// <summary>Mail's running outcome number, from 1 (<c>outcomeSeq</c>).</summary>
    public uint OutcomeSeq;

    /// <summary>The destination's <c>EntityId</c> value.</summary>
    public uint Destination;

    /// <summary>The acting player's <c>PlayerSlot</c> value.</summary>
    public byte Actor;

    /// <summary>The <see cref="DeliveryVia"/> value.</summary>
    public byte Via;

    /// <summary>The <see cref="EvalResult"/> value: 1 Accept, 2 Misdeliver.</summary>
    public byte Result;

    /// <summary>The mail kind's <c>DefIndex</c> value.</summary>
    public ushort Kind;

    /// <summary>Units consumed (0 for a floor stop).</summary>
    public ushort Units;

    /// <summary>Pay (positive) or penalty (negative), cents.</summary>
    public long Cents;

    /// <summary>Consumed units that were late (not dead).</summary>
    public ushort LateUnits;

    /// <summary>Consumed units that were dead.</summary>
    public ushort DeadUnits;

    /// <summary>The complaint this outcome raised, permille (saturating).</summary>
    public ushort ComplaintPermille;

    /// <summary><see cref="FlagStoppedAtFloor"/>, <see cref="FlagCursed"/>.</summary>
    public byte Flags;

    /// <summary>The entry's own <c>AddressId</c> value on a misdelivery; 0 otherwise.</summary>
    public uint IntendedAddress;
}
