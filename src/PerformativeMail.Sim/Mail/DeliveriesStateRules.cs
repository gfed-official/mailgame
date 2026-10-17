namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The single effective-state helper (Mail R4, TR-mail-013): used by the host consume paths (stories 007 and 008) and the
/// client's facts constructors (story 010), so the closing-grace rule has one copy (ADR-0017 risks, "grace logic differs").
/// Pure and allocation-free.
/// </summary>
/// <example>
/// <code>
/// // Deliveries closed at tick 1000; an action started at 999 and applied at 1090 still counts (grace 90).
/// DestState s = DeliveriesStateRules.Effective(true, false, 1000u, 999u, 1090u, balance.CloseGraceTicks);   // Open
/// </code>
/// </example>
public static class DeliveriesStateRules
{
    /// <summary>
    /// The state a consume sees. Deliveries count as open when they are open now, or when the action started (a hand or
    /// throw action, so <paramref name="actionStartTick"/> has a value) before <paramref name="closeTick"/> and is applied by
    /// <c>closeTick + closeGraceTicks</c>. Not open gives <see cref="DestState.Closed"/> even for a locked district (Closed
    /// wins); open but the district not unlocked gives <see cref="DestState.Locked"/>; otherwise <see cref="DestState.Open"/>.
    /// </summary>
    /// <param name="districtUnlocked">True when the destination's district is unlocked.</param>
    /// <param name="openNow">True when deliveries are open now.</param>
    /// <param name="closeTick">The tick deliveries closed.</param>
    /// <param name="actionStartTick">The tick the action started, or null for a feeder or NPC (no grace).</param>
    /// <param name="applyTick">The tick the action is applied.</param>
    /// <param name="closeGraceTicks">The grace window in ticks, from the Mail balance record (default 90).</param>
    /// <returns>The effective state.</returns>
    public static DestState Effective(
        bool districtUnlocked, bool openNow, uint closeTick, uint? actionStartTick, uint applyTick, int closeGraceTicks)
    {
        bool open = openNow
            || (actionStartTick.HasValue
                && actionStartTick.Value < closeTick
                && (long)applyTick <= (long)closeTick + closeGraceTicks);
        if (!open)
        {
            return DestState.Closed;
        }

        return districtUnlocked ? DestState.Open : DestState.Locked;
    }
}
