using Godot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Game.Screens;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/002: the ADR-0019 scene tree, the root's process settings, inert
/// presenters, explicit project settings read back at runtime and the live root window state.
/// Runs against the un-booted tree.
/// </summary>
[SmokeScenario("main-tree")]
public sealed class MainTreeSmoke : SmokeScenario
{
    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        CheckTree(ctx, loop);
        CheckRoot(ctx, loop);
        CheckPresenters(ctx, loop);
        CheckSettings(ctx);
        CheckWindow(ctx);
        CheckNoAutoloads(ctx);
    }

    private static void CheckTree(SmokeContext ctx, GameLoop loop)
    {
        CheckNodes(ctx, loop);
        CheckDebugAndOrder(ctx, loop);
        CheckIdentity(ctx, loop);
    }

    private static void CheckNodes(SmokeContext ctx, GameLoop loop)
    {
        Check<Node>(ctx, loop, "Input");
        Check<WorldPresenter>(ctx, loop, "World");
        Check<Node3D>(ctx, loop, "World/Terrain");
        Check<Node3D>(ctx, loop, "World/Destinations");
        Check<Node3D>(ctx, loop, "World/Entities");
        Check<Node3D>(ctx, loop, "World/Flights");
        Check<Node3D>(ctx, loop, "World/Props");
        Check<Node3D>(ctx, loop, "World/CameraRig");
        Check<Node3D>(ctx, loop, "World/CameraRig/Boom");
        Check<Camera3D>(ctx, loop, "World/CameraRig/Boom/Camera3D");
        Check<HudPresenter>(ctx, loop, "Hud");
        Check<AudioPresenter>(ctx, loop, "Audio");
        Check<Node>(ctx, loop, "Screens");
        Check<BootErrorScreen>(ctx, loop, "Screens/BootError");
        Check<BootErrorScreen>(ctx, loop, "Screens/ContentError");
        foreach (string screen in new[] { "Screens/BootError", "Screens/ContentError" })
        {
            Expect(ctx, $"{screen} hidden in the un-booted tree", loop.GetNodeOrNull<Control>(screen)?.Visible ?? true, false);
        }
    }

    private static void CheckDebugAndOrder(SmokeContext ctx, GameLoop loop)
    {
        // Debug exists iff this is a debug build (GameLoop strips it otherwise).
        bool isDebug = OS.IsDebugBuild();
        CanvasLayer? debug = loop.GetNodeOrNull<CanvasLayer>("Debug");
        if ((debug is not null) == isDebug)
        {
            ctx.Pass($"Debug CanvasLayer present={debug is not null} matches OS.IsDebugBuild()={isDebug}");
        }
        else
        {
            ctx.Fail($"Debug CanvasLayer present={debug is not null} but OS.IsDebugBuild()={isDebug}");
        }

        // The SmokeRunner (added last by GameLoop) is not part of the authored tree.
        var expected = new List<string> { "Input", "World", "Hud", "Audio", "Screens" };
        if (isDebug)
        {
            expected.Add("Debug");
        }

        string expectedOrder = string.Join(",", expected);
        string actualOrder = string.Join(",", loop.GetChildren().Where(c => c is not SmokeRunner).Select(c => c.Name.ToString()));
        if (expectedOrder == actualOrder)
        {
            ctx.Pass($"Main child order is {actualOrder}");
        }
        else
        {
            ctx.Fail($"Main child order is {actualOrder}, expected {expectedOrder}");
        }

        if (loop.GetChildCount() > 0 && loop.GetChild(0).Name == "Input")
        {
            ctx.Pass("Input is child index 0 of Main");
        }
        else
        {
            ctx.Fail("Input is not child index 0 of Main");
        }

    }

    private static void CheckIdentity(SmokeContext ctx, GameLoop loop)
    {
        Expect(ctx, "Main node name", loop.Name.ToString(), "Main");
        if (loop.GetParent() == ctx.Tree.Root)
        {
            ctx.Pass("Main is a direct child of the SceneTree root");
        }
        else
        {
            ctx.Fail($"Main parent is {loop.GetParent()?.Name}, expected the SceneTree root");
        }

        int loops = CountGameLoops(ctx.Tree.Root);
        if (loops == 1)
        {
            ctx.Pass("exactly one GameLoop in the tree");
        }
        else
        {
            ctx.Fail($"GameLoop count in the tree is {loops}, expected 1");
        }
    }

    private static void CheckRoot(SmokeContext ctx, GameLoop loop)
    {
        Expect(ctx, "Main.ProcessMode", loop.ProcessMode, Node.ProcessModeEnum.Always);
        Expect(ctx, "Main.ProcessPriority", loop.ProcessPriority, int.MinValue);
        Expect(ctx, "Main.ProcessThreadGroup", loop.ProcessThreadGroup, Node.ProcessThreadGroupEnum.Inherit);
    }

    private static void CheckPresenters(SmokeContext ctx, GameLoop loop)
    {
        foreach (string path in new[] { "World", "Hud", "Audio" })
        {
            if (loop.GetNodeOrNull<Node>(path) is IPresenter presenter)
            {
                Expect(ctx, $"{path} presenter IsInitialized", presenter.IsInitialized, false);
            }
            else
            {
                ctx.Fail($"{path} is not an IPresenter");
            }
        }
    }

    private static void CheckSettings(SmokeContext ctx)
    {
        ExpectSetting(ctx, "application/run/main_scene", "res://src/Game/Main.tscn");
        ExpectSetting(ctx, "application/run/low_processor_mode", false);
        ExpectSetting(ctx, "display/window/size/viewport_width", 1920);
        ExpectSetting(ctx, "display/window/size/viewport_height", 1080);
        ExpectSetting(ctx, "display/window/size/window_width_override", 1600);
        ExpectSetting(ctx, "display/window/size/window_height_override", 900);
        ExpectSetting(ctx, "display/window/stretch/mode", "canvas_items");
        ExpectSetting(ctx, "display/window/stretch/aspect", "keep_height");
        ExpectSetting(ctx, "physics/common/physics_interpolation", false);
    }

    private static void CheckWindow(SmokeContext ctx)
    {
        Window root = ctx.Tree.Root;
        Expect(ctx, "root ContentScaleMode", root.ContentScaleMode, Window.ContentScaleModeEnum.CanvasItems);
        Expect(ctx, "root ContentScaleAspect", root.ContentScaleAspect, Window.ContentScaleAspectEnum.KeepHeight);
        Expect(ctx, "root ContentScaleSize", root.ContentScaleSize, new Vector2I(1920, 1080));
        Expect(ctx, "OS.LowProcessorUsageMode", OS.LowProcessorUsageMode, false);
    }

    private static void CheckNoAutoloads(SmokeContext ctx)
    {
        int total = 0;
        var autoloads = new List<string>();
        foreach (Godot.Collections.Dictionary prop in ProjectSettings.Singleton.GetPropertyList())
        {
            string propName = prop["name"].AsString();
            total++;
            if (propName.StartsWith("autoload/", StringComparison.Ordinal))
            {
                autoloads.Add(propName);
            }
        }

        // Sanity: the iteration must see real settings, or "none found" proves nothing.
        if (total == 0 || !ProjectSettings.HasSetting("application/run/main_scene"))
        {
            ctx.Fail($"ProjectSettings property iteration saw {total} properties; autoload check is not meaningful");
        }
        else if (autoloads.Count == 0)
        {
            ctx.Pass($"no autoload/* settings among {total} ProjectSettings properties");
        }
        else
        {
            ctx.Fail($"autoload settings present: {string.Join(",", autoloads)}");
        }
    }

    private static void Check<T>(SmokeContext ctx, GameLoop loop, string path)
        where T : Node
    {
        Node? node = loop.GetNodeOrNull<Node>(path);
        if (node is null)
        {
            ctx.Fail($"{path} is missing");
        }
        else if (node.GetType() != typeof(T))
        {
            ctx.Fail($"{path} is {node.GetType().Name}, expected {typeof(T).Name}");
        }
        else
        {
            ctx.Pass($"{path} exists as {typeof(T).Name}");
        }
    }

    private static int CountGameLoops(Node node)
    {
        int count = node is GameLoop ? 1 : 0;
        foreach (Node child in node.GetChildren())
        {
            count += CountGameLoops(child);
        }

        return count;
    }

    private static void Expect<T>(SmokeContext ctx, string what, T actual, T expected)
        where T : notnull
    {
        if (actual.Equals(expected))
        {
            ctx.Pass($"{what} == {expected}");
        }
        else
        {
            ctx.Fail($"{what} is {actual}, expected {expected}");
        }
    }

    private static void ExpectSetting(SmokeContext ctx, string key, string expected)
        => ExpectSetting(ctx, key, Variant.Type.String, expected, v => v.AsString());

    private static void ExpectSetting(SmokeContext ctx, string key, int expected)
        => ExpectSetting(ctx, key, Variant.Type.Int, expected, v => v.AsInt32());

    private static void ExpectSetting(SmokeContext ctx, string key, bool expected)
        => ExpectSetting(ctx, key, Variant.Type.Bool, expected, v => v.AsBool());

    private static void ExpectSetting<T>(SmokeContext ctx, string key, Variant.Type type, T expected, Func<Variant, T> read)
        where T : notnull
    {
        if (!ProjectSettings.HasSetting(key))
        {
            ctx.Fail($"setting {key} is not present");
            return;
        }

        Variant value = ProjectSettings.GetSetting(key);
        if (value.VariantType != type)
        {
            ctx.Fail($"setting {key} has variant type {value.VariantType}, expected {type}");
            return;
        }

        Expect(ctx, $"setting {key}", read(value), expected);
    }
}
