using System.Text;
using Godot;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Hud;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Game.Input;

/// <summary>
/// Boot step 4, the input router (ADR-0019 section 3, ADR-0012 sections 4, 6 and 9; input-controls story 028), in two halves.
/// <para>
/// <see cref="Run"/> (the step-4 <see cref="IBootContributor"/>): validates <see cref="BuiltInActionMap.Definitions"/> with
/// <see cref="ActionMapValidator.ValidateForBoot(IReadOnlyList{ActionDef}, out ActionMap)"/> and throws on any error, so boot
/// stops on the error screen (never a silent fallback); reads <c>user://input.json</c> through <see cref="InputSettingsFile"/>
/// into <see cref="Settings"/> (the Sim's <c>IClientInputSettings</c>), writing the codec's notes to <see cref="Log"/> and at
/// most one warning; builds the router over a <see cref="StopwatchInputClock"/>, a new <see cref="UiEventLog"/> and the
/// late-bound <see cref="Ports"/> (the session that publishes them is step 5-6's); and places the adapter as the last child of
/// the root window (deferred, spike H-1). No verb sink exists yet (Throwing 007, Grid Inventory 024, the HotbarSelect seam), so
/// none is registered and a fired immediate logs <c>no_sink</c> (story 013); the step-4 line lists them.
/// </para>
/// <para>
/// <see cref="AttachToLoop"/> (after boot, once the frame sequence is installed): binds the client tick to the booted session and
/// any port a contributor published, initialises the adapter, and registers, in this order, the adapter's motion flush, then
/// <see cref="RouterFrameCalls.BeforeRunFrame"/> (<c>DispatchImmediates</c> → <c>FlushToLatch</c>) as pre-frame hooks and
/// <see cref="RouterFrameCalls.AfterPresent"/> (<c>PresentedPromptLatch.Update</c>) as a post-present hook. The adapter stays
/// inert until then, so a failed boot's error screen gets every event. Nothing here pauses anything (never
/// <c>SceneTree.Paused</c>). Main thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var input = new InputBoot(this);                          // GameLoop: one of BootPlan's step-4 contributors
/// // ... RequestBoot runs input.Run(ctx) at step 4, then installs the frame sequence ...
/// input.AttachToLoop(this);                                 // first, before any other pre-frame hook
/// </code>
/// </example>
/// <param name="main">The <c>Main</c> node (in the tree when step 4 runs): its tree's root window gets the adapter, and the adapter's
/// debug focus check scans it.</param>
/// <param name="settingsPath">The settings file; <see cref="InputSettingsFile.DefaultPath"/> unless a smoke points elsewhere.</param>
/// <param name="definitions">The action definitions to validate; null for <see cref="BuiltInActionMap.Definitions"/>.</param>
public sealed class InputBoot(Node main, string settingsPath = InputSettingsFile.DefaultPath, IReadOnlyList<ActionDef?>? definitions = null)
    : IBootContributor
{
    private readonly Node _main = main ?? throw new ArgumentNullException(nameof(main));
    private PublishedStates? _published;
    private ActionMap? _map;
    private PresentedPromptLatch? _prompt;
    private bool _attached;

    /// <inheritdoc />
    public int Step => BootPlan.InputStep;

    /// <summary>The router; null until <see cref="Run"/> succeeded.</summary>
    public InputRouter? Router { get; private set; }

    /// <summary>The adapter node; null until <see cref="Run"/> succeeded.</summary>
    public InputAdapter? Adapter { get; private set; }

    /// <summary>The live input settings; the Sim reads the hold time through it (<c>IClientInputSettings</c>).</summary>
    public InputSettingsHolder Settings { get; } = new();

    /// <summary>What step 4 read from the settings file; null before <see cref="Run"/>.</summary>
    public InputSettingsRead? SettingsRead { get; private set; }

    /// <summary>The router's ms clock (the adapter stamps with the same instance).</summary>
    public StopwatchInputClock Clock { get; } = new();

    /// <summary>The shared event log the router writes.</summary>
    public UiEventLog Log { get; } = new();

    /// <summary>The router's ClientLayer ports, bound by <see cref="AttachToLoop"/>.</summary>
    public InputPortSlots Ports { get; } = new();

    /// <summary>The router's per-frame calls; null until <see cref="AttachToLoop"/>.</summary>
    public RouterFrameCalls? FrameCalls { get; private set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The action map has a validation error.</exception>
    public void Run(BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ValidationReport report = ActionMapValidator.ValidateForBoot(definitions ?? BuiltInActionMap.Definitions(), out ActionMap? map);
        if (map is null)
        {
            throw new InvalidOperationException($"The input action map failed validation ({report.Errors.Count} error(s)):\n{report}");
        }

        ctx.Log.Write($"BOOT 4 input: action map valid ({map.Count} actions, {report.Warnings.Count} warning(s))");

        InputSettingsRead read = InputSettingsFile.Read(settingsPath);
        SettingsRead = read;
        Settings.Apply(read.Settings);
        read.Report?.WriteTo(Log, Clock.NowMs);
        ctx.Log.Write($"BOOT 4 input: {read.Summary}");
        if (read.Warning is { } warning)
        {
            GD.PushWarning(warning);
        }

        _prompt = new PresentedPromptLatch(Ports.Interaction);
        Router = new InputRouter(map, Settings.Current, Clock, Ports.Interaction, _prompt, Ports.Carrier, Ports.ThrowAim, Ports.Ticks, Log);
        _map = map;
        ctx.Published.Add(Settings);
        ctx.Published.Add(Router);
        ctx.Log.Write($"BOOT 4 input: immediate sinks: none yet (step 7 and AttachToLoop register them); immediate actions: {UnsunkActions(map, Router)}");

        _published = ctx.Published;
        Adapter = InputAdapter.AttachToRoot(_main.GetTree().Root);
        ctx.Log.Write("BOOT 4 input: router built, adapter queued as the last child of the root window");
    }

    /// <summary>
    /// Wires the booted router into <paramref name="loop"/>'s installed frame sequence (see the class summary). Call once, right
    /// after the sequence is installed and before any other pre-frame hook is registered, so the adapter's hook runs first.
    /// </summary>
    /// <param name="loop">The loop whose sequence runs the hooks and whose session owns the latch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Run"/> has not succeeded, the loop has no booted session or sequence, or this already ran.</exception>
    public void AttachToLoop(GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (Router is null || Adapter is null || _prompt is null)
        {
            throw new InvalidOperationException("InputBoot.AttachToLoop needs a successful boot step 4 first.");
        }

        if (_attached)
        {
            throw new InvalidOperationException("InputBoot.AttachToLoop already ran.");
        }

        BootSession session = loop.Session ?? throw new InvalidOperationException("InputBoot.AttachToLoop needs the booted session (steps 5-6).");
        Ports.BindSession(session.Session);
        BindPublished();
        FrameCalls = RouterFrameCalls.ForSession(Router, _prompt, session.Session);
        var hooks = new FrameHooks(FrameCalls);

        // The adapter first: its summed mouse motion must reach the router before the router flushes.
        loop.RegisterPreFrameHook(Adapter);
        loop.RegisterPreFrameHook(hooks);
        loop.RegisterPostPresentHook(hooks);
        Adapter.Initialize(new AdapterDeps { Router = Router, Clock = Clock, Settings = Settings.Current, Main = _main });
        RegisterSinks(loop);
        _attached = true;
        GD.Print($"[InputBoot] attached: pre-frame adapter, dispatch+flush; post-present prompt latch; ports {Ports.Describe()}");
        GD.Print($"[InputBoot] no_sink: {UnsunkActions(_map!, Router)}");
    }

    /// <summary>The pause overlay (Esc / P); null until <see cref="AttachToLoop"/>.</summary>
    public PauseOverlay? PauseOverlay { get; private set; }

    // The verb sinks this step owns: Throw's commit (the client loop's release sender) and the Back ladder ending in the solo pause.
    private void RegisterSinks(GameLoop loop)
    {
        if (_published is { } published && published.TryGet(out ClientVerbSinks? verbs))
        {
            Router!.RegisterSink(ActionId.Throw, verbs.Throw);
            for (ushort id = ActionId.HotbarSlot1; id <= ActionId.HotbarSlot8; id++)
            {
                Router.RegisterSink(id, verbs.HotbarKeys);
            }
        }

        PauseOverlay = PauseOverlay.Create();
        _main.AddChild(PauseOverlay);
        var back = new BackLadder(Router!, () => PauseOverlay.Visible = loop.TogglePause());
        Router!.RegisterSink(ActionId.Back, back);
        Router.RegisterSink(ActionId.Pause, back);
    }

    private void BindPublished()
    {
        if (_published is not { } published)
        {
            return;
        }

        if (published.Types.Contains(typeof(IInteractionQuery)))
        {
            Ports.BindInteraction(published.Get<IInteractionQuery>());
        }

        if (published.Types.Contains(typeof(ICarrierStatusSource)))
        {
            Ports.BindCarrier(published.Get<ICarrierStatusSource>());
        }

        if (published.Types.Contains(typeof(IThrowAimView)))
        {
            Ports.BindThrowAim(published.Get<IThrowAimView>());
        }
    }

    private static string UnsunkActions(ActionMap map, InputRouter router)
    {
        var line = new StringBuilder();
        foreach (ActionDef d in map.All)
        {
            if ((d.Delivery == Delivery.Immediate || d.CommitOnRelease) && !router.HasSink(d.Id))
            {
                line.Append(line.Length == 0 ? string.Empty : ",").Append(d.StringId);
            }
        }

        return line.ToString();
    }

    // The two frame-sequence hooks over the router's calls (ADR-0012 sections 4 and 6 order).
    private sealed class FrameHooks(RouterFrameCalls calls) : IPreFrameHook, IPostPresentHook
    {
        public void OnPreFrame() => calls.BeforeRunFrame();

        public void OnPostPresent() => calls.AfterPresent();
    }
}
