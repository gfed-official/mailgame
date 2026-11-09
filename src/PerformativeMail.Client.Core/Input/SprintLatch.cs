using PerformativeMail.Sim.Content;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The Toggle-mode sprint latch and its F8 unlatch (story 012; GDD G7, F8; TR-input-010): one latch shared by every device in
/// Toggle mode. F8 counts consecutive <b>client ticks</b> (from <see cref="IClientTickSource"/>, never ms) that consumed a
/// neutral move intent: <c>unlatch = |moveIntent| == 0 for ≥ ceil(sprintUnlatchSeconds × 30)</c> ticks, 8 at 0.25 s. The router
/// mirrors <see cref="Latched"/> into the held table. 0 B per call.
/// </summary>
internal sealed class SprintLatch
{
    /// <summary>F8's tick count: <c>ceil(250 ms × 30 / 1000)</c> = 8.</summary>
    public const int UnlatchTicks = ((InputDefaults.SprintUnlatchMs * SimSettings.TickRateHz) + 999) / 1000;

    private uint _lastTick;
    private bool _flushedNeutral;
    private int _neutralTicks;
    private bool _restart;

    /// <summary>True while the latch holds the sprint bit on.</summary>
    public bool Latched { get; private set; }

    /// <summary>Flips the latch (a Toggle-mode press); the neutral count restarts.</summary>
    /// <returns>The new value.</returns>
    public bool Flip()
    {
        Latched = !Latched;
        _neutralTicks = 0;
        _restart = true;
        return Latched;
    }

    /// <summary>Drops the latch.</summary>
    public void Unlatch()
    {
        Latched = false;
        _neutralTicks = 0;
    }

    /// <summary>
    /// F8, once per flush: the client ticks run since the last flush consumed the intent that flush pushed, so they extend the
    /// neutral run when it was neutral and reset it otherwise (ticks before a <see cref="Flip"/> never count). Then records whether
    /// this flush's intent is neutral.
    /// </summary>
    /// <param name="clientTick">The published client tick.</param>
    /// <param name="neutralNow">True when the intent this flush pushes is (0, 0).</param>
    /// <returns>True when the latch dropped on this call.</returns>
    public bool Step(uint clientTick, bool neutralNow)
    {
        uint elapsed = unchecked(clientTick - _lastTick);
        _lastTick = clientTick;
        bool dropped = false;
        if (_restart)
        {
            _restart = false;
        }
        else if (Latched)
        {
            _neutralTicks = _flushedNeutral ? (int)Math.Min((ulong)_neutralTicks + elapsed, UnlatchTicks) : 0;
            if (_neutralTicks >= UnlatchTicks)
            {
                Unlatch();
                dropped = true;
            }
        }

        _flushedNeutral = neutralNow;
        if (!neutralNow)
        {
            _neutralTicks = 0;
        }

        return dropped;
    }
}
