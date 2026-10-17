using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The per-world sheet the systems hold: a stable <see cref="IStatSheet"/> instance that forwards every read to the latest
/// immutable <see cref="StatSheetCache"/> the writer published, plus the sheet <see cref="Version"/>. It holds the cache only,
/// never the modifier pools or the source list (the writer owns those and holds this sheet, not the reverse). Publishing is a
/// plain reference swap on the Sim tick thread, so it is not safe to read from another thread while a push or remove runs.
/// </summary>
internal sealed class LiveStatSheet : IStatSheet
{
    private StatSheetCache _current;

    internal LiveStatSheet(StatSheetCache initial)
    {
        _current = initial;
    }

    /// <inheritdoc />
    public uint Version { get; private set; }

    /// <inheritdoc />
    public int Read(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0) =>
        _current.Read(stat, qualifier, slot, transientAdd);

    /// <inheritdoc />
    public StatExplain Explain(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0) =>
        _current.Explain(stat, qualifier, slot, transientAdd);

    /// <inheritdoc />
    public bool Has(RuleFlag flag, PlayerSlot? slot = null) => _current.Has(flag, slot);

    /// <summary>Makes <paramref name="next"/> the sheet every later read sees, at <paramref name="version"/>.</summary>
    internal void Publish(StatSheetCache next, uint version)
    {
        _current = next;
        Version = version;
    }
}
