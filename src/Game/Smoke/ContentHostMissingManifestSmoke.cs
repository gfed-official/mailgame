using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Content;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Screens;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/007 AC 3's failure arm (ADR-0019 sections 3 and 4, CDD-LOAD-2/-3 host half). With no
/// <c>res://content/manifest.txt</c> (the repo today, checked first): (1) <see cref="GameLoop.SelectContentSource"/> picks
/// <see cref="HostContentBootSource"/> for <c>--content=host</c> or <c>PM_CONTENT=host</c> and <see cref="BuiltInContentSource"/>
/// otherwise; (2) the production plan over that source fails at <c>BOOT 2 content FAIL</c>, the host ends
/// <see cref="ContentHostState.Failed"/> with one diagnostic naming the manifest path, no content is placed, no later step
/// runs and no frame sequence is installed (the Sim is never composed), and one frame later <c>Main/Screens/ContentError</c>
/// shows the manifest path (approved by Evan 2026-11-01 (TD-204 item 7)); (3) a sub-run of this Godot binary,
/// <c>--smoke=selftest-needs-boot --content=host</c>, takes the real <c>RequestBoot()</c> path and exits 4 (boot failed),
/// its runner line naming the step-2 failure and the manifest path.
/// </summary>
[SmokeScenario("content-host-missing-manifest")]
public sealed class ContentHostMissingManifestSmoke : SmokeScenario
{
    private const string ManifestPath = "res://content/manifest.txt";
    private const string SubRunScenario = "selftest-needs-boot";

    private HostContentBootSource _source = null!;
    private int _frames;

    /// <inheritdoc />
    public override double TimeoutSeconds => 180;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        if (!Expect(ctx, $"precondition: FileExists({ManifestPath})", Godot.FileAccess.FileExists(ManifestPath), false)
            || !Expect(ctx, "precondition: Main/Screens/ContentError is wired", ctx.Loop.ContentErrorPanel is not null, true))
        {
            ctx.Quit();
            return;
        }

        CheckSelection(ctx);
        BootWithHostContent(ctx);
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        // The fault sink may be delivered deferred; the screen is checked one frame after the boot fault.
        if (++_frames < 2)
        {
            return SmokeStatus.Running;
        }

        BootErrorScreen content = ctx.Loop.ContentErrorPanel!;
        Expect(ctx, "Main/Screens/ContentError visible", content.Visible, true);
        Expect(ctx, "content error title", content.HeadingText, BootErrorScreen.ContentHeading);
        Expect(ctx, $"content error label names {ManifestPath}", content.MessageText.Contains(ManifestPath, StringComparison.Ordinal), true);
        ctx.Pass($"screen text: {content.HeadingText} | {content.WhereText} | {content.MessageText}");
        SubRun(ctx);
        return SmokeStatus.Done;
    }

    private static void CheckSelection(SmokeContext ctx)
    {
        IContentBootSource byArg = GameLoop.SelectContentSource(["--content=host"], null);
        Expect(ctx, "--content=host selects ContentHost at res://content",
            byArg is HostContentBootSource { Host.Root: ContentHost.DefaultRoot, Host.State: ContentHostState.Unloaded }, true);
        Expect(ctx, "PM_CONTENT=host selects ContentHost", GameLoop.SelectContentSource([], "host") is HostContentBootSource, true);
        Expect(ctx, "no opt-in selects BuiltInContent", GameLoop.SelectContentSource([], null).Name, "BuiltInContent");
    }

    private void BootWithHostContent(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        _source = new HostContentBootSource(new ContentHost());
        BootContext boot = loop.BeginBootForSmoke();
        bool ok = new BootSequence(loop.BuildBootPlan(content: _source)).Run(boot);
        IReadOnlyList<string> lines = boot.Log.Lines;
        ContentHost host = _source.Host;

        Expect(ctx, "BootSequence.Run returns", ok, false);
        Expect(ctx, "BOOT 2 content FAIL line", BootSmokeFaults.Logged(lines, "BOOT 2 content FAIL InvalidOperationException: The host content in res://content did not load (1 diagnostic(s))"), true);
        Expect(ctx, "ContentHost.State", host.State, ContentHostState.Failed);
        Expect(ctx, "ContentHost diagnostics", string.Join(" | ", host.Diagnostics),
            $"ContentHostReadException: cannot read {ManifestPath}: FileNotFound (Godot Error {(int)Error.FileNotFound})");
        Expect(ctx, "failure recorded as step 2 content", $"{boot.Failure?.Step},{boot.Failure?.Kind}", $"2,{BootFailureKind.Content}");
        Expect(ctx, "no ContentSet placed", boot.Content is null, true);
        Expect(ctx, "BOOT 3 line logged (a later step ran)", BootSmokeFaults.Logged(lines, "BOOT 3 "), false);
        Expect(ctx, "loop faulted, no frame sequence installed (Sim never composed)", $"{loop.IsFaulted},{loop.HasSequence}", "True,False");
    }

    // The production boot in a process of its own: RequestBoot() reads --content=host from the user args and must fail.
    private static void SubRun(SmokeContext ctx)
    {
        string[] args = ["--headless", "--path", ProjectSettings.GlobalizePath("res://"), "--", $"--smoke={SubRunScenario}", "--content=host"];
        var output = new Godot.Collections.Array();
        // TD-207 item 6: Mail Dev default, pending Evan: a blocking OS.Execute, bounded only by the child's watchdog.
        int exit = OS.Execute(OS.GetExecutablePath(), args, output, readStderr: true);
        string[] lines = string.Concat(output.Select(o => o.AsString())).Split('\n');
        string? failLine = lines.FirstOrDefault(l => l.StartsWith($"SMOKE {SubRunScenario} FAIL boot failed: ", StringComparison.Ordinal));

        Expect(ctx, $"sub-run --smoke={SubRunScenario} --content=host exit code", exit, 4);
        Expect(ctx, "sub-run result line", lines.Any(l => l.StartsWith($"SMOKE RESULT name={SubRunScenario} pass=0 fail=1 exit=4", StringComparison.Ordinal)), true);
        Expect(ctx, $"sub-run runner FAIL line names BOOT 2 content FAIL and {ManifestPath}",
            failLine is not null && failLine.Contains("(BOOT 2 content FAIL ", StringComparison.Ordinal) && failLine.Contains(ManifestPath, StringComparison.Ordinal), true);
        Expect(ctx, "sub-run logged the Failed host load", lines.Any(l => l.StartsWith("[ContentHost] load res://content: Failed in ", StringComparison.Ordinal)), true);
        Expect(ctx, "sub-run logged a BOOT 3 line", lines.Any(l => l.StartsWith("BOOT 3 ", StringComparison.Ordinal)), false);
        ctx.Pass($"sub-run: {failLine}");
    }
}
