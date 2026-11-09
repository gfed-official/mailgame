using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The host's facts constructors (ADR-0016 section 3, ADR-0017 section 4; mail-destinations story 010): the facts
/// <see cref="MailRules.Evaluate"/> and <see cref="MailPay"/> read, built from the authoritative registry and mail table. The client
/// builds the same structs from its mirrors with <see cref="MailClientFacts"/>; the consume routine uses these. 0 B.
/// </summary>
/// <example><code>DestinationFacts d = MailHostFacts.Destination(registry, o, holdStart, now, balance.CloseGraceTicks);</code></example>
internal static class MailHostFacts
{
    /// <summary>
    /// A destination's facts with its effective state (<see cref="DeliveriesStateRules.Effective"/>: open, or within the closing grace
    /// for an action started before the close; judged from the store's open-window start).
    /// </summary>
    /// <param name="registry">The destination registry.</param>
    /// <param name="o">The destination row.</param>
    /// <param name="actionStartTick">The tick the action started, or null for no grace.</param>
    /// <param name="applyTick">The tick the action is applied.</param>
    /// <param name="closeGraceTicks">The grace window in ticks.</param>
    /// <returns>The facts.</returns>
    public static DestinationFacts Destination(
        DestinationRegistry registry, DestinationOrdinal o, uint? actionStartTick, uint applyTick, int closeGraceTicks)
    {
        DeliveriesState s = registry.Deliveries;
        DestState state = DeliveriesStateRules.Effective(
            s.IsDistrictUnlocked(registry.DistrictOf(o)), s.Open, s.CloseTick, actionStartTick, applyTick, closeGraceTicks, s.OpenSinceTick);
        return registry.FactsOf(o, state);
    }

    /// <summary>A mail unit's facts from its table row.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The facts.</returns>
    public static UnitFacts Unit(in MailRow row) =>
        new(row.Kind, ItemCategory.Mail, row.Address, row.SpawnValue, row.DeadlineShift, row.Flags);
}
