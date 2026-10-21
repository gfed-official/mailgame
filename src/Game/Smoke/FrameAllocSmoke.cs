using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/003 AC6: steady-state allocation of the managed frame sequence
/// (<c>FrameSequence.Run</c>) with recording doubles, two pre and two post hooks and a fake clock
/// that releases ticks. 60 warm-up frames, then <c>GC.GetAllocatedBytesForCurrentThread()</c> over
/// 600 frames. Godot's own per-frame marshalling is excluded by measuring the managed method, not
/// <c>_Process</c>. Advisory: one PASS line carries the number and never fails the run.
/// </summary>
[SmokeScenario("frame-alloc")]
public sealed class FrameAllocSmoke : SmokeScenario
{
    private const int WarmUpFrames = 60;
    private const int MeasuredFrames = 600;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        var rig = new FrameLoopRig(preHooks: 2, postHooks: 2);
        rig.Driver.Start(rig.Clock.Timestamp);

        // 1.25 tick periods per frame: a mix of 1- and 2-tick frames and a moving alpha.
        long step = FakeClock.TickCounts * 5 / 4;
        bool ok = RunFrames(rig, WarmUpFrames, step);
        long before = GC.GetAllocatedBytesForCurrentThread();
        ok &= RunFrames(rig, MeasuredFrames, step);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        if (!ok || rig.Log.Overflowed || rig.Driver.State != TickDriverState.Running)
        {
            ctx.Fail($"frame sequence did not complete cleanly (ok={ok}, overflow={rig.Log.Overflowed}, driver={rig.Driver.State})");
            return;
        }

        ctx.Pass($"FrameSequence.Run x {MeasuredFrames} after {WarmUpFrames} warm-up: {bytes} B allocated " +
            $"(advisory, target 0 B; ticks {rig.Run.TotalTicks}, run-frame calls {rig.Run.Calls})");
    }

    private static bool RunFrames(FrameLoopRig rig, int frames, long step)
    {
        bool ok = true;
        for (int i = 0; i < frames; i++)
        {
            rig.Log.Clear();
            rig.Clock.Advance(step);
            ok &= rig.Sequence.Run();
        }

        return ok;
    }
}
