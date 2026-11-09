using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>The outcome of <see cref="IClaimCorroborator.Check"/> (ADR-0015 section 1 check 9). None of them rejects a release.</summary>
public enum Corroboration : byte
{
    /// <summary>The claim is within the tolerance (3D, inclusive) of a history sample: the claim is the feet.</summary>
    Corroborated = 0,

    /// <summary>History exists but no sample corroborates the claim: the newest sample is the feet and <c>originClamped</c> is set.</summary>
    Clamped = 1,

    /// <summary>No usable sample (empty history, or the position is stale): the host's last known position is the feet and <c>originClamped</c> is set.</summary>
    NoHistory = 2,
}

/// <summary>
/// Throwing's claim-and-corroborate port (ADR-0015 section 1 check 9, ADR-0014 section 5, GDD F5; throwing-catching story 009): whether
/// a release's claimed feet lie within <c>claimToleranceMm</c> of a host history sample of the thrower, and the position to use when they
/// do not. At M-1a Player Character's history is the newest sample only (<c>IPositionHistory</c>, story 019), so "nearest sample" is
/// the newest; ADR-0020 (M0) adds the ring behind the same call. Read-only, allocation-free.
/// </summary>
/// <example>
/// <code>
/// Corroboration c = corroborator.Check(slot, in claim, toleranceMm, out Vec3Mm newest);
/// Vec3Mm feet = c == Corroboration.Corroborated ? claim : newest;
/// </code>
/// </example>
public interface IClaimCorroborator
{
    /// <summary>Corroborates <paramref name="claim"/> against the history of <paramref name="slot"/>.</summary>
    /// <param name="slot">The thrower's slot.</param>
    /// <param name="claim">The claimed feet, mm.</param>
    /// <param name="toleranceMm">The 3D tolerance, mm (inclusive).</param>
    /// <param name="newest">The newest history sample, or the host's last known position when there is none.</param>
    /// <returns>The outcome.</returns>
    Corroboration Check(PlayerSlot slot, in Vec3Mm claim, int toleranceMm, out Vec3Mm newest);
}
