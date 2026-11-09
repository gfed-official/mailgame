using System.Runtime.InteropServices;
using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Camera;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Hud;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// player-character story 026. Over the real boot the World presenter publishes <see cref="IHudFocusFeed"/>, and in a debug build the
/// Hud carries the <see cref="FocusDebugOverlay"/>; with no client <see cref="IInteractionQuery"/> yet the feed stays hidden. A
/// <see cref="FocusAnchorPresenter"/> over a scripted snapshot (an unavailable, half-held target with a cancel) and a camera at the origin
/// looking down −Z then shows: the anchor 5 m in front is visible at the viewport centre, its fallback-cube bounds enclose that point, the
/// snapshot's data is copied unchanged and <c>PresentedSeq</c> echoes the snapshot's <c>Seq</c>; the same anchor behind the camera is hidden.
/// </summary>
[SmokeScenario("focus-feed")]
public sealed class FocusFeedSmoke : SmokeScenario
{
    private const ushort Seq = 7;
    private const ushort ProgressPm = 416;

    private PresenterProbeView _probe = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        _probe = new PresenterProbeView { Tag = "focus" };
        loop.GetNode("World").AddChild(_probe);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (_probe.Deps is not { } deps)
        {
            ctx.Fail("no presenter deps");
            return;
        }

        CheckBoot(ctx, loop, deps);
        CheckProjection(ctx, loop, deps);
    }

    // The snapshot's setters are the Sim's; the smoke writes its documented explicit layout (TD-209 item 1) instead.
    private static InteractionSnapshot Snapshot(AnchorRef focused, StringId reason)
    {
        InteractionSnapshot s = default;
        Span<byte> b = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref s, 1));
        MemoryMarshal.Write(b[4..], Seq);
        b[6] = (byte)InteractState.Holding;
        b[7] = (byte)CancelReason.Swim;
        MemoryMarshal.Write(b[8..], focused);
        b[39] = 0;   // not available
        MemoryMarshal.Write(b[40..], reason);
        MemoryMarshal.Write(b[50..], ProgressPm);
        return s;
    }

    private static void CheckBoot(SmokeContext ctx, GameLoop loop, PresenterDeps deps)
    {
        Expect(ctx, "IHudFocusFeed published at boot", deps.Published.TryGet(out IHudFocusFeed? feed), true);
        Expect(ctx, "debug build: the Hud carries the focus debug overlay", loop.GetNodeOrNull<FocusDebugOverlay>("Hud/FocusDebugOverlay") is not null, OS.IsDebugBuild());
        loop._Process(0);
        Expect(ctx, "no client IInteractionQuery yet: the feed stays hidden", feed is { Current.Visible: false }, true);
    }

    private static void CheckProjection(SmokeContext ctx, GameLoop loop, PresenterDeps deps)
    {
        var anchor = new AnchorRef(new EntityId(0x7FFF_0001u), 0);
        var reason = new StringId(StringNamespace.Item, 3);
        var query = new ScriptedQuery(Snapshot(anchor, reason));
        var points = new ScriptedPoints { Point = new Vec3Mm(0, 0, -5000) };
        var published = new PublishedStates();
        published.Add<IInteractionQuery>(query);
        published.Add<IAnchorPoints>(points);
        var camera = new Camera3D { Name = "FocusProbeCamera" };
        loop.GetNode("World").AddChild(camera);
        try
        {
            var focus = new FocusAnchorPresenter(camera, new PresenterDeps
            {
                World = deps.World,
                Clock = deps.Clock,
                Events = deps.Events,
                Published = published,
                Audio = deps.Audio,
                Views = deps.Views,
                Entities = deps.Entities,
            });
            focus.Present(0);
            FocusFeed f = focus.Current;
            Vector2 centre = camera.GetViewport().GetVisibleRect().Size / 2f;
            Expect(ctx, "anchor in front: visible, kind Focused", f.Visible && f.Kind == FocusFeedKind.Focused, true);
            Expect(ctx, "PresentedSeq echoes the snapshot Seq", f.PresentedSeq, Seq);
            Expect(ctx, $"screen position is the viewport centre ({f.ScreenPos} vs {centre})", f.ScreenPos.DistanceTo(centre) < 1f, true);
            Expect(ctx, $"bounds enclose the point and have area ({f.ScreenBounds})", f.ScreenBounds.HasPoint(f.ScreenPos) && f.ScreenBounds.Area > 0f, true);
            Expect(
                ctx,
                "unavailable reason, state, progress and cancel passed through unchanged",
                f.Focused == anchor && !f.Available && f.Reason == reason && f.State == InteractState.Holding && f.ProgressPm == ProgressPm && f.LastCancel == CancelReason.Swim,
                true);

            points.Point = new Vec3Mm(0, 0, 5000);
            focus.Present(0);
            Expect(ctx, "anchor behind the camera: hidden, kind None", !focus.Current.Visible && focus.Current.Kind == FocusFeedKind.None, true);
            Expect(ctx, "hidden feed still echoes the Seq", focus.Current.PresentedSeq, Seq);
        }
        finally
        {
            camera.QueueFree();
        }
    }

    private sealed class ScriptedQuery(InteractionSnapshot snapshot) : IInteractionQuery
    {
        private readonly InteractionSnapshot _snapshot = snapshot;

        public ref readonly InteractionSnapshot Current => ref _snapshot;
    }

    private sealed class ScriptedPoints : IAnchorPoints
    {
        public Vec3Mm Point { get; set; }

        public bool TryGetPoint(AnchorRef anchor, out Vec3Mm point)
        {
            point = Point;
            return true;
        }
    }
}
