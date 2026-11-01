using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's read port onto Mail's destination registry (ADR-0017 catch volumes; ADR-0015 section 4; throwing-catching story
/// 003). <see cref="ReceiverViewBuilder"/> reads the mailbox catch volumes through it, never from content. Ordinals run
/// 0 to <see cref="Count"/> - 1 in lot order, which is also ascending destination id (World allocates the ids contiguously in
/// lot order), so ascending ordinal is ascending <see cref="VolumeReceiver.Key"/>. The Sim adapter is
/// <see cref="MailCatchVolumeSource"/>; the client adapter over <c>DestinationMirror</c> is story 004's.
/// </summary>
/// <example>
/// <code>
/// ICatchVolumeSource source = new MailCatchVolumeSource(mail);
/// var mbA = new VolumeReceiver(source.CatchVolumeAt(0), source.DestinationAt(0).Value);
/// </code>
/// </example>
public interface ICatchVolumeSource
{
    /// <summary>The number of destinations.</summary>
    int Count { get; }

    /// <summary>The destination entity at <paramref name="ordinal"/>.</summary>
    /// <param name="ordinal">0 to <see cref="Count"/> - 1.</param>
    /// <returns>The destination's entity id.</returns>
    EntityId DestinationAt(int ordinal);

    /// <summary>The world catch box at <paramref name="ordinal"/> (inclusive corners).</summary>
    /// <param name="ordinal">0 to <see cref="Count"/> - 1.</param>
    /// <returns>The catch volume.</returns>
    AabbMm CatchVolumeAt(int ordinal);
}
