using Godot;
using PerformativeMail.Client.Core.Presentation;

namespace PerformativeMail.Game.Views.Player;

/// <summary>
/// The radians side of BAM yaw at the view boundary (player-character story 022, ADR-0013 section 3): <c>Rotation.Y = bam × 2π / 65536</c>
/// with no sign flip (BAM 0 faces −Z, 16384 faces −X), and camera yaw → BAM rounded half away from zero then wrapped by
/// <see cref="YawMath.Wrap"/> (unchecked <c>&amp; 0xFFFF</c>, so negative yaws wrap). The integer helpers are <see cref="YawMath"/>.
/// </summary>
/// <example><code>view.Rotation = new Vector3(0f, YawRadians.ToRotationY(pose.Yaw), 0f); ushort look = YawRadians.ToBam(camera.Rotation.Y);</code></example>
public static class YawRadians
{
    /// <summary>The node's <c>Rotation.Y</c> for a BAM yaw.</summary>
    /// <param name="bam">The yaw, BAM.</param>
    /// <returns>Radians in [0, 2π).</returns>
    public static float ToRotationY(ushort bam) => bam * Mathf.Tau / 65536f;

    /// <summary>A yaw in radians (any range, e.g. a camera's <c>Rotation.Y</c>) as BAM.</summary>
    /// <param name="radians">The yaw, radians.</param>
    /// <returns>The yaw, BAM.</returns>
    public static ushort ToBam(float radians) => YawMath.Wrap((long)Math.Round(radians * 65536.0 / Math.Tau, MidpointRounding.AwayFromZero));
}
