using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// One sent release as the thrower's client saw it (throwing-catching story 007), for the trail and telemetry (stories 016, 020): the
/// sender's own release number, the client tick of the release, the class in the state at the release frame and how long it had been red.
/// The origin is always unknown to be clamped on the client (the host decides, check 9). Values only.
/// </summary>
/// <example><code>bool red = record.ClassAtRelease == EndClass.Misdeliver;</code></example>
public readonly struct ReleaseRecord
{
    /// <summary>The sender's release number, from 1.</summary>
    public readonly uint Seq;

    /// <summary>The client tick of the release.</summary>
    public readonly uint ClientTick;

    /// <summary>The preview's end class at the release frame.</summary>
    public readonly EndClass ClassAtRelease;

    /// <summary><c>(clientTick − RedSinceTick) × 1000 / 30</c> (floor) when the class was Misdeliver, else 0.</summary>
    public readonly int RedAgeAtReleaseMs;

    /// <summary>Always 1 on the client: whether the host clamped the origin is not known here.</summary>
    public readonly byte OriginClampedUnknown;

    /// <summary>The claimed feet sent.</summary>
    public readonly Vec3Mm Feet;

    /// <summary>Direction x sent.</summary>
    public readonly short DirX;

    /// <summary>Direction y sent.</summary>
    public readonly short DirY;

    /// <summary>Direction z sent.</summary>
    public readonly short DirZ;

    /// <summary>Creates a record.</summary>
    /// <param name="seq">The release number.</param>
    /// <param name="clientTick">The client tick.</param>
    /// <param name="classAtRelease">The class at release.</param>
    /// <param name="redAgeAtReleaseMs">The red age, ms.</param>
    /// <param name="feet">The claimed feet.</param>
    /// <param name="dirX">Direction x.</param>
    /// <param name="dirY">Direction y.</param>
    /// <param name="dirZ">Direction z.</param>
    public ReleaseRecord(uint seq, uint clientTick, EndClass classAtRelease, int redAgeAtReleaseMs, Vec3Mm feet, short dirX, short dirY, short dirZ)
    {
        Seq = seq;
        ClientTick = clientTick;
        ClassAtRelease = classAtRelease;
        RedAgeAtReleaseMs = redAgeAtReleaseMs;
        OriginClampedUnknown = 1;
        Feet = feet;
        DirX = dirX;
        DirY = dirY;
        DirZ = dirZ;
    }
}
