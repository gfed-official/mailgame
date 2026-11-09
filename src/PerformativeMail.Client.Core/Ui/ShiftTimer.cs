using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>The shift timer's displayed state (F3 time-only subset).</summary>
/// <param name="Visible">False while there is no deadline.</param>
/// <param name="DisplaySeconds">Whole seconds left, rounded up, monotone within one key.</param>
/// <param name="Phase">Key part: the shift phase.</param>
/// <param name="DeadlineTick">Key part: the deadline tick.</param>
public readonly record struct ShiftTimer(bool Visible, int DisplaySeconds, byte Phase, uint DeadlineTick);

/// <summary>GDD F3 (time subset): <c>disp = ceil(max(0, deadlineMs − now) / 1000)</c>, never rising while the key is unchanged.</summary>
public static class ShiftTimerF3
{
    /// <summary>Steps the timer.</summary>
    /// <param name="prev">Last frame's timer.</param>
    /// <param name="phase">The shift phase.</param>
    /// <param name="deadlineTick">The deadline tick.</param>
    /// <param name="renderMs">The render clock, ms.</param>
    /// <returns>This frame's timer.</returns>
    public static ShiftTimer Step(in ShiftTimer prev, byte phase, uint deadlineTick, long renderMs)
    {
        if (deadlineTick == 0)
        {
            return new ShiftTimer(false, 0, phase, deadlineTick);
        }

        long remMs = Math.Max(0L, (long)deadlineTick * 1000L / TickRate.Hz - renderMs);
        int disp = (int)((remMs + 999L) / 1000L);
        if (prev.Visible && prev.Phase == phase && prev.DeadlineTick == deadlineTick)
        {
            disp = Math.Min(prev.DisplaySeconds, disp);
        }

        return new ShiftTimer(true, disp, phase, deadlineTick);
    }
}
