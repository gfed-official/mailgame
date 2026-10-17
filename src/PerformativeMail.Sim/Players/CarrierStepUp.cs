namespace PerformativeMail.Sim.Players;

/// <summary>
/// Step-up onto a low box (GDD <c>player-character-movement.md</c> P4 step-up; ADR-0013 section 4; story 008 part B, PCM-P4-1b). Called
/// from <see cref="CarrierSlide.Resolve"/> for each overlapped candidate before it slides: a carrier is lifted onto the box top
/// (x/z unchanged) instead of sliding when all of these hold: it is <see cref="LocoState.Grounded"/> (never while Airborne); the top is
/// above the feet by at most <c>stepUpMm</c> (400); and the whole capsule standing on the top (<c>top .. top + capsuleHeightMm</c>, 1400)
/// overlaps no candidate, which is the "top + 1400 clear of every other candidate's bottom" headroom rule (it also refuses a step
/// that would put the capsule into a wall). Blocked tiles have no top (infinite height), so they are never stepped onto. Pure,
/// allocation-free, no statics.
/// </summary>
/// <example><code>if (CarrierStepUp.TryStepUp(ref state, in env, in scratch, in box)) { continue; }</code></example>
public static class CarrierStepUp
{
    /// <summary>Lifts a Grounded carrier onto <paramref name="box"/>'s top when the step-up rule allows it.</summary>
    /// <param name="state">The carrier; only <see cref="CarrierState.Y"/> changes.</param>
    /// <param name="env">Read-only ports and settings (<c>stepUpMm</c>, capsule size).</param>
    /// <param name="scratch">The tick's candidates (the headroom check).</param>
    /// <param name="box">The overlapped box.</param>
    /// <returns>True when the carrier now stands on the top.</returns>
    public static bool TryStepUp(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, in ColliderBox box)
    {
        if (state.Loco != LocoState.Grounded)
        {
            return false;
        }

        long rise = box.MaxY - state.Y;
        if (rise <= 0 || rise > env.Settings.StepUpMm)
        {
            return false;
        }

        if (CarrierColliders.FirstOverlap(in env, in scratch, state.X, box.MaxY, state.Z, out _))
        {
            return false;   // no headroom (or another box in the way)
        }

        state.Y = (int)box.MaxY;
        return true;
    }
}
