using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The jump decisions of the pure carrier step (GDD <c>player-character-movement.md</c> P5 and F3; ADR-0013 section 2): launch speeds, the
/// press buffer, coyote time and the Bunny Hop extra jump. Integer only; no clock, RNG, statics or allocation. Every constant comes
/// from <see cref="PlayerSettings"/> (<c>g</c>, <c>jumpHeightMm</c>, <c>extraJumpPm</c>, <c>coyoteTicks</c>, <c>jumpBufferTicks</c>,
/// <c>maxExtraJumps</c>) or the input's <c>ExtraJumps</c> stat. <see cref="CarrierGravity.Apply"/> sequences these inside tick phase 4.
/// <para>
/// Timing: a press arms <c>JumpBufferLeft = jumpBufferTicks</c> on its own tick (and may fire on that tick); the buffer then covers the
/// following <c>jumpBufferTicks</c> ticks, so a press 4 ticks before landing fires on the landing tick and one 5 ticks before does not.
/// Coyote time works the same way: <c>CoyoteLeft = coyoteTicks</c> is set on the tick the ground is lost, and the following
/// <c>coyoteTicks</c> ticks still allow a jump.
/// </para>
/// <para>
/// Launch speeds are recomputed from the settings on each launch (one integer square root of about 16 iterations, only on a tick that
/// fires a jump) rather than cached: <see cref="PlayerSettings"/> is a record with exactly one property per balance key (a test pins
/// that), and a cached value would go stale under a <c>with</c> copy.
/// </para>
/// </summary>
/// <example>
/// <code>
/// int v0 = CarrierJump.LaunchSpeed(settings);        // 7589 at g 24 000 and jumpHeightMm 1200
/// int v0Extra = CarrierJump.ExtraLaunchSpeed(settings);   // 6830 at extraJumpPm 900
/// </code>
/// </example>
public static class CarrierJump
{
    private const int PermilleUnit = 1000;

    /// <summary>Upper bound of the byte-sized <c>JumpBufferLeft</c> and <c>CoyoteLeft</c> counters.</summary>
    private const int MaxCounterTicks = byte.MaxValue;

    /// <summary>F3 <c>v0 = isqrt(2 x g x jumpHeightMm)</c>, mm/s (7589 at the defaults).</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The launch speed of a ground (or coyote) jump; 0 when <c>g</c> or the height is not positive.</returns>
    /// <example><code>int v0 = CarrierJump.LaunchSpeed(settings);</code></example>
    public static int LaunchSpeed(PlayerSettings settings)
    {
        // ulong holds 2 x int.Max x int.Max, so there is no overflow for any int pair.
        ulong product = 2UL * (ulong)Math.Max(0, settings.G) * (ulong)Math.Max(0, settings.JumpHeightMm);
        return (int)Math.Min(Fix.ISqrt(product), (uint)int.MaxValue);
    }

    /// <summary>F3 <c>v0Extra = v0 x extraJumpPm / 1000</c> (one rounding, truncated), mm/s (6830 at the defaults). It sets vertical speed; it is never added to it.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The launch speed of the mid-air jump.</returns>
    /// <example><code>int v0Extra = CarrierJump.ExtraLaunchSpeed(settings);</code></example>
    public static int ExtraLaunchSpeed(PlayerSettings settings)
    {
        SimCounterSink none = default;
        long v = Fix.MulDiv(LaunchSpeed(settings), Math.Max(0, settings.ExtraJumpPm), PermilleUnit, ref none);
        return (int)Math.Min(v, int.MaxValue);
    }

    /// <summary>
    /// P5: a jump is refused (no vertical-speed change and no buffer arm) in the Panel or Dead context, while Swimming or Mounted
    /// (by locomotion state or by the Mounted context bit), and for a host-authored body (Dead or Frozen: zero input).
    /// </summary>
    /// <param name="loco">The carrier's locomotion state.</param>
    /// <param name="context">This tick's context bits.</param>
    /// <param name="hostAuthored">The host authors this body.</param>
    /// <returns>True when a press may act.</returns>
    /// <example><code>bool ok = CarrierJump.IsAllowed(LocoState.Grounded, CarrierContextFlags.None, false);   // true</code></example>
    public static bool IsAllowed(LocoState loco, CarrierContextFlags context, bool hostAuthored) =>
        !hostAuthored
        && loco is LocoState.Grounded or LocoState.Airborne
        && (context & (CarrierContextFlags.Panel | CarrierContextFlags.Dead | CarrierContextFlags.Mounted)) == 0;

    /// <summary>
    /// Arms or clears the press buffer for this tick. A disallowed context clears the buffer (a press cannot wait through a panel or a
    /// swim); an allowed press sets <c>JumpBufferLeft = jumpBufferTicks</c>.
    /// </summary>
    /// <param name="state">The carrier.</param>
    /// <param name="input">This tick's input (<c>JumpPressed</c> is an edge on this tick only).</param>
    /// <param name="settings">The settings.</param>
    /// <param name="allowed">The result of <see cref="IsAllowed"/>.</param>
    /// <returns>True when a press is armed on this tick (it may fire at once, and is not aged at the end of this tick).</returns>
    public static bool Arm(ref CarrierState state, in CarrierInput input, PlayerSettings settings, bool allowed)
    {
        if (!allowed)
        {
            state.JumpBufferLeft = 0;
            return false;
        }

        if (!input.JumpPressed)
        {
            return false;
        }

        state.JumpBufferLeft = (byte)Math.Clamp(settings.JumpBufferTicks, 0, MaxCounterTicks);
        return true;
    }

    /// <summary>True when the Bunny Hop jump is available: the stat grants one (capped by <c>maxExtraJumps</c>) and this airborne period has not used it.</summary>
    /// <param name="state">The carrier.</param>
    /// <param name="input">This tick's input (<c>ExtraJumps</c>).</param>
    /// <param name="settings">The settings.</param>
    /// <returns>True when a mid-air press may fire the extra jump.</returns>
    public static bool ExtraAvailable(in CarrierState state, in CarrierInput input, PlayerSettings settings) =>
        Math.Min(input.ExtraJumps, settings.MaxExtraJumps) >= 1 && (state.Flags & CarrierFlags.ExtraJumpUsed) == 0;

    /// <summary>
    /// Sets the launch: vertical speed to <paramref name="speedMmS"/> (set, never added), remainder zeroed, <c>Airborne</c>, and the
    /// buffer and coyote counters consumed. The caller integrates this tick with the new speed (tick 1 of the arc).
    /// </summary>
    /// <param name="state">The carrier.</param>
    /// <param name="speedMmS">The launch speed.</param>
    public static void Launch(ref CarrierState state, int speedMmS)
    {
        state.Vy = speedMmS;
        state.RemY = 0;
        state.Loco = LocoState.Airborne;
        state.JumpBufferLeft = 0;
        state.CoyoteLeft = 0;
    }

    /// <summary>Ages the press buffer and the coyote window at the end of the tick; neither ages on the tick it was set.</summary>
    /// <param name="state">The carrier.</param>
    /// <param name="armedNow">A press was armed on this tick.</param>
    /// <param name="justLeftGround">The ground was lost on this tick (coyote just started).</param>
    public static void AgeCounters(ref CarrierState state, bool armedNow, bool justLeftGround)
    {
        if (!armedNow && state.JumpBufferLeft > 0)
        {
            state.JumpBufferLeft--;
        }

        if (state.Loco == LocoState.Airborne && state.CoyoteLeft > 0 && !justLeftGround)
        {
            state.CoyoteLeft--;
        }
    }

    /// <summary>The coyote window to start when the ground is lost: <c>coyoteTicks</c> clamped to the byte counter.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The ticks.</returns>
    public static byte CoyoteStart(PlayerSettings settings) => (byte)Math.Clamp(settings.CoyoteTicks, 0, MaxCounterTicks);
}
