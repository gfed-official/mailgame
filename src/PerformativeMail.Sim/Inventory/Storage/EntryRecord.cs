using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Storage;

/// <summary>
/// One pooled inventory entry (GDD <c>grid-inventory.md</c> R2; ADR-0009 section 5; TR-inventory-003; story grid-inventory/002).
/// An entry occupies a <see cref="W"/> x <see cref="H"/> rectangle at top-left (<see cref="X"/>, <see cref="Y"/>) of the container in
/// <see cref="ContainerSlot"/>, optionally <see cref="Rotated"/> (the effective footprint is then <c>H x W</c>; square footprints are
/// stored unrotated, see <see cref="Footprint.Canonicalise"/>). It holds mail (<see cref="MailKind"/>, <see cref="AddressId"/>,
/// <see cref="Flags"/>, <see cref="MailIds"/>) or an item (<see cref="ItemId"/>); <see cref="IsMail"/> says which key is live, both
/// fields are always present. <see cref="Count"/> is 1 to 255 for a live record (a record driven to 0 is freed in the same call by the
/// caller). Sequential layout, fields ordered widest first so the struct is 32 bytes (guardrail: at most 40). A default value is a
/// free pool slot (<see cref="Id"/> none).
/// </summary>
/// <example>
/// <code>
/// ref EntryRecord e = ref pool[slot];
/// Footprint.Effective(e.W, e.H, e.Rotated, out int fw, out int fh);
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential)]
public struct EntryRecord
{
    /// <summary>The <see cref="ContainerSlot"/> value of an entry that is in no container yet (fresh from <see cref="EntryPool.TryAllocate"/>).</summary>
    public const ushort NoContainer = 0xFFFF;

    /// <summary>The entry's id (real, bit 31 clear); none while the pool slot is free.</summary>
    public EntryId Id;

    /// <summary>Mail key: the packed address (<c>AddressId.Pack</c>); 0 for an item.</summary>
    public uint AddressId;

    /// <summary>Intake arrival sequence (R2: Intake entries carry it); 0 elsewhere.</summary>
    public uint ArrivalSeq;

    /// <summary>The handle of the entry's ascending <c>mailIds[]</c> run in the mail-ids arena (story 003); default for an item.</summary>
    public MailIdsHandle MailIds;

    /// <summary>The <see cref="ContainerTable"/> slot of the holding container, or <see cref="NoContainer"/>.</summary>
    public ushort ContainerSlot;

    /// <summary>Mail key: the mail kind; 0 for an item.</summary>
    public ushort MailKind;

    /// <summary>Item key: the item def ordinal; 0 for mail.</summary>
    public ushort ItemId;

    /// <summary>Column of the top-left cell.</summary>
    public byte X;

    /// <summary>Row of the top-left cell.</summary>
    public byte Y;

    /// <summary>Unrotated footprint width (columns).</summary>
    public byte W;

    /// <summary>Unrotated footprint height (rows).</summary>
    public byte H;

    /// <summary>Stack count, 1 to 255 while live (written through <see cref="EntryPool.SetCount"/>, which asserts the range).</summary>
    public byte Count;

    /// <summary>Mail key: the mail flags; 0 for an item.</summary>
    public byte Flags;

    /// <summary>True when the footprint is placed rotated (effective <c>H x W</c>); always false for a square footprint.</summary>
    public bool Rotated;

    /// <summary>True when the mail key is live, false when the item key is.</summary>
    public bool IsMail;

    /// <summary>True when the record describes a live entry.</summary>
    public readonly bool IsLive => !Id.IsNone;
}

/// <summary>
/// A handle to an entry's <c>mailIds[]</c> run in the <see cref="MailIdArena"/> (story grid-inventory/003): the run's size class,
/// block, start unit and count packed into one <c>uint</c> as <c>classPlusOne</c> (4 bits, 1 to 9), <c>block</c> (12 bits),
/// <c>start</c> (8 bits) and <c>countMinusOne</c> (8 bits, count 1 to 256). <c>Raw</c> 0 is therefore never a live run:
/// <c>default</c> is "no run" (an item, or mail with no ids yet).
/// </summary>
/// <example>
/// <code>
/// var h = new MailIdsHandle(classIndex: 3, block: 7, start: 2, count: 5);   // units [2, 7) of block 7 in the 8-unit class
/// </code>
/// </example>
public readonly struct MailIdsHandle : IEquatable<MailIdsHandle>
{
    private const int ClassShift = 28;
    private const int BlockShift = 16;
    private const int StartShift = 8;
    private const uint BlockMask = 0xFFFu;
    private const uint ByteMask = 0xFFu;

    /// <summary>The packed handle; 0 is "no run".</summary>
    public readonly uint Raw;

    /// <summary>Packs a run. Fields outside their widths assert under <c>INVENTORY_ASSERT</c>; Release masks them.</summary>
    /// <param name="classIndex">The size class, 0 to 8.</param>
    /// <param name="block">The block in the class, 0 to 4095.</param>
    /// <param name="start">The first live unit of the block, 0 to 255.</param>
    /// <param name="count">The live unit count, 1 to 256.</param>
    public MailIdsHandle(int classIndex, int block, int start, int count)
    {
        InventoryAssert.That(
            classIndex >= 0 && classIndex < MailIdArena.ClassCount && block >= 0 && block <= (int)BlockMask && start >= 0 && start <= (int)ByteMask && count >= 1 && count <= MailIdArena.MaxUnits,
            SimAssertCode.InventoryArenaHandleInvalid);
        Raw = (((uint)(classIndex + 1) & 0xFu) << ClassShift)
            | (((uint)block & BlockMask) << BlockShift)
            | (((uint)start & ByteMask) << StartShift)
            | ((uint)(count - 1) & ByteMask);
    }

    /// <summary>True when the handle is <c>default</c> (no run).</summary>
    public bool IsNone => Raw == 0u;

    /// <summary>The size class (0 to 8); -1 for none.</summary>
    public int ClassIndex => (int)(Raw >> ClassShift) - 1;

    /// <summary>The block in the class (0 to 4095); 0 for none.</summary>
    public int Block => (int)((Raw >> BlockShift) & BlockMask);

    /// <summary>The first live unit of the block (0 to 255); 0 for none.</summary>
    public int Start => (int)((Raw >> StartShift) & ByteMask);

    /// <summary>The live unit count (1 to 256); 0 for none.</summary>
    public int Count => IsNone ? 0 : (int)(Raw & ByteMask) + 1;

    /// <inheritdoc />
    public bool Equals(MailIdsHandle other) => Raw == other.Raw;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MailIdsHandle other && Raw == other.Raw;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Raw);

    /// <summary>Equality on <see cref="Raw"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(MailIdsHandle left, MailIdsHandle right) => left.Raw == right.Raw;

    /// <summary>Inequality on <see cref="Raw"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(MailIdsHandle left, MailIdsHandle right) => left.Raw != right.Raw;
}

/// <summary>
/// Footprint rules (GDD R2): rotation is a property of the placement, a square footprint canonicalises to unrotated, and the effective
/// (grid-space) footprint of a rotated placement is <c>h x w</c>. Pure integer helpers; stateless.
/// </summary>
/// <example>
/// <code>
/// bool rotated = true;
/// Footprint.Canonicalise(2, 2, ref rotated);              // rotated == false
/// Footprint.Effective(1, 2, true, out int fw, out int fh); // 2 x 1
/// </code>
/// </example>
public static class Footprint
{
    /// <summary>Clears <paramref name="rotated"/> when the footprint is square (rotation of a square is the identity).</summary>
    /// <param name="w">Unrotated width.</param>
    /// <param name="h">Unrotated height.</param>
    /// <param name="rotated">The requested rotation; forced to false for a square.</param>
    public static void Canonicalise(byte w, byte h, ref bool rotated)
    {
        if (w == h)
        {
            rotated = false;
        }
    }

    /// <summary>The grid-space footprint: <c>w x h</c> unrotated, <c>h x w</c> rotated.</summary>
    /// <param name="w">Unrotated width.</param>
    /// <param name="h">Unrotated height.</param>
    /// <param name="rotated">The placement's rotation.</param>
    /// <param name="fw">Effective width (columns covered).</param>
    /// <param name="fh">Effective height (rows covered).</param>
    public static void Effective(byte w, byte h, bool rotated, out int fw, out int fh)
    {
        if (rotated)
        {
            fw = h;
            fh = w;
        }
        else
        {
            fw = w;
            fh = h;
        }
    }
}
