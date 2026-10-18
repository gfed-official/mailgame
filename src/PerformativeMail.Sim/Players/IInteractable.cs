using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The port a verb's owner implements for each anchor it registers (ADR-0014 section 1; TR-movement-011). Defined by Players;
/// Players calls only these pure hooks. Every hook MUST be pure: no mutation of owner state, no events, no RNG, no allocation.
/// They read the owner's state (host state on the host, the owner's ClientLayer mirror on a client). Every parameter is
/// <c>in</c>, <c>ref</c>, <c>out</c> or a value type, never a class reference (an architecture test checks this). Owner effects
/// happen only through requests (ops) or the owner's own phases.
/// </summary>
/// <example>
/// <code>
/// sealed class DoorAnchor : IInteractable
/// {
///     public bool IsAvailable(in InteractionContext c, out StringId reason) { reason = StringId.None; return true; }
///     // ...the other hooks...
/// }
/// </code>
/// </example>
public interface IInteractable
{
    /// <summary>Whether the verb can be used now.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="reason">When false, the unavailable-reason key; <see cref="StringId.None"/> when true.</param>
    /// <returns>True when available.</returns>
    /// <example><code>bool ok = impl.IsAvailable(in c, out StringId why);</code></example>
    bool IsAvailable(in InteractionContext c, out StringId reason);

    /// <summary>
    /// The hold length in ticks, latched at press. The default is the shared function:
    /// <c>Hold.HoldTicks(c.HoldBaseMs, 0, c.HoldSettingMs, c.HoldSettings)</c>; a verb that needs an <c>insertPm</c> overrides it
    /// and still calls <see cref="Hold.HoldTicks(int, int, int, in HoldSettings)"/>.
    /// </summary>
    /// <param name="latch">The latch (anchor and press-tick payload).</param>
    /// <param name="c">The call context.</param>
    /// <returns>The hold length in ticks.</returns>
    /// <example><code>int ticks = impl.GetHoldTicks(in latch, in c);   // 12 for 400/400</code></example>
    int GetHoldTicks(in InteractionLatch latch, in InteractionContext c)
    {
        HoldSettings settings = c.HoldSettings;   // a property read is a copy; Hold.HoldTicks takes it by in
        return Hold.HoldTicks(c.HoldBaseMs, 0, c.HoldSettingMs, in settings);
    }

    /// <summary>Whether an in-progress hold must end now (for example the destination's branch changed).</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="reason">When true, the cancel reason key.</param>
    /// <returns>True to cancel.</returns>
    /// <example><code>if (impl.ShouldCancel(in latch, in c, out StringId why)) { /* cancel */ }</code></example>
    bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason);

    /// <summary>Writes the owner's 32 opaque prompt bytes for the HUD.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="args">The destination bytes.</param>
    /// <example><code>impl.WritePrompt(in c, ref args);</code></example>
    void WritePrompt(in InteractionContext c, ref PromptArgs args);

    /// <summary>Builds the one request for a completed press or hold (client only).</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="op">The op to fill.</param>
    /// <returns>True when a request was built.</returns>
    /// <example><code>if (impl.TryBuildRequest(in latch, in c, ref op)) { /* send */ }</code></example>
    bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op);

    /// <summary>Fills <see cref="InteractionLatch.OwnerPayload"/> at press (the press-tick branch); pure with respect to owner state.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="latch">The latch being built.</param>
    /// <example><code>impl.OnPress(in c, ref latch);</code></example>
    void OnPress(in InteractionContext c, ref InteractionLatch latch);
}
