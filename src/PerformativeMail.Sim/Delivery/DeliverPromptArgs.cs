using System.Buffers.Binary;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>The Deliver prompt's branch byte (offset 0 of the <see cref="DeliverPromptArgs"/> layout); 0 means nothing was written.</summary>
public enum DeliverPromptBranch : byte
{
    /// <summary>Nothing written (all-zero bytes).</summary>
    None = 0,

    /// <summary>MATCH (MD3 step 2).</summary>
    Match = 1,

    /// <summary>MISDELIVER_ONE (MD3 step 3).</summary>
    MisdeliverOne = 2,

    /// <summary>REJECT: the destination is CLOSED or LOCKED (MD2 rows 4 and 5, MD3 step 1).</summary>
    Reject = 3,

    /// <summary>NOTHING (MD2 row 6, MD3 step 4).</summary>
    Nothing = 4,

    /// <summary>UNAVAILABLE: MD2 rows 1 to 3 (panel open, swim depth, an own pending op).</summary>
    Unavailable = 5,
}

/// <summary>The Deliver prompt's flags byte (offset 1).</summary>
[Flags]
public enum DeliverPromptFlags : byte
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>MISDELIVER_ONE: the wallet would land below the floor ("Can't afford a wrong house").</summary>
    FloorRefusal = 1,

    /// <summary>MISDELIVER_ONE: the type does not accept the selected kind ("Won't fit here"; the hold is blocked).</summary>
    WontFit = 2,

    /// <summary>The branch is still settling (the MD3 tracker has not reached <c>branchSettleTicks</c>).</summary>
    Settling = 4,
}

/// <summary>
/// The one definition of the Deliver prompt's 32 B <see cref="PromptArgs"/> layout (manual-delivery story 010 AC-3; ADR-0014 section 7
/// "owner-supplied, 32 B"). All values little-endian; every byte not listed is 0:
/// <list type="table">
/// <item><term>0</term><description>u8 <see cref="Branch"/></description></item>
/// <item><term>1</term><description>u8 <see cref="Flags"/></description></item>
/// <item><term>2-5</term><description><see cref="Key"/>: namespace u8, reserved u8, index u16</description></item>
/// <item><term>6-9</term><description><see cref="Address"/> (world namespace), same encoding</description></item>
/// <item><term>10-11</term><description>u16 <see cref="Units"/></description></item>
/// <item><term>12-15</term><description>i32 <see cref="PayCents"/></description></item>
/// <item><term>16-19</term><description>i32 <see cref="PenaltyCents"/></description></item>
/// <item><term>20-31</term><description>reserved</description></item>
/// </list>
/// <see cref="Write"/> and <see cref="TryRead"/> are the only code that touches these offsets; the HUD decodes through
/// <see cref="TryRead"/>. Values only: the Sim never formats a string (MD10).
/// </summary>
/// <param name="Branch">The branch.</param>
/// <param name="Flags">The flags.</param>
/// <param name="Key">The row's key, a <see cref="DeliverPromptKeys"/> id.</param>
/// <param name="Address">The address argument: a world display <see cref="StringId"/>, or none.</param>
/// <param name="Units">The unit count argument.</param>
/// <param name="PayCents">The predicted pay, integer cents.</param>
/// <param name="PenaltyCents">The predicted penalty, integer cents.</param>
/// <example>
/// <code>
/// DeliverPromptArgs.Write(new DeliverPromptArgs(DeliverPromptBranch.Nothing, DeliverPromptFlags.None, DeliverPromptKeys.Nothing, address, 0, 0, 0), ref args);
/// bool ok = DeliverPromptArgs.TryRead(in args, out DeliverPromptArgs back);
/// </code>
/// </example>
public readonly record struct DeliverPromptArgs(
    DeliverPromptBranch Branch, DeliverPromptFlags Flags, StringId Key, StringId Address, ushort Units, int PayCents, int PenaltyCents)
{
    private const int BranchAt = 0;
    private const int FlagsAt = 1;
    private const int KeyAt = 2;
    private const int AddressAt = 6;
    private const int UnitsAt = 10;
    private const int PayAt = 12;
    private const int PenaltyAt = 16;
    private const int ReservedAt = 20;
    private const DeliverPromptFlags KnownFlags = DeliverPromptFlags.FloorRefusal | DeliverPromptFlags.WontFit | DeliverPromptFlags.Settling;

    /// <summary>Zeroes <paramref name="args"/> and writes <paramref name="value"/> in the layout. Allocation-free.</summary>
    /// <param name="value">The prompt values.</param>
    /// <param name="args">The destination bytes.</param>
    public static void Write(in DeliverPromptArgs value, ref PromptArgs args)
    {
        Span<byte> b = args;
        b.Clear();
        b[BranchAt] = (byte)value.Branch;
        b[FlagsAt] = (byte)value.Flags;
        WriteId(b.Slice(KeyAt), value.Key);
        WriteId(b.Slice(AddressAt), value.Address);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(UnitsAt), value.Units);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(PayAt), value.PayCents);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(PenaltyAt), value.PenaltyCents);
    }

    /// <summary>
    /// Reads the layout back. False (and <c>default</c>) for bytes no <see cref="Write"/> produces: a branch outside 1 to 5, an unknown
    /// flag, a non-zero reserved byte or a key outside <see cref="StringNamespace.Reason"/> (so all-zero bytes, "nothing written", read
    /// as false). Allocation-free.
    /// </summary>
    /// <param name="args">The prompt bytes.</param>
    /// <param name="value">The values when true.</param>
    /// <returns>True when the bytes are a Deliver prompt.</returns>
    public static bool TryRead(in PromptArgs args, out DeliverPromptArgs value)
    {
        ReadOnlySpan<byte> b = args;
        var branch = (DeliverPromptBranch)b[BranchAt];
        var flags = (DeliverPromptFlags)b[FlagsAt];
        StringId key = ReadId(b.Slice(KeyAt));
        if (branch < DeliverPromptBranch.Match || branch > DeliverPromptBranch.Unavailable || (flags & ~KnownFlags) != 0
            || b[KeyAt + 1] != 0 || b[AddressAt + 1] != 0 || b.Slice(ReservedAt).IndexOfAnyExcept((byte)0) >= 0
            || key.Ns != StringNamespace.Reason)
        {
            value = default;
            return false;
        }

        value = new DeliverPromptArgs(
            branch,
            flags,
            key,
            ReadId(b.Slice(AddressAt)),
            BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(UnitsAt)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(PayAt)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(PenaltyAt)));
        return true;
    }

    private static void WriteId(Span<byte> dst, StringId id)
    {
        dst[0] = (byte)id.Ns;
        dst[1] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(2), id.Index);
    }

    private static StringId ReadId(ReadOnlySpan<byte> src) => new((StringNamespace)src[0], BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(2)));
}
