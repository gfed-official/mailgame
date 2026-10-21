namespace PerformativeMail.Game.Hosting;

/// <summary>
/// A post-present hook (story godot-host-shell 003 decision 1, TD-110): runs every frame after <c>Present</c>, in
/// registration order. Registered explicitly through <c>GameLoop.RegisterPostPresentHook</c> before the first frame (no
/// reflection discovery). Input-controls 028 registers the <c>PresentedPromptLatch</c> update. A hook that throws faults
/// the loop. Main thread only.
/// </summary>
/// <example>
/// <code>
/// sealed class LatchUpdate(PresentedPromptLatch latch) : IPostPresentHook { public void OnPostPresent() =&gt; latch.Update(); }
/// loop.RegisterPostPresentHook(new LatchUpdate(latch));
/// </code>
/// </example>
public interface IPostPresentHook
{
    /// <summary>Called once per frame, after <c>Present</c>.</summary>
    void OnPostPresent();
}
