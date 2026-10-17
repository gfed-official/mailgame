using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>Per-stat constants a sheet needs to locate and resolve a cell; copied from the <see cref="StatDef"/> and resolved bounds at build.</summary>
internal readonly record struct StatLayout(
    int FirstCell,
    int QualifierCount,
    bool Qualified,
    StatScope Scope,
    StatOps Ops,
    StatRounding Rounding,
    int Min,
    int Max);

/// <summary>
/// One cached cell: the F1 components and result, and where its contributing sources sit in the explain buffer. A plain
/// struct with a public readonly field so a read can pass <see cref="Resolution"/> by reference without a copy.
/// </summary>
internal readonly struct StatCell
{
    /// <summary>The cached F1 components and result (built with no transient).</summary>
    public readonly StatResolution Resolution;

    /// <summary>Index of this cell's first contribution in the explain buffer.</summary>
    public readonly int SourceOffset;

    /// <summary>The number of contributions.</summary>
    public readonly int SourceCount;

    public StatCell(StatResolution resolution, int sourceOffset, int sourceCount)
    {
        Resolution = resolution;
        SourceOffset = sourceOffset;
        SourceCount = sourceCount;
    }
}

/// <summary>
/// The <see cref="IStatSheet"/> that <see cref="StatSheet.Build"/> returns: a flat array of cells indexed by
/// <c>(stat, qualifier ordinal, sheet)</c>, where sheet 0 is the team sheet and sheets 1 to 8 are the personal sheets by
/// slot. Each cell caches the resolved value and the pre-rounding F1 components (<see cref="StatResolution"/>). It holds
/// no modifier list, no pool and no <see cref="ContentSet"/>: <see cref="Read"/> cannot walk modifiers, so its cost does
/// not depend on how many are live (CDD-PERF-2 structural clause). The explain buffer is a derived copy of the
/// contributors (<see cref="StatContribution"/>), touched only by <see cref="Explain"/>. Immutable after construction;
/// safe to read from any thread. No Int64 fast path (ADR-0010 section 7).
/// </summary>
/// <example>
/// <code>
/// IStatSheet sheet = StatSheet.Build(content, sources);   // an instance of this type
/// int value = sheet.Read(Stat.LateValueRatioPm);          // one array load
/// </code>
/// </example>
public sealed class StatSheetCache : IStatSheet
{
    private readonly StatLayout[] _layouts;
    private readonly StatCell[] _cells;
    private readonly StatContribution[] _contributions;
    private readonly int[] _flagCounts;
    private readonly int _flagWidth;

    internal StatSheetCache(StatLayout[] layouts, StatCell[] cells, StatContribution[] contributions, int[] flagCounts, int flagWidth)
    {
        _layouts = layouts;
        _cells = cells;
        _contributions = contributions;
        _flagCounts = flagCounts;
        _flagWidth = flagWidth;
    }

    /// <inheritdoc />
    public uint Version => 0;

    /// <inheritdoc />
    public bool Has(RuleFlag flag, PlayerSlot? slot = null)
    {
        int f = (int)flag - 1;
        if (unchecked((uint)f) >= (uint)_flagWidth)
        {
            return false;
        }

        int live = _flagCounts[f];
        if (slot.HasValue)
        {
            PlayerSlot s = slot.GetValueOrDefault();
            SimAssert.That(!s.IsNone, ContentAssertCode.StatSheetBadSlot);
            if (!s.IsNone)
            {
                live += _flagCounts[((1 + s.Value) * _flagWidth) + f];
            }
        }

        return live > 0;
    }

    /// <inheritdoc />
    public int Read(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0)
    {
        if (!Locate(stat, qualifier, slot, ref transientAdd, out int layoutIndex, out int cellIndex, out int fallback))
        {
            return fallback;
        }

        ref readonly StatCell cell = ref _cells[cellIndex];
        if (transientAdd == 0)
        {
            return cell.Resolution.Value;
        }

        ref readonly StatLayout layout = ref _layouts[layoutIndex];
        return StatResolve.ResolveWithTransient(layout.Rounding, layout.Min, layout.Max, in cell.Resolution, transientAdd).Value;
    }

    /// <inheritdoc />
    public StatExplain Explain(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0)
    {
        if (!Locate(stat, qualifier, slot, ref transientAdd, out int layoutIndex, out int cellIndex, out int fallback))
        {
            return new StatExplain(fallback, fallback, StatClamp.None, 0, null, 0, 0);
        }

        ref readonly StatCell cell = ref _cells[cellIndex];
        if (transientAdd == 0)
        {
            StatResolution r = cell.Resolution;
            return new StatExplain(r.Value, r.Unclamped, r.ClampedAt, 0, _contributions, cell.SourceOffset, cell.SourceCount);
        }

        ref readonly StatLayout layout = ref _layouts[layoutIndex];
        StatResolution t = StatResolve.ResolveWithTransient(layout.Rounding, layout.Min, layout.Max, in cell.Resolution, transientAdd);
        return new StatExplain(t.Value, t.Unclamped, t.ClampedAt, transientAdd, _contributions, cell.SourceOffset, cell.SourceCount);
    }

    /// <summary>
    /// Validates the arguments (SIM_ASSERT on each fault) and finds the cell. False means no cell: <paramref name="fallback"/>
    /// is the value to return. A bad slot reads the team sheet and a bad transient is zeroed, and the lookup goes on.
    /// </summary>
    private bool Locate(
        Stat stat, ushort qualifier, PlayerSlot? slot, ref int transientAdd, out int layoutIndex, out int cellIndex, out int fallback)
    {
        layoutIndex = (int)stat - 1;
        cellIndex = -1;
        fallback = 0;
        bool known = unchecked((uint)layoutIndex) < (uint)_layouts.Length;
        SimAssert.That(known, ContentAssertCode.StatSheetUnknownStat);
        if (!known)
        {
            return false;
        }

        ref readonly StatLayout layout = ref _layouts[layoutIndex];
        if (!ResolveQualifier(in layout, qualifier, out int ordinal))
        {
            fallback = layout.Min;
            return false;
        }

        int sheet = ResolveSheet(in layout, slot);
        transientAdd = SanitizeTransient(in layout, transientAdd);
        cellIndex = ((layout.FirstCell + ordinal) * StatSheet.SheetCount) + sheet;
        return true;
    }

    /// <summary>The qualifier ordinal of the cell: 0 for an unqualified stat (any qualifier is ignored, asserted), else the checked qualifier. False when it is out of range.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ResolveQualifier(in StatLayout layout, ushort qualifier, out int ordinal)
    {
        if (!layout.Qualified)
        {
            SimAssert.That(qualifier == StatSheet.NoQualifier, ContentAssertCode.StatSheetQualifierOnUnqualified);
            ordinal = 0;
            return true;
        }

        bool inRange = qualifier < layout.QualifierCount;
        SimAssert.That(inRange, ContentAssertCode.StatSheetBadQualifier);
        ordinal = inRange ? qualifier : 0;
        return inRange;
    }

    /// <summary>The sheet to read: 0 (team) for a null, invalid (asserted) or team-stat slot, else 1 plus the slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ResolveSheet(in StatLayout layout, PlayerSlot? slot)
    {
        if (!slot.HasValue)
        {
            return 0;
        }

        PlayerSlot s = slot.GetValueOrDefault();
        SimAssert.That(!s.IsNone, ContentAssertCode.StatSheetBadSlot);
        return !s.IsNone && layout.Scope != StatScope.Team ? 1 + s.Value : 0;
    }

    /// <summary>The transient to apply: the argument, or 0 when it is negative or the stat does not accept Add (each asserted).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SanitizeTransient(in StatLayout layout, int transientAdd)
    {
        if (transientAdd == 0)
        {
            return 0;
        }

        bool negative = transientAdd < 0;
        bool addAllowed = (layout.Ops & StatOps.Add) != 0;
        SimAssert.That(!negative, ContentAssertCode.StatSheetNegativeTransient);
        SimAssert.That(addAllowed, ContentAssertCode.StatSheetTransientNotAdd);
        return negative || !addAllowed ? 0 : transientAdd;
    }
}
