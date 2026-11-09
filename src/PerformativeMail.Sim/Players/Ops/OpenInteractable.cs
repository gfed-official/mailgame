using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Ops;

/// <summary>
/// The Open verb's <see cref="IInteractable"/> for container anchors (grid-inventory story 019; GDD R10: "the Intake and PO Depot consoles
/// are the anchors, not the building"). Players registers it on Inventory's behalf, which cannot reach <see cref="IPlayersApi"/>
/// (<c>SystemGraph</c> declares Player Character → Grid Inventory). Always available (R10: no ownership checks); no hold, no cancel and no
/// prompt bytes. <see cref="TryBuildRequest"/> builds the one <see cref="PlayerOpKinds.Open"/> request from the latch: the anchor entity's
/// value is the container id (<see cref="OpenOp.AnchorOf"/>) and the claim is the press-time position. Every hook is pure and
/// allocation-free.
/// </summary>
/// <example><code>RegisterResult r = players.RegisterAnchor(AnchorDef.For(OpenOp.AnchorOf(intake), console, Verb.Open), new OpenInteractable());</code></example>
internal sealed class OpenInteractable : IInteractable
{
    /// <summary>Always available.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="reason"><see cref="StringId.None"/>.</param>
    /// <returns>True.</returns>
    public bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return true;
    }

    /// <summary>Never cancels (Open is a Tap).</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="reason"><see cref="StringId.None"/>.</param>
    /// <returns>False.</returns>
    public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return false;
    }

    /// <summary>Writes no prompt bytes (the HUD shows the verb alone).</summary>
    /// <param name="c">The call context.</param>
    /// <param name="args">The destination bytes (left untouched).</param>
    public void WritePrompt(in InteractionContext c, ref PromptArgs args)
    {
    }

    /// <summary>Builds the <see cref="PlayerOpKinds.Open"/> request: the latched anchor's container and the claimed position.</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="op">The op to fill.</param>
    /// <returns>True.</returns>
    public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op)
    {
        op.Kind = PlayerOpKinds.Open;
        op.SetPayload(new OpenPayload
        {
            Container = latch.Anchor.Entity.Value,
            ClaimedX = latch.Claimed.X,
            ClaimedY = latch.Claimed.Y,
            ClaimedZ = latch.Claimed.Z,
        });
        return true;
    }

    /// <summary>Latches nothing.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="latch">The latch being built (left untouched).</param>
    public void OnPress(in InteractionContext c, ref InteractionLatch latch)
    {
    }
}
