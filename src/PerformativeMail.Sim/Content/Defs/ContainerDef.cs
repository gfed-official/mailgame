using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// A container type (Grid R1, CD10). <see cref="GridW"/> is columns and <see cref="GridH"/> is rows, following Grid R1
/// (spec 08 writes rows by columns; Grid wins). The grid is required for grid/slots layouts and absent for pile, so it
/// has a presence flag; the loader (story 018) enforces CV-GEN-14. <c>allowedCategories</c> omitted means any, which is
/// different from present-but-empty (an error, CV-GRD-03), so it too has a presence flag.
/// </summary>
/// <example>
/// <code>
/// var chest = new ContainerDef
/// {
///     Id = "chest", Layout = ContainerLayout.Grid, HasGrid = true, GridW = 8, GridH = 4,
///     BeltAccess = BeltAccess.InserterOnly,
/// };
/// </code>
/// </example>
public sealed record class ContainerDef : IDef
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Layout (required).</summary>
    public required ContainerLayout Layout { get; init; }

    /// <summary>True when <see cref="GridW"/>/<see cref="GridH"/> are present (grid/slots layouts; false for pile). Default false.</summary>
    public bool HasGrid { get; init; }

    /// <summary>Columns; read only when <see cref="HasGrid"/> (default 0).</summary>
    public int GridW { get; init; }

    /// <summary>Rows; read only when <see cref="HasGrid"/> (default 0).</summary>
    public int GridH { get; init; }

    /// <summary>View hint (default <see cref="ContainerView.Auto"/>: the runtime D7 rule decides).</summary>
    public ContainerView View { get; init; } = ContainerView.Auto;

    /// <summary>Belt access (default <see cref="Content.BeltAccess.None"/>).</summary>
    public BeltAccess BeltAccess { get; init; } = BeltAccess.None;

    /// <summary>True when <see cref="AllowedCategories"/> restricts the container; false = any category (default false).</summary>
    public bool HasAllowedCategories { get; init; }

    /// <summary>Allowed categories; read only when <see cref="HasAllowedCategories"/> (default empty).</summary>
    public ImmutableArray<ItemCategory> AllowedCategories { get; init; } = ImmutableArray<ItemCategory>.Empty;

    /// <summary>True when the container is carried in the hands (default false).</summary>
    public bool HandCarried { get; init; }

    /// <summary>False = no player, inserter or belt op may add an entry (default true, CD10).</summary>
    public bool AcceptsInsert { get; init; } = true;

    /// <summary>Slot indices reserved (slots layout only; default empty).</summary>
    public ImmutableArray<int> ReservedSlots { get; init; } = ImmutableArray<int>.Empty;
}
