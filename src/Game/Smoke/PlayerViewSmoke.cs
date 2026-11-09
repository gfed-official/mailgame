using Godot;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Views.Player;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// player-character story 022 over the real boot with a fake clock. Before the loopback join is answered no player view exists. Once
/// <c>ILocalPlayer.IsKnown</c>, one <c>PlayerView</c> sits under <c>World/Entities</c>, registered (live) under the local player's id.
/// It has the greybox shape (<c>RigOffset</c> → <c>Body</c>, <c>Hat</c>, <c>Facing</c>, <c>HeldItemAnchor</c>, <c>HatAnchor</c>) and no
/// collider or physics body. With forward input the carrier walks, and the view follows <c>ICarrierView</c>. On each tick boundary
/// (alpha 0) it shows <c>Previous</c>. Half a tick later (alpha 500) it shows the midpoint of <c>Previous</c> and <c>Current</c>, never
/// below the ground, and faces the body yaw. The yaw convention is checked too: BAM 16384 turns −Z forward onto −X, and camera yaw → BAM
/// rounds half away from zero and wraps negatives.
/// </summary>
[SmokeScenario("player-view")]
public sealed class PlayerViewSmoke : SmokeScenario
{
    private const int MaxJoinFrames = 10;
    private const int WalkTicks = 15;

    private readonly FakeClock _clock = new();
    private PresenterProbeView _probe = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        loop.UseBootOptionsForSmoke(new BootOptions { Clock = _clock });
        _probe = new PresenterProbeView { Tag = "player" };
        loop.GetNode("World").AddChild(_probe);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (loop.Session is not { LocalPlayer: { } local } session || _probe.Deps is not { } deps)
        {
            ctx.Fail("no booted session with a local player, or no presenter deps");
            return;
        }

        Node entities = loop.GetNode("World/Entities");
        Expect(ctx, "no player view before the join", entities.GetChildCount(), 0);
        loop._Process(0);   // starts the driver; no tick
        int frames = 0;
        while (!local.IsKnown && frames++ < MaxJoinFrames)
        {
            Tick(loop);
        }

        if (!Expect(ctx, "ILocalPlayer.IsKnown", local.IsKnown, true))
        {
            return;
        }

        Tick(loop);
        Expect(ctx, "one view under World/Entities once the local player is known", entities.GetChildCount(), 1);
        if (entities.GetChildCount() != 1 || entities.GetChild(0) is not PlayerView view)
        {
            ctx.Fail("World/Entities child is not a PlayerView");
            return;
        }

        Expect(ctx, "registry holds the view under the local player's id", deps.Entities.TryGet(local.Id, out Node3D found) && ReferenceEquals(found, view), true);
        CheckShape(ctx, view);
        CheckFollows(ctx, loop, session, view);
        CheckConventions(ctx);
    }

    private static void CheckShape(SmokeContext ctx, PlayerView view)
    {
        Expect(ctx, "view is initialised", view.IsInitialized, true);
        foreach (string path in new[] { "RigOffset", "RigOffset/Body", "RigOffset/Hat", "RigOffset/Facing", "RigOffset/HeldItemAnchor", "RigOffset/HatAnchor" })
        {
            Expect(ctx, $"view has {path}", view.GetNodeOrNull(path) is not null, true);
        }

        Expect(ctx, "body is a 0.3 m × 1.4 m capsule", view.GetNodeOrNull<MeshInstance3D>("RigOffset/Body")?.Mesh is CapsuleMesh { Radius: 0.3f, Height: 1.4f }, true);
        Expect(ctx, "no collider under the view", view.FindChildren("*", nameof(CollisionObject3D), true, false).Count, 0);
        Expect(ctx, "no physics body under the view", view.FindChildren("*", nameof(PhysicsBody3D), true, false).Count, 0);
    }

    private void CheckFollows(SmokeContext ctx, GameLoop loop, BootSession session, PlayerView view)
    {
        ICarrierView carrier = session.Carrier.View;
        if (!carrier.HasCarrier)
        {
            ctx.Fail("carrier has not stepped");
            return;
        }

        (int startX, int startZ) = (carrier.Current.X, carrier.Current.Z);
        int boundaryMisses = 0;
        for (int i = 0; i < WalkTicks; i++)
        {
            session.SetMoveInputForSmoke(0, 32767);
            Tick(loop);
            if (!At(view, carrier.Previous))
            {
                boundaryMisses++;
            }
        }

        Expect(ctx, $"carrier walked over {WalkTicks} ticks ({startX},{startZ} → {carrier.Current.X},{carrier.Current.Z})", carrier.Current.X != startX || carrier.Current.Z != startZ, true);
        Expect(ctx, "still moving on the last tick (Previous ≠ Current)", carrier.Previous.X != carrier.Current.X || carrier.Previous.Z != carrier.Current.Z, true);
        Expect(ctx, "on every tick boundary (alpha 0) the view shows Previous", boundaryMisses, 0);

        _clock.Advance(FakeClock.TickCounts / 2);
        loop._Process(0);
        ref readonly CarrierState p = ref carrier.Previous;
        ref readonly CarrierState c = ref carrier.Current;
        var mid = new Vector3((p.X + c.X) / 2000f, 0f, (p.Z + c.Z) / 2000f);
        var flat = new Vector3(view.Position.X, 0f, view.Position.Z);
        Expect(ctx, $"half a tick later the view is midway ({flat} vs {mid})", flat.DistanceTo(mid) <= 0.0015f, true);
        Expect(ctx, "the view is never below the ground", view.Position.Y * 1000f >= session.World.TerrainHeightMm(view.LastPose.XMm, view.LastPose.ZMm) - 1, true);
        Vector3 forward = -view.GlobalBasis.Z;
        float theta = YawRadians.ToRotationY(view.LastPose.Yaw);
        Expect(ctx, "the view faces the body yaw (forward = (−sin θ, −cos θ))", forward.IsEqualApprox(new Vector3(-Mathf.Sin(theta), 0f, -Mathf.Cos(theta))), true);
    }

    private static void CheckConventions(SmokeContext ctx)
    {
        var probe = new Node3D { Rotation = new Vector3(0f, YawRadians.ToRotationY(16384), 0f) };
        Expect(ctx, "BAM 16384: −Z forward points along −X", (-probe.Basis.Z).IsEqualApprox(Vector3.Left), true);
        probe.Rotation = new Vector3(0f, YawRadians.ToRotationY(0), 0f);
        Expect(ctx, "BAM 0: forward is −Z", (-probe.Basis.Z).IsEqualApprox(Vector3.Forward), true);
        probe.Free();
        float halfStep = Mathf.Tau / 65536f / 2f;
        Expect(ctx, "camera yaw −π/2 → BAM 49152 (negative wraps)", YawRadians.ToBam(-Mathf.Pi / 2f), (ushort)49152);
        Expect(ctx, "camera yaw 2π → BAM 0", YawRadians.ToBam(Mathf.Tau), (ushort)0);
        Expect(ctx, "camera yaw +½ BAM step rounds away to 1", YawRadians.ToBam(halfStep * 1.0001f), (ushort)1);
        Expect(ctx, "camera yaw −½ BAM step rounds away to 65535", YawRadians.ToBam(-halfStep * 1.0001f), (ushort)65535);
    }

    private static bool At(PlayerView view, in CarrierState s) =>
        Mathf.Abs((view.Position.X * 1000f) - s.X) <= 1f && Mathf.Abs((view.Position.Z * 1000f) - s.Z) <= 1f;

    private void Tick(GameLoop loop)
    {
        _clock.Advance(FakeClock.TickCounts);
        loop._Process(0);
    }
}
