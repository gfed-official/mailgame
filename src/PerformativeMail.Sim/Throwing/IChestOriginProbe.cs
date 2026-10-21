using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The chest-to-origin port of F1 (ADR-0015 section 3; GDD T4 "launch from inside a wall"). <see cref="LaunchMath.Compute"/>
/// asks it whether the straight segment from the thrower's chest to the computed hand origin is blocked by a solid or the
/// heightfield; if so the launch starts at the chest instead (<c>originFromChest = true</c>). The real probe needs world
/// solids and the heightfield and is supplied by Throwing story 003; until then <see cref="NoChestProbe"/> reports
/// "clear". Implementations must be pure (no mutation, RNG or allocation) because the host, the aim preview and every
/// peer call <see cref="LaunchMath.Compute"/> with the same inputs.
/// </summary>
/// <example>
/// <code>
/// bool blocked = probe.IsBlocked(in chest, in origin);   // false for NoChestProbe
/// </code>
/// </example>
public interface IChestOriginProbe
{
    /// <summary>Whether the segment from <paramref name="chest"/> to <paramref name="origin"/> is blocked.</summary>
    /// <param name="chest">The thrower's chest point, mm (feet + chest height).</param>
    /// <param name="origin">The F1 hand origin, mm.</param>
    /// <returns>True when the launch must start at the chest.</returns>
    bool IsBlocked(in Vec3Mm chest, in Vec3Mm origin);
}
