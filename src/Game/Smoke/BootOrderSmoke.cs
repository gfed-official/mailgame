using System.Diagnostics;
using System.Reflection;
using Godot;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Hosting;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/004 AC1, AC2, AC4 and AC6 (ADR-0019 section 3 Validation Criteria: "a headless boot of
/// <c>Main.tscn</c> logs the boot steps in section 3 order"). Runs the real production boot through the runner
/// (<see cref="NeedsBoot"/>) after installing a <see cref="FrameLoopRig"/> sequence unarmed in the pre-boot hook, then
/// asserts: the nine status lines in ascending order with no FAIL and which steps are NOT WIRED; the step 1
/// <c>MinSize</c> read-back (or, if it differs headless, the logged read-back line) and every <c>BOOT env</c> line,
/// <c>Sim.AssertsEnabled == OS.IsDebugBuild()</c> in an unoptimised (Debug) game build, the logged boot thread; the step 2
/// table counts and hash; the empty step 3/4 slots; the error screen installed as the fault sink and hidden; the loopback
/// presence defaulted; a refused second <c>RequestBoot</c>; <c>StartPending</c> right after boot and, one frame later, the
/// rig's driver started by <c>_Process</c>.
/// </summary>
[SmokeScenario("boot-order")]
public sealed class BootOrderSmoke : SmokeScenario
{
    private static readonly string[] _envKeys =
    [
        "window_size", "content_scale_mode", "content_scale_aspect", "sim_asserts_enabled", "is_debug_build",
        "dotnet_version", "stopwatch_frequency", "culture", "boot_thread",
    ];

    private readonly LogRecorder _log = new();
    private FrameLoopRig _rig = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        _rig = new FrameLoopRig(preHooks: 1, postHooks: 1);
        loop.InstallForSmoke(_rig.Sequence, armStart: false);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;

        // Step 9 armed the start; nothing has started the driver yet.
        Expect(ctx, "StartPending immediately after RequestBoot (step 9 armed)", loop.StartPending, true);
        Expect(ctx, "rig driver state immediately after RequestBoot", _rig.Driver.State, TickDriverState.Idle);
        Expect(ctx, "loop not faulted after boot", loop.IsFaulted, false);
        Expect(ctx, "RequestBoot call count", loop.BootRequestCount, 1);

        IReadOnlyList<string> lines = loop.LastBootLog?.Lines ?? [];
        CheckOrder(ctx, lines);
        CheckWindow(ctx, loop, lines);
        CheckEnv(ctx, loop, lines);
        CheckContentAndSlots(ctx, lines);
        CheckHostState(ctx, loop);
        CheckSecondCall(ctx, loop);
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        // One frame after boot: _Process consumed the armed start and started the rig's driver.
        Expect(ctx, "StartPending one frame after boot", ctx.Loop.StartPending, false);
        Expect(ctx, "rig driver state one frame after boot", _rig.Driver.State, TickDriverState.Running);
        Expect(ctx, "run-frame calls on the first frame", _rig.Run.Calls, 1);
        Expect(ctx, "loop not faulted one frame after boot", ctx.Loop.IsFaulted, false);
        return SmokeStatus.Done;
    }

    private static void CheckOrder(SmokeContext ctx, IReadOnlyList<string> lines)
    {
        foreach (string line in lines)
        {
            ctx.Pass($"boot log: {line}");
        }

        List<(int Number, string Name, string Status)> status = BootSmoke.StatusLines(lines);
        Expect(ctx, "BOOT status line numbers in order", string.Join(",", status.Select(s => s.Number)), "1,2,3,4,5,6,7,8,9");
        Expect(ctx, "BOOT status line names in order", string.Join(",", status.Select(s => s.Name)), "window,content,persistence,input,session,sim,world,views,start");
        Expect(ctx, "BOOT FAIL lines", status.Count(s => s.Status == "FAIL"), 0);
        string notWired = string.Join(",", status.Where(s => s.Status == "NOT WIRED").Select(s => $"{s.Number} {s.Name}"));
        ctx.Pass($"NOT WIRED steps (story 011 asserts none): {notWired}");
        Expect(ctx, "NOT WIRED steps at story 004", notWired, "5 session,6 sim,7 world,8 views");
        Expect(ctx, "step 5 NOT WIRED line names its story", BootSmoke.Has(lines, "BOOT 5 session NOT WIRED (story 005)"), true);
    }

    private static void CheckWindow(SmokeContext ctx, GameLoop loop, IReadOnlyList<string> lines)
    {
        Vector2I min = loop.GetWindow().MinSize;
        string back = $"{min.X}x{min.Y}";
        Expect(ctx, "step 1 logged the set and read-back MinSize", BootSmoke.Has(lines, $"BOOT 1 window: min-size set 1280x720 readback {back}"), true);
        if (min == new Vector2I(1280, 720))
        {
            ctx.Pass($"GetWindow().MinSize read back {back} == 1280x720 (headless={DisplayServer.GetName()})");
            Expect(ctx, "no min-size readback mismatch line", BootSmoke.Has(lines, "BOOT 1 window min-size readback"), false);
        }
        else
        {
            // UNVERIFIED headless behaviour: the setter did not throw (step 1 ok); the value is recorded, not asserted.
            Expect(ctx, $"read-back mismatch logged (display {DisplayServer.GetName()})", BootSmoke.Has(lines, $"BOOT 1 window min-size readback {back}"), true);
            Expect(ctx, "step 1 ok despite the read-back mismatch (setter did not throw)", BootSmoke.Has(lines, "BOOT 1 window ok"), true);
        }
    }

    private static void CheckEnv(SmokeContext ctx, GameLoop loop, IReadOnlyList<string> lines)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines.Where(l => l.StartsWith("BOOT env ", StringComparison.Ordinal)))
        {
            string kv = line["BOOT env ".Length..];
            int eq = kv.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && !env.TryAdd(kv[..eq], kv[(eq + 1)..]))
            {
                ctx.Fail($"BOOT env key logged twice: {kv[..eq]}");
            }
        }

        foreach (string key in _envKeys)
        {
            Expect(ctx, $"BOOT env {key} logged", env.ContainsKey(key), true);
        }

        bool isDebug = OS.IsDebugBuild();
        Expect(ctx, "BOOT env sim_asserts_enabled matches Sim.AssertsEnabled", env.GetValueOrDefault("sim_asserts_enabled", "?"), PerformativeMail.Sim.Sim.AssertsEnabled.ToString());
        Expect(ctx, "BOOT env is_debug_build matches OS.IsDebugBuild()", env.GetValueOrDefault("is_debug_build", "?"), isDebug.ToString());

        // Debug / ExportDebug game builds are unoptimised; there the pair must match. A Release game build run from the
        // editor has asserts off but IsDebugBuild true, so nothing is asserted there (check-build-config covers Export*).
        bool unoptimised = typeof(GameLoop).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true;
        if (unoptimised)
        {
            Expect(ctx, "Sim.AssertsEnabled == OS.IsDebugBuild() (unoptimised Debug game build)", PerformativeMail.Sim.Sim.AssertsEnabled, isDebug);
        }
        else
        {
            ctx.Pass($"optimised game build: Sim.AssertsEnabled={PerformativeMail.Sim.Sim.AssertsEnabled}, OS.IsDebugBuild()={isDebug} logged, equality not asserted");
        }

        Expect(ctx, "BOOT env boot_thread equals GameLoop.BootThreadId", env.GetValueOrDefault("boot_thread", "?"), loop.BootThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Expect(ctx, "BOOT env content_scale_mode", env.GetValueOrDefault("content_scale_mode", "?"), Window.ContentScaleModeEnum.CanvasItems.ToString());
        Expect(ctx, "BOOT env content_scale_aspect", env.GetValueOrDefault("content_scale_aspect", "?"), Window.ContentScaleAspectEnum.KeepHeight.ToString());
    }

    private static void CheckContentAndSlots(SmokeContext ctx, IReadOnlyList<string> lines)
    {
        string? content = lines.FirstOrDefault(l => l.StartsWith("BOOT 2 content: ", StringComparison.Ordinal));
        Expect(ctx, "step 2 content detail line logged", content is not null, true);
        if (content is not null)
        {
            Expect(ctx, "step 2 source is BuiltInContent", content.Contains("source=BuiltInContent ", StringComparison.Ordinal), true);
            foreach (string table in new[] { "containers", "items", "mail_kinds", "destination_types", "mail_mixes" })
            {
                Expect(ctx, $"step 2 logs a non-zero {table} count", HasPositive(content, table), true);
            }

            int h = content.IndexOf("hash=", StringComparison.Ordinal);
            string hash = h < 0 ? string.Empty : new string(content[(h + 5)..].TakeWhile(char.IsAsciiHexDigitLower).ToArray());
            Expect(ctx, "step 2 logs the 8-hex content hash", hash.Length, 8);
        }

        Expect(ctx, "step 3 slot line", BootSmoke.Has(lines, "BOOT 3 persistence: no contributor"), true);
        Expect(ctx, "step 4 slot line", BootSmoke.Has(lines, "BOOT 4 input: no contributor"), true);
    }

    private static void CheckHostState(SmokeContext ctx, GameLoop loop)
    {
        Expect(ctx, "Main/Screens/BootError wired as GameLoop.ErrorScreen", loop.ErrorScreen is not null && loop.ErrorScreen.GetPath().ToString().EndsWith("/Main/Screens/BootError", StringComparison.Ordinal), true);
        Expect(ctx, "GameLoop.FaultSink is the error screen after boot", ReferenceEquals(loop.FaultSink, loop.ErrorScreen), true);
        Expect(ctx, "BootError hidden after a clean boot", loop.ErrorScreen?.Visible ?? true, false);
        Expect(ctx, "ContentError hidden after a clean boot", BootSmoke.ContentScreen(loop)?.Visible ?? true, false);
        Expect(ctx, "PeerPresence defaulted to the loopback presence (none was set)", ReferenceEquals(loop.PeerPresence, LoopbackPeerPresence.Instance), true);
        Expect(ctx, "boot ran on the GameLoop boot thread", System.Environment.CurrentManagedThreadId, loop.BootThreadId);
    }

    private void CheckSecondCall(SmokeContext ctx, GameLoop loop)
    {
        BootLog? first = loop.LastBootLog;
        loop.LogObserver = _log.Observer;
        bool again = loop.RequestBoot();
        loop.LogObserver = null;
        Expect(ctx, "second RequestBoot returns the first result", again, true);
        Expect(ctx, "second RequestBoot logged '[GameLoop] boot already ran'", _log.Count("[GameLoop] boot already ran"), 1);
        Expect(ctx, "second RequestBoot ran no plan (same boot log)", ReferenceEquals(loop.LastBootLog, first), true);
        Expect(ctx, "RequestBoot call count after the refused call", loop.BootRequestCount, 2);
    }

    private static bool HasPositive(string line, string key)
    {
        int i = line.IndexOf($" {key}=", StringComparison.Ordinal);
        if (i < 0)
        {
            return false;
        }

        string digits = new(line[(i + key.Length + 2)..].TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out int n) && n > 0;
    }
}
