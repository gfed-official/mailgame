using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// An item type (spec 08 section 2.1, CD10): tools, materials, consumables, ammo. Mail kinds are
/// <see cref="MailKindDef"/>. Units are stored already scaled to integers (money in cents). The spec's
/// <c>tool</c>/<c>weapon</c>/<c>consumable</c> blocks have no M-1a reader; their first consumer adds them as optional
/// leaves. <c>standardIssue</c> is deliberately not a field (CD10).
/// </summary>
/// <example>
/// <code>
/// var axe = new ItemDef
/// {
///     Id = "axe", Name = "item.axe", Category = ItemCategory.Tool, GridW = 1, GridH = 2,
///     MaxStack = 1, WeightClass = WeightClass.Light, SellPriceCents = 25,
/// };
/// </code>
/// </example>
public sealed record class ItemDef : IDef
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Localisation key.</summary>
    public required string Name { get; init; }

    /// <summary>Item category (required).</summary>
    public required ItemCategory Category { get; init; }

    /// <summary>Footprint width in cells (required).</summary>
    public required int GridW { get; init; }

    /// <summary>Footprint height in cells (required).</summary>
    public required int GridH { get; init; }

    /// <summary>Most items per stack, at least 1 (required).</summary>
    public required int MaxStack { get; init; }

    /// <summary>Throw/weight class. Required with no default (CD7): a silent default would make a forgotten field throwable at light speed.</summary>
    public required WeightClass WeightClass { get; init; }

    /// <summary>Sell price in cents at the PO shop; 0 = not sellable (default 0).</summary>
    public long SellPriceCents { get; init; }

    /// <summary>True when the item is sold by the shop and <see cref="BuyPriceCents"/> is meaningful (default false: omitted).</summary>
    public bool HasBuyPrice { get; init; }

    /// <summary>Buy price in cents; read only when <see cref="HasBuyPrice"/> (default 0).</summary>
    public long BuyPriceCents { get; init; }

    /// <summary>True when the item can be carried in the hands (default true, CD10 / Grid R5).</summary>
    public bool CarryByHand { get; init; } = true;

    /// <summary>Equipment slot (default <see cref="Content.EquipSlot.None"/>).</summary>
    public EquipSlot EquipSlot { get; init; } = EquipSlot.None;

    /// <summary>Free-form tags (default empty).</summary>
    public ImmutableArray<string> Tags { get; init; } = ImmutableArray<string>.Empty;
}
