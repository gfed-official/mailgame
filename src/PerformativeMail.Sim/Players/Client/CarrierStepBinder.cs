using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The owner-role carrier step (ADR-0013 section 1, ADR-0016 section 2; player-character story 020): Players' <see cref="IClientStep"/>
/// at <see cref="ClientStepId.CarrierStep"/>, after <c>InputLatch</c> and before <c>Previews</c>. Once per client tick it
/// <list type="number">
/// <item><description>waits until the ports are attached and <see cref="ILocalPlayer.IsKnown"/>; on the first such tick it seeds the owned
/// carrier at <see cref="CarrierSpawn.At"/> (the host join's spawn), then steps it on that same tick;</description></item>
/// <item><description>builds the tick's <see cref="CarrierStatus"/> (Panel from <see cref="InputFrameFlags.PanelOpen"/>; the client
/// assumes Alive at M-1a) and translates the frame with <see cref="CarrierInputMapper"/> (load from <see cref="ICarrierLoadSource"/>,
/// <c>MoveSpeedAddPm</c> / <c>ExtraJumps</c> from the client stat sheet);</description></item>
/// <item><description>calls <see cref="OwnedCarrier.Step"/> (the only movement code), flushes the solver counters into the layer's
/// counters (<see cref="CarrierCounterFlush"/>), publishes <see cref="View"/>, and writes one <see cref="TransformSample"/> with a
/// per-sender increasing <c>Seq</c> through <see cref="ITransformSampleSink"/> every tick (M-1a solo; the 15 Hz / 4 Hz cadence is
/// ADR-0020's). A refused write is counted in <see cref="SamplesRefused"/> and never retried within the tick.</description></item>
/// </list>
/// Focus, press and hold are not run here (they are <c>Previews</c>). Reads only its constructor ports, the step context and the
/// attached <see cref="ClientPorts"/>; never <c>SimWorld</c>, a clock or <c>delta</c>. Allocation-free per step. Not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var carrier = new CarrierStepBinder(clientWorld, settings, clientStats, new EmptyRemoteCapsuleSource(), new EmptySupportTransformSource());
/// carrier.BindTo(builder);
/// ClientLayer layer = builder.Build();
/// layer.Attach(in ports);
/// layer.Step(isFirstTickInBatch: true);
/// ref readonly CarrierState now = ref carrier.View.Current;
/// </code>
/// </example>
public sealed class CarrierStepBinder : IClientStep
{
    private readonly IWorldQuery _world;
    private readonly PlayerSettings _settings;
    private readonly IStatSheet _stats;
    private readonly IRemoteCapsuleSource _remotes;
    private readonly ISupportTransformSource _supports;
    private readonly CarrierView _view = new();
    private readonly PlayerRecord _record = new() { Life = LifeState.Alive };
    private CarrierScratch _scratch;
    private CarrierState _state;
    private PlayerSlot _slot;
    private EntityId _self;
    private bool _seeded;
    private uint _sampleSeq;
    private ushort _statusSeq;

    /// <summary>Creates the step.</summary>
    /// <param name="world">The client's world tables (terrain, solids, bounds, Post Office pad).</param>
    /// <param name="settings">The <c>player.*</c> settings.</param>
    /// <param name="stats">The client stat sheet copy (static at M-1a).</param>
    /// <param name="remotes">Other players' capsules (the empty source in solo).</param>
    /// <param name="supports">Vehicles and belts (the empty source at M-1a).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public CarrierStepBinder(IWorldQuery world, PlayerSettings settings, IStatSheet stats, IRemoteCapsuleSource remotes, ISupportTransformSource supports)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(supports);
        _world = world;
        _settings = settings;
        _stats = stats;
        _remotes = remotes;
        _supports = supports;
        _scratch = new CarrierScratch(settings.MaxCarrierCandidates);
    }

    /// <summary>The published carrier (also the <see cref="ICarrierFocusSource"/> for <see cref="FocusStep"/>).</summary>
    public CarrierView View => _view;

    /// <summary>Samples the sink accepted.</summary>
    public long SamplesWritten { get; private set; }

    /// <summary>Samples the sink refused (counted, never retried within the tick).</summary>
    public long SamplesRefused { get; private set; }

    /// <summary>Binds this step at <see cref="ClientStepId.CarrierStep"/>.</summary>
    /// <param name="builder">The client layer builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public void BindTo(ClientLayerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Bind(ClientStepId.CarrierStep, this);
    }

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        if (!ctx.IsAttached)
        {
            return;
        }

        ref readonly ClientPorts ports = ref ctx.Ports;
        if (!_seeded)
        {
            ILocalPlayer local = ports.LocalPlayer;
            if (!local.IsKnown)
            {
                return;
            }

            _slot = local.Slot;
            _self = local.Id;
            _state = CarrierSpawn.At(_slot, _world);
            _seeded = true;
        }

        ref readonly InputFrame frame = ref ctx.Input;
        bool panelOpen = (frame.Flags & InputFrameFlags.PanelOpen) != 0;
        bool aiming = (frame.HeldBits & FrameActions.Bit(FrameAction.Aim)) != 0;
        CarrierStatus before = CarrierStatus.Build(in _record, in _state, cursorHolding: false, panelOpen, buildMode: false, aiming, AnchorRef.None, _statusSeq);
        int addPm = _stats.Read(Stat.MoveSpeedAddPm, slot: _slot);
        int extraJumps = _stats.Read(Stat.ExtraJumps, slot: _slot);
        CarrierInput input = CarrierInputMapper.Map(in frame, in before, ports.CarrierLoad.SpeedPermille, addPm, extraJumps, _settings.BuffCapPm);

        CarrierState previous = _state;
        var env = new CarrierEnv(_world, _remotes, _supports, _settings, _self);
        _scratch.Clear();
        OwnedCarrier.Step(ref _state, in input, in env, ref _scratch);
        var result = new CarrierStepResult
        {
            DxMm = _state.X - previous.X,
            DyMm = _state.Y - previous.Y,
            DzMm = _state.Z - previous.Z,
            SubSteps = _scratch.SubStepsMaxThisTick,
            SlideIterations = _scratch.SlideIterationsMax,
            StepClamped = _scratch.StepClamped,
            Depenetrations = _scratch.Depenetrations,
        };
        CarrierCounterFlush.Flush(ctx.Counters, ref _scratch);

        _statusSeq++;
        CarrierStatus after = CarrierStatus.Build(in _record, in _state, cursorHolding: false, panelOpen, buildMode: false, aiming, AnchorRef.None, _statusSeq);
        _view.Publish(ctx.ClientTick, in previous, in _state, in input, in after, in result, frame.LookYaw, frame.LookPitch);
        EmitSample(ports.TransformSamples, frame.LookPitch, in after);
    }

    private void EmitSample(ITransformSampleSink sink, short lookPitch, in CarrierStatus status)
    {
        SampleFlags flags = status.Swimming ? SampleFlags.Swimming : SampleFlags.None;
        if ((_state.Flags & CarrierFlags.Sprinting) != 0)
        {
            flags |= SampleFlags.Sprinting;
        }

        var sample = new TransformSample
        {
            Seq = ++_sampleSeq,
            PosXMm = _state.X,
            PosYMm = _state.Y,
            PosZMm = _state.Z,
            YawBam = _state.BodyYaw,
            PitchBam = unchecked((ushort)lookPitch),
            Slot = _slot.Value,
            Flags = (byte)flags,
        };
        if (sink.TryWrite(in sample))
        {
            SamplesWritten++;
        }
        else
        {
            SamplesRefused++;
        }
    }
}
