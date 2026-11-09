using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The client mirror reads the aim preview needs beyond the selected entry (throwing-catching story 004): destination facts for
/// <c>MailRules.Evaluate</c>, a version of the destinations' phase and open state, and the wallet (a cache-key input only; the end class never
/// reads it). Throwing-defined and narrow: mail-destinations story 010's <c>DestinationMirror</c> and the wallet mirror implement it through
/// one adapter when they land; tests use a double.
/// </summary>
/// <example><code>if (mirror.TryGetDestination(contact.ReceiverKey, now, now + age, out DestinationFacts d)) { /* Evaluate */ }</code></example>
public interface IThrowPreviewMirror
{
    /// <summary>Changes whenever any destination's phase or open state (or the deliveries state) changes.</summary>
    uint DestinationVersion { get; }

    /// <summary>The mirrored wallet.</summary>
    Cents Wallet { get; }

    /// <summary>
    /// The facts of the destination whose catch-volume key is <paramref name="destinationKey"/>, with <see cref="DestinationFacts.State"/>
    /// the effective state Mail's helper (<see cref="DeliveriesStateRules.Effective"/>) gives a throw released at
    /// <paramref name="actionStartTick"/> and landing at <paramref name="applyTick"/>, as the host will judge it.
    /// </summary>
    /// <param name="destinationKey">The destination's <c>EntityId.Value</c> (the mailbox receiver key).</param>
    /// <param name="actionStartTick">The release tick (the current client tick).</param>
    /// <param name="applyTick">The landing tick.</param>
    /// <param name="facts">The facts.</param>
    /// <returns>False when no mirrored destination has the key.</returns>
    bool TryGetDestination(uint destinationKey, uint actionStartTick, uint applyTick, out DestinationFacts facts);
}
