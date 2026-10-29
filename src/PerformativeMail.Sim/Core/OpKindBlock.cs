using System.Collections.Immutable;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// An inclusive range of <see cref="OpKind"/> values owned by one system (ADR-0007 section 4). Later systems
/// take 64-wide blocks from 0x01C0 by amending the ADR and appending a block here.
/// </summary>
/// <example>
/// <code>
/// bool ok = OpKindBlock.Players.Contains((OpKind)0x0041);   // true
/// </code>
/// </example>
public readonly record struct OpKindBlock
{
    /// <summary>Creates a block.</summary>
    /// <param name="name">Owning system, for diagnostics.</param>
    /// <param name="first">First value in the block.</param>
    /// <param name="last">Last value in the block (inclusive).</param>
    public OpKindBlock(string name, ushort first, ushort last)
    {
        Name = name;
        First = first;
        Last = last;
    }

    /// <summary>Owning system, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>First value in the block.</summary>
    public ushort First { get; }

    /// <summary>Last value in the block (inclusive).</summary>
    public ushort Last { get; }

    /// <summary>Sim Core: 0x0001 to 0x003F.</summary>
    public static OpKindBlock Core => new("SimCore", 0x0001, 0x003F);

    /// <summary>Players and movement: 0x0040 to 0x007F.</summary>
    public static OpKindBlock Players => new("Players", 0x0040, 0x007F);

    /// <summary>Inventory: 0x0080 to 0x00FF.</summary>
    public static OpKindBlock Inventory => new("Inventory", 0x0080, 0x00FF);

    /// <summary>Mail and delivery: 0x0100 to 0x013F.</summary>
    public static OpKindBlock MailDelivery => new("MailDelivery", 0x0100, 0x013F);

    /// <summary>Throwing: 0x0140 to 0x017F.</summary>
    public static OpKindBlock Throwing => new("Throwing", 0x0140, 0x017F);

    /// <summary>Economy: 0x0180 to 0x01BF (ADR-0007 section 4 amendment 2026-10-28; first kind <c>WalletChanged</c> 0x0180, mail-destinations story 005).</summary>
    public static OpKindBlock Economy => new("Economy", 0x0180, 0x01BF);

    /// <summary>Every defined block in ascending, disjoint order. Append new blocks here when ADR-0007 section 4 is amended.</summary>
    /// <remarks>Backed by a separate holder type: a static <c>ImmutableArray&lt;OpKindBlock&gt;</c> field on the struct itself fails to load on the .NET 8 runtime.</remarks>
    public static ImmutableArray<OpKindBlock> All => OpKindBlockTable.Blocks;

    /// <summary>Finds the block that owns <paramref name="kind"/>.</summary>
    /// <param name="kind">The kind to locate.</param>
    /// <param name="block">The owning block, or <c>default</c> when none contains the kind (including <see cref="OpKind.None"/>).</param>
    /// <returns>True when a block in <see cref="All"/> contains the kind.</returns>
    /// <example>
    /// <code>
    /// if (OpKindBlock.TryOwnerOf(op.Kind, out OpKindBlock owner)) { Log(owner.Name); }
    /// </code>
    /// </example>
    public static bool TryOwnerOf(OpKind kind, out OpKindBlock block)
    {
        foreach (OpKindBlock candidate in All)
        {
            if (candidate.Contains(kind))
            {
                block = candidate;
                return true;
            }
        }

        block = default;
        return false;
    }

    /// <summary>True when <paramref name="kind"/> lies inside this block.</summary>
    /// <param name="kind">The kind to test.</param>
    /// <returns>True when <c>First &lt;= kind &lt;= Last</c>.</returns>
    public bool Contains(OpKind kind) => (ushort)kind >= First && (ushort)kind <= Last;
}

/// <summary>Holds the ascending block table (see <see cref="OpKindBlock.All"/>).</summary>
internal static class OpKindBlockTable
{
    /// <summary>The blocks, ascending and disjoint.</summary>
    public static readonly ImmutableArray<OpKindBlock> Blocks = ImmutableArray.Create(
        OpKindBlock.Core, OpKindBlock.Players, OpKindBlock.Inventory, OpKindBlock.MailDelivery, OpKindBlock.Throwing, OpKindBlock.Economy);
}
