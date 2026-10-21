using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// Inventory's 24 B reply extra (ADR-0009 section 4 and Risks; story grid-inventory/004), written into ADR-0007's <see cref="Core.Reply.Extra"/>
/// with <see cref="Core.Reply.Ok{T}"/> / <see cref="Core.Reply.SetExtra{T}"/> and read back with <see cref="Core.Reply.ExtraAs{T}"/>.
/// <c>LayoutKind.Explicit</c> with fixed offsets 0, 4, 8, 12, 14, 16, 18, 20, 22, 23 (a sizeof + offset test pins them); its fields are
/// never passed by <c>ref</c> (no unaligned refs on a future ARM export). <c>A</c>/<c>B</c> are the op's primary source and destination
/// containers. Unit and entry counts are display-only and saturate at 65,535: set them through <see cref="Saturate"/> or the setters.
/// </summary>
/// <example>
/// <code>
/// var extra = new InvReplyExtra { ResultVersionA = 3, ResultVersionB = 9 };
/// extra.SetUnits(requested: 70_000, got: 12);   // RequestedUnits == 65_535
/// return Reply.Ok(in op, in extra);
/// </code>
/// </example>
[StructLayout(LayoutKind.Explicit, Size = SizeBytes)]
public struct InvReplyExtra
{
    /// <summary>Total size in bytes; equals <see cref="Core.ReplyExtra.Size"/>.</summary>
    public const int SizeBytes = 24;

    /// <summary>The saturation ceiling of every u16 count.</summary>
    public const int CountCeiling = ushort.MaxValue;

    /// <summary>The cause bits (0-2) of <see cref="CauseAndFlags"/> (an <see cref="Events.InvCause"/>).</summary>
    public const byte CauseMask = 0x07;

    /// <summary>The <c>unchanged</c> flag (bit 3) of <see cref="CauseAndFlags"/>: the call changed no container (a no-op).</summary>
    public const byte FlagUnchanged = 0x08;

    /// <summary>The <c>appliedModeSize</c> flag (bit 4) of <see cref="CauseAndFlags"/> (the sort story's).</summary>
    public const byte FlagAppliedModeSize = 0x10;

    /// <summary>Resulting version of the primary source container (current version on a reject).</summary>
    [FieldOffset(0)]
    public uint ResultVersionA;

    /// <summary>Resulting version of the primary destination container.</summary>
    [FieldOffset(4)]
    public uint ResultVersionB;

    /// <summary>The actor credited with the change (raw <c>ActorId</c>).</summary>
    [FieldOffset(8)]
    public uint ByActorId;

    /// <summary>Units requested (saturating).</summary>
    [FieldOffset(12)]
    public ushort RequestedUnits;

    /// <summary>Units actually moved (saturating).</summary>
    [FieldOffset(14)]
    public ushort GotUnits;

    /// <summary>Entries requested (saturating).</summary>
    [FieldOffset(16)]
    public ushort RequestedEntries;

    /// <summary>Entries actually handled (saturating).</summary>
    [FieldOffset(18)]
    public ushort GotEntries;

    /// <summary>Entries skipped by a batch op (saturating).</summary>
    [FieldOffset(20)]
    public ushort SkippedEntries;

    /// <summary>Cause (3 bits: taken, sorted, consumed, expired, destroyed, system) and flags (unchanged, appliedModeSize).</summary>
    [FieldOffset(22)]
    public byte CauseAndFlags;

    /// <summary>Low byte of the first skipped entry's reason (batch ops).</summary>
    [FieldOffset(23)]
    public byte SkipReason;

    /// <summary>Clamps a count to the u16 field range: below 0 gives 0, above 65,535 gives 65,535.</summary>
    /// <param name="value">The count.</param>
    /// <returns>The saturated value.</returns>
    public static ushort Saturate(int value) => value <= 0 ? (ushort)0 : value >= CountCeiling ? ushort.MaxValue : (ushort)value;

    /// <summary>The cause carried in bits 0-2 of <see cref="CauseAndFlags"/>.</summary>
    public readonly Events.InvCause Cause => (Events.InvCause)(CauseAndFlags & CauseMask);

    /// <summary>True when the <see cref="FlagUnchanged"/> bit is set.</summary>
    public readonly bool Unchanged => (CauseAndFlags & FlagUnchanged) != 0;

    /// <summary>Writes the cause bits and the <c>unchanged</c> flag, keeping the other flags (story 005 plumbing; contested outcomes are stories 013/014).</summary>
    /// <param name="cause">The cause (masked to 3 bits).</param>
    /// <param name="unchanged">True when the call changed nothing.</param>
    public void SetCause(Events.InvCause cause, bool unchanged)
    {
        int keep = CauseAndFlags & ~(CauseMask | FlagUnchanged);
        CauseAndFlags = (byte)(keep | ((byte)cause & CauseMask) | (unchanged ? FlagUnchanged : 0));
    }

    /// <summary>Sets <see cref="RequestedUnits"/> and <see cref="GotUnits"/>, saturating.</summary>
    /// <param name="requested">Units requested.</param>
    /// <param name="got">Units moved.</param>
    public void SetUnits(int requested, int got)
    {
        RequestedUnits = Saturate(requested);
        GotUnits = Saturate(got);
    }

    /// <summary>Sets <see cref="RequestedEntries"/>, <see cref="GotEntries"/> and <see cref="SkippedEntries"/>, saturating.</summary>
    /// <param name="requested">Entries requested.</param>
    /// <param name="got">Entries handled.</param>
    /// <param name="skipped">Entries skipped.</param>
    public void SetEntries(int requested, int got, int skipped)
    {
        RequestedEntries = Saturate(requested);
        GotEntries = Saturate(got);
        SkippedEntries = Saturate(skipped);
    }
}
