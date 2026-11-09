using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.World;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// godot-host-shell story 009 AC-3 over the real boot: <c>World/Destinations</c> holds exactly one <c>Lot_&lt;Number&gt;</c> per
/// <see cref="IWorldQuery.Lots"/> entry (10 on the M-1a street), uniquely named, at the lot anchor in metres inside the map bounds, yawed
/// by <see cref="FaceYaw.ToBam"/>, registered (live) in the entity-view registry under the lot's destination, each with one instance
/// of the <c>destination_types</c> scene (the placeholder) and no collider anywhere under it.
/// </summary>
[SmokeScenario("destinations-lots")]
public sealed class DestinationsLotsSmoke : SmokeScenario
{
    private PresenterProbeView _probe = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        _probe = new PresenterProbeView { Tag = "lots" };
        loop.GetNode("World").AddChild(_probe);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        if (_probe.Deps is not { } deps)
        {
            ctx.Fail("probe has no deps");
            return;
        }

        Node destinations = ctx.Loop.GetNode("World/Destinations");
        ReadOnlySpan<LotRecord> lots = deps.World.Lots;
        Expect(ctx, "lots on the M-1a street", lots.Length, 10);
        Expect(ctx, "children under World/Destinations", destinations.GetChildCount(), lots.Length);
        Expect(ctx, "unique child names", destinations.GetChildren().Select(c => c.Name.ToString()).Distinct().Count(), lots.Length);
        MapBounds b = deps.World.Bounds;
        var ids = new HashSet<uint>();
        for (int i = 0; i < lots.Length; i++)
        {
            LotRecord lot = lots[i];
            ids.Add(lot.Destination.Value);
            var node = destinations.GetNodeOrNull<Node3D>($"Lot_{lot.Number}");
            if (node is null)
            {
                ctx.Fail($"Lot_{lot.Number} missing");
                continue;
            }

            var anchor = new Vector3(lot.Anchor.X / 1000f, lot.Anchor.Y / 1000f, lot.Anchor.Z / 1000f);
            float yaw = FaceYaw.ToBam(lot.Face) * Mathf.Tau / 65536f;
            bool inside = node.Position.X * 1000 >= b.MinXMm && node.Position.X * 1000 <= b.MaxXMm
                && node.Position.Z * 1000 >= b.MinZMm && node.Position.Z * 1000 <= b.MaxZMm;
            Expect(ctx, $"Lot_{lot.Number} at anchor {anchor} inside bounds", node.Position.IsEqualApprox(anchor) && inside, true);
            Expect(ctx, $"Lot_{lot.Number} faces {lot.Face} (yaw {yaw:0.000})", Mathf.IsEqualApprox(node.Rotation.Y, yaw), true);
            Expect(ctx, $"Lot_{lot.Number} registered live", deps.Entities.TryGet(lot.Destination, out Node3D reg) && ReferenceEquals(reg, node), true);
            Expect(ctx, $"Lot_{lot.Number} view", node.GetChildCount() == 1 ? node.GetChild(0).Name.ToString() : $"{node.GetChildCount()} children", "PlaceholderView");
            Expect(ctx, $"Lot_{lot.Number} has no collider", node.FindChildren("*", nameof(CollisionObject3D), true, false).Count, 0);
        }

        Expect(ctx, "unique destination ids", ids.Count, lots.Length);
        Expect(ctx, "registry entries (the lots only)", deps.Entities.Count, lots.Length);
    }
}
