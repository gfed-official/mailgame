using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The interaction prompt last presented on screen (GDD G1; ADR-0012 section 3; ADR-0014 section 7): the focused anchor, its mode
/// and the snapshot <see cref="InteractionSnapshot.Seq"/> it came from. Values only; <c>default</c> is "nothing presented".
/// </summary>
/// <param name="Anchor">The focused anchor the prompt named.</param>
/// <param name="Seq">The <see cref="InteractionSnapshot.Seq"/> of the presented snapshot.</param>
/// <param name="Mode">The anchor's mode (Tap, Hold or Channel).</param>
/// <example><code>var p = new PresentedPrompt(new AnchorRef(new EntityId(0x0200_0001u), 0), 9, InteractMode.Hold);</code></example>
public readonly record struct PresentedPrompt(AnchorRef Anchor, ushort Seq, InteractMode Mode);

/// <summary>
/// The port the router reads the presented prompt through (story 012). ADR-0014 section 7 leaves "the presented snapshot" to
/// ADR-0016, which defines no mechanism; <see cref="PresentedPromptLatch"/> supplies it. An Interact press edge is stamped from
/// <see cref="TryGet"/> at event time, never from the live focus.
/// </summary>
/// <example><code>if (source.TryGet(out PresentedPrompt p)) { uint entity = p.Anchor.Entity.Value; }</code></example>
public interface IPresentedPromptSource
{
    /// <summary>The last presented prompt.</summary>
    /// <param name="prompt">The prompt, or <c>default</c> when none.</param>
    /// <returns>False when nothing with a target has been presented (first frame, or no focus).</returns>
    bool TryGet(out PresentedPrompt prompt);
}
