using Godot;
using PerformativeMail.Client.Core.Camera;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Game.Views.Player;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Camera;

/// <summary>
/// The third-person camera on <c>World/CameraRig → Boom → Camera3D</c> (player-character story 023, ADR-0019 section 2; GDD P9). Inert
/// until <see cref="Initialize"/>, which takes the published <see cref="ICarrierView"/>, the world tables and the render clock. Each
/// frame, once the carrier has stepped, <see cref="Present"/>:
/// <list type="bullet">
/// <item>follows the rendered feet (<see cref="CarrierRenderPose.Compute"/>, the pose <c>PlayerView</c> shows) with the
/// <see cref="FollowSpring"/>, timed by <c>IRenderClock.RenderSimTimeMs</c> differences (never <c>delta</c>);</item>
/// <item>blends the default and aim profiles over 100 ms on <c>CarrierStatus.Aiming</c> (<see cref="CameraBlend"/>);</item>
/// <item>solves the boom against the static solids and terrain (<see cref="BoomSolver"/>), smooths its length
/// (<see cref="BoomSmoother"/>, seeded from the first solve) and places the camera from <see cref="CameraPose.Compose"/> (the terrain
/// floor and water-plane clamps are its);</item>
/// <item>applies the carrier's look yaw and the clamped pitch to <c>Boom</c> unsmoothed, every frame.</item>
/// </list>
/// The first placed frame makes the camera current, taking over from <c>TerrainView</c>'s fallback pose. Nothing read from Godot feeds
/// back into the maths, and nothing here feeds the Sim. Main thread only.
/// </summary>
/// <example><code>// Main.tscn: World/CameraRig carries this script; WorldPresenter's subtree walk initialises and presents it.</code></example>
public partial class CameraRig : Node3D, IInjectable<PresenterDeps>, IPresentedView
{
    /// <summary>
    /// The focus cone threshold pushed to the router once at <see cref="Initialize"/>: <c>cosMinPm</c> for vertical FOV 80 at 16:9 with
    /// <c>focusConePm</c> 900 (PCM-P11-1). Fixed for M-1a; a FOV or aspect setting would recompute it.
    /// </summary>
    public const int FocusCosMinPm = 635;

    /// <summary>A frame gap above this (ms of render time) snaps the pivot instead of springing.</summary>
    public const long SnapGapMs = 1000;

    /// <summary>A rendered-feet jump above this (mm, a respawn or teleport) snaps the pivot instead of springing.</summary>
    public const float SnapDistanceMm = 10_000f;

    private readonly FollowSpring _spring = new();
    private readonly CameraSettings _settings = new();
    private ICarrierView? _carrier;
    private IWorldQuery? _world;
    private IRenderClock? _clock;
    private Node3D? _boom;
    private Camera3D? _camera;
    private Action<int>? _focusConeSink;
    private CameraProfile _blendFrom = CameraProfile.Default;
    private CameraProfile _blendTo = CameraProfile.Default;
    private long _blendStartMs;
    private bool _aiming;
    private long _lastMs;

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <summary>The rig's camera; null before <see cref="Initialize"/>.</summary>
    public Camera3D? Camera => _camera;

    /// <summary>True once the rig has placed the camera (and made it current).</summary>
    public bool HasTakenOver { get; private set; }

    /// <summary>The smoothed boom length of the last placed frame, mm.</summary>
    public int BoomMm { get; private set; }

    /// <summary>
    /// Binds the router's focus-cone entry (<c>IFocusConeSink.SetFocusCosMinPm</c> once input-controls 009 lands). It receives
    /// <see cref="FocusCosMinPm"/> once: at <see cref="Initialize"/>, or now if the rig is already initialised.
    /// </summary>
    /// <param name="sink">The sink.</param>
    public void BindFocusConeSink(Action<int> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _focusConeSink = sink;
        if (IsInitialized)
        {
            sink(FocusCosMinPm);
        }
    }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        _boom = GetNode<Node3D>("Boom");
        _camera = GetNode<Camera3D>("Boom/Camera3D");
        _world = deps.World;
        _clock = deps.Clock;
        deps.Published.TryGet(out _carrier);
        _camera.Fov = _settings.FovDeg(aiming: false);
        IsInitialized = true;
        _focusConeSink?.Invoke(FocusCosMinPm);
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        if (_carrier is not { HasCarrier: true } carrier || _world is not { } world || _clock is not { } clock
            || _boom is not { } boom || _camera is not { } camera)
        {
            return;
        }

        long now = clock.RenderSimTimeMs;
        long dtMs = HasTakenOver ? now - _lastMs : 0;
        _lastMs = now;
        ref readonly CarrierStatus status = ref carrier.Status;
        CameraProfile profile = Profile(status.Aiming, now, camera);

        CarrierRenderPose pose = CarrierRenderPose.Compute(in carrier.Previous, in carrier.Current, alphaPermille, world);
        var feet = new Vector3(pose.XMm, pose.YMm, pose.ZMm);
        if (!HasTakenOver || dtMs < 0 || dtMs > SnapGapMs || (feet - _spring.Position).LengthSquared() > SnapDistanceMm * SnapDistanceMm)
        {
            _spring.Reset(feet);
        }
        else
        {
            _spring.Step(feet, dtMs / 1000f);
        }

        Vector3 f = _spring.Position;
        var followed = new Vec3Mm(Mathf.RoundToInt(f.X), Mathf.RoundToInt(f.Y), Mathf.RoundToInt(f.Z));
        Vec3Mm pivot = CameraPose.Pivot(in followed, in profile);
        ushort yaw = carrier.LookYaw;
        short pitch = CameraSettings.ClampPitch(carrier.LookPitch);
        int shoulder = _settings.ShoulderMm(in profile);
        int target = BoomSolver.Solve(world, in pivot, yaw, pitch, shoulder, profile.BoomMm);
        BoomMm = HasTakenOver ? BoomSmoother.Step(BoomMm, target, dtMs) : target;
        Vec3Mm cam = CameraPose.Compose(world, in pivot, yaw, pitch, shoulder, profile.BoomMm, BoomMm, status.Swimming);

        Vector3 pivotM = Metres(in pivot);
        GlobalPosition = pivotM;
        boom.Rotation = new Vector3(pitch * Mathf.Tau / 65536f, YawRadians.ToRotationY(yaw), 0f);
        camera.Transform = new Transform3D(Basis.Identity, boom.GlobalBasis.Inverse() * (Metres(in cam) - pivotM));
        if (!HasTakenOver)
        {
            camera.Current = true;
            HasTakenOver = true;
        }
    }

    private static Vector3 Metres(in Vec3Mm v) => new(v.X / 1000f, v.Y / 1000f, v.Z / 1000f);

    // Default ↔ aim over CameraBlend.BlendMs of render time; a change mid-blend starts from the profile shown now.
    private CameraProfile Profile(bool aiming, long nowMs, Camera3D camera)
    {
        if (aiming != _aiming)
        {
            _blendFrom = CameraBlend.Lerp(in _blendFrom, in _blendTo, nowMs - _blendStartMs);
            _blendTo = aiming ? CameraProfile.Aim : CameraProfile.Default;
            _blendStartMs = nowMs;
            _aiming = aiming;
            camera.Fov = _settings.FovDeg(aiming);
        }

        return CameraBlend.Lerp(in _blendFrom, in _blendTo, nowMs - _blendStartMs);
    }
}
