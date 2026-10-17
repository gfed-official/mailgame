using Godot;
using PerformativeMail.Game.Smoke;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// Root of the one main scene (<c>res://src/Game/Main.tscn</c>, ADR-0019 section 1). Story 002
/// owns only the skeleton: process settings, the Debug-node strip, the smoke hand-off and the
/// boot seam. The frame loop is story 003, the boot sequence is story 004. Deliberately no
/// static instance, no lookups beyond its own direct children, no pause or scene changes.
/// </summary>
public partial class GameLoop : Node
{
    private const string SmokeArgPrefix = "--smoke=";

    /// <summary>
    /// Test seam: when false, <see cref="_Ready"/> does not call <see cref="RequestBoot"/>.
    /// Tests instantiate the scene with this off to inspect the un-booted tree.
    /// </summary>
    [Export]
    public bool AutoBoot { get; set; } = true;

    public override void _Ready()
    {
        // Also set in Main.tscn; repeated here so the live values never depend on the scene file.
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
        ProcessThreadGroup = ProcessThreadGroupEnum.Inherit;

        bool isDebug = OS.IsDebugBuild();
        if (!isDebug)
        {
            CanvasLayer? debug = GetNodeOrNull<CanvasLayer>("Debug");
            if (debug is not null)
            {
                RemoveChild(debug);
                debug.QueueFree();
            }
        }

        string? smokeName = FindSmokeName();
        if (smokeName is not null)
        {
            if (isDebug)
            {
                // The runner decides whether to boot (scenario NeedsBoot), so skip AutoBoot.
                AddChild(new SmokeRunner { ScenarioName = smokeName });
                return;
            }

            GD.Print("[GameLoop] --smoke is ignored in a non-debug build");
        }

        if (AutoBoot)
        {
            RequestBoot();
        }
    }

    /// <summary>
    /// Boot seam. Story 004 replaces this body with the real boot sequence.
    /// </summary>
    /// <returns>True when boot succeeded; false when it did not run or failed.</returns>
    public bool RequestBoot()
    {
        GD.Print("[GameLoop] boot not wired (story 004)");
        return false;
    }

    private static string? FindSmokeName()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(SmokeArgPrefix, StringComparison.Ordinal))
            {
                return arg[SmokeArgPrefix.Length..];
            }
        }

        return null;
    }
}
