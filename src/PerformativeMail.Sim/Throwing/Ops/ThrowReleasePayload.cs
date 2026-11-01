using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Payload of <see cref="ThrowOpKinds.ThrowRelease"/> (ADR-0015 section 1): 28 B, padding-free, in the ADR's field order (entryId 0,
/// units 4, side 5, flags 6, reserved 7, feet 8-19, dir 20-25, pad 26-27). There is no tick field and no hold duration; the
/// request's <c>seq</c> is the op's <c>ReqId</c> and the actor is <c>Op.Actor</c>. The wire codec (<c>ThrowReleaseCodec</c>, in the
/// adapter core) refuses a <see cref="Side"/> other than -1 or +1 and any non-zero <see cref="Flags"/>, <see cref="Reserved"/> or
/// <see cref="Pad"/> as <c>bad_request</c>.
/// </summary>
/// <example><code>var p = new ThrowReleasePayload { EntryId = entry, Units = 1, Side = 1, FeetX = x, FeetY = y, FeetZ = z, DirX = 0, DirY = 0, DirZ = 16384 };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ThrowReleasePayload
{
    /// <summary>The hotbar entry being thrown.</summary>
    public uint EntryId;

    /// <summary>Units thrown (must equal the entry's count; the handler's check).</summary>
    public byte Units;

    /// <summary>Throwing hand: -1 or +1.</summary>
    public sbyte Side;

    /// <summary>No bit is defined at M-1a: must be zero.</summary>
    public byte Flags;

    /// <summary>Reserved, must be zero.</summary>
    public byte Reserved;

    /// <summary>Claimed feet x, mm (Player Character P13 layout).</summary>
    public int FeetX;

    /// <summary>Claimed feet y, mm.</summary>
    public int FeetY;

    /// <summary>Claimed feet z, mm.</summary>
    public int FeetZ;

    /// <summary>Aim direction x, scale 16384.</summary>
    public short DirX;

    /// <summary>Aim direction y, scale 16384.</summary>
    public short DirY;

    /// <summary>Aim direction z, scale 16384.</summary>
    public short DirZ;

    /// <summary>Explicit padding, must be zero.</summary>
    public ushort Pad;
}
