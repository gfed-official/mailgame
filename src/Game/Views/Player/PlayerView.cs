using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Player;

/// <summary>
/// The greybox carrier (player-character story 022, ADR-0019 sections 2 and 6): the root of <c>PlayerView.tscn</c>
/// (<c>RigOffset → Body, Hat, Facing, HeldItemAnchor, HatAnchor</c>; no collision, no physics body). Inert until
/// <see cref="Initialize"/>, which takes the published <see cref="ICarrierView"/> and the world tables; it moves only inside
/// <see cref="Present"/>, to <see cref="CarrierRenderPose.Compute"/> of the view's previous and current tick states (mm / 1000 here, at
/// the view boundary; yaw by <see cref="YawRadians.ToRotationY"/>). It reads the carrier and writes nothing back; <c>RigOffset</c>
/// leans forward <see cref="SprintLeanRadians"/> while the carrier sprints, and its position is left for Manual Delivery's lunge. Hidden until the carrier has stepped once.
/// The own carrier never hides what the player is aiming at or clips into the camera (GDD P9 framing, WP-G): every mesh pixel-dither fades
/// below <see cref="NearFadeM"/> from the camera, and while the camera-to-focused-anchor segment passes through the capsule
/// (<see cref="Occluding"/>) the whole carrier is drawn see-through at <see cref="OccludingAlpha"/>. Main thread only.
/// </summary>
/// <example><code>view.Initialize(deps); view.Present(alphaPermille);</code></example>
public partial class PlayerView : Node3D, IInjectable<PresenterDeps>, IPresentedView
{
    /// <summary>The sprint lean, forward about the rig's X axis (GDD player-character-movement, animation table: "Sprint: 15° lean").</summary>
    public const float SprintLeanRadians = 15f * Mathf.Pi / 180f;

    /// <summary>Camera distance below which a mesh pixel-dither fades out (GDD P9: "below 0.8 m from the head the carrier dither-fades").</summary>
    public const float NearFadeM = 0.8f;

    /// <summary>The carrier's opacity while it hides the focused anchor.</summary>
    public const float OccludingAlpha = 0.35f;

    // The capsule's radius plus a margin, and its height plus the hat, metres (PlayerView.tscn greybox).
    private const float OccluderRadiusM = 0.5f;
    private const float OccluderHeightM = 2.1f;

    private readonly List<StandardMaterial3D> _materials = [];
    private ICarrierView? _carrier;
    private Node3D? _rig;
    private IWorldQuery? _world;
    private IInteractionQuery? _focus;
    private IAnchorPoints? _anchors;

    /// <inheritdoc />
    public bool IsInitialized => _carrier is not null;

    /// <summary>The pose of the last <see cref="Present"/> that showed the carrier.</summary>
    public CarrierRenderPose LastPose { get; private set; }

    /// <summary>True while the carrier stands between the camera and the focused anchor (and is drawn see-through).</summary>
    public bool Occluding { get; private set; }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        _world = deps.World;
        _carrier = deps.Published.Get<ICarrierView>();
        deps.Published.TryGet(out _focus);
        deps.Published.TryGet(out _anchors);
        Visible = _carrier.HasCarrier;
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        if (_carrier is not { } carrier || _world is not { } world)
        {
            return;
        }

        if (!carrier.HasCarrier)
        {
            Visible = false;
            return;
        }

        CarrierRenderPose pose = CarrierRenderPose.Compute(in carrier.Previous, in carrier.Current, alphaPermille, world);
        LastPose = pose;
        Position = new Vector3(pose.XMm / 1000f, pose.YMm / 1000f, pose.ZMm / 1000f);
        Rotation = new Vector3(0f, YawRadians.ToRotationY(pose.Yaw), 0f);
        _rig ??= GetNodeOrNull<Node3D>("RigOffset");
        if (_rig is not null)
        {
            bool sprinting = (carrier.Current.Flags & CarrierFlags.Sprinting) != 0;
            _rig.Rotation = new Vector3(sprinting ? -SprintLeanRadians : 0f, 0f, 0f);
        }

        Visible = true;
        SetOccluding(HidesFocus(Position));
    }

    // The camera-to-anchor segment, seen from above, passes within the occluder radius of the carrier's axis, at a height the capsule covers.
    private bool HidesFocus(Vector3 feet)
    {
        if (_focus is null || _anchors is null || GetViewport()?.GetCamera3D() is not { } camera)
        {
            return false;
        }

        AnchorRef focused = _focus.Current.Focused;
        if (focused.IsNone || !_anchors.TryGetPoint(focused, out Vec3Mm p))
        {
            return false;
        }

        Vector3 c = camera.GlobalPosition;
        var a = new Vector3(p.X / 1000f, p.Y / 1000f, p.Z / 1000f);
        var d = new Vector2(a.X - c.X, a.Z - c.Z);
        float len2 = d.LengthSquared();
        if (len2 < 1e-6f)
        {
            return false;
        }

        float t = Mathf.Clamp(((feet.X - c.X) * d.X + ((feet.Z - c.Z) * d.Y)) / len2, 0f, 1f);
        var closest = new Vector2(c.X + (d.X * t), c.Z + (d.Y * t));
        float y = c.Y + ((a.Y - c.Y) * t);
        return t > 0f && t < 1f && closest.DistanceTo(new Vector2(feet.X, feet.Z)) < OccluderRadiusM && y >= feet.Y && y <= feet.Y + OccluderHeightM;
    }

    private void SetOccluding(bool occluding)
    {
        if (_materials.Count == 0)
        {
            TakeMaterials(this);
        }

        if (occluding == Occluding)
        {
            return;
        }

        Occluding = occluding;
        foreach (StandardMaterial3D m in _materials)
        {
            m.Transparency = occluding ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
            Color albedo = m.AlbedoColor;
            m.AlbedoColor = new Color(albedo.R, albedo.G, albedo.B, occluding ? OccludingAlpha : 1f);
        }
    }

    // Own copies of the scene's materials (the .tscn sub-resources are shared), with the near-camera dither fade switched on.
    private void TakeMaterials(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D shared)
            {
                var own = (StandardMaterial3D)shared.Duplicate();
                own.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither;
                own.DistanceFadeMinDistance = NearFadeM / 2f;
                own.DistanceFadeMaxDistance = NearFadeM;
                mesh.MaterialOverride = own;
                _materials.Add(own);
            }

            TakeMaterials(child);
        }
    }
}
