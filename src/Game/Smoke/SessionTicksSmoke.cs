using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// godot-host-shell story 005 AC-1, AC-4 and AC-5: the real boot builds the session once (steps 5-6 log the session, the seed and
/// compose+attach at tick 0), <c>GameLoop</c> holds it, and 90 fake-clock frames of 1/30 s through <c>GameLoop._Process</c> run exactly
/// 90 ticks with no dropped tick and no fault. The render clock is sampled once per frame between the run and <c>Present</c>: on every
/// tick boundary <c>RenderSimTimeMs == ((long)HostTick * 1000 + AlphaPermille) / 30</c> with alpha 0 and <c>IsValid</c> true; half a tick
/// later alpha is 500; while the driver is paused alpha is frozen at 0.
/// </summary>
[SmokeScenario("session-ticks")]
public sealed class SessionTicksSmoke : SmokeScenario
{
    private const int Frames = 90;

    private readonly FakeClock _clock = new();

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop) => loop.UseBootOptionsForSmoke(new BootOptions { Clock = _clock });

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        IReadOnlyList<string> lines = loop.BootLines;
        foreach (string line in lines.Where(l => l.StartsWith("BOOT 5", StringComparison.Ordinal) || l.StartsWith("BOOT 6", StringComparison.Ordinal)))
        {
            ctx.Pass($"boot log: {line}");
        }

        Expect(ctx, "step 5 logged the loopback session", BootSmokeFaults.Logged(lines, "BOOT 5 session: loopback HostSession profile=M1aGreybox seed="), true);
        Expect(ctx, "step 5 logged the named run seed", lines.Any(l => l.Contains($"seed={SessionBootStep.M1aRunSeed} (M1aRunSeed)", StringComparison.Ordinal)), true);
        Expect(ctx, "step 6 logged compose+attach at tick 0", BootSmokeFaults.Logged(lines, "BOOT 6 sim: compose+attach world=Ready tick=0 client_attached=True"), true);
        Expect(ctx, "steps 5 and 6 ok", BootSmokeFaults.Logged(lines, "BOOT 5 session ok") && BootSmokeFaults.Logged(lines, "BOOT 6 sim ok"), true);

        BootSession? session = loop.Session;
        if (!Expect(ctx, "GameLoop holds the booted session", session is not null && loop.HasSequence, true) || session?.RenderClock is not { } render)
        {
            return;
        }

        Expect(ctx, "seed", session.Seed, SessionBootStep.M1aRunSeed);
        Expect(ctx, "driver frequency is the clock's", session.Driver.Frequency, FakeClock.Freq);
        Expect(ctx, "driver catch-up cap from SimSettings", session.Driver.MaxCatchUpTicksPerFrame, 4);
        Expect(ctx, "driver idle until the first _Process (F12)", session.Driver.State, TickDriverState.Idle);

        // The first frame starts the driver at the clock's time and runs no tick.
        loop._Process(0);
        Expect(ctx, "first frame started the driver", session.Driver.State, TickDriverState.Running);
        Expect(ctx, "first frame ran no tick", session.CurrentTick, 0u);

        int boundaryMismatches = 0;
        for (int i = 0; i < Frames; i++)
        {
            _clock.Advance(FakeClock.TickCounts);
            loop._Process(0);
            if (render.AlphaPermille != 0 || render.HostTick != session.CurrentTick || !render.IsValid
                || render.RenderSimTimeMs != (((long)render.HostTick * 1000) + render.AlphaPermille) / TickRate.Hz)
            {
                boundaryMismatches++;
            }
        }

        Expect(ctx, $"World.CurrentTick after {Frames} frames of 1/30 s", session.CurrentTick, (uint)Frames);
        Expect(ctx, "Driver.Counters dropped ticks", session.Driver.Counters.DroppedTicksTotal, 0L);
        Expect(ctx, "world not Faulted", session.WorldState != SimState.Faulted, true);
        Expect(ctx, "loop not faulted", loop.IsFaulted, false);
        Expect(ctx, "render clock on every tick boundary: alpha 0, HostTick == CurrentTick, IsValid, F1 formula", boundaryMismatches, 0);
        Expect(ctx, "RenderSimTimeMs at tick 90", render.RenderSimTimeMs, 3000L);

        // Half a tick later: alpha 500, no new tick.
        _clock.Advance(FakeClock.TickCounts / 2);
        loop._Process(0);
        Expect(ctx, "half a tick later: AlphaPermille", render.AlphaPermille, 500);
        Expect(ctx, "half a tick later: RenderSimTimeMs", render.RenderSimTimeMs, RenderClock.ComputeRenderSimTimeMs(90, 500));

        // Paused: alpha frozen at 0, the tick does not move.
        Expect(ctx, "solo pause accepted", loop.TryPause(out string reason), true);
        _clock.Advance(FakeClock.TickCounts * 3);
        loop._Process(0);
        Expect(ctx, $"paused: AlphaPermille frozen at 0 ({reason})", render.AlphaPermille, 0);
        Expect(ctx, "paused: no tick ran", session.CurrentTick, (uint)Frames);
        loop.Resume();
    }
}
