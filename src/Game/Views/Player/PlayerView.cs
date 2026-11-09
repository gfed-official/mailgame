using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Presentation;
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
/// leans forward <see cref="SprintLeanRadians"/> while the carrier sprints, and its position is left for Manual Delivery's lunge. Hidden until the carrier has stepped once. Main thread only.
/// </summary>
/// <example><code>view.Initialize(deps); view.Present(alphaPermille);</code></example>
public partial class PlayerView : Node3D, IInjectable<PresenterDeps>, IPresentedView
{
    /// <summary>The sprint lean, forward about the rig's X axis (GDD player-character-movement, animation table: "Sprint: 15° lean").</summary>
    public const float SprintLeanRadians = 15f * Mathf.Pi / 180f;

    private ICarrierView? _carrier;
    private Node3D? _rig;
    private IWorldQuery? _world;

    /// <inheritdoc />
    public bool IsInitialized => _carrier is not null;

    /// <summary>The pose of the last <see cref="Present"/> that showed the carrier.</summary>
    public CarrierRenderPose LastPose { get; private set; }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        _world = deps.World;
        _carrier = deps.Published.Get<ICarrierView>();
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
    }
}
