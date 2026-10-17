using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using PerformativeMail.Sim;

namespace SpikeGodotDotnet;

// Spike only: one GameLoop node; _Process feeds the stub TickDriver.
// Reports runtime identity, Sim type load, and _Process timing, then quits.
public partial class GameLoop : Node
{
    private readonly SpikeTickDriver _driver = new();
    private long _startTimestamp;
    private int _frames;
    private double _sumDelta;
    private double _minDelta = double.MaxValue;
    private double _maxDelta;
    private double _sumWallMs;
    private long _prevTs;
    private int _maxTicksInFrame;
    private int _framesWithZeroTicks;
    private bool _onMainThread = true;
    private int _mainThreadId;

    [Export]
    public double RunSeconds { get; set; } = 5.0;

    public override void _Ready()
    {
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        string? maxFpsArg = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--maxfps=", StringComparison.Ordinal));
        if (maxFpsArg is not null)
        {
            Engine.MaxFps = int.Parse(maxFpsArg["--maxfps=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }

        GD.Print($"[spike] Engine: {Engine.GetVersionInfo()["string"]}");
        GD.Print($"[spike] RuntimeInformation.FrameworkDescription: {RuntimeInformation.FrameworkDescription}");
        GD.Print($"[spike] System.Environment.Version: {System.Environment.Version}");
        GD.Print($"[spike] System.Private.CoreLib location: {typeof(object).Assembly.Location}");
        GD.Print($"[spike] Sim type load: {typeof(SimAssembly).FullName} from {typeof(SimAssembly).Assembly.GetName().Name} v{typeof(SimAssembly).Assembly.GetName().Version}; SimAssembly.Name={SimAssembly.Name}");
        GD.Print($"[spike] Sim assembly location: {typeof(SimAssembly).Assembly.Location}");
        GD.Print($"[spike] Sim assembly target: {typeof(SimAssembly).Assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false).Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().FirstOrDefault()?.FrameworkName}");
        GD.Print($"[spike] CurrentCulture='{System.Globalization.CultureInfo.CurrentCulture.Name}' InvariantMode={AppContext.TryGetSwitch("System.Globalization.Invariant", out bool inv) && inv}");
        GD.Print($"[spike] Stopwatch.Frequency={Stopwatch.Frequency} IsHighResolution={Stopwatch.IsHighResolution}");
        GD.Print($"[spike] Engine.MaxFps={Engine.MaxFps} PhysicsTicksPerSecond={Engine.PhysicsTicksPerSecond} vsync={DisplayServer.WindowGetVsyncMode()}");
        GD.Print($"[spike] main managed thread id={_mainThreadId}");
        if (System.Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--pausetest"))
        {
            ProcessMode = ProcessModeEnum.Always;
            GetTree().Paused = true;
            GD.Print($"[spike] pausetest: ProcessMode={ProcessMode} SceneTree.Paused={GetTree().Paused}");
        }

        _startTimestamp = Stopwatch.GetTimestamp();
        _prevTs = _startTimestamp;
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            _onMainThread = false;
        }

        int ticks = _driver.Advance(now, Stopwatch.Frequency);
        _maxTicksInFrame = Math.Max(_maxTicksInFrame, ticks);
        if (ticks == 0)
        {
            _framesWithZeroTicks++;
        }

        _frames++;
        _sumDelta += delta;
        _minDelta = Math.Min(_minDelta, delta);
        _maxDelta = Math.Max(_maxDelta, delta);
        _sumWallMs += (now - _prevTs) * 1000.0 / Stopwatch.Frequency;
        _prevTs = now;

        double elapsed = (now - _startTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed >= RunSeconds)
        {
            GD.Print($"[spike] elapsed={elapsed:F3}s frames={_frames} avgFps={_frames / elapsed:F1}");
            GD.Print($"[spike] _Process delta: avg={_sumDelta / _frames * 1000:F3}ms min={_minDelta * 1000:F3}ms max={_maxDelta * 1000:F3}ms; Stopwatch avg={_sumWallMs / _frames:F3}ms");
            GD.Print($"[spike] ticks run={_driver.TicksRun} dropped={_driver.TicksDropped} expected~={elapsed * SpikeTickDriver.TickHz:F1} maxTicksInOneFrame={_maxTicksInFrame} framesWithZeroTicks={_framesWithZeroTicks}");
            GD.Print($"[spike] _Process always on main managed thread: {_onMainThread}");
            GD.Print($"[spike] SceneTree.Paused at end={GetTree().Paused}");
            GetTree().Quit();
        }
    }
}
