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
/// mirror (fed through MirrorDrain) reports, with the loop never faulted; a third of the way into Prep the HUD shift clock is shown,
/// counting down to Delivery (WP-H; hidden all Prep on eedc110). Steps 7 and 8 must really build the world and resolve the
/// views: no <c>BOOT</c> line says <c>no world-build steps</c> or <c>views NOT WIRED</c> (the grey-void boot of 545df2f), step 7 ran
/// the world-build steps (Destinations among them) and step 8 logged <c>BOOT 8 views ok</c>.
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

        IReadOnlyList<string> lines = loop.BootLines;
        Expect(ctx, "no BOOT line says no world-build steps", lines.Any(l => l.Contains("no world-build steps", StringComparison.Ordinal)), false);
        Expect(ctx, "no BOOT line says views NOT WIRED", lines.Any(l => l.StartsWith("BOOT 8 ", StringComparison.Ordinal) && l.Contains("NOT WIRED", StringComparison.Ordinal)), false);
        Expect(ctx, "step 7 ran the world-build steps", lines.Any(l => l.StartsWith("BOOT 7 world: world-build steps ran=", StringComparison.Ordinal)), true);
        Expect(ctx, "step 7 built the destinations", lines.Any(l => l.StartsWith("BOOT 7 world: destinations lots=", StringComparison.Ordinal)), true);
        Expect(ctx, "BOOT 8 views ok", lines.Contains("BOOT 8 views ok"), true);
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
        Control? timer = loop.GetNodeOrNull<Control>("Hud/Persistent/ShiftTimerView");
        string prepText = string.Empty;
        for (int i = 0; i < Frames; i++)
        {
            _clock.Advance(FakeClock.TickCounts);
            loop._Process(0);
            if (i == PrepTicks / 3)
            {
                prepText = timer is { Visible: true } ? timer.GetChild<Label>(0).Text : string.Empty;
            }
        }

        Expect(ctx, $"the HUD shift clock shows in Prep, counting down to Delivery ('{prepText}')", prepText is "00:02" or "00:03", true);

        Expect(ctx, $"ticks after {Frames} frames", loop.Session.CurrentTick, (uint)Frames);
        Expect(ctx, "loop not faulted", loop.IsFaulted, false);
        Expect(ctx, "run mirror reached Delivery", run.Phase, ShiftPhase.Delivery);
    }
}
