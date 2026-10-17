using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The one immutable content set a <c>SimWorld</c> is built from (ADR-0010 section 4): the <see cref="SimSettings"/>, the
/// <see cref="BalanceTable"/> with the typed owner records built once from it, and one <see cref="DefTable{T}"/> per def
/// type in ordinal <c>DefIndex</c> order. It is held for the process lifetime and passed by reference (never reached
/// through a static, ADR-0003 section 4); systems take the set or one sub-table by constructor and may cache sub-tables
/// in <c>readonly</c> fields. A set is never swapped into a running world. The constructor is internal: the only ways to
/// obtain one are <see cref="BuiltInContent.CreateM1a"/> and (M0) the loader, both of which run the stage 4 to 5 rules
/// and release the candidate only through <c>ValidationResult.AcceptedSet</c>. Immutable; safe from any thread.
/// </summary>
/// <example>
/// <code>
/// ContentSet content = BuiltInContent.CreateM1a();
/// ref readonly MailKindDef letter = ref content.MailKinds[DefIndex.FromValue(1)];
/// int walk = content.Player.WalkMmPerSec;   // a system receives only its own record
/// </code>
/// </example>
public sealed class ContentSet
{
    internal ContentSet(
        BalanceTable balance,
        ImmutableArray<StatBounds> resolvedStatBounds,
        DefTable<ContainerDef> containers,
        DefTable<ItemDef> items,
        DefTable<MailKindDef> mailKinds,
        DefTable<DestinationTypeDef> destinationTypes,
        DefTable<MailMixDef> mailMixes)
    {
        Balance = balance;
        Settings = SimSettings.FromTable(balance);
        Player = PlayerBalance.FromTable(balance);
        Inventory = InventoryBalance.FromTable(balance);
        Mail = MailBalance.FromTable(balance);
        Throw = ThrowBalance.FromTable(balance);
        ResolvedStatBounds = resolvedStatBounds;
        Containers = containers;
        Items = items;
        MailKinds = mailKinds;
        DestinationTypes = destinationTypes;
        MailMixes = mailMixes;
    }

    /// <summary>The simulation settings (<c>sim.*</c>).</summary>
    public SimSettings Settings { get; }

    /// <summary>Every balance value, addressed by <see cref="BalanceKey"/>.</summary>
    public BalanceTable Balance { get; }

    /// <summary>The <c>player.*</c> record (Player Character).</summary>
    public PlayerBalance Player { get; }

    /// <summary>The <c>inventory.*</c> record (Grid Inventory).</summary>
    public InventoryBalance Inventory { get; }

    /// <summary>The <c>mail.*</c> record (Mail and Destinations).</summary>
    public MailBalance Mail { get; }

    /// <summary>The <c>throw.*</c> record (Throwing and Catching; the epic's "ThrowDef").</summary>
    public ThrowBalance Throw { get; }

    /// <summary>The resolved clamp bounds of every stat, resolved once per set (<c>StatDefs.ResolveBounds</c>), in <see cref="StatDefs.Count"/> order.</summary>
    public ImmutableArray<StatBounds> ResolvedStatBounds { get; }

    /// <summary>The containers.</summary>
    public DefTable<ContainerDef> Containers { get; }

    /// <summary>The items.</summary>
    public DefTable<ItemDef> Items { get; }

    /// <summary>The mail kinds.</summary>
    public DefTable<MailKindDef> MailKinds { get; }

    /// <summary>The destination types.</summary>
    public DefTable<DestinationTypeDef> DestinationTypes { get; }

    /// <summary>The mail mixes.</summary>
    public DefTable<MailMixDef> MailMixes { get; }
}
