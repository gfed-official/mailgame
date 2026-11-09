using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// The yaw that turns a view to face a lot's <see cref="FaceDir"/> (godot-host-shell story 009). ADR-0013 section 3 convention: BAM 0
/// faces -Z, <c>forward(θ) = (-sin θ, -cos θ)</c> in (x, z), positive turns counter-clockwise seen from +Y, so the view sets
/// <c>Rotation.Y = bam × 2π / 65536</c> with no sign flip.
/// </summary>
/// <example><code>view.Rotation = new Vector3(0, FaceYaw.ToBam(lot.Face) * Mathf.Tau / 65536f, 0);</code></example>
public static class FaceYaw
{
    /// <summary>The BAM yaw whose forward is the face direction.</summary>
    /// <param name="face">The direction.</param>
    /// <returns>0 (-Z), 16384 (-X), 32768 (+Z) or 49152 (+X).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not a <see cref="FaceDir"/>.</exception>
    public static ushort ToBam(FaceDir face) => face switch
    {
        FaceDir.NegZ => 0,
        FaceDir.NegX => 16384,
        FaceDir.PosZ => 32768,
        FaceDir.PosX => 49152,
        _ => throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown face direction."),
    };
}
