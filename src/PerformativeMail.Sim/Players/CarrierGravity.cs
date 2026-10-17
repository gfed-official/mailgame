using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// What phase 4 hands to the sweep (phase 5): the vertical displacement of the tick and whether an armed or buffered jump press is
/// still unconsumed (so a landing inside the sweep launches it on the landing tick, as story 006 specified).
/// </summary>
/// <example><code>GravityPlan plan = CarrierGravity.Apply(ref state, in input, in env, ref sink);   // plan.Dy = -14 while falling from rest</code></example>
public struct GravityPlan
{
    /// <summary>The trapezoid vertical displacement of the tick, mm (0 for a grounded carrier that does not launch).</summary>
    public int Dy;

    /// <summary>A press is armed or buffered and was not consumed by a launch; the sweep fires it if the carrier lands.</summary>
    public bool PendingJump;
}

/// <summary>
/// Tick phase 4 of the pure carrier step: ground validity, jump launch and gravity (GDD <c>player-character-movement.md</c> P4, P5, F3;
/// ADR-0013 section 2). Vertical motion is the trapezoid with a carried remainder (<c>n = vy + vy' + RemY</c>,
/// <c>dy = FloorDiv(n, 60)</c>, <c>RemY = n - 60 x dy</c>, so <c>RemY</c> is always 0 to 59), all in <see cref="long"/>, one rounding
/// per formula. Gravity acts only while <see cref="LocoState.Airborne"/>.
/// <para>
/// This phase decides and integrates; it does not move the feet. The displacement it returns (<see cref="GravityPlan.Dy"/>) is applied by
/// <see cref="CarrierSweep"/> in sub-steps, where landing, snap-down and the terrain wall are resolved against the height field. No
/// fall damage and no landing slow-down exist (P5): a landing never touches horizontal velocity.
/// </para>
/// <para>
/// Entry check: a Grounded carrier is held on the support under its feet (<see cref="CarrierSweep.GroundResolve"/>); if the support is
/// steeper than <c>slopeMaxPm</c> or more than <c>stepUpMm</c> (the snap-down distance, P4) below the feet it goes Airborne with
/// <c>CoyoteLeft = coyoteTicks</c> and gravity acts on this very tick.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Inside OwnedCarrier.Step, after F2 and the support carry:
/// GravityPlan plan = CarrierGravity.Apply(ref state, in input, in env, in scratch, ref sink);
/// </code>
/// </example>
public static class CarrierGravity
{
    private const int RemainderDenominator = 60;

    /// <summary>The per-tick speed change <c>g / 30</c>, mm/s (800 at <c>g</c> 24 000; <c>g</c> is a multiple of 30 by validator rule PC 10).</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>mm/s lost per tick.</returns>
    public static int PerTickMmS(PlayerSettings settings) => settings.G / Hosting.TickRate.Hz;

    /// <summary>
    /// One F3 trapezoid step: <c>vy' = max(vy - g/30, -terminalMmS)</c>, <c>n = vy + vy' + RemY</c>, <c>dy = FloorDiv(n, 60)</c>,
    /// <c>RemY = n - 60 x dy</c>. At the cap the remainder is still carried.
    /// </summary>
    /// <param name="vy">Vertical speed, mm/s; becomes <c>vy'</c>.</param>
    /// <param name="remY">Vertical remainder in 1/60 mm; updated.</param>
    /// <param name="perTickMmS"><c>g / 30</c>.</param>
    /// <param name="terminalMmS">The fall-speed cap (positive).</param>
    /// <param name="sink">Counter sink for the division helper.</param>
    /// <returns>The vertical displacement of the tick, mm (at most 667 in magnitude at terminal speed 20 000).</returns>
    /// <example><code>int dy = CarrierGravity.Integrate(ref vy, ref remY, 800, 20_000, ref sink);</code></example>
    public static int Integrate(ref int vy, ref int remY, int perTickMmS, int terminalMmS, ref SimCounterSink sink)
    {
        long next = Math.Max((long)vy - perTickMmS, -(long)terminalMmS);
        long n = (long)vy + next + remY;
        long dy = Fix.FloorDiv(n, RemainderDenominator, ref sink);
        remY = (int)(n - (RemainderDenominator * dy));
        vy = (int)next;
        return (int)dy;
    }

    /// <summary>
    /// Phase 4: press buffer, ground validity, launch (ground, coyote, extra jump), then gravity integration, in that order. A launch
    /// integrates on its own tick with the launch speed (tick 1 of the arc). A Swimming carrier has no gravity and no vertical motion
    /// (<c>Vy</c>, <c>RemY</c> and coyote zeroed; a jump is refused; story 009); a Mounted carrier is untouched (Vehicles, M1).
    /// </summary>
    /// <param name="state">The carrier, updated in place (velocity, remainder, counters and Loco; never the feet position).</param>
    /// <param name="input">This tick's input.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates (gathered before this phase; box tops are supports).</param>
    /// <param name="sink">Counter sink for the division helpers.</param>
    /// <returns>The vertical displacement and the unconsumed press, for the sweep.</returns>
    public static GravityPlan Apply(ref CarrierState state, in CarrierInput input, in CarrierEnv env, in CarrierScratch scratch, ref SimCounterSink sink)
    {
        PlayerSettings settings = env.Settings;
        var plan = default(GravityPlan);
        bool allowed = CarrierJump.IsAllowed(state.Loco, input.ContextFlags, input.HostAuthored);
        bool armedNow = CarrierJump.Arm(ref state, in input, settings, allowed);
        if (state.Loco == LocoState.Swimming)
        {
            // Story 009 (ADR-0013 section 5): surface-only swimming. No gravity, no vertical motion, no jump (Arm refused and cleared the
            // buffer above); the feet stay on the water plane, which phase 8 re-asserts.
            state.Vy = 0;
            state.RemY = 0;
            state.CoyoteLeft = 0;
            return plan;
        }

        if (state.Loco is not (LocoState.Grounded or LocoState.Airborne))
        {
            return plan;
        }

        bool justLeft = EntryGround(ref state, in env, in scratch);

        // A press that has not been consumed: armed this tick or still buffered. Launch clears the buffer, so this is consumed once.
        bool pending = allowed && (armedNow || state.JumpBufferLeft > 0);
        if (state.Loco == LocoState.Grounded)
        {
            if (pending)
            {
                Launch(ref state, CarrierJump.LaunchSpeed(settings), clearExtra: true);
                plan.Dy = IntegrateState(ref state, settings, ref sink);
            }
        }
        else
        {
            AirborneTick(ref state, in input, settings, pending, armedNow, ref plan, ref sink);
        }

        CarrierJump.AgeCounters(ref state, armedNow, justLeft);
        return plan;
    }

    /// <summary>The airborne half of phase 4: coyote jump, extra jump and gravity. A press that no launch consumed stays pending for a landing in the sweep.</summary>
    private static void AirborneTick(
        ref CarrierState state, in CarrierInput input, PlayerSettings settings, bool pending, bool armedNow, ref GravityPlan plan, ref SimCounterSink sink)
    {
        if (pending && state.CoyoteLeft > 0)
        {
            Launch(ref state, CarrierJump.LaunchSpeed(settings), clearExtra: false);
            pending = false;
        }
        else if (armedNow && CarrierJump.ExtraAvailable(in state, in input, settings))
        {
            Launch(ref state, CarrierJump.ExtraLaunchSpeed(settings), clearExtra: false);
            state.Flags |= CarrierFlags.ExtraJumpUsed;
            pending = false;
        }

        plan.Dy = IntegrateState(ref state, settings, ref sink);
        plan.PendingJump = pending;
    }

    /// <summary>
    /// Holds a Grounded carrier on the support under its feet; returns true when the ground was lost (non-walkable terrain, or the support
    /// fell away by more than <c>stepUpMm</c>), which starts coyote time and leaves the carrier Airborne with zero vertical speed.
    /// </summary>
    private static bool EntryGround(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch)
    {
        if (state.Loco != LocoState.Grounded)
        {
            return false;
        }

        state.Vy = 0;
        state.RemY = 0;
        state.CoyoteLeft = 0;
        int support = CarrierSweep.GroundResolve(in env, in scratch, state.X, state.Y, state.Z, out bool walkable, out _, out _);
        if (CarrierSweep.SnapToSupport(ref state, support, walkable, env.Settings))
        {
            return false;
        }

        CarrierSweep.LoseGround(ref state, env.Settings);
        return true;
    }

    private static void Launch(ref CarrierState state, int speedMmS, bool clearExtra)
    {
        CarrierJump.Launch(ref state, speedMmS);
        if (clearExtra)
        {
            state.Flags &= ~CarrierFlags.ExtraJumpUsed;
        }
    }

    /// <summary>Fires the buffered press on the tick the sweep landed the carrier: launches with the ground speed and integrates tick 1 of the arc. Returns the displacement to apply.</summary>
    internal static int LaunchOnLanding(ref CarrierState state, PlayerSettings settings, ref SimCounterSink sink)
    {
        Launch(ref state, CarrierJump.LaunchSpeed(settings), clearExtra: true);
        return IntegrateState(ref state, settings, ref sink);
    }

    /// <summary>Integrates one tick of gravity into <c>Vy</c> / <c>RemY</c> and returns the displacement (the feet are moved by the sweep).</summary>
    private static int IntegrateState(ref CarrierState state, PlayerSettings settings, ref SimCounterSink sink)
    {
        int vy = state.Vy;
        int rem = state.RemY;
        int dy = Integrate(ref vy, ref rem, PerTickMmS(settings), settings.TerminalMmS, ref sink);
        state.Vy = vy;
        state.RemY = rem;
        return dy;
    }

    /// <summary>Landing: Grounded on the support, <c>Vy = 0</c>, <c>RemY = 0</c>, the extra jump and coyote cleared. Horizontal velocity is untouched (no slow-down, no damage).</summary>
    internal static void Land(ref CarrierState state, int supportMm)
    {
        state.Y = supportMm;
        state.Vy = 0;
        state.RemY = 0;
        state.Loco = LocoState.Grounded;
        state.CoyoteLeft = 0;
        state.Flags &= ~CarrierFlags.ExtraJumpUsed;
    }
}
