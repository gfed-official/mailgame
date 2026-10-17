using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// One source's contribution to a cell, as listed by <see cref="IStatSheet.Explain"/>: the identity of the modifier
/// (CD13 <c>sourceKind, sourceId, stackIndex, ownerSlot</c>) and what it did. This is a derived copy kept by the sheet for
/// the UI and debug path; it is not the live modifier list. Immutable with structural equality.
/// </summary>
/// <param name="SourceKind">What pushed it.</param>
/// <param name="SourceId">The source def's index.</param>
/// <param name="StackIndex">Which stack of the source this is.</param>
/// <param name="OwnerSlot">The owning slot, or <see cref="PlayerSlot.None"/> for a team source.</param>
/// <param name="Qualifier">The modifier's own qualifier (<see cref="StatSheet.NoQualifier"/> when it applies to every qualifier).</param>
/// <param name="Op">Add, Mul or Set.</param>
/// <param name="Value">In the stat's unit; Mul in permille.</param>
/// <param name="Lifetime">The lifetime tag.</param>
/// <example>
/// <code>
/// foreach (StatContribution c in sheet.Explain(Stat.MoveSpeedAddPm, slot: slot).Sources) { /* c.Op, c.Value */ }
/// </code>
/// </example>
public readonly record struct StatContribution(
    SourceKind SourceKind,
    DefIndex SourceId,
    byte StackIndex,
    PlayerSlot OwnerSlot,
    ushort Qualifier,
    ModifierOp Op,
    int Value,
    ModifierLifetime Lifetime);

/// <summary>
/// The answer of <see cref="IStatSheet.Explain"/> (CD19): the resolved (clamped) value, the unclamped value F1 produced
/// (saturated to int64), which clamp bound applied, and the sources that contributed. <see cref="Sources"/> is a view
/// over a buffer owned by the sheet, valid for the life of the sheet and never changing (a sheet is immutable); reading
/// it allocates nothing. Immutable. The card numbers shown by the UI come from here, never from a recomputation.
/// </summary>
/// <example>
/// <code>
/// StatExplain e = sheet.Explain(Stat.MoveSpeedAddPm, slot: slot, transientAdd: 300);
/// // e.Unclamped == 660, e.Resolved == 400, e.ClampedAt == StatClamp.Max, e.Sources.Length == 3
/// </code>
/// </example>
public readonly struct StatExplain
{
    private readonly StatContribution[]? _buffer;
    private readonly int _offset;
    private readonly int _count;

    internal StatExplain(int resolved, long unclamped, StatClamp clampedAt, int transientAdd, StatContribution[]? buffer, int offset, int count)
    {
        Resolved = resolved;
        Unclamped = unclamped;
        ClampedAt = clampedAt;
        TransientAdd = transientAdd;
        _buffer = buffer;
        _offset = offset;
        _count = count;
    }

    /// <summary>The clamped value, equal to what <see cref="IStatSheet.Read"/> returns for the same arguments.</summary>
    public int Resolved { get; }

    /// <summary>X before the clamp (the Set value when a Set was live), saturated to int64.</summary>
    public long Unclamped { get; }

    /// <summary>Which bound the clamp applied, if any.</summary>
    public StatClamp ClampedAt { get; }

    /// <summary>The transient Add that was applied: the requested one, or 0 when it was invalid or not requested.</summary>
    public int TransientAdd { get; }

    /// <summary>The modifiers that applied to this cell (team first, then the slot's), in source-list order; the transient is not a source.</summary>
    public ReadOnlySpan<StatContribution> Sources => _buffer is null ? ReadOnlySpan<StatContribution>.Empty : new ReadOnlySpan<StatContribution>(_buffer, _offset, _count);
}
