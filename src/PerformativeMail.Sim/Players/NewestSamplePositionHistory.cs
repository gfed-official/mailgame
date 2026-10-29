using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>The M-1a <see cref="IPositionHistory"/>: a claim is corroborated by the player's newest host position only (no ring, no dead reckoning).</summary>
internal sealed class NewestSamplePositionHistory : IPositionHistory
{
    private readonly HostPositionTable _table;

    public NewestSamplePositionHistory(HostPositionTable table)
    {
        _table = table;
    }

    public bool Corroborates(PlayerSlot slot, in ClaimedPosition claim, int toleranceMm)
    {
        ref readonly HostPosition p = ref _table[slot];
        return PlayersRange.Within((long)claim.X - p.Feet.X, (long)claim.Y - p.Feet.Y, (long)claim.Z - p.Feet.Z, toleranceMm);
    }
}
