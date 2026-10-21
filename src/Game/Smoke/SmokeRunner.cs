using Godot;
using PerformativeMail.Game.Hosting;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Added by <see cref="GameLoop"/> (debug builds only) when <c>--smoke=&lt;name&gt;</c> is passed.
/// Runs the named scenario, prints <c>SMOKE</c> lines and quits from code with the exit code.
/// The <c>exit=</c> field of the final <c>SMOKE RESULT</c> line is authoritative: a timeout also
/// prints a FAIL-shaped line (so it counts as a failure) but its RESULT exit is 5, not 1.
/// The wall-clock timeout starts in <c>_Ready</c>, before boot, so a stalled boot is caught too.
/// </summary>
public partial class SmokeRunner : Node
{
    private const int ExitFailed = 1;
    private const int ExitUnknown = 2;
    private const int ExitNoAssertions = 3;
    private const int ExitBootFailed = 4;
    private const int ExitTimeout = 5;
    private const string RunnerName = "runner";

    private readonly System.Diagnostics.Stopwatch _clock = new();
    private SmokeScenario? _scenario;
    private SmokeContext? _ctx;
    private bool _started;
    private bool _finished;

    /// <summary>The scenario name, or <c>list</c>.</summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// Starts the wall clock, discovers scenarios and handles <c>list</c> / unknown names; for a
    /// known scenario it defers <see cref="Begin"/> one frame and enables processing.
    /// Any exception becomes a FAIL line and exit 1.
    /// </summary>
    public override void _Ready()
    {
        // Explicit, not inherited: the watchdog must keep running while a scenario pauses the SceneTree.
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(false);
        _clock.Start();
        try
        {
            Setup();
        }
        catch (Exception ex)
        {
            Crash(ex);
        }
    }

    /// <summary>
    /// Watchdog and poll loop: enforces the scenario timeout every frame (including before
    /// <c>Start</c> has run), then polls the scenario once it has started.
    /// </summary>
    /// <param name="delta">Frame time in seconds (unused).</param>
    public override void _Process(double delta)
    {
        if (_finished || _scenario is null || _ctx is null)
        {
            return;
        }

        try
        {
            if (_clock.Elapsed.TotalSeconds > _scenario.TimeoutSeconds)
            {
                _ctx.Fail($"timeout after {_scenario.TimeoutSeconds}s");
                Finish(ExitTimeout);
            }
            else if (_started && (_scenario.Poll(_ctx) == SmokeStatus.Done || _ctx.QuitRequested))
            {
                Finish(null);
            }
        }
        catch (Exception ex)
        {
            Crash(ex);
        }
    }

    private static Dictionary<string, Type> Discover(out string? duplicateError)
    {
        duplicateError = null;
        var found = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in typeof(SmokeRunner).Assembly.GetTypes())
        {
            var attr = (SmokeScenarioAttribute?)Attribute.GetCustomAttribute(type, typeof(SmokeScenarioAttribute));
            if (attr is null || type.IsAbstract || !type.IsSubclassOf(typeof(SmokeScenario)))
            {
                continue;
            }

            if (found.TryGetValue(attr.Name, out Type? existing))
            {
                string[] names = [existing.FullName ?? existing.Name, type.FullName ?? type.Name];
                Array.Sort(names, StringComparer.Ordinal);
                duplicateError = $"duplicate scenario name '{attr.Name}': {names[0]}, {names[1]}";
                return found;
            }

            found.Add(attr.Name, type);
        }

        return found;
    }

    private void Setup()
    {
        Dictionary<string, Type> found = Discover(out string? duplicateError);
        if (duplicateError is not null)
        {
            Abort(RunnerName, duplicateError, ExitFailed);
            return;
        }

        if (ScenarioName == "list")
        {
            foreach (KeyValuePair<string, Type> entry in found.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var scenario = (SmokeScenario)Activator.CreateInstance(entry.Value)!;
                GD.Print($"SMOKE LIST {entry.Key} expect={scenario.ExpectedExit}");
            }

            _finished = true;
            Quit(0);
            return;
        }

        if (!found.TryGetValue(ScenarioName, out Type? type))
        {
            _finished = true;
            GD.Print($"SMOKE RESULT name={ScenarioName} pass=0 fail=0 exit={ExitUnknown}");
            Quit(ExitUnknown);
            return;
        }

        _scenario = (SmokeScenario)Activator.CreateInstance(type)!;
        Callable.From(Begin).CallDeferred();
        SetProcess(true);
    }

    private void Begin()
    {
        if (_finished || _scenario is null)
        {
            return;
        }

        try
        {
            var loop = GetParent() as GameLoop
                ?? throw new InvalidOperationException("SmokeRunner is not a child of GameLoop");
            _ctx = new SmokeContext(ScenarioName, loop, GetTree());
            if (_scenario.NeedsBoot)
            {
                _scenario.BeforeBoot(loop);
            }

            if (_scenario.NeedsBoot && !loop.RequestBoot())
            {
                // Name the failing step so an exit 4 shows which boot failure produced it.
                string failed = loop.LastBootLog?.Lines.LastOrDefault(l => l.Contains(" FAIL ", StringComparison.Ordinal)) ?? "no BOOT FAIL line";
                _ctx.Fail($"boot failed: GameLoop.RequestBoot() returned false ({failed})");
                Finish(ExitBootFailed);
                return;
            }

            _scenario.Start(_ctx);
            _started = true;
            if (_ctx.QuitRequested)
            {
                Finish(null);
            }
        }
        catch (Exception ex)
        {
            Crash(ex);
        }
    }

    private void Crash(Exception ex)
    {
        if (_finished)
        {
            return;
        }

        string message = $"{ex.GetType().Name}: {ex.Message}";
        if (_ctx is not null)
        {
            _ctx.Fail(message);
            Finish(ExitFailed);
            return;
        }

        Abort(ScenarioName.Length == 0 ? RunnerName : ScenarioName, message, ExitFailed);
    }

    private void Abort(string name, string message, int code)
    {
        _finished = true;
        SetProcess(false);
        GD.Print($"SMOKE {name} FAIL {message.ReplaceLineEndings(" ")}");
        GD.Print($"SMOKE RESULT name={name} pass=0 fail=1 exit={code}");
        Quit(code);
    }

    private void Finish(int? forcedCode)
    {
        if (_finished || _ctx is null)
        {
            return;
        }

        _finished = true;
        SetProcess(false);
        int code = forcedCode
            ?? (_ctx.FailCount > 0 ? ExitFailed : _ctx.PassCount == 0 ? ExitNoAssertions : 0);
        GD.Print($"SMOKE RESULT name={_ctx.Name} pass={_ctx.PassCount} fail={_ctx.FailCount} exit={code}");
        Quit(code);
    }

    private void Quit(int code) => GetTree().Quit(code);
}
