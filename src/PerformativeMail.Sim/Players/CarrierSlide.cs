using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Step (2) of every sub-step: the box pass of the horizontal move (GDD <c>player-character-movement.md</c> P4; ADR-0013 section 4;
/// TR-movement-006; story 008). The sub-step's horizontal displacement (already resolved against terrain) is replayed from the sub-step
/// start in increments no longer than the capsule radius (a diagonal 300 + 300 mm sub-step is 424 mm, which could carry the centre
/// across a thin box; an increment of at most <c>r</c> from a clear position can only touch a rectangle, never cross it). After each
/// increment overlaps are resolved by at most the sub-step's remaining slide budget (<c>slideIterations</c>, 3, shared with the terrain
/// slides): push out by the <see cref="CylinderVsBox.Mtv"/>, then remove the velocity component into the normal (exact on axis faces,
/// Q15 <see cref="Fix.MulDiv"/> on a corner). If the budget runs out while still overlapping, the increment is undone (the carrier
/// stays where it was clear). Pure, allocation-free, no statics.
/// </summary>
/// <example><code>CarrierSlide.SweepBoxes(ref state, in env, in scratch, startX, startZ, maxSlides, ref slides, ref sink);</code></example>
public static class CarrierSlide
{
    private const int MaxIncrements = 64;

    /// <summary>
    /// Replays the move from (<paramref name="startX"/>, <paramref name="startZ"/>) to the current feet
    /// position against the tick's candidates. No candidates: nothing happens (terrain-only behaviour is unchanged).
    /// </summary>
    /// <param name="state">The carrier; feet and horizontal (or, on a y push, vertical) velocity updated.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's candidates.</param>
    /// <param name="startX">Feet X at the start of the sub-step.</param>
    /// <param name="startZ">Feet Z at the start of the sub-step.</param>
    /// <param name="maxSlides">The per-sub-step slide budget.</param>
    /// <param name="slides">Slides used in this sub-step so far; incremented per push-out.</param>
    /// <param name="sink">Counter sink for the division helpers.</param>
    public static void SweepBoxes(
        ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, int startX, int startZ, int maxSlides, ref int slides, ref SimCounterSink sink)
    {
        if (!CarrierColliders.HasAny(in scratch))
        {
            return;
        }

        int tx = (int)((long)state.X - startX);
        int tz = (int)((long)state.Z - startZ);
        state.X = startX;
        state.Z = startZ;
        int k = Increments(tx, tz, Math.Max(1, env.Settings.CapsuleRadiusMm));
        for (int j = 0; j < k; j++)
        {
            int px = state.X;
            int py = state.Y;
            int pz = state.Z;
            state.X = AddClamped(state.X, CarrierSweep.Part(tx, k, j));
            state.Z = AddClamped(state.Z, CarrierSweep.Part(tz, k, j));
            Resolve(ref state, in env, in scratch, px, py, pz, maxSlides, ref slides, ref sink);
        }
    }

    /// <summary>
    /// Resolves overlaps at the current feet position: up to the remaining budget, push out of the first overlapped candidate and remove
    /// the velocity into its normal; undo to (<paramref name="prevX"/>, <paramref name="prevY"/>, <paramref name="prevZ"/>) when the
    /// budget is spent and an overlap remains.
    /// </summary>
    /// <param name="state">The carrier.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's candidates.</param>
    /// <param name="prevX">Last clear feet X (also the exit-side reference of a centre-inside push).</param>
    /// <param name="prevY">Last clear feet Y.</param>
    /// <param name="prevZ">Last clear feet Z.</param>
    /// <param name="maxSlides">The per-sub-step slide budget.</param>
    /// <param name="slides">Slides used so far.</param>
    /// <param name="sink">Counter sink.</param>
    public static void Resolve(
        ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, int prevX, int prevY, int prevZ, int maxSlides, ref int slides, ref SimCounterSink sink)
    {
        long r = env.Settings.CapsuleRadiusMm;
        long h = env.Settings.CapsuleHeightMm;
        while (CarrierColliders.FirstOverlap(in env, in scratch, state.X, state.Y, state.Z, out ColliderBox box))
        {
            // Step-up (story 008 part B): a Grounded carrier whose overlapped box top is within stepUpMm of the feet, with headroom,
            // is lifted onto the top instead of sliding. Each step strictly raises the feet to a candidate top, so the loop terminates.
            if (CarrierStepUp.TryStepUp(ref state, in env, in scratch, in box))
            {
                continue;
            }

            if (slides >= maxSlides)
            {
                state.X = prevX;
                state.Y = prevY;
                state.Z = prevZ;
                return;
            }

            slides++;
            CylinderContact c = CylinderVsBox.Mtv(state.X, state.Y, state.Z, r, h, prevX, prevZ, in box);
            state.X = AddClamped(state.X, c.Dx);
            state.Y = AddClamped(state.Y, c.Dy);
            state.Z = AddClamped(state.Z, c.Dz);
            RemoveIntoNormal(ref state, in c, ref sink);
        }
    }

    /// <summary><c>v -= n (v . n)</c> when <c>v . n &lt; 0</c>: exact on axis faces, Q15 on a corner (one <see cref="Fix.MulDiv"/> per component).</summary>
    /// <param name="state">The carrier.</param>
    /// <param name="c">The contact.</param>
    /// <param name="sink">Counter sink.</param>
    public static void RemoveIntoNormal(ref CarrierState state, in CylinderContact c, ref SimCounterSink sink)
    {
        switch (c.Axis)
        {
            case ContactAxis.X:
                if ((long)state.Vx * c.Sign < 0)
                {
                    state.Vx = 0;
                }

                break;
            case ContactAxis.Z:
                if ((long)state.Vz * c.Sign < 0)
                {
                    state.Vz = 0;
                }

                break;
            case ContactAxis.Y:
                if ((long)state.Vy * c.Sign < 0)
                {
                    state.Vy = 0;
                    state.RemY = 0;
                }

                break;
            case ContactAxis.Corner:
                long dot = ((long)state.Vx * c.NxQ15) + ((long)state.Vz * c.NzQ15);
                if (dot < 0)
                {
                    const long OneSq = 1L << 30;
                    state.Vx = (int)Math.Clamp(state.Vx - Fix.MulDiv(c.NxQ15, dot, OneSq, ref sink), int.MinValue, int.MaxValue);
                    state.Vz = (int)Math.Clamp(state.Vz - Fix.MulDiv(c.NzQ15, dot, OneSq, ref sink), int.MinValue, int.MaxValue);
                }

                break;
        }
    }

    /// <summary>The increment count: the smallest <c>k</c> with <c>k^2 r^2 &gt;= |t|^2</c> (0 for no move).</summary>
    private static int Increments(long tx, long tz, long r)
    {
        long len2 = (tx * tx) + (tz * tz);
        int k = 0;
        while (k < MaxIncrements && (long)k * k * r * r < len2)
        {
            k++;
        }

        return k;
    }

    private static int AddClamped(int a, long b) => (int)Math.Clamp(a + b, int.MinValue, int.MaxValue);
}
