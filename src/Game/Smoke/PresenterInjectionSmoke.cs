using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Players.Client;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// godot-host-shell story 006 AC-1, AC-2 and AC-4 over the real boot and frame loop. Probe views sit in the <c>World</c> (nested under
/// <c>World/Entities</c>), <c>Hud</c> and <c>Audio</c> subtrees before boot: none is initialised when step 6 ends, each exactly once
/// after step 7, and step 7 logs its presenters and its one world-build step (story 009's destinations). The deps carry the published <c>ICarrierView</c> and the
/// adapter's event stream; a missing published state throws naming its type. Each frame presents World, then Hud, then Audio, views in
/// registration order with the frame's alpha; a view registered between frames is initialised at once and presented from the next
/// frame, and one registered during <c>Present</c> is first presented the frame after.
/// </summary>
[SmokeScenario("presenter-injection")]
public sealed class PresenterInjectionSmoke : SmokeScenario
{
    private readonly FakeClock _clock = new();
    private readonly List<string> _presented = [];
    private PresenterProbeView _world = null!;   // nodes are created in BeforeBoot, not when the runner lists scenarios
    private PresenterProbeView _hud = null!;
    private PresenterProbeView _audio = null!;
    private int _initsAfterStep6 = -1;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        loop.UseBootOptionsForSmoke(new BootOptions { Clock = _clock });
        _world = AddProbe(loop, "World/Entities", "world");
        _hud = AddProbe(loop, "Hud", "hud");
        _audio = AddProbe(loop, "Audio", "audio");

        IBootStep[] steps = [.. loop.BuildBootPlan()];
        steps[BootPlan.SimStep - 1] = new AfterStep(steps[BootPlan.SimStep - 1], () => _initsAfterStep6 = _world.Inits + _hud.Inits + _audio.Inits);
        loop.InstallBootStepsForSmoke(steps);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (!Expect(ctx, "GameLoop holds the booted session", loop.Session is not null, true) || loop.Session is not { } session)
        {
            return;
        }

        CheckInjection(ctx, loop.BootLines);
        if (_world.Deps is not { } deps)
        {
            ctx.Fail("world probe has no deps");
            return;
        }

        CheckDeps(ctx, deps, session);
        CheckFrames(ctx, loop, deps);
    }

    private void CheckInjection(SmokeContext ctx, IReadOnlyList<string> lines)
    {
        foreach (string line in lines.Where(l => l.StartsWith("BOOT 7 ", StringComparison.Ordinal)))
        {
            ctx.Pass($"boot log: {line}");
        }

        Expect(ctx, "step 7 ok", BootSmokeFaults.Logged(lines, "BOOT 7 world ok"), true);
        Expect(ctx, "step 7 logs the presenters", BootSmokeFaults.Logged(lines, "BOOT 7 world: presenters World,Hud,Audio initialized"), true);
        Expect(ctx, "step 7 runs the one world-build step (destinations, story 009)", BootSmokeFaults.Logged(lines, "BOOT 7 world: world-build steps ran=1"), true);
        Expect(ctx, "probe inits when step 6 ends", _initsAfterStep6, 0);
        Expect(ctx, "probe inits after boot (world, hud, audio)", $"{_world.Inits},{_hud.Inits},{_audio.Inits}", "1,1,1");
        Expect(ctx, "probes share one PresenterDeps", ReferenceEquals(_world.Deps, _hud.Deps) && ReferenceEquals(_hud.Deps, _audio.Deps), true);
    }

    private static void CheckDeps(SmokeContext ctx, PresenterDeps deps, BootSession session)
    {
        Expect(ctx, "deps publish the owned carrier's ICarrierView", ReferenceEquals(deps.Published.Get<ICarrierView>(), session.Carrier.View), true);
        try
        {
            deps.Published.Get<IDisposable>();
            ctx.Fail("Get of an unpublished state did not throw");
        }
        catch (InvalidOperationException ex)
        {
            Expect(ctx, $"Get of an unpublished state names it ({ex.Message})", ex.Message.Contains(typeof(IDisposable).FullName!, StringComparison.Ordinal), true);
        }

        Expect(ctx, "deps.Events is the client adapter's DecodedEvents", ReferenceEquals(deps.Events, session.Session.ClientAdapter.DecodedEvents), true);
        Expect(ctx, "deps.World is the client's world tables", ReferenceEquals(deps.World, session.World), true);
    }

    private void CheckFrames(SmokeContext ctx, GameLoop loop, PresenterDeps deps)
    {
        var worldPresenter = loop.GetNode<WorldPresenter>("World");
        var late = new PresenterProbeView { Tag = "late", Log = _presented };
        var spawned = new PresenterProbeView { Tag = "spawned", Log = _presented };

        Frame(loop, 0);   // starts the driver
        Expect(ctx, "frame 1 present order", string.Join(",", _presented), "world,hud,audio");

        worldPresenter.Register(late);
        Expect(ctx, "a view registered after boot is initialised at once", late.Inits, 1);
        _world.OnPresent = () =>
        {
            _world.OnPresent = null;
            worldPresenter.Register(spawned);
        };
        Frame(loop, FakeClock.TickCounts + (FakeClock.TickCounts / 2));
        Expect(ctx, "frame 2 present order (late view next frame; spawned during Present not yet)", string.Join(",", _presented), "world,late,hud,audio");
        Expect(ctx, "frame 2 alpha", deps.Clock.AlphaPermille, 500);
        Expect(ctx, "alpha passed through unchanged (world, late, hud, audio)", $"{_world.LastAlpha},{late.LastAlpha},{_hud.LastAlpha},{_audio.LastAlpha}", "500,500,500,500");
        Expect(ctx, "a view registered during Present is initialised at once", spawned.Inits, 1);

        Frame(loop, FakeClock.TickCounts);
        Expect(ctx, "frame 3 present order", string.Join(",", _presented), "world,late,spawned,hud,audio");
        Expect(ctx, "inits stay at one (world, hud, audio, late, spawned)", $"{_world.Inits},{_hud.Inits},{_audio.Inits},{late.Inits},{spawned.Inits}", "1,1,1,1,1");
        late.Free();
        spawned.Free();
    }

    private PresenterProbeView AddProbe(GameLoop loop, string parent, string tag)
    {
        var probe = new PresenterProbeView { Tag = tag, Log = _presented };
        loop.GetNode(parent).AddChild(probe);
        return probe;
    }

    private void Frame(GameLoop loop, long counts)
    {
        _presented.Clear();
        _clock.Advance(counts);
        loop._Process(0);
    }

    /// <summary>Runs a step, then a callback (the scenario reads the probes between steps 6 and 7).</summary>
    private sealed class AfterStep(IBootStep inner, Action after) : IBootStep
    {
        public int Number => inner.Number;

        public string Name => inner.Name;

        public BootFailureKind FailureKind => inner.FailureKind;

        public BootStepResult Run(BootContext ctx)
        {
            BootStepResult result = inner.Run(ctx);
            after();
            return result;
        }
    }
}
