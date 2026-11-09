using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Integration.PlayerCharacter;

/// <summary>
/// Player Character story 020 (ADR-0016 sections 1, 2 and 6, ADR-0013 section 1): the owner-role <see cref="CarrierStepBinder"/> in an
/// in-memory ClientLayer harness, driven by a plain <see cref="ClientLayerSut.Step"/> loop (no frame runner, ADR-0002 section 5) with the
/// input pushed through the real <see cref="InputLatch"/> and the ports attached once. One test per acceptance criterion; the frame to
/// input translation is one table. The OQ23 measurement and the Replay ACs are deferred to hardening (M-1a simplify table, Mail Lead plan
/// 2026-11-08); the 0 B and p99 gates are advisory until M0 (erratum E2) and have no test here.
/// </summary>
public class CarrierStepClientLayerTests
{
    private const short Full = 32767;

    // ------------------------------------------------------------ AC Binding

    [Fact]
    public void Binding_OneStep_RunsMirrorDrainThenLatchThenCarrierThenFocus_AndOnlyTheCarrierMoves()
    {
        // Arrange: probes at MirrorDrain and at 4a Focus read the published view.
        var rig = new Rig();
        var seen = new List<string>();
        rig.Probes = (seen, rig.Carrier.View);
        rig.Build();
        rig.Walk(0, Full, 0);

        // Act
        rig.Layer.Step(true);
        rig.Layer.Step(false);

        // Assert: at MirrorDrain the view still holds the previous tick; at Focus it holds this tick.
        Assert.Equal(new[] { "drain:none", "focus:0", "drain:0", "focus:1" }, seen);
        Assert.Equal(2, rig.Sink.Attempts.Count);
    }

    // ------------------------------------------------------------ AC Frame -> input translation

    public enum MapCase
    {
        MoveAndLookPassThrough,
        SprintHeldBit,
        AimHeldBit,
        JumpPressEdge,
        JumpReleaseEdgeOnly,
        PanelOpenFlag,
        NegativeAddPmIsZero,
        AddPmAboveCapIsCapped,
        NegativeExtraJumpsIsZero,
    }

    [Theory]
    [InlineData(MapCase.MoveAndLookPassThrough)]
    [InlineData(MapCase.SprintHeldBit)]
    [InlineData(MapCase.AimHeldBit)]
    [InlineData(MapCase.JumpPressEdge)]
    [InlineData(MapCase.JumpReleaseEdgeOnly)]
    [InlineData(MapCase.PanelOpenFlag)]
    [InlineData(MapCase.NegativeAddPmIsZero)]
    [InlineData(MapCase.AddPmAboveCapIsCapped)]
    [InlineData(MapCase.NegativeExtraJumpsIsZero)]
    public void Mapper_TranslatesTheFrame(MapCase c)
    {
        // Arrange
        var frame = new InputFrame { MoveX = -12_000, MoveY = 20_000, LookYaw = 40_000, Flags = InputFrameFlags.FirstInBatch };
        int addPm = 100;
        int extraJumps = 1;
        switch (c)
        {
            case MapCase.SprintHeldBit:
                frame.HeldBits = FrameActions.Bit(FrameAction.Sprint);
                break;
            case MapCase.AimHeldBit:
                frame.HeldBits = FrameActions.Bit(FrameAction.Aim);
                break;
            case MapCase.JumpPressEdge:
                AddEdge(ref frame, new InputEdge(FrameAction.Interact, EdgeKind.Press, 1, 0));
                AddEdge(ref frame, new InputEdge(FrameAction.Jump, EdgeKind.Press, 2, 0));
                break;
            case MapCase.JumpReleaseEdgeOnly:
                AddEdge(ref frame, new InputEdge(FrameAction.Jump, EdgeKind.Release, 1, 0));
                break;
            case MapCase.PanelOpenFlag:
                frame.Flags |= InputFrameFlags.PanelOpen;
                break;
            case MapCase.NegativeAddPmIsZero:
                addPm = -50;
                break;
            case MapCase.AddPmAboveCapIsCapped:
                addPm = 401;
                break;
            case MapCase.NegativeExtraJumpsIsZero:
                extraJumps = -1;
                break;
        }

        var status = new CarrierStatus { Body = BodyContext.OnFoot, PanelOpen = (frame.Flags & InputFrameFlags.PanelOpen) != 0 };

        // Act
        CarrierInput input = CarrierInputMapper.Map(in frame, in status, speedPermille: 800, addPm, extraJumps, buffCapPm: 400);

        // Assert
        Assert.Equal(-12_000, input.MoveX);
        Assert.Equal(20_000, input.MoveY);
        Assert.Equal(40_000, input.LookYaw);
        Assert.Equal(800, input.SpeedPermille);
        Assert.False(input.HostAuthored);
        Assert.Equal(c == MapCase.SprintHeldBit, input.SprintHeld);
        Assert.Equal(c == MapCase.AimHeldBit, input.Aiming);
        Assert.Equal(c == MapCase.JumpPressEdge, input.JumpPressed);
        Assert.Equal(c == MapCase.PanelOpenFlag ? CarrierContextFlags.Panel : CarrierContextFlags.None, input.ContextFlags);
        Assert.Equal(c switch { MapCase.NegativeAddPmIsZero => 0, MapCase.AddPmAboveCapIsCapped => 400, _ => 100 }, input.BuffAddPm);
        Assert.Equal(c == MapCase.NegativeExtraJumpsIsZero ? 0 : 1, input.ExtraJumps);
    }

    [Fact]
    public void CatchUpBatchOfThree_OneJumpPress_JumpsOnceOnTickOne_LaterTicksKeepHeldStateAndLatestMove()
    {
        // Arrange: a grounded carrier one tick in.
        var rig = new Rig().Build();
        rig.Layer.Step(true);
        rig.Writer.SetState(new InputState { MoveY = Full, LookYaw = 0, HeldBits = FrameActions.Bit(FrameAction.Sprint) });
        rig.Writer.PushEdge(new InputEdge(FrameAction.Jump, EdgeKind.Press, 1, 0));
        var inputs = new List<CarrierInput>();

        // Act: one frame, three catch-up ticks.
        for (int i = 0; i < 3; i++)
        {
            rig.Layer.Step(i == 0);
            inputs.Add(rig.Carrier.View.LastInput);
            if (i == 0)
            {
                Assert.Equal(LocoState.Airborne, rig.Carrier.View.Current.Loco);
            }
        }

        // Assert
        Assert.Equal(new[] { true, false, false }, inputs.Select(x => x.JumpPressed));
        Assert.All(inputs, x => Assert.True(x.SprintHeld));
        Assert.All(inputs, x => Assert.Equal(Full, x.MoveY));
        Assert.True(rig.Carrier.View.Current.Vy < rig.Carrier.View.Previous.Vy, "the later ticks fall; no second jump impulse");
    }

    // ------------------------------------------------------------ AC PCM-FEEL-1 (end to end)

    [Fact]
    [Trait("Id", "PCM-FEEL-1")]
    public void Feel_WalkLatchedBeforeTheTick_Moves41Mm_AndA180Turn_CompletesBodyYawIn8Ticks()
    {
        // Arrange: slot 0 spawns facing yaw 0; forward at camera yaw 0 is -Z.
        var rig = new Rig().Build();
        rig.Layer.Step(true);
        int z0 = rig.Carrier.View.Current.Z;

        // Act: a walk latched before the tick.
        rig.Walk(0, Full, 0);
        rig.Layer.Step(true);

        // Assert: 41 +/- 1 mm by the end of that tick's CarrierStep.
        Assert.InRange(z0 - rig.Carrier.View.Current.Z, 40, 42);

        // Act: the stick reverses (a 180 degree camera-relative turn).
        var yaws = new List<ushort>();
        rig.Walk(0, -Full, 0);
        for (int i = 0; i < 8; i++)
        {
            rig.Layer.Step(i == 0);
            yaws.Add(rig.Carrier.View.Current.BodyYaw);
        }

        // Assert: not done after 7 ticks, done on the 8th.
        Assert.NotEqual(32768, yaws[6]);
        Assert.Equal(32768, yaws[7]);
    }

    [Fact]
    [Trait("Id", "PCM-P3-3")]
    public void CameraYaw90_ForwardStick_HeadsAlongCameraForward_EndToEnd()
    {
        // Arrange
        var rig = new Rig().Build();

        // Act: forward at camera yaw 90 degrees (16384 BAM) for 8 ticks.
        rig.Walk(0, Full, 16_384);
        for (int i = 0; i < 8; i++)
        {
            rig.Layer.Step(true);
        }

        // Assert: forward(90 deg) is -X at the walk cap, and the body faces the camera's heading.
        ref readonly CarrierState s = ref rig.Carrier.View.Current;
        Assert.Equal(-5000, s.Vx);
        Assert.InRange(Math.Abs(s.Vz), 0, 87);
        Assert.InRange(Math.Abs(unchecked((short)(s.BodyYaw - 16_384))), 0, 25);
    }

    // ------------------------------------------------------------ AC Inputs from other systems

    [Fact]
    public void PanelOpenFrame_SetsStatusPanelAndContextPanel_TheNextFrameClearsBoth_LoadAndStatsFeedTheInput()
    {
        // Arrange: a half-speed load source.
        var rig = new Rig { Load = new ConstantCarrierLoadSource(500, 12) }.Build();

        // Act
        rig.Writer.SetState(new InputState { MoveY = Full, Flags = InputFrameFlags.PanelOpen });
        rig.Layer.Step(true);
        CarrierStatus open = rig.Carrier.View.Status;
        CarrierInput openInput = rig.Carrier.View.LastInput;
        rig.Writer.SetState(new InputState { MoveY = Full });
        rig.Layer.Step(true);

        // Assert
        Assert.True(open.PanelOpen);
        Assert.Equal(CarrierContextFlags.Panel, openInput.ContextFlags);
        Assert.False(rig.Carrier.View.Status.PanelOpen);
        Assert.Equal(CarrierContextFlags.None, rig.Carrier.View.LastInput.ContextFlags);
        Assert.Equal(500, rig.Carrier.View.LastInput.SpeedPermille);
        Assert.Equal(0, rig.Carrier.View.LastInput.BuffAddPm);   // the static M-1a sheet: MoveSpeedAddPm 0
        Assert.Equal(0, rig.Carrier.View.LastInput.ExtraJumps);
    }

    // ------------------------------------------------------------ AC Seeding

    [Fact]
    public void Seeding_WaitsForTheLocalPlayer_ThenTheFirstSampleIsTheHostsSeededPosition()
    {
        // Arrange: the host side joins slot 0 on the same world.
        WorldTables world = WorldTablesFixture.Pad();
        PlayersRig host = PlayersRig.Create(world);
        host.JoinAndTick(0);
        var rig = new Rig(world) { Known = false }.Build();

        // Act: two ticks before the join reply, then one after.
        rig.Layer.Step(true);
        rig.Layer.Step(true);
        int before = rig.Sink.Attempts.Count;
        rig.Local.IsKnown = true;
        rig.Layer.Step(true);

        // Assert
        Assert.Equal(0, before);
        TransformSample first = Assert.Single(rig.Sink.Attempts);
        HostPosition seeded = host.HostPosition(0);
        Assert.Equal((seeded.Feet.X, seeded.Feet.Y, seeded.Feet.Z, seeded.Yaw), (first.PosXMm, first.PosYMm, first.PosZMm, first.YawBam));
        Assert.Equal(0, first.Slot);
    }

    // ------------------------------------------------------------ AC Transform sample emission

    [Fact]
    public void Samples_OnePerTickInOrder_SeqIncreases_ARefusedWriteIsCountedAndNotRetried()
    {
        // Arrange: the sink refuses its third write; the carrier sprints.
        var rig = new Rig().Build();
        rig.Sink.RefuseAt = 3;
        rig.Writer.SetState(new InputState { MoveY = Full, HeldBits = FrameActions.Bit(FrameAction.Sprint) });

        // Act
        for (int i = 0; i < 5; i++)
        {
            rig.Layer.Step(true);
        }

        // Assert
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5 }, rig.Sink.Attempts.Select(s => s.Seq));
        Assert.Equal(1, rig.Carrier.SamplesRefused);
        Assert.Equal(4, rig.Carrier.SamplesWritten);
        TransformSample last = rig.Sink.Attempts[^1];
        ref readonly CarrierState s = ref rig.Carrier.View.Current;
        Assert.Equal((s.X, s.Y, s.Z, s.BodyYaw), (last.PosXMm, last.PosYMm, last.PosZMm, last.YawBam));
        Assert.Equal((byte)SampleFlags.Sprinting, last.Flags);
    }

    // ------------------------------------------------------------ AC Published state (+ counter flush)

    [Fact]
    public void View_PublishesCurrentAndPreviousState_Aim_Status_SweptResult_AndLastInput()
    {
        // Arrange
        var rig = new Rig().Build();
        rig.Writer.SetState(new InputState { MoveY = Full, LookYaw = 0, LookPitch = -1200 });

        // Act
        rig.Layer.Step(true);
        CarrierState afterFirst = rig.Carrier.View.Current;
        rig.Layer.Step(true);

        // Assert
        ICarrierView view = rig.Carrier.View;
        Assert.True(view.HasCarrier);
        Assert.Equal(1u, view.Tick);
        Assert.Equal(afterFirst.Z, view.Previous.Z);
        Assert.Equal(view.Current.Z - view.Previous.Z, view.LastStep.DzMm);
        Assert.True(view.LastStep.DzMm < 0);
        Assert.Equal((ushort)0, view.LookYaw);
        Assert.Equal(-1200, view.LookPitch);
        Assert.Equal(AnchorRef.None, view.Status.OccupiedSeat);
        Assert.Equal(BodyContext.OnFoot, view.Status.Body);
        Assert.Equal(Full, view.LastInput.MoveY);
        Assert.True(rig.Layer.Counters[CounterId.CarrierSubSteps] >= view.LastStep.SubSteps && view.LastStep.SubSteps > 0, "solver counters flushed to the layer's counters");
    }

    private static void AddEdge(ref InputFrame frame, InputEdge e)
    {
        frame.EdgeStorage[frame.EdgeCount] = e;
        frame.EdgeCount++;
    }

    /// <summary>The harness: a ClientLayer over FX-Pad with the carrier bound and test-double ports.</summary>
    private sealed class Rig
    {
        private readonly WorldTables _world;

        public Rig(WorldTables? world = null)
        {
            _world = world ?? WorldTablesFixture.Pad();
            ContentSet content = BuiltInContent.CreateM1a();
            Carrier = new CarrierStepBinder(
                _world,
                PlayerSettings.FromBalance(content.Player),
                StatSheet.Build(content, new StatSources([])),
                new EmptyRemoteCapsuleSource(),
                new EmptySupportTransformSource());
        }

        public CarrierStepBinder Carrier { get; }

        public RecordingSink Sink { get; } = new();

        public LocalPlayer Local { get; } = new();

        public ICarrierLoadSource Load { get; init; } = ConstantCarrierLoadSource.Unloaded();

        public bool Known { get; init; } = true;

        public (List<string> Log, ICarrierView View)? Probes { get; set; }

        public ClientLayerSut Layer { get; private set; } = null!;

        public IInputLatchWriter Writer => Layer.InputWriter;

        public Rig Build()
        {
            var clock = new Clock();
            var builder = new ClientLayerBuilder(clock);
            if (Probes is { } p)
            {
                builder.Bind(ClientStepId.MirrorDrain, new Probe("drain", p.Log, p.View));
                builder.BindPreview(PreviewSubStep.Focus, new Probe("focus", p.Log, p.View));
            }

            Carrier.BindTo(builder);
            Layer = builder.Build();
            Local.IsKnown = Known;
            Layer.Attach(new ClientPorts { Requests = new NoRequests(), HostClock = clock, TransformSamples = Sink, CarrierLoad = Load, LocalPlayer = Local });
            return this;
        }

        public void Walk(short x, short y, ushort lookYaw) => Writer.SetState(new InputState { MoveX = x, MoveY = y, LookYaw = lookYaw });
    }

    private sealed class Probe(string name, List<string> log, ICarrierView view) : IClientStep
    {
        public void Step(in ClientStepContext ctx) => log.Add(view.HasCarrier ? $"{name}:{view.Tick}" : $"{name}:none");
    }

    private sealed class RecordingSink : ITransformSampleSink
    {
        public List<TransformSample> Attempts { get; } = new();

        public int RefuseAt { get; set; } = -1;

        public bool TryWrite(in TransformSample s)
        {
            Attempts.Add(s);
            return Attempts.Count != RefuseAt;
        }
    }

    private sealed class LocalPlayer : ILocalPlayer
    {
        public PlayerSlot Slot => PlayerSlot.FromValue(0);

        public EntityId Id => EntityId.Compose(EntityClass.Player, 1);

        public bool IsKnown { get; set; }
    }

    private sealed class NoRequests : IRequestSink
    {
        public bool TrySend(in Op op) => false;
    }

    private sealed class Clock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => true;
    }
}
