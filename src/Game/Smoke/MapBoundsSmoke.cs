using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Camera;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.World;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// The street ends (WP-G item 5; playtest of f145219: the carrier ran to where the ground mesh stopped and saw sky below the horizon),
/// driven by keys and mouse through the production adapter: running west off the street end and north up the lawn beside the Post
/// Office, the carrier stops <see cref="CarrierBounds.InsetMm"/> inside the map; a fence stands on each map side with ground beyond it;
/// and with the camera behind the carrier toward the edge, the boom pulls in so the camera stays inside the map.
/// </summary>
[SmokeScenario("map-bounds")]
public sealed class MapBoundsSmoke : SmokeScenario
{
    private const int RunTicks = 240;
    private const int SlackMm = 50;

    // The gap between the Post Office (x 4 to 20 m) and house 1 (from x 28.5 m) runs north to the map edge.
    private const int NorthLaneXMm = 24_000;

    // Look yaw facing +X: the camera hangs behind the carrier, toward -X.
    private const ushort FaceEast = 49_152;

    private readonly FakeClock _clock = new();

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override double TimeoutSeconds => 90;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop) => loop.UseBootOptionsForSmoke(new BootOptions { Clock = _clock });

    /// <inheritdoc />
    public override void Start(SmokeContext ctx) =>
        Expect(ctx, "a session booted", ctx.Loop.Session is not null, true);

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (loop.InputBoot?.Adapter is not { } adapter || !adapter.IsInsideTree())
        {
            return SmokeStatus.Running;
        }

        if (loop.Session is not { LocalPlayer: { } local } session || loop.GetNodeOrNull<CameraRig>("World/CameraRig") is not { } rig)
        {
            ctx.Fail("no booted session or camera rig");
            return SmokeStatus.Done;
        }

        var drive = new SmokeDriver(_clock, loop, session.Carrier.View);
        CarrierView carrier = drive.Carrier;
        MapBounds b = session.World.Bounds;
        loop._Process(0);
        drive.TickUntil(20, () => local.IsKnown);

        // The fence on every side, the ground past it.
        foreach (string side in new[] { "West", "East", "North", "South" })
        {
            Expect(ctx, $"a fence stands on the {side} map side", loop.GetNodeOrNull<MeshInstance3D>($"World/Terrain/MapEdge/Fence/{side}") is not null, true);
            Expect(ctx, $"ground reaches past the {side} map side", loop.GetNodeOrNull<MeshInstance3D>($"World/Terrain/MapEdge/Skirt/{side}") is not null, true);
        }

        // West off the street end, sprinting.
        drive.Hold(Key.Shift, true);
        drive.Hold(Key.A, true);
        drive.TickN(RunTicks);
        drive.Hold(Key.A, false);
        drive.Hold(Key.Shift, false);
        drive.TickN(6);
        long west = carrier.Current.X - b.MinXMm;
        Expect(ctx, $"the west street end stops the carrier {CarrierBounds.InsetMm} mm inside ({west} mm)", Math.Abs(west - CarrierBounds.InsetMm) <= SlackMm, true);

        // The camera behind the carrier, toward the edge.
        drive.Turn(FaceEast);
        drive.TickN(30);
        float camX = rig.Camera!.GlobalPosition.X * 1000f;
        Expect(ctx, $"facing away from the west edge, the camera stays inside the map (x {camX:0} mm, boom {rig.BoomMm} mm)", camX >= b.MinXMm, true);
        drive.Turn(0);

        // North up the lawn beside the Post Office.
        drive.MoveTo(NorthLaneXMm, carrier.Current.Z);
        drive.Hold(Key.Shift, true);
        drive.Hold(Key.W, true);
        drive.TickN(RunTicks);
        drive.Hold(Key.W, false);
        drive.Hold(Key.Shift, false);
        drive.TickN(6);
        long north = carrier.Current.Z - b.MinZMm;
        Expect(ctx, $"the north map side stops the carrier {CarrierBounds.InsetMm} mm inside ({north} mm)", Math.Abs(north - CarrierBounds.InsetMm) <= SlackMm, true);
        return SmokeStatus.Done;
    }
}
