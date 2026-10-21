using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// The Deliver verb's <see cref="IInteractable"/> (Manual Delivery GDD MD1, F2, MD9; TR-delivery-001, TR-delivery-015; ADR-0014
/// sections 1 and 4). One instance serves every Deliver anchor (<see cref="DeliverAnchors.RegisterAll"/>); the hooks tell the
/// anchors apart by <see cref="InteractionContext.Anchor"/>. Every hook is pure: it reads its inputs and the injected stat sheet,
/// mutates nothing, raises nothing and allocates nothing. <see cref="InteractionContext"/> carries no stat sheet, so the sheet comes
/// in through the constructor.
/// <para>
/// Story 001 fills <see cref="GetHoldTicks"/> only: the shared <c>holdEff</c> (<see cref="Hold.HoldTicks(int, int, int, in HoldSettings)"/>)
/// with the actor's <see cref="Stat.DeliverInsertPm"/>, read on every call and never cached. The other hooks return fixed values
/// until stories 003 (<see cref="IsAvailable"/>), 004 (<see cref="OnPress"/>, <see cref="ShouldCancel"/>,
/// <see cref="TryBuildRequest"/>) and 010 (<see cref="WritePrompt"/>) fill them.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var impl = new DeliverInteractable(statSheet);
/// RegisterResult r = DeliverAnchors.RegisterAll(players, registry, content, impl);
/// </code>
/// </example>
internal sealed class DeliverInteractable : IInteractable
{
    private readonly IStatSheet _stats;

    /// <summary>Creates the shared Deliver hooks over <paramref name="stats"/>.</summary>
    /// <param name="stats">The stat sheet read for <see cref="Stat.DeliverInsertPm"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
    public DeliverInteractable(IStatSheet stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        _stats = stats;
    }

    /// <summary>Not available until story 003 (MD2) defines the rule: always false, with no reason key.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="reason"><see cref="StringId.None"/>.</param>
    /// <returns>False.</returns>
    public bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return false;
    }

    /// <summary>
    /// The Deliver hold length (F2): <c>Hold.HoldTicks(holdBaseMs, insertPm, holdSettingMs, settings)</c> with
    /// <c>insertPm</c> the actor's personal <see cref="Stat.DeliverInsertPm"/> (the StatDef clamps it to 0 to 750; the shared
    /// function clamps again defensively). A pure function of the context and the sheet's current value.
    /// </summary>
    /// <param name="latch">The latch (unused: the length depends only on the anchor's base, the setting and the stat).</param>
    /// <param name="c">The call context.</param>
    /// <returns>The hold length in ticks.</returns>
    /// <example><code>int ticks = impl.GetHoldTicks(in latch, in c);   // 6 for base 400 at 400 ms with insertPm 500</code></example>
    public int GetHoldTicks(in InteractionLatch latch, in InteractionContext c)
    {
        int insertPm = _stats.Read(Stat.DeliverInsertPm, StatSheet.NoQualifier, c.Actor);
        HoldSettings settings = c.HoldSettings;   // a property read is a copy; Hold.HoldTicks takes it by in
        return Hold.HoldTicks(c.HoldBaseMs, insertPm, c.HoldSettingMs, in settings);
    }

    /// <summary>Never cancels until story 004 defines the cancel rule.</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="reason"><see cref="StringId.None"/>.</param>
    /// <returns>False.</returns>
    public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return false;
    }

    /// <summary>Writes nothing until story 010 defines the Deliver prompt.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="args">The destination bytes (left untouched).</param>
    public void WritePrompt(in InteractionContext c, ref PromptArgs args)
    {
    }

    /// <summary>Builds no request until story 004 defines the Deliver op.</summary>
    /// <param name="latch">The latch.</param>
    /// <param name="c">The call context.</param>
    /// <param name="op">The op (left untouched).</param>
    /// <returns>False.</returns>
    public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op) => false;

    /// <summary>Latches nothing until story 004 defines the press-tick payload.</summary>
    /// <param name="c">The call context.</param>
    /// <param name="latch">The latch being built (left untouched).</param>
    public void OnPress(in InteractionContext c, ref InteractionLatch latch)
    {
    }
}
