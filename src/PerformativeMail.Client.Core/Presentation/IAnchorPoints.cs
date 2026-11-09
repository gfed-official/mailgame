using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// Where an interaction anchor is, as Presentation reads it from <see cref="PublishedStates"/> (player-character story 026): the
/// snapshot carries only the <see cref="AnchorRef"/>, so the focus feed looks the point up here and never touches the anchor registry.
/// Read-only; the client's anchor registry (<c>AnchorRegistry.TryGet</c> + <c>DefAt(i).Point</c>) is the intended implementation. Read
/// on the main thread.
/// </summary>
/// <example><code>if (points.TryGetPoint(snapshot.Focused, out Vec3Mm p)) { Vector3 m = new(p.X / 1000f, p.Y / 1000f, p.Z / 1000f); }</code></example>
public interface IAnchorPoints
{
    /// <summary>The anchor's point, if it is registered.</summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="point">The point, mm.</param>
    /// <returns>True when the anchor is registered.</returns>
    bool TryGetPoint(AnchorRef anchor, out Vec3Mm point);
}
