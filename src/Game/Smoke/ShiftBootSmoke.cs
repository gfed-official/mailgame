using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Run;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// M-1a wave-3 assembly: the production boot plan (step 4's <c>InputBoot</c> included) boots with the shift stub's Prep cut to
/// <see cref="PrepTicks"/> ticks; the input boot is attached to the loop, the HUD has its persistent, inventory and aim layers and
/// <c>World/Flights</c> its flight view; then fake-clock frames of 1/30 s run the shift from Prep into Delivery, which the client's run
/// mirror (fed through MirrorDrain) reports, with the loop never faulted.
/// </summary>
[SmokeScenario("shift-boot")]
public sealed class ShiftBootSmoke : SmokeScenario
{
    private const int PrepTicks = 90;
    private const int Frames = PrepTicks + 10;

    private readonly FakeClock _clock = new();

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop) =>
        loop.UseBootOptionsForSmoke(new BootOptions
        {
            Clock = _clock,
            FixtureProfile = SimProfile.M1aGreybox.WithShiftStub(new ShiftStubSettings { PrepTicks = PrepTicks }),
        });

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        foreach (string step in new[] { "BOOT 4 input ok", "BOOT 5 session ok", "BOOT 6 sim ok", "BOOT 7 world ok" })
        {
            Expect(ctx, step, loop.BootLines.Contains(step), true);
        }

        Expect(ctx, "production InputBoot attached to the loop", loop.InputBoot?.FrameCalls is not null, true);
        foreach (string path in new[] { "Hud/Persistent", "Hud/Inventory", "Hud/Aim", "World/Flights/Flights" })
        {
            Expect(ctx, $"{path} installed", loop.GetNodeOrNull<Node>(path) is not null, true);
        }

        if (!Expect(ctx, "client mirrors bound", loop.Session?.Mirrors is not null, true))
        {
            return;
        }

        RunMirror run = loop.Session!.Mirrors!.Run;
        loop._Process(0);   // starts the driver; no tick
        for (int i = 0; i < Frames; i++)
        {
            _clock.Advance(FakeClock.TickCounts);
            loop._Process(0);
        }

        Expect(ctx, $"ticks after {Frames} frames", loop.Session.CurrentTick, (uint)Frames);
        Expect(ctx, "loop not faulted", loop.IsFaulted, false);
        Expect(ctx, "run mirror reached Delivery", run.Phase, ShiftPhase.Delivery);
    }
}
