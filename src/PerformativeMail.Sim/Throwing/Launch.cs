using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The F1 output and the whole F2 input (ADR-0015 Key Interfaces): launch origin in mm, launch velocity in mm/s and the
/// thrown-item gravity in mm/s^2. Together with the flight age in ticks it fixes every position of the flight through
/// <see cref="Ballistic.PositionAt"/>. Immutable, blittable, 28 B.
/// </summary>
/// <example>
/// <code>
/// var l = new Launch(new Vec3Mm(10_250, 1200, 19_700), new Vec3Mm(0, 2911, -13_695), 9800);
/// Vec3Mm p1 = Ballistic.PositionAt(in l, 1);   // (10 250, 1292, 19 244)
/// </code>
/// </example>
public readonly struct Launch
{
    /// <summary>Launch origin, mm.</summary>
    public readonly Vec3Mm Origin;

    /// <summary>Launch velocity, mm/s.</summary>
    public readonly Vec3Mm Vel;

    /// <summary>Thrown-item gravity, mm/s^2 (<c>throw.g</c> at launch).</summary>
    public readonly int G;

    /// <summary>Creates a launch.</summary>
    /// <param name="origin">Launch origin, mm.</param>
    /// <param name="vel">Launch velocity, mm/s.</param>
    /// <param name="g">Gravity, mm/s^2.</param>
    public Launch(Vec3Mm origin, Vec3Mm vel, int g)
    {
        Origin = origin;
        Vel = vel;
        G = g;
    }
}
