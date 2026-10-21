using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Hosting;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/003 AC4 (TR-sim-009, TR-input-014): pause is a tick-driver state. With a
/// real driver and a fake clock: Networking's refusal (a remote peer present) keeps the driver
/// running; an allowed pause freezes ticks and alpha at 0 while hooks and <c>Present</c> still run;
/// <c>SceneTree.Paused = true</c> neither stops <c>_Process</c> nor changes the tick count; after
/// <see cref="GameLoop.Resume"/> the first frame runs at most one tick despite a 10 s jump, and the
/// Sim keeps ticking while the tree is still paused. The scenario is the only place that sets
/// <c>SceneTree.Paused</c> (src/Game/Smoke/ is excluded from the guards for this) and clears it.
/// </summary>
[SmokeScenario("pause-driver-state")]
public sealed class PauseDriverStateSmoke : SmokeScenario
{
    private const int PausedFrames = 3;

    private readonly FakePeerPresence _presence = new() { AnyRemotePeer = true };
    private FrameLoopRig _rig = null!;
    private int _step;
    private int _framesInPhase;
    private int _callsBefore;
    private int _preBefore;
    private int _postBefore;
    private int _presentBefore;
    private long _ticksAtTreePause;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        _rig = new FrameLoopRig(preHooks: 1, postHooks: 1);
        ctx.Loop.PeerPresence = _presence;
        ctx.Loop.InstallForSmoke(_rig.Sequence, armStart: true);
        Mark();
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        try
        {
            SmokeStatus status = Step(ctx);
            if (status == SmokeStatus.Done)
            {
                ctx.Tree.Paused = false;
            }

            return status;
        }
        catch
        {
            ctx.Tree.Paused = false;
            throw;
        }
    }

    private SmokeStatus Step(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        TickDriver driver = _rig.Driver;
        switch (_step)
        {
            case 0:
            {
                Expect(ctx, "driver state after the first armed frame", driver.State, TickDriverState.Running);

                // Networking and Session says a remote peer is present: the pause is refused.
                bool paused = loop.TryPause(out string reason);
                Expect(ctx, "TryPause with a remote peer present returns", paused, false);
                if (reason.Length > 0)
                {
                    ctx.Pass($"pause refusal reason (logged by GameLoop): {reason}");
                }
                else
                {
                    ctx.Fail("pause refused with an empty reason");
                }

                Expect(ctx, "driver state after the refused pause", driver.State, TickDriverState.Running);
                _rig.Clock.Advance(2 * FakeClock.TickCounts);
                Next();
                return SmokeStatus.Running;
            }

            case 1:
            {
                Expect(ctx, "ticks on the frame after the refused pause", _rig.Run.TicksThisFrame, 2);
                _presence.AnyRemotePeer = false;
                bool paused = loop.TryPause(out string reason);
                Expect(ctx, "TryPause with no remote peer returns", paused, true);
                Expect(ctx, "TryPause reason when allowed", reason, string.Empty);
                Expect(ctx, "driver state after TryPause", driver.State, TickDriverState.Paused);
                _rig.Clock.Advance(FakeClock.TickCounts * 3 / 2);
                Next();
                return SmokeStatus.Running;
            }

            case 2:
                // Driver paused, tree running: no ticks, alpha frozen at 0, hooks and Present still run.
                CheckPausedFrame(ctx, $"driver-paused frame {_framesInPhase + 1}");
                _rig.Clock.Advance(FakeClock.TickCounts * 3 / 2);
                if (++_framesInPhase < PausedFrames)
                {
                    Mark();
                    return SmokeStatus.Running;
                }

                ctx.Tree.Paused = true;
                _ticksAtTreePause = driver.TicksRunTotal;
                Next();
                return SmokeStatus.Running;

            case 3:
                // Driver paused and SceneTree paused: _Process still runs (ProcessMode.Always).
                Expect(ctx, "SceneTree.Paused while tree-paused", ctx.Tree.Paused, true);
                CheckPausedFrame(ctx, $"tree-paused frame {_framesInPhase + 1}");
                Expect(ctx, $"tree-paused frame {_framesInPhase + 1}: total ticks unchanged", driver.TicksRunTotal, _ticksAtTreePause);

                // 10 s pass while paused: none of it may become ticks after Resume.
                _rig.Clock.Advance(10 * FakeClock.Freq / PausedFrames);
                if (++_framesInPhase < PausedFrames)
                {
                    Mark();
                    return SmokeStatus.Running;
                }

                loop.Resume();
                Expect(ctx, "driver state after Resume (tree still paused)", driver.State, TickDriverState.Running);
                _rig.Clock.Advance(FakeClock.TickCounts);
                Next();
                return SmokeStatus.Running;

            case 4:
            {
                Expect(ctx, "SceneTree.Paused on the first frame after Resume", ctx.Tree.Paused, true);
                Expect(ctx, "run-frame calls on the first frame after Resume", _rig.Run.Calls - _callsBefore, 1);
                int n = _rig.Run.TicksThisFrame;
                if (n <= 1)
                {
                    ctx.Pass($"first frame after Resume ran {n} tick(s) (<= 1, no burst after a 10 s pause)");
                }
                else
                {
                    ctx.Fail($"first frame after Resume ran {n} ticks (burst)");
                }

                _rig.Clock.Advance(2 * FakeClock.TickCounts);
                Next();
                return SmokeStatus.Running;
            }

            default:
                Expect(ctx, "ticks while SceneTree.Paused and the driver running (tree pause never stops the Sim)", _rig.Run.TicksThisFrame, 2);
                Expect(ctx, "loop not faulted", loop.IsFaulted, false);
                ctx.Tree.Paused = false;
                Expect(ctx, "SceneTree.Paused restored", ctx.Tree.Paused, false);
                return SmokeStatus.Done;
        }
    }

    private void CheckPausedFrame(SmokeContext ctx, string label)
    {
        Expect(ctx, $"{label}: _Process ran run-frame once", _rig.Run.Calls - _callsBefore, 1);
        Expect(ctx, $"{label}: ticks", _rig.Run.TicksThisFrame, 0);
        Expect(ctx, $"{label}: driver.AlphaPermille", _rig.Driver.AlphaPermille, 0);
        Expect(ctx, $"{label}: Present alpha", _rig.Presentation.LastAlpha, 0);
        Expect(ctx, $"{label}: Present calls", _rig.Presentation.Calls - _presentBefore, 1);
        Expect(ctx, $"{label}: hook calls (pre,post)", $"{_rig.Pre[0].Calls - _preBefore},{_rig.Post[0].Calls - _postBefore}", "1,1");
    }

    private void Next()
    {
        _step++;
        _framesInPhase = 0;
        Mark();
    }

    private void Mark()
    {
        _callsBefore = _rig.Run.Calls;
        _preBefore = _rig.Pre[0].Calls;
        _postBefore = _rig.Post[0].Calls;
        _presentBefore = _rig.Presentation.Calls;
    }
}
