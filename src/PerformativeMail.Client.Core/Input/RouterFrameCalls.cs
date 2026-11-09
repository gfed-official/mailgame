using PerformativeMail.Client.Core.Net;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's per-frame calls in the order ADR-0012 sections 4 and 6 bind (input-controls story 028): before
/// <c>RunFrame</c>, <see cref="InputRouter.DispatchImmediates"/> then <see cref="InputRouter.FlushToLatch"/> into the session's
/// <see cref="InputLatch"/>; after <c>Present</c>, <see cref="PresentedPromptLatch.Update"/>, so the next frame's Interact
/// presses carry the prompt the HUD just drew (EPIC.md's "presented snapshot" seam). The host shell's frame sequence calls
/// <see cref="BeforeRunFrame"/> from a pre-frame hook and <see cref="AfterPresent"/> from a post-present hook; this type holds
/// no input logic. 0 B per call after the router's own work. Main thread only.
/// </summary>
/// <example>
/// <code>
/// var calls = RouterFrameCalls.ForSession(router, prompt, session);
/// calls.BeforeRunFrame();   // pre-frame hook, after the adapter's motion flush
/// // RunFrame, render-clock sample, Present
/// calls.AfterPresent();     // post-present hook
/// </code>
/// </example>
/// <param name="router">The router.</param>
/// <param name="latch">The push side of the owning client's input latch.</param>
/// <param name="prompt">The presented-prompt latch the router was built with.</param>
public sealed class RouterFrameCalls(InputRouter router, IInputLatchWriter latch, PresentedPromptLatch prompt)
{
    private readonly InputRouter _router = router ?? throw new ArgumentNullException(nameof(router));
    private readonly IInputLatchWriter _latch = latch ?? throw new ArgumentNullException(nameof(latch));
    private readonly PresentedPromptLatch _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

    /// <summary>The calls over the booted session's own client latch (the host client, ADR-0011 loopback).</summary>
    /// <param name="router">The router.</param>
    /// <param name="prompt">The presented-prompt latch the router was built with.</param>
    /// <param name="session">The booted host session.</param>
    /// <returns>The calls.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static RouterFrameCalls ForSession(InputRouter router, PresentedPromptLatch prompt, HostSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new RouterFrameCalls(router, session.Client.InputWriter, prompt);
    }

    /// <summary>Immediates to their sinks, then this frame's edges and state to the latch; once per frame, before <c>RunFrame</c>.</summary>
    public void BeforeRunFrame()
    {
        _router.DispatchImmediates();
        _router.FlushToLatch(_latch);
    }

    /// <summary>Latches the interaction prompt just presented; once per frame, after <c>Present</c>.</summary>
    public void AfterPresent() => _prompt.Update();
}
