using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The <see cref="IPresentedPromptSource"/> the game uses (story 012): <see cref="Update"/> copies the focused anchor, mode and
/// sequence of <see cref="IInteractionQuery.Current"/>. <c>GameLoop</c> calls it once per frame right after <c>Present()</c>
/// (story 028), so the next frame's presses carry the prompt the HUD just drew. Holds no reference to the snapshot; 0 B per call.
/// </summary>
/// <example>
/// <code>
/// var latch = new PresentedPromptLatch(interactionQuery);
/// // after Present():
/// latch.Update();
/// </code>
/// </example>
public sealed class PresentedPromptLatch : IPresentedPromptSource
{
    private readonly IInteractionQuery _query;
    private PresentedPrompt _prompt;

    /// <summary>Creates a latch with nothing presented.</summary>
    /// <param name="query">The published interaction snapshot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    public PresentedPromptLatch(IInteractionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
    }

    /// <summary>Latches the snapshot just presented. Call after the HUD drew the prompt.</summary>
    public void Update()
    {
        ref readonly InteractionSnapshot s = ref _query.Current;
        _prompt = new PresentedPrompt(s.Focused, s.Seq, s.Mode);
    }

    /// <inheritdoc />
    public bool TryGet(out PresentedPrompt prompt)
    {
        prompt = _prompt;
        return !_prompt.Anchor.IsNone;
    }
}
