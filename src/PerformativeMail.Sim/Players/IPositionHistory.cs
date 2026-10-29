using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The F5 corroboration seam (ADR-0014 section 5): whether a claimed feet position lies within <c>claimToleranceMm</c> of a sample
/// the host holds for the player. M-1a corroborates against the newest sample only (<see cref="NewestSamplePositionHistory"/>);
/// ADR-0020 (M0) drops in the <c>rangeHistoryTicks + 3</c> ring and the dead-reckoned sample behind the same call. Read-only,
/// allocation-free.
/// </summary>
internal interface IPositionHistory
{
    /// <summary>True when some corroborating sample of <paramref name="slot"/> is within <paramref name="toleranceMm"/> (3D, inclusive) of <paramref name="claim"/>.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="claim">The claimed feet position.</param>
    /// <param name="toleranceMm">The tolerance, mm.</param>
    /// <returns>True when corroborated.</returns>
    bool Corroborates(PlayerSlot slot, in ClaimedPosition claim, int toleranceMm);
}
