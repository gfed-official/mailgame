namespace PerformativeMail.Game.Hosting;

/// <summary>
/// A pre-frame hook (story godot-host-shell 003 decision 1, TD-110; ADR-0012 sections 4/6): runs every frame before
/// <c>RunFrame</c>, in registration order. Registered explicitly through <c>GameLoop.RegisterPreFrameHook</c> before the
/// first frame (no reflection discovery). Input-controls 028 registers <c>DispatchImmediates()</c> then
/// <c>FlushToLatch(latch)</c>. A hook that throws faults the loop. Main thread only.
/// </summary>
/// <example>
/// <code>
/// sealed class Dispatch(InputRouter router) : IPreFrameHook { public void OnPreFrame() =&gt; router.DispatchImmediates(); }
/// loop.RegisterPreFrameHook(new Dispatch(router));
/// </code>
/// </example>
public interface IPreFrameHook
{
    /// <summary>Called once per frame, before <c>RunFrame</c>.</summary>
    void OnPreFrame();
}
