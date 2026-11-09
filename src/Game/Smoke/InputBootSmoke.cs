using Godot;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Input;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story input-controls 028: the real boot with an <see cref="InputBoot"/> step-4 contributor over the shell's frame-loop rig
/// (recording run, sample and present; the booted session is kept for its latch). Rows: step 4 validates the map, reads the
/// settings file (H-R12-1: <c>holdSettingMs: 600</c> and Deliver <c>"Toggle"</c> applied; a malformed file gives the defaults
/// and one warning; a missing file the defaults and none) and builds the router; an invalid map fails the step (boot stops);
/// <see cref="InputBoot.AttachToLoop"/> puts the adapter last under the root window and, with a real Q press, the Drop sink runs
/// before <c>RunFrame</c> and the post-present hooks after <c>Present</c>; an immediate with no sink logs <c>no_sink</c>; a short
/// run of frames ends with no fault and no event-log overwrite. The 0-tick and 3-tick latch rows are
/// <c>RouterFrameCallsTests</c> (the latch types are Sim types <c>check-sim-access.sh</c> keeps out of this folder). Writes three
/// <c>user://smoke_input_028_*.json</c> files and deletes them.
/// </summary>
[SmokeScenario("input-boot")]
public sealed class InputBootSmoke : SmokeScenario
{
    private const string ToggleFile = "user://smoke_input_028_toggle.json";
    private const string MalformedFile = "user://smoke_input_028_malformed.json";
    private const string MissingFile = "user://smoke_input_028_missing.json";
    private const int SinkCode = FramePhase.PreHook + 1;
    private const int Frames = 120;

    private readonly List<InputAdapter> _extraAdapters = [];
    private FrameLoopRig _rig = null!;
    private InputBoot _boot = null!;
    private Node? _lateChild;
    private int _step;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        WriteFile(ToggleFile, "{\"version\": 1, \"holdSettingMs\": 600, \"holdModeDeliver\": \"Toggle\"}");
        WriteFile(MalformedFile, "{\"holdSettingMs\": 600,");
        Remove(MissingFile);
        _rig = new FrameLoopRig(preHooks: 0, postHooks: 1, register: false);
        loop.InstallForSmoke(_rig.Sequence, armStart: false);
        _boot = new InputBoot(loop, ToggleFile);
        loop.InstallBootStepsForSmoke(loop.BuildBootPlan(contributors: [_boot]));
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        IReadOnlyList<string> lines = ctx.Loop.BootLines;
        foreach (string line in lines.Where(l => l.StartsWith("BOOT 4", StringComparison.Ordinal)))
        {
            ctx.Pass($"boot log: {line}");
        }

        Expect(ctx, "step 4 ran InputBoot and passed",
            BootSmokeFaults.Logged(lines, "BOOT 4 input: contributor InputBoot") && BootSmokeFaults.Logged(lines, "BOOT 4 input ok"), true);
        Expect(ctx, "boot ok, session kept", ctx.Loop.BootResult == true && ctx.Loop.Session is not null, true);

        // H-R12-1, file with holdSettingMs 600 and Deliver Toggle.
        Expect(ctx, "H-R12-1: IClientInputSettings.HoldSettingMs from the file", _boot.Settings.HoldSettingMs, 600);
        Expect(ctx, "H-R12-1: the router's Deliver mode from the file", _boot.Settings.Current.HoldModeDeliver, HoldMode.Toggle);
        Expect(ctx, "H-R12-1: no warning for a valid file", _boot.SettingsRead is { Warning: null }, true);

        CheckSettingsVariant(ctx, MalformedFile, "malformed", expectWarning: true);
        CheckSettingsVariant(ctx, MissingFile, "missing", expectWarning: false);
        CheckInvalidMap(ctx);

        _boot.AttachToLoop(ctx.Loop);
        ctx.Loop.RegisterPostPresentHook(_rig.Post[0]);
        _boot.Router!.RegisterSink(ActionId.Drop, new LoggingSink(_rig.Log));
        ExpectThrows<InvalidOperationException>(ctx, "AttachToLoop a second time throws", () => _boot.AttachToLoop(ctx.Loop));
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        switch (_step++)
        {
            case 0:
                // The adapters' deferred AddChild ran at the end of the first frame. Drop the variant boots' adapters, then add a
                // root child after the boot's adapter: the D-1 re-assert (deferred too) must move the adapter back to last.
                RemoveExtraAdapters();
                _lateChild = new Node { Name = "LateRootChild" };
                ctx.Tree.Root.CallDeferred(Node.MethodName.AddChild, _lateChild);
                return SmokeStatus.Running;
            case 1:
                return SmokeStatus.Running;
            case 2:
                CheckPlacement(ctx);
                CheckFrameOrder(ctx);
                CheckNoSink(ctx);
                CheckShortRun(ctx);
                Cleanup();
                return SmokeStatus.Done;
            default:
                return SmokeStatus.Done;
        }
    }

    private static void Inject(InputEvent e)
    {
        Godot.Input.ParseInputEvent(e);
        Godot.Input.FlushBufferedEvents();
    }

    private static void Tap(Key key)
    {
        Inject(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        Inject(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
    }

    private void CheckSettingsVariant(SmokeContext ctx, string path, string what, bool expectWarning)
    {
        var boot = new InputBoot(ctx.Loop, path);
        BootContext bootCtx = NewContext(out List<Exception> faults);
        bool ok = new BootSequence([new ContributorSlotStep(BootPlan.InputStep, "input", [boot])]).Run(bootCtx);
        if (boot.Adapter is { } adapter)
        {
            _extraAdapters.Add(adapter);
        }

        Expect(ctx, $"H-R12-1 {what} file: step 4 ok, defaults applied", $"{ok}/{faults.Count}/{boot.Settings.Current.Equals(InputSettings.Defaults)}", "True/0/True");
        Expect(ctx, $"H-R12-1 {what} file: warnings", boot.SettingsRead?.Warning is null ? 0 : 1, expectWarning ? 1 : 0);
    }

    private void CheckInvalidMap(SmokeContext ctx)
    {
        // A duplicated definition (same id and string id) is a validation error: the step throws and the boot stops.
        ActionDef[] defs = [.. BuiltInActionMap.Definitions(), BuiltInActionMap.Definitions()[0]];
        var boot = new InputBoot(ctx.Loop, MissingFile, defs);
        BootContext bootCtx = NewContext(out List<Exception> faults);
        bool ok = new BootSequence([new ContributorSlotStep(BootPlan.InputStep, "input", [boot])]).Run(bootCtx);
        Expect(ctx, "invalid action map: boot fails at step 4 with one fault and no router or adapter",
            $"{ok}/{bootCtx.Failure?.Step}/{faults.Count}/{boot.Router is null && boot.Adapter is null}", "False/4/1/True");
        Expect(ctx, "invalid action map: the fault names the validation",
            faults.Count == 1 && faults[0].Message.Contains("failed validation", StringComparison.Ordinal), true);
    }

    private BootContext NewContext(out List<Exception> faults)
    {
        List<Exception> seen = [];
        faults = seen;
        return new BootContext(new BootLog(GD.Print), System.Environment.CurrentManagedThreadId, seen.Add, null);
    }

    private void CheckPlacement(SmokeContext ctx)
    {
        InputAdapter adapter = _boot.Adapter!;
        Window root = ctx.Tree.Root;
        Expect(ctx, "adapter is a child of the root window", adapter.GetParent() == root, true);
        Expect(ctx, "adapter is the last root child after a later root child entered (D-1 re-assert)",
            $"{adapter.GetIndex() == root.GetChildCount() - 1}/{_lateChild?.GetParent() == root && _lateChild.GetIndex() < adapter.GetIndex()}", "True/True");
        Expect(ctx, "adapter initialised by AttachToLoop", adapter.IsInitialized, true);
    }

    private void CheckFrameOrder(SmokeContext ctx)
    {
        // Q is Drop (Immediate) in World: fed in _Input, dispatched by the pre-frame hook before RunFrame.
        Tap(Key.Q);
        _rig.Log.Clear();
        ctx.Loop._Process(0);
        Expect(ctx, "frame order: Drop sink, RunFrame, sample, Present, post-present hook", _rig.Log.Describe(),
            $"{SinkCode},{FramePhase.RunFrame},{FramePhase.Sample},{FramePhase.Present},{FramePhase.PostHook + 1}");
        _rig.Log.Clear();
        ctx.Loop._Process(0);
        Expect(ctx, "next frame: the immediate is not dispatched again", _rig.Log.Describe(),
            $"{FramePhase.RunFrame},{FramePhase.Sample},{FramePhase.Present},{FramePhase.PostHook + 1}");
    }

    private void CheckNoSink(SmokeContext ctx)
    {
        // X is ShoulderSwap (Immediate) in World; no camera-rig sink exists yet.
        long before = _boot.Log.TotalWritten;
        Tap(Key.X);
        ctx.Loop._Process(0);
        bool noSink = false;
        int start = (int)Math.Max(0, _boot.Log.Count - (_boot.Log.TotalWritten - before));
        for (int i = start; i < _boot.Log.Count; i++)
        {
            if (_boot.Log.TryGet(i, out UiEventEntry e) && e.Kind == LogKind.Dropped && e.IdA == ActionId.ShoulderSwap && e.Flags == (byte)DropCause.NoSink)
            {
                noSink = true;
            }
        }

        Expect(ctx, "ShoulderSwap without a sink logs no_sink", noSink, true);
    }

    private void CheckShortRun(SmokeContext ctx)
    {
        for (int i = 0; i < Frames; i++)
        {
            _rig.Clock.Advance(FakeClock.TickCounts);
            if (i % 10 == 0)
            {
                Tap(Key.Space);
            }

            ctx.Loop._Process(0);
        }

        Expect(ctx, $"{Frames} frames with input: loop not faulted", ctx.Loop.IsFaulted, false);
        Expect(ctx, $"{Frames} frames: the driver ticked", _rig.Run.TotalTicks > 0, true);
        Expect(ctx, $"{Frames} frames: event log not overwritten", _boot.Log.OverwriteCount, 0L);
    }

    private void RemoveExtraAdapters()
    {
        foreach (InputAdapter adapter in _extraAdapters)
        {
            adapter.GetParent()?.RemoveChild(adapter);
            adapter.QueueFree();
        }

        _extraAdapters.Clear();
    }

    private void Cleanup()
    {
        _lateChild?.QueueFree();
        Remove(ToggleFile);
        Remove(MalformedFile);
    }

    private static void WriteFile(string path, string text)
    {
        using Godot.FileAccess f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write)
            ?? throw new InvalidOperationException($"cannot write {path}: {Godot.FileAccess.GetOpenError()}");
        f.StoreBuffer(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private static void Remove(string path)
    {
        if (Godot.FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }

    // The Drop verb's stand-in sink: writes its phase code to the rig's order log.
    private sealed class LoggingSink(FrameOrderLog log) : IImmediateActionSink
    {
        public void OnImmediate(in ImmediateAction a) => log.Add(SinkCode);
    }
}
