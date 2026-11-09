using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The client's facts constructors (ADR-0016 section 3; mail-destinations story 010): the same <see cref="DestinationFacts"/> and
/// <see cref="UnitFacts"/> the host builds with its registry and mail table, built here from the <see cref="DestinationMirror"/> and a
/// panel entry (<see cref="PanelEntryFacts"/>: kind, address and flags from the entry's key, spawn value and deadline from its value-hint
/// runs). The rule functions (<see cref="MailRules.Evaluate"/>, <see cref="MailPay"/>) then run unchanged. Display only: nothing here
/// reaches the wallet or the consume routine. 0 B.
/// </summary>
/// <example>
/// <code>
/// if (MailClientFacts.TryDestination(mirror, dest, null, hostTick, balance.CloseGraceTicks, out DestinationFacts d))
/// {
///     DeliverEval e = MailRules.Evaluate(in d, MailClientFacts.Unit(in entry, 0));
/// }
/// </code>
/// </example>
public static class MailClientFacts
{
    /// <summary>A destination's facts with its effective state, as the host's consume routine computes it.</summary>
    /// <param name="mirror">The destination mirror.</param>
    /// <param name="dest">The destination entity.</param>
    /// <param name="actionStartTick">The tick the action started (the client's estimated host tick at latch), or null for no grace.</param>
    /// <param name="applyTick">The tick the action would apply (the client's estimated host tick).</param>
    /// <param name="closeGraceTicks">The grace window, from the Mail balance.</param>
    /// <param name="f">The facts when found; default otherwise.</param>
    /// <returns>True when <paramref name="dest"/> is a destination.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> is null.</exception>
    public static bool TryDestination(
        DestinationMirror mirror, EntityId dest, uint? actionStartTick, uint applyTick, int closeGraceTicks, out DestinationFacts f)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        if (!mirror.TryGetOrdinal(dest, out int o))
        {
            f = default;
            return false;
        }

        f = mirror.FactsAt(o, mirror.EffectiveStateAt(o, actionStartTick, applyTick, closeGraceTicks));
        return true;
    }

    /// <summary>
    /// The facts of one unit of a panel entry: the entry's kind, category, address and flags with run <paramref name="run"/>'s spawn
    /// value and deadline shift. With no runs (<see cref="HasHint"/> false) the value and deadline are 0, and the preview shows "?".
    /// </summary>
    /// <param name="e">The panel entry.</param>
    /// <param name="run">The value-hint run, 0 (oldest) to <c>RunCount - 1</c>.</param>
    /// <returns>The facts.</returns>
    public static UnitFacts Unit(in PanelEntryFacts e, int run)
    {
        PanelHintRun r = e.Run(run);
        return new UnitFacts(DefIndex.FromValue(e.KindKey), e.Category, e.Address, r.UnitSpawnValue, r.DeadlineShift, (MailFlags)e.Flags);
    }

    /// <summary>True when the entry carries value-hint runs, so <see cref="Unit"/>'s spawn value and deadline are known.</summary>
    /// <param name="e">The panel entry.</param>
    /// <returns>True when hinted.</returns>
    public static bool HasHint(in PanelEntryFacts e) => e.RunCount > 0;
}
