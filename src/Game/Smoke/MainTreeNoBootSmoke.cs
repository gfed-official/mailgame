using Godot;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Screens;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/004 AC8 (TD-098 item 2): the <c>AutoBoot = false</c> seam. Instantiates a second
/// <c>Main.tscn</c> from its <see cref="PackedScene"/>, sets <c>AutoBoot = false</c> (and has it ignore <c>--smoke=</c>, so
/// its <c>_Ready</c> takes the production path), adds it to the tree and asserts <c>_Ready</c> never called
/// <c>RequestBoot</c> (counter 0) and nothing was booted (no sink, presence, armed start or boot log; screens hidden; the
/// scene-wired <c>BootErrorPanel</c> / <c>ContentErrorPanel</c> exports resolved). Control: an explicit
/// <c>RequestBoot()</c> on that instance boots (counter 1, loopback presence defaulted because none was set — TD-120, the
/// screen installed as the sink) and a second call is refused with the first result (counter 2); and a third instance with
/// <c>AutoBoot = true</c> boots from its own <c>_Ready</c> (counter 1). The two booting instances echo their own
/// <c>BOOT</c> lines to stdout, after this scenario's start; the process's own boot log is <c>boot-order</c>'s.
/// </summary>
[SmokeScenario("main-tree-noboot")]
public sealed class MainTreeNoBootSmoke : SmokeScenario
{
    private const string MainScene = "res://src/Game/Main.tscn";

    private readonly LogRecorder _log = new();

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        Expect(ctx, "the smoke host's own RequestBoot call count (--smoke skips AutoBoot)", ctx.Loop.BootAttempts, 0);
        var scene = GD.Load<PackedScene>(MainScene);
        GameLoop main = scene.Instantiate<GameLoop>();
        main.Name = "MainNoBoot";
        main.AutoBoot = false;
        main.SmokeArgsIgnored = true;
        ctx.Tree.Root.AddChild(main);
        try
        {
            CheckUnbooted(ctx, main);
            CheckControl(ctx, main);
        }
        finally
        {
            ctx.Tree.Root.RemoveChild(main);
            main.QueueFree();
        }

        CheckAutoBoot(ctx, scene);
    }

    // Positive control for the seam: the same scene with AutoBoot = true boots from its own _Ready.
    private static void CheckAutoBoot(SmokeContext ctx, PackedScene scene)
    {
        GameLoop auto = scene.Instantiate<GameLoop>();
        auto.Name = "MainAutoBoot";
        auto.SmokeArgsIgnored = true;
        ctx.Tree.Root.AddChild(auto);
        try
        {
            Expect(ctx, "AutoBoot = true control: _Ready called RequestBoot once", auto.BootAttempts, 1);
            Expect(ctx, "AutoBoot = true control: the boot ran (boot log written, start armed)", auto.BootLines.Count > 0 && auto.BootResult == true && auto.StartPending, true);
            Expect(ctx, "AutoBoot = true control: not faulted", auto.IsFaulted, false);
        }
        finally
        {
            ctx.Tree.Root.RemoveChild(auto);
            auto.QueueFree();
        }
    }

    private static void CheckUnbooted(SmokeContext ctx, GameLoop main)
    {
        Expect(ctx, "instance is inside the tree (its _Ready ran)", main.IsInsideTree() && main.IsNodeReady(), true);
        Expect(ctx, "AutoBoot", main.AutoBoot, false);
        Expect(ctx, "RequestBoot call count after _Ready with AutoBoot = false", main.BootAttempts, 0);
        Expect(ctx, "no boot lines and no boot result", main.BootLines.Count == 0 && main.BootResult is null, true);
        Expect(ctx, "StartPending", main.StartPending, false);
        Expect(ctx, "IsFaulted", main.IsFaulted, false);
        Expect(ctx, "no PeerPresence provided", main.PeerPresence is null, true);
        Expect(ctx, "FaultSink is still the log-only default", ReferenceEquals(main.FaultSink, LogFaultSink.Instance), true);
        Expect(ctx, "no SmokeRunner added to the instance", main.GetChildren().Any(c => c is SmokeRunner), false);

        BootErrorScreen? boot = main.GetNodeOrNull<BootErrorScreen>("Screens/BootError");
        BootErrorScreen? content = main.GetNodeOrNull<BootErrorScreen>("Screens/ContentError");
        Expect(ctx, "BootErrorPanel export resolves to the instance's Screens/BootError", boot is not null && ReferenceEquals(main.BootErrorPanel, boot), true);
        Expect(ctx, "ContentErrorPanel export resolves to the instance's Screens/ContentError", content is not null && ReferenceEquals(main.ContentErrorPanel, content), true);
        Expect(ctx, "BootError sink shows nothing before boot", boot?.Showing ?? BootErrorScreen.Shown.SessionFault, BootErrorScreen.Shown.Nothing);
        Expect(ctx, "BootError hidden", boot?.Visible ?? true, false);
        Expect(ctx, "ContentError hidden", content?.Visible ?? true, false);
    }

    private void CheckControl(SmokeContext ctx, GameLoop main)
    {
        main.LogObserver = _log.Observer;
        Expect(ctx, "control: explicit RequestBoot on the instance returns", main.RequestBoot(), true);
        Expect(ctx, "control: RequestBoot call count", main.BootAttempts, 1);
        Expect(ctx, "control: PeerPresence defaulted to the loopback presence (none was set, TD-120)", ReferenceEquals(main.PeerPresence, LoopbackPeerPresence.Instance), true);
        Expect(ctx, "control: FaultSink is the instance's error screen", ReferenceEquals(main.FaultSink, main.BootErrorPanel), true);
        Expect(ctx, "control: StartPending (step 9 armed)", main.StartPending, true);
        Expect(ctx, "control: second RequestBoot returns the first result", main.RequestBoot(), true);
        Expect(ctx, "control: '[GameLoop] boot already ran' logged once", _log.Count("[GameLoop] boot already ran"), 1);
        Expect(ctx, "control: RequestBoot call count after the refused call", main.BootAttempts, 2);
        main.LogObserver = null;
    }
}
