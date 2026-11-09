namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The single effective-state helper (Mail R4, TR-mail-013): used by the host consume paths (stories 007 and 008) and the
/// client's facts constructors (story 010), so the closing-grace rule has one copy (ADR-0017 risks, "grace logic differs").
/// Pure and allocation-free.
/// </summary>
/// <example>
/// <code>
/// // Deliveries opened at 500 and closed at tick 1000; an action started at 999 and applied at 1090 still counts (grace 90).
/// DestState s = DeliveriesStateRules.Effective(true, false, 1000u, 999u, 1090u, balance.CloseGraceTicks, 500u);   // Open
///
/// // Closed at 100, reopened at 180: an action started at 150 (while closed) and applied at 200 is judged closed.
/// DestState t = DeliveriesStateRules.Effective(true, true, 100u, 150u, 200u, balance.CloseGraceTicks, 180u);   // Closed
/// </code>
/// </example>
public static class DeliveriesStateRules
{
    /// <summary>
    /// The state a consume sees. An action is judged by when it started (Mail R4): a feeder or NPC drop
    /// (<paramref name="actionStartTick"/> null) counts as open when deliveries are open now. A hand or throw action counts as
    /// open when it started in the current open window (deliveries open now and the start at or after
    /// <paramref name="openSinceTick"/>), or started before <paramref name="closeTick"/> in the window that close ended (while
    /// closed, at or after <paramref name="openSinceTick"/>; while open, only a close at or before the apply tick) and is applied
    /// by <c>closeTick + closeGraceTicks</c>. With the window start unknown (0) while open, a start at or after a past close is
    /// not in the current window: released or begun while closed, it is closed. Not open gives <see cref="DestState.Closed"/>
    /// even for a locked district (Closed wins); open but the district not unlocked gives <see cref="DestState.Locked"/>;
    /// otherwise <see cref="DestState.Open"/>.
    /// </summary>
    /// <param name="districtUnlocked">True when the destination's district is unlocked.</param>
    /// <param name="openNow">True when deliveries are open now.</param>
    /// <param name="closeTick">The tick deliveries close or closed: the scheduled close while open, else the last close (0 when never).</param>
    /// <param name="actionStartTick">The tick the action started, or null for a feeder or NPC (no grace).</param>
    /// <param name="applyTick">The tick the action is applied.</param>
    /// <param name="closeGraceTicks">The grace window in ticks, from the Mail balance record (default 90).</param>
    /// <param name="openSinceTick">The first tick of the current (or, while closed, the last) open window; 0 when unknown.</param>
    /// <returns>The effective state.</returns>
    public static DestState Effective(
        bool districtUnlocked, bool openNow, uint closeTick, uint? actionStartTick, uint applyTick, int closeGraceTicks, uint openSinceTick)
    {
        bool open;
        if (!actionStartTick.HasValue)
        {
            open = openNow;
        }
        else
        {
            uint start = actionStartTick.Value;
            bool pastClose = closeTick != 0u && closeTick <= applyTick;
            bool inCurrentWindow = openNow && start >= openSinceTick && (openSinceTick != 0u || !pastClose || start < closeTick);
            bool inGrace = start < closeTick
                && (openNow ? pastClose : start >= openSinceTick)
                && (long)applyTick <= (long)closeTick + closeGraceTicks;
            open = inCurrentWindow || inGrace;
        }

        if (!open)
        {
            return DestState.Closed;
        }

        return districtUnlocked ? DestState.Open : DestState.Locked;
    }
}
