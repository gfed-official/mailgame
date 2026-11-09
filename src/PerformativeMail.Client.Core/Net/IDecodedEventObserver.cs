using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Sees every event record <see cref="ClientAdapter"/> decodes into its inbox, in the frame it is decoded (ADR-0016 section 5;
/// godot-host-shell story 006). The shell sets <c>PresentationEventStream</c> here at boot step 7. Called inside
/// <see cref="ClientAdapter.PumpInbound"/>, in batch-tick then ordinal order; replies are not events and are not observed.
/// </summary>
/// <example><code>adapter.DecodedEvents = presentationEventStream;</code></example>
public interface IDecodedEventObserver
{
    /// <summary>One decoded record, just added to the inbox.</summary>
    /// <param name="sourceTick">The host tick of its batch.</param>
    /// <param name="entry">The decoded entry.</param>
    void OnDecoded(uint sourceTick, in EventEntry entry);
}
