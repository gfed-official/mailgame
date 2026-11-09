using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Camera;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Game.Views.Player;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.World;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// player-character story 023 over the real boot with a fake clock. <c>World/CameraRig</c> is initialised at boot and pushes the fixed
/// focus cone once. When the carrier walks the pivot trails the rendered feet (spring, not a snap), and once it stands it settles on
/// feet + 1.6 m with the rig's camera current. A second rig over a scripted carrier facing away from a house wall then checks the rest:
/// inert until <c>Initialize</c>, look yaw applied unsmoothed on the frame it changes, the boom pulled in against the house box with the
/// camera outside it, and the aim profile's shoulder/boom ratio (0.75 / 2.4) replacing the default's (0.6 / 3.0).
/// </summary>
[SmokeScenario("camera-rig")]
public sealed class CameraRigSmoke : SmokeScenario
{
    private const int MaxJoinFrames = 10;
    private const int WalkTicks = 10;
    private const int SettleTicks = 30;
    private const long FrameMs = 33;

    private readonly FakeClock _clock = new();
    private readonly List<int> _conePushes = [];
    private PresenterProbeView _probe = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        loop.UseBootOptionsForSmoke(new BootOptions { Clock = _clock });
        _probe = new PresenterProbeView { Tag = "camera" };
        loop.GetNode("World").AddChild(_probe);
        loop.GetNode<CameraRig>("World/CameraRig").BindFocusConeSink(_conePushes.Add);
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

        var rig = loop.GetNode<CameraRig>("World/CameraRig");
        Expect(ctx, "rig initialised at boot", rig.IsInitialized, true);
        Expect(ctx, "focus cone pushed once at Initialize", string.Join(",", _conePushes), CameraRig.FocusCosMinPm.ToString(System.Globalization.CultureInfo.InvariantCulture));
        loop._Process(0);
        int frames = 0;
        while (!local.IsKnown && frames++ < MaxJoinFrames)
        {
            Tick(loop);
        }

        Tick(loop);
        if (loop.GetNode("World/Entities").GetChildCount() != 1 || loop.GetNode("World/Entities").GetChild(0) is not PlayerView view)
        {
            ctx.Fail("no PlayerView once the local player is known");
            return;
        }

        CheckFollows(ctx, loop, session, rig, view);
        CheckScripted(ctx, loop, deps);
    }

    private static Vector3 FeetM(in CarrierRenderPose p) => new(p.XMm / 1000f, p.YMm / 1000f, p.ZMm / 1000f);

    private static void Present(CameraRig rig, ScriptedClock clock, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            clock.RenderSimTimeMs += FrameMs;
            rig.Present(0);
        }
    }

    private void CheckFollows(SmokeContext ctx, GameLoop loop, BootSession session, CameraRig rig, PlayerView view)
    {
        float maxLag = 0f;
        for (int i = 0; i < WalkTicks; i++)
        {
            session.SetMoveInputForSmoke(0, 32767);
            Tick(loop);
            var flatRig = new Vector3(rig.GlobalPosition.X, 0f, rig.GlobalPosition.Z);
            Vector3 feet = FeetM(view.LastPose);
            maxLag = Mathf.Max(maxLag, flatRig.DistanceTo(new Vector3(feet.X, 0f, feet.Z)));
        }

        Expect(ctx, $"walking: the pivot trails the rendered feet (max lag {maxLag:0.000} m)", maxLag > 0.005f, true);
        session.SetMoveInputForSmoke(0, 0);
        for (int i = 0; i < SettleTicks; i++)
        {
            Tick(loop);
        }

        Vector3 want = FeetM(view.LastPose) + new Vector3(0f, 1.6f, 0f);
        Expect(ctx, $"standing: the pivot settles on feet + 1.6 m ({rig.GlobalPosition} vs {want})", rig.GlobalPosition.DistanceTo(want) < 0.01f, true);
        Expect(ctx, "the rig took over and its camera is current", rig.HasTakenOver && rig.Camera is { Current: true }, true);
    }

    private void CheckScripted(SmokeContext ctx, GameLoop loop, PresenterDeps deps)
    {
        IWorldQuery world = deps.World;
        StaticSolid? house = null;
        foreach (StaticSolid s in world.StaticSolids)
        {
            if (s.Kind == SolidKind.House)
            {
                house = s;
                break;
            }
        }

        if (house is not { } h)
        {
            ctx.Fail("no house solid on the street");
            return;
        }

        // Feet 1 m outside the house's +Z face, looking +Z (yaw 32768): the boom points back into the house.
        int x = (h.Box.Min.X + h.Box.Max.X) / 2;
        int z = h.Box.Max.Z + 1000;
        var carrier = new ScriptedCarrier { LookYaw = 32768 };
        carrier.Place(x, (int)world.TerrainHeightMm(x, z), z);
        var clock = new ScriptedClock { RenderSimTimeMs = 1000 };
        var published = new PublishedStates();
        published.Add<ICarrierView>(carrier);
        var rig = new CameraRig { Name = "ScriptedRig" };
        var boom = new Node3D { Name = "Boom" };
        var camera = new Camera3D { Name = "Camera3D" };
        rig.AddChild(boom);
        boom.AddChild(camera);
        loop.GetNode("World").AddChild(rig);
        try
        {
            Transform3D before = camera.GlobalTransform;
            rig.Present(0);
            Expect(ctx, "inert before Initialize", !rig.HasTakenOver && camera.GlobalTransform.IsEqualApprox(before), true);
            rig.Initialize(new PresenterDeps
            {
                World = world,
                Clock = clock,
                Events = deps.Events,
                Published = published,
                Audio = deps.Audio,
                Views = deps.Views,
                Entities = deps.Entities,
            });
            Present(rig, clock, 1);
            carrier.LookYaw = 30000;
            Present(rig, clock, 1);
            Expect(ctx, "look yaw applied unsmoothed on the frame it changes", Mathf.IsEqualApprox(boom.Rotation.Y, YawRadians.ToRotationY(30000)), true);
            carrier.LookYaw = 32768;
            Present(rig, clock, 10);
            float wallZ = h.Box.Max.Z / 1000f;
            Expect(ctx, $"boom pulled in against the house (boom {rig.BoomMm} mm < 2000)", rig.BoomMm < 2000, true);
            Expect(ctx, $"camera stays outside the house box (z {camera.GlobalPosition.Z:0.000} > {wallZ:0.000})", camera.GlobalPosition.Z > wallZ, true);
            float defaultRatio = camera.Position.X / camera.Position.Z;
            carrier.SetAiming(true);
            Present(rig, clock, 5);
            float aimRatio = camera.Position.X / camera.Position.Z;
            Expect(ctx, $"default shoulder/boom ratio ≈ 0.2 ({defaultRatio:0.000})", Mathf.Abs(defaultRatio - 0.2f) < 0.02f, true);
            Expect(ctx, $"aim shoulder/boom ratio ≈ 0.3125 ({aimRatio:0.000})", Mathf.Abs(aimRatio - 0.3125f) < 0.02f, true);
        }
        finally
        {
            rig.QueueFree();
        }
    }

    private void Tick(GameLoop loop)
    {
        _clock.Advance(FakeClock.TickCounts);
        loop._Process(0);
    }

    private sealed class ScriptedClock : IRenderClock
    {
        public uint HostTick => 0;

        public int AlphaPermille => 0;

        public long RenderSimTimeMs { get; set; }

        public bool IsValid => true;
    }

    private sealed class ScriptedCarrier : ICarrierView
    {
        private readonly CarrierStepResult _step;
        private readonly CarrierInput _input;
        private CarrierState _state;
        private CarrierStatus _status;

        public bool HasCarrier => true;

        public uint Tick => 1;

        public ref readonly CarrierState Current => ref _state;

        public ref readonly CarrierState Previous => ref _state;

        public ushort LookYaw { get; set; }

        public short LookPitch => 0;

        public void SetAiming(bool aiming) => _status = new CarrierStatus { Aiming = aiming };

        public ref readonly CarrierStatus Status => ref _status;

        public ref readonly CarrierStepResult LastStep => ref _step;

        public ref readonly CarrierInput LastInput => ref _input;

        public void Place(int x, int y, int z)
        {
            _state.X = x;
            _state.Y = y;
            _state.Z = z;
        }
    }
}
