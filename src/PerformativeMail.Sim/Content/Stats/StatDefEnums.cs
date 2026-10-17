namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The unit a stat is stored and read in (CD12 "Unit" column). Closed; values are never reused.
/// </summary>
/// <example>
/// <code>
/// StatUnit unit = StatDefs.Get(Stat.RespawnMs).Unit;   // StatUnit.Ms
/// </code>
/// </example>
public enum StatUnit : byte
{
    /// <summary>Permille (1000 = 1.0); the <c>pm</c> rows.</summary>
    Permille = 1,

    /// <summary>A plain count.</summary>
    Count = 2,

    /// <summary>Milliseconds.</summary>
    Ms = 3,
}

/// <summary>
/// Whose sheet a stat lives on (CD12 "Scope" column). Closed; values are never reused.
/// </summary>
/// <example>
/// <code>
/// StatScope scope = StatDefs.Get(Stat.MailMaxStack).Scope;   // StatScope.Team
/// </code>
/// </example>
public enum StatScope : byte
{
    /// <summary>The team sheet (shared by all players).</summary>
    Team = 1,

    /// <summary>A player's own sheet.</summary>
    Personal = 2,

    /// <summary>Either (a stat both sheets can carry, for example <c>InventoryRows</c>).</summary>
    Both = 3,
}

/// <summary>
/// What a stat is qualified by (CD12 "Qualifier" column): one value per qualifier def, or none. Closed; append-only
/// (later rows add <c>Building</c> and <c>Item</c>).
/// </summary>
/// <example>
/// <code>
/// QualifierKind q = StatDefs.Get(Stat.InventoryRows).Qualifier;   // QualifierKind.Container
/// </code>
/// </example>
public enum QualifierKind : byte
{
    /// <summary>Not qualified: one value per sheet.</summary>
    None = 1,

    /// <summary>Qualified by a <c>ContainerDef</c>.</summary>
    Container = 2,

    /// <summary>Qualified by a <c>MailKindDef</c>.</summary>
    MailKind = 3,
}

/// <summary>
/// The modifier ops a stat accepts (CD12 "Ops" column), as flags. Closed; append-only.
/// </summary>
/// <example>
/// <code>
/// bool canMul = (StatDefs.Get(Stat.InventoryRows).Ops &amp; StatOps.Mul) != 0;   // true
/// </code>
/// </example>
[Flags]
public enum StatOps : byte
{
    /// <summary>No ops (never a valid row).</summary>
    None = 0,

    /// <summary>Additive terms (the sum enters before the clamp).</summary>
    Add = 1,

    /// <summary>Multiplicative terms (permille factors).</summary>
    Mul = 2,

    /// <summary>A Set (an exact value; the stat's <see cref="SetResolve"/> picks between several).</summary>
    Set = 4,
}

/// <summary>
/// How several Set sources combine (CD12 "Set (min-wins)" / "Set (max-wins)"). Closed; values are never reused.
/// </summary>
/// <example>
/// <code>
/// SetResolve r = StatDefs.Get(Stat.RespawnMs).SetResolve;   // SetResolve.MinWins
/// </code>
/// </example>
public enum SetResolve : byte
{
    /// <summary>The stat has no Set op, so there is nothing to resolve.</summary>
    NotApplicable = 1,

    /// <summary>The smallest Set value wins.</summary>
    MinWins = 2,

    /// <summary>The largest Set value wins.</summary>
    MaxWins = 3,
}

/// <summary>
/// The single rounding step of F1 (CD12 "rounding"; CD11 formula): HalfUp is the default where CD12 names none.
/// Closed; values are never reused.
/// </summary>
/// <example>
/// <code>
/// StatRounding r = StatDefs.Get(Stat.InventoryRows).Rounding;   // StatRounding.Ceil
/// </code>
/// </example>
public enum StatRounding : byte
{
    /// <summary>Half away from zero, symmetric on magnitude (the default).</summary>
    HalfUp = 1,

    /// <summary>True floor division.</summary>
    Floor = 2,

    /// <summary>True ceiling division.</summary>
    Ceil = 3,
}

/// <summary>Where a stat's base value comes from. Closed; append-only.</summary>
/// <example>
/// <code>
/// BaseSourceKind k = StatDefs.Get(Stat.MailMaxStack).Base.Kind;   // BaseSourceKind.MailKindMaxStack
/// </code>
/// </example>
public enum BaseSourceKind : byte
{
    /// <summary>A code constant (<see cref="BaseSource.Value"/>).</summary>
    Constant = 1,

    /// <summary>A balance key, by name (<see cref="BaseSource.Key"/>).</summary>
    Balance = 2,

    /// <summary>The qualifier container's grid rows (<c>ContainerDef.GridH</c>); needs a content set (StatSheet build, story 014).</summary>
    ContainerRows = 3,

    /// <summary>The qualifier mail kind's <c>maxStack</c>; needs a content set (StatSheet build, story 014).</summary>
    MailKindMaxStack = 4,
}

/// <summary>Where a clamp bound comes from. Closed; append-only.</summary>
/// <example>
/// <code>
/// StatBoundKind k = StatDefs.Get(Stat.DeliverInsertPm).Max.Kind;   // StatBoundKind.Balance
/// </code>
/// </example>
public enum StatBoundKind : byte
{
    /// <summary>A code constant: a correctness bound (<see cref="StatBound.Value"/>).</summary>
    Constant = 1,

    /// <summary>A balance key, by name: a design clamp (<see cref="StatBound.Key"/>).</summary>
    Balance = 2,

    /// <summary>The stat's own base value (CD12 <c>RespawnMs</c>: <c>respawnMinMs..base</c>); a max only.</summary>
    Base = 3,
}
