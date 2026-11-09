using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// What a passing <see cref="ThrowValidator.Validate(in ThrowReleasePayload, ActorId, uint, in ThrowHostFacts, out ThrowPlan)"/> hands the
/// commit (ADR-0015 section 1; throwing-catching story 009): every value the launch needs, so story 010's <c>Commit</c> never recomputes,
/// never re-validates and never fails. Stack-only; <c>default</c> after a rejection.
/// </summary>
/// <example><code>if (ThrowValidator.Validate(in p, op.Actor, tick, in facts, out ThrowPlan plan).IsOk) { /* commit plan */ }</code></example>
public readonly struct ThrowPlan
{
    /// <summary>The thrower's slot.</summary>
    public readonly PlayerSlot Thrower;

    /// <summary>The hotbar entry to detach.</summary>
    public readonly EntryId Entry;

    /// <summary>Its units (the entry's whole count).</summary>
    public readonly byte Units;

    /// <summary>The feet the launch uses: the claim when corroborated, else the newest sample or last known position.</summary>
    public readonly Vec3Mm FeetUsed;

    /// <summary>True when <see cref="FeetUsed"/> is not the claim (check 9; <c>ThrowLaunched</c>'s flag).</summary>
    public readonly bool OriginClamped;

    /// <summary>Direction x, scale 16384 (within the check 8 bounds).</summary>
    public readonly short DirX;

    /// <summary>Direction y.</summary>
    public readonly short DirY;

    /// <summary>Direction z.</summary>
    public readonly short DirZ;

    /// <summary>The throwing hand, -1 or +1.</summary>
    public readonly sbyte Side;

    /// <summary>The slot's leaky-bucket clock after the launch, <c>tat'</c>. At M-1a the slot's current <c>Tat</c> (story 028 computes it).</summary>
    public readonly uint TatNext;

    /// <summary>Creates a plan.</summary>
    /// <param name="thrower">The thrower's slot.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="units">Its units.</param>
    /// <param name="feetUsed">The feet the launch uses.</param>
    /// <param name="originClamped">Whether the feet are not the claim.</param>
    /// <param name="dirX">Direction x.</param>
    /// <param name="dirY">Direction y.</param>
    /// <param name="dirZ">Direction z.</param>
    /// <param name="side">The throwing hand.</param>
    /// <param name="tatNext">The slot's <c>tat'</c>.</param>
    public ThrowPlan(PlayerSlot thrower, EntryId entry, byte units, Vec3Mm feetUsed, bool originClamped, short dirX, short dirY, short dirZ, sbyte side, uint tatNext)
    {
        Thrower = thrower;
        Entry = entry;
        Units = units;
        FeetUsed = feetUsed;
        OriginClamped = originClamped;
        DirX = dirX;
        DirY = dirY;
        DirZ = dirZ;
        Side = side;
        TatNext = tatNext;
    }
}
