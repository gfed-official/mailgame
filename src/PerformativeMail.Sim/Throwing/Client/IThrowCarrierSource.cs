using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The local carrier facts the aim preview reads each client tick (throwing-catching story 004): the predicted feet (the position a
/// <c>ThrowRelease</c> claims), the body yaw, the camera aim and the published <see cref="CarrierStatus"/> (its <c>Aiming</c> and
/// <c>Swimming</c>). Throwing-defined and narrow, as <c>ICarrierFocusSource</c> is for focus: player-character story 020's client
/// carrier view implements it after <c>ClientStepId.CarrierStep</c>; tests use a double.
/// </summary>
/// <example><code>ThrowAim.Quantise(source.AimYaw, source.AimPitch, loft, out short dx, out short dy, out short dz);</code></example>
public interface IThrowCarrierSource
{
    /// <summary>The predicted feet this tick, mm.</summary>
    Vec3Mm Feet { get; }

    /// <summary>The body yaw, BAM (F1's defence branch only).</summary>
    ushort BodyYaw { get; }

    /// <summary>The camera yaw, BAM (0 faces −Z).</summary>
    ushort AimYaw { get; }

    /// <summary>The camera pitch, signed BAM (positive looks up).</summary>
    short AimPitch { get; }

    /// <summary>The carrier's status this tick.</summary>
    ref readonly CarrierStatus Status { get; }
}
