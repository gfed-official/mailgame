using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The pure kinematic step of an owned carrier (GDD <c>player-character-movement.md</c> P3/P4; ADR-0013 section 1). No clock, RNG,
/// statics, allocation or exceptions. Implemented: phases 1-2 (story 005), 4 gravity and jump (story 006, <see cref="CarrierGravity"/>),
/// 5 the terrain sub-step sweep and 7 the map-edge clamp (story 007, <see cref="CarrierSweep"/>, <see cref="CarrierBounds"/>), the once-per-tick collider gather, tick-start depenetration, box slides, step-up and ceilings (story 008, <see cref="CarrierColliders"/>, <see cref="CarrierDepenetrate"/>, <see cref="CarrierSlide"/>, <see cref="CarrierStepUp"/>), 8 water, wading,
/// swimming and haul-out (story 009, <see cref="CarrierWater"/>, <see cref="CarrierHaulOut"/>); support carry and separation are placeholders in the P4 order.
/// </summary>
/// <example><code>OwnedCarrier.Step(ref state, in input, in env, ref scratch);</code></example>
public static class OwnedCarrier
{
    /// <summary>Advances <paramref name="state"/> by one tick.</summary>
    /// <param name="state">The carrier, updated in place.</param>
    /// <param name="input">This tick's input.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">Fixed-capacity scratch and probe counters.</param>
    public static void Step(ref CarrierState state, in CarrierInput input, in CarrierEnv env, ref CarrierScratch scratch)
    {
        // The numerics helpers count into this block; it is folded into the scratch counters at the end (TD-071 item 2) and flushed
        // to CounterId.FixDivZero / FixClamped by CarrierCounterFlush.
        Span<long> counts = stackalloc long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(counts);
        PlayerSettings settings = env.Settings;

        // P4 order: (1) input + body yaw, (2) F2 velocity, (3) support carry, (4) gravity, (5) sweep, (6) separation,
        // (7) map clamp, (8) water.
        InputAndYaw(ref state, in input, settings, ref sink, out long wx, out long wz, out bool hasInput);
        HorizontalVelocity(ref state, in input, settings, wx, wz, hasInput, ref sink);
        SupportCarry(ref state);
        CarrierColliders.Gather(ref scratch, in env, in state);   // story 008: once per tick, before the phase-4 ground check reads box tops
        CarrierDepenetrate.Run(ref state, ref scratch, in env);   // story 008: a box that appeared over the carrier, on the fresh candidates
        GravityPlan plan = CarrierGravity.Apply(ref state, in input, in env, in scratch, ref sink);
        Sweep(ref state, in env, ref scratch, ref sink, ref plan);
        Separation(ref state);
        MapClamp(ref state, in env, in scratch, ref sink);
        CarrierWater.Apply(ref state, in env, in scratch, wx, wz, hasInput, ref sink);   // story 009: phase 8, applies from the next tick
        FoldCounters(ref scratch, in sink);
    }

    /// <summary>Adds this step's numerics-helper counts to the scratch counters (saturating at <see cref="int.MaxValue"/>).</summary>
    private static void FoldCounters(ref CarrierScratch scratch, in SimCounterSink sink)
    {
        scratch.FixDivZero = AddSaturating(scratch.FixDivZero, sink.Get(PrimitiveCounter.FixDivZero));
        scratch.FixClamped = AddSaturating(scratch.FixClamped, sink.Get(PrimitiveCounter.FixClamped));
    }

    private static int AddSaturating(int current, long add) => (int)Math.Min((long)current + add, int.MaxValue);

    /// <summary>Phase 1: sprint rules, camera-relative direction (world vector scaled Q15 x Q15) and body yaw.</summary>
    private static void InputAndYaw(
        ref CarrierState state, in CarrierInput input, PlayerSettings settings, ref SimCounterSink sink, out long wx, out long wz, out bool hasInput)
    {
        int mx = input.MoveX;
        int my = input.MoveY;
        hasInput = mx != 0 || my != 0;
        CarrierHeading.ClampInput(ref mx, ref my, ref sink);
        CarrierHeading.ToWorld(mx, my, input.LookYaw, out wx, out wz);
        UpdateSprint(ref state, in input, hasInput);

        if (input.Aiming)
        {
            state.BodyYaw = input.LookYaw;
        }
        else if (hasInput)
        {
            ushort target = CarrierHeading.TargetYaw(wx, wz, state.BodyYaw);
            state.BodyYaw = CarrierHeading.TurnToward(state.BodyYaw, target, CarrierHeading.YawStep(settings.TurnDegPerSec));
        }
    }

    /// <summary>
    /// P3 sprint rules: needs move input and a held key; starts only when Grounded, persists while Airborne; never active while
    /// Swimming, Mounted, Dead or in Panel context.
    /// </summary>
    private static void UpdateSprint(ref CarrierState state, in CarrierInput input, bool hasInput)
    {
        bool blocked = state.Loco is LocoState.Swimming or LocoState.Mounted
            || (input.ContextFlags & (CarrierContextFlags.Dead | CarrierContextFlags.Panel | CarrierContextFlags.Mounted)) != 0;
        bool sprinting = (state.Flags & CarrierFlags.Sprinting) != 0;
        if (blocked || !hasInput || !input.SprintHeld)
        {
            sprinting = false;
        }
        else if (!sprinting && state.Loco == LocoState.Grounded)
        {
            sprinting = true;
        }

        state.Flags = sprinting ? state.Flags | CarrierFlags.Sprinting : state.Flags & ~CarrierFlags.Sprinting;
    }

    /// <summary>Phase 2: F1 speed cap for the gait, camera-relative target, F2.</summary>
    private static void HorizontalVelocity(
        ref CarrierState state, in CarrierInput input, PlayerSettings settings, long wx, long wz, bool hasInput, ref SimCounterSink sink)
    {
        int baseMmS = state.Loco == LocoState.Swimming ? settings.SwimMmPerSec
            : (state.Flags & CarrierFlags.Sprinting) != 0 ? settings.SprintMmPerSec
            : settings.WalkMmPerSec;
        int vRef = CarrierSpeed.Final(baseMmS, input.SpeedPermille, input.BuffAddPm, settings.BuffCapPm, wadePm: CarrierWater.StoredWadePm(in state), ref sink);   // the previous tick's phase-8 result
        int tx = 0;
        int tz = 0;
        if (hasInput)
        {
            CarrierHeading.TargetVelocity(wx, wz, vRef, out tx, out tz, ref sink);
        }

        CarrierAccel.Apply(ref state.Vx, ref state.Vz, tx, tz, vRef, hasInput, state.Loco == LocoState.Airborne, settings, ref sink);
    }

    private static void SupportCarry(ref CarrierState state)
    {
        // Out of M-1a scope (P7).
    }

    /// <summary>Phase 5: own horizontal displacement from the 1/30 mm remainders, swept with phase 4's vertical displacement in sub-steps (story 008 adds boxes).</summary>
    private static void Sweep(ref CarrierState state, in CarrierEnv env, ref CarrierScratch scratch, ref SimCounterSink sink, ref GravityPlan plan)
    {
        int dx = CarrierSweep.HorizontalStep(ref state.RemX, state.Vx, ref sink);
        int dz = CarrierSweep.HorizontalStep(ref state.RemZ, state.Vz, ref sink);
        if (state.Loco == LocoState.Swimming)
        {
            CarrierSweep.SwimMove(ref state, in env, ref scratch, ref sink, dx, dz);   // story 009: walls and ledges stop a swimmer
            return;
        }

        CarrierSweep.Run(ref state, in env, ref scratch, ref sink, dx, dz, ref plan);
    }

    private static void Separation(ref CarrierState state)
    {
        // Out of M-1a scope (P8).
    }

    /// <summary>Phase 7: clamp 1000 mm inside the map rectangle, zero the outward velocity, and re-ground the feet if the clamp moved them.</summary>
    private static void MapClamp(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, ref SimCounterSink sink)
    {
        if (CarrierBounds.Clamp(ref state, env.World))
        {
            CarrierSweep.Settle(ref state, in env, in scratch, ref sink);
        }
    }
}
