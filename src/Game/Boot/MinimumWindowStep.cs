using System.Globalization;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Boot step 1, window (ADR-0019 section 7): sets the window's minimum size to 1280×720, reads it back into
/// <see cref="BootContext.MinSizeReadBack"/> and logs the environment facts ADR-0001 / ADR-0002 leave UNVERIFIED as
/// <c>BOOT env key=value</c> lines (window size, content-scale mode and aspect, <c>Sim.AssertsEnabled</c>,
/// <c>OS.IsDebugBuild()</c>, the .NET runtime version, the <c>Stopwatch</c> frequency, the current culture and the boot
/// thread). A read-back that differs (UNVERIFIED under <c>--headless</c>) is logged as
/// <c>BOOT 1 window min-size readback &lt;w&gt;x&lt;h&gt;</c> and does not fail the step; only a throwing setter does.
/// </summary>
/// <param name="window">The root window.</param>
/// <param name="isDebugBuild"><c>OS.IsDebugBuild()</c>, passed in so the step stays engine-free.</param>
/// <example><code>IBootStep step = new MinimumWindowStep(new GodotBootWindow(GetWindow()), OS.IsDebugBuild());</code></example>
public sealed class MinimumWindowStep(IBootWindow window, bool isDebugBuild) : IBootStep
{
    /// <summary>The minimum window size (ADR-0019 section 7).</summary>
    public static readonly (int Width, int Height) MinWindowSize = (1280, 720);

    private readonly IBootWindow _window = window ?? throw new ArgumentNullException(nameof(window));

    /// <inheritdoc />
    public int Number => BootPlan.WindowStep;

    /// <inheritdoc />
    public string Name => "window";

    /// <inheritdoc />
    public BootStepResult Run(BootContext ctx)
    {
        _window.MinSize = MinWindowSize;
        (int Width, int Height) back = _window.MinSize;
        ctx.MinSizeReadBack = back;
        ctx.Log.Write($"BOOT {Number} {Name}: min-size set {Size(MinWindowSize)} readback {Size(back)}");
        if (back != MinWindowSize)
        {
            ctx.Log.Write($"BOOT {Number} {Name} min-size readback {Size(back)}");
        }

        string culture = CultureInfo.CurrentCulture.Name;
        Env(ctx, "window_size", Size(_window.Size));
        Env(ctx, "content_scale_mode", _window.ContentScaleMode);
        Env(ctx, "content_scale_aspect", _window.ContentScaleAspect);
        Env(ctx, "sim_asserts_enabled", PerformativeMail.Sim.Sim.AssertsEnabled.ToString(CultureInfo.InvariantCulture));
        Env(ctx, "is_debug_build", isDebugBuild.ToString(CultureInfo.InvariantCulture));
        Env(ctx, "dotnet_version", System.Environment.Version.ToString());
        Env(ctx, "stopwatch_frequency", System.Diagnostics.Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        Env(ctx, "culture", culture.Length == 0 ? "(invariant)" : culture);
        Env(ctx, "boot_thread", System.Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture));
        return BootStepResult.Ok;
    }

    private static string Size((int Width, int Height) size) =>
        string.Create(CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}");

    private static void Env(BootContext ctx, string key, string value) => ctx.Log.Write($"BOOT env {key}={value}");
}
