using System.Globalization;
using PerformativeMail.Sim.Content.Balance;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The resolved clamp of one stat: <see cref="Min"/> and <see cref="Max"/> as int32 (F1 variables min_s, max_s).
/// <see cref="IsValid"/> is false when min exceeds max (CV-GEN-15). Immutable.
/// </summary>
/// <param name="Stat">The stat.</param>
/// <param name="Min">The lowest result (inclusive).</param>
/// <param name="Max">The highest result (inclusive).</param>
/// <example>
/// <code>
/// StatBounds b = StatDefs.Get(Stat.ThrowRangePermille).ResolveBounds(table);   // Min 1000, Max 1500
/// </code>
/// </example>
public readonly record struct StatBounds(Stat Stat, int Min, int Max)
{
    /// <summary>True when <see cref="Min"/> is at most <see cref="Max"/>.</summary>
    public bool IsValid => Min <= Max;
}

/// <summary>
/// The code-side row of one <see cref="Stat"/> (CD12, ADR-0010 section 7): unit, scope, qualifier kind, base source,
/// allowed ops, Set resolve, rounding and the two clamp bounds. Design clamps are balance keys held by name; only
/// correctness bounds are code constants. The rows are in <see cref="StatDefs"/>. Immutable; safe from any thread.
/// </summary>
/// <param name="Stat">The stat this row describes.</param>
/// <param name="Unit">The stored unit.</param>
/// <param name="Scope">Whose sheet it lives on.</param>
/// <param name="Qualifier">What it is qualified by.</param>
/// <param name="Base">Where its base value comes from.</param>
/// <param name="Ops">The ops it accepts.</param>
/// <param name="SetResolve">How several Sets combine (<see cref="Stats.SetResolve.NotApplicable"/> without the Set op).</param>
/// <param name="Rounding">The rounding of the single F1 division.</param>
/// <param name="Min">The lower clamp bound.</param>
/// <param name="Max">The upper clamp bound.</param>
/// <example>
/// <code>
/// StatDef def = StatDefs.Get(Stat.RespawnMs);
/// StatBounds b = def.ResolveBounds(table);   // Min = player.respawnMinMs, Max = player.respawnMs (the base)
/// </code>
/// </example>
public readonly record struct StatDef(
    Stat Stat,
    StatUnit Unit,
    StatScope Scope,
    QualifierKind Qualifier,
    BaseSource Base,
    StatOps Ops,
    SetResolve SetResolve,
    StatRounding Rounding,
    StatBound Min,
    StatBound Max)
{
    /// <summary>
    /// Resolves <see cref="Min"/> and <see cref="Max"/> against <paramref name="table"/>: a constant is itself, a balance
    /// key is read by name through the table's registry (composition-time, not per-tick: it does a string lookup), and
    /// <see cref="StatBoundKind.Base"/> is the stat's own constant or balance-key base. The result may have min above max
    /// (see <see cref="StatBounds.IsValid"/>); <see cref="StatDefs.ResolveBounds"/> reports that as CV-GEN-15.
    /// </summary>
    /// <param name="table">A table whose registry holds every key this row names.</param>
    /// <exception cref="ArgumentException">The registry lacks a named key.</exception>
    /// <exception cref="InvalidOperationException">A <see cref="StatBoundKind.Base"/> bound on a row whose base needs a content set (a bug in the row).</exception>
    /// <exception cref="OverflowException">A resolved value does not fit in int32.</exception>
    /// <example>
    /// <code>
    /// StatBounds b = StatDefs.Get(Stat.ExtraJumps).ResolveBounds(table);   // 0 .. player.maxExtraJumps
    /// </code>
    /// </example>
    public StatBounds ResolveBounds(BalanceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new StatBounds(Stat, ResolveBound(Min, table), ResolveBound(Max, table));
    }

    /// <summary>Describes a bound for messages: the key name, or the constant, or <c>base</c>.</summary>
    /// <param name="bound">One of this row's bounds.</param>
    public string Describe(StatBound bound) => bound.Kind switch
    {
        StatBoundKind.Balance => bound.Key,
        StatBoundKind.Base => Base.Kind == BaseSourceKind.Balance ? Base.Key : "base",
        _ => "constant",
    };

    private int ResolveBound(StatBound bound, BalanceTable table) => bound.Kind switch
    {
        StatBoundKind.Constant => bound.Value,
        StatBoundKind.Balance => ReadKey(table, bound.Key),
        _ => ResolveBase(table),
    };

    internal int ResolveBase(BalanceTable table) => Base.Kind switch
    {
        BaseSourceKind.Constant => Base.Value,
        BaseSourceKind.Balance => ReadKey(table, Base.Key),
        _ => throw new InvalidOperationException("Stat #" + ((int)Stat).ToString(CultureInfo.InvariantCulture) + " has a base bound but its base needs a content set."),
    };

    private static int ReadKey(BalanceTable table, string name)
    {
        if (!table.Registry.TryFind(name, out BalanceKey key))
        {
            throw new ArgumentException("The balance table's registry has no row " + name + ".", nameof(table));
        }

        return checked((int)table.Get(key));
    }
}
