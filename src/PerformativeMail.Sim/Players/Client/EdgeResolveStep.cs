using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The local carrier's Interact press edges at <see cref="PreviewSubStep.EdgeResolve"/> (4d) (ADR-0014 section 3; ADR-0016 section
/// 2; input-controls R4.3, G1; GDD P11, P12; TR-movement-012). Each tick's press edges are applied in arrival order through
/// <see cref="PressResolver"/>; the target is the edge's presented snapshot (<c>TargetEntity</c>, <c>TargetSlot</c>), never the
/// focus. Per outcome:
/// <list type="bullet">
/// <item>Catch and catch-armed: <see cref="ICatchArmSender.Send"/> <c>(down: true)</c> and a <see cref="InteractState.Channel"/>
/// with no anchor. A swimmer sends it too: the host refuses the arm (Evan's swim-catch ruling, 2026-10-20 22:05 PT, ADR-0014 §3
/// erratum).</item>
/// <item>Tap: a transient latch (<c>PressTick</c>, <c>Claimed</c>, then <see cref="IInteractable.OnPress"/>),
/// <see cref="IInteractable.TryBuildRequest"/>, <see cref="IRequestSink.TrySend"/>; the state stays Idle.</item>
/// <item>Hold: the latch, <see cref="IInteractable.OnPress"/> first, then <see cref="IInteractable.GetHoldTicks"/> once (never
/// recomputed, M-MD9-1); <see cref="InteractState.Holding"/>. Channel verbs latch the same way without a hold length.</item>
/// <item>Unavailable: nothing fires; <see cref="CancelReason.Unavailable"/> with the reason key. A refused send (sink or sender
/// false) records <see cref="CancelReason.Busy"/> and stays Idle.</item>
/// <item>Ignored (a hold or channel in progress): counted <see cref="CounterId.InteractPressIgnored"/> (D5 — Mail Lead default,
/// pending Evan (TD-171)). Refused (Dead, Panel, Mounted without the own seat; D6 — Mail Lead default, pending Evan (TD-171)):
/// nothing.</item>
/// </list>
/// Release and cancel edges, hold accrual and <see cref="FocusState.Latched"/> are story 017's. Reads only its constructor ports
/// and <see cref="ClientStepContext.Input"/>, never <c>SimWorld</c>. Runs only on ticks with edges. Allocation free; not
/// thread-safe.
/// </summary>
/// <example>
/// <code>
/// var press = new EdgeResolveStep(source, players.Anchors, gate, settings, inputSettings, predictor, catchArm, requests, layerCounters, self);
/// builder.BindPreview(PreviewSubStep.EdgeResolve, press);
/// InteractState s = press.Interaction.State;
/// </code>
/// </example>
public sealed class EdgeResolveStep : IClientStep
{
    private readonly ICarrierFocusSource _source;
    private readonly AnchorRegistry _anchors;
    private readonly PlayerSettings _settings;
    private readonly IClientInputSettings _input;
    private readonly ICatchArmSender _catchArm;
    private readonly IRequestSink _requests;
    private readonly SimCounters _counters;
    private readonly PlayerSlot _self;
    private readonly HoldSettings _hold;
    private readonly PressResolver _resolver;
    private InteractionStatus _interaction;

    /// <summary>Creates the step.</summary>
    /// <param name="source">The local carrier's feet and status.</param>
    /// <param name="anchors">The anchor registry (read only).</param>
    /// <param name="gate">The framework context gate.</param>
    /// <param name="settings">The players settings (<c>ChestMm</c>, <c>NearAxisMm</c>, <c>MinHoldTicks</c>).</param>
    /// <param name="input">The client input settings (<c>HoldSettingMs</c>, read at press only).</param>
    /// <param name="predictor">The catch predictor (<see cref="NullCatchPredictor"/> until Throwing's exists).</param>
    /// <param name="catchArm">Throwing's <c>CatchArm</c> sender.</param>
    /// <param name="requests">The ClientLayer's request port.</param>
    /// <param name="counters">The ClientLayer's counters.</param>
    /// <param name="self">The local player's slot, for <see cref="InteractionContext.Actor"/> and <see cref="CatchQuery.Self"/> (D2 — Mail Lead default, pending Evan (TD-171); story 020 wires it).</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    public EdgeResolveStep(
        ICarrierFocusSource source,
        AnchorRegistry anchors,
        InteractionGate gate,
        PlayerSettings settings,
        IClientInputSettings input,
        ICatchPredictor predictor,
        ICatchArmSender catchArm,
        IRequestSink requests,
        SimCounters counters,
        PlayerSlot self)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catchArm);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(counters);
        _resolver = new PressResolver(anchors, gate, predictor, settings);
        _source = source;
        _anchors = anchors;
        _settings = settings;
        _input = input;
        _catchArm = catchArm;
        _requests = requests;
        _counters = counters;
        _self = self;
        _hold = HoldSettings.FromPlayerSettings(settings);
    }

    /// <summary>The interaction after the last <see cref="Step"/>; story 017 advances and ends it through this reference.</summary>
    public ref InteractionStatus Interaction => ref _interaction;

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        ReadOnlySpan<InputEdge> edges = ctx.Input.Edges;
        for (int i = 0; i < edges.Length; i++)
        {
            ref readonly InputEdge e = ref edges[i];
            if (e.Action == FrameAction.Interact && e.Kind == EdgeKind.Press)
            {
                Press(in e, in ctx);
            }
        }
    }

    private void Press(in InputEdge edge, in ClientStepContext ctx)
    {
        ref readonly CarrierStatus status = ref _source.Status;
        Vec3Mm feet = _source.Feet;
        Vec3Mm chest = FocusScore.Chest(in feet, _settings.ChestMm);
        AnchorRef presented = edge.TargetEntity == 0 ? AnchorRef.None : new AnchorRef(new EntityId(edge.TargetEntity), edge.TargetSlot);
        var query = new CatchQuery { Tick = ctx.ClientTick, Self = _self, Chest = chest, Presented = presented };

        // The hooks' Tick follows story 014 (the host-tick estimate, TD-170 item 4); PressTick is the client tick.
        var press = new InteractionContext
        {
            Tick = ctx.HostClock.IsValid ? ctx.HostClock.EstimatedHostTick : 0u,
            Actor = _self,
            ChestMm = chest,
            HoldSettingMs = _input.HoldSettingMs,
            HoldSettings = _hold,
            IsHost = 0,
        };
        SimCounterSink sink = _counters.PrimitiveSink();
        PressResolution r = _resolver.Resolve(_interaction.State, in status, in query, in press, ref sink);
        var latch = new InteractionLatch { PressTick = ctx.ClientTick, Claimed = new ClaimedPosition(feet.X, feet.Y, feet.Z) };
        switch (r.Outcome)
        {
            case PressOutcome.Ignored:
                _counters.Add(CounterId.InteractPressIgnored);
                break;
            case PressOutcome.Unavailable:
                Cancel(CancelReason.Unavailable, r.Reason);
                break;
            case PressOutcome.Catch:
            case PressOutcome.CatchArmed:
                latch.Verb = Verb.Catch;
                latch.Mode = InteractMode.Channel;
                if (_catchArm.Send(down: true))
                {
                    Latch(InteractState.Channel, in latch);
                }
                else
                {
                    Cancel(CancelReason.Busy, StringId.None);
                }

                break;
            case PressOutcome.Verb:
                StartVerb(r.Index, in press, ref latch);
                break;
            default:
                break;
        }
    }

    private void StartVerb(int index, in InteractionContext press, ref InteractionLatch latch)
    {
        ref readonly AnchorDef def = ref _anchors.DefAt(index);
        IInteractable impl = _anchors.ImplAt(index);
        InteractionContext c = PressResolver.ForAnchor(in press, in def, _anchors.ContextMaskAt(index));
        latch.Anchor = def.Ref;
        latch.Verb = def.Verb;
        latch.Mode = def.Mode;
        impl.OnPress(in c, ref latch);
        switch (def.Mode)
        {
            case InteractMode.Tap:
                Op op = default;
                if (impl.TryBuildRequest(in latch, in c, ref op) && !_requests.TrySend(in op))
                {
                    Cancel(CancelReason.Busy, StringId.None);
                }

                break;
            case InteractMode.Hold:
                latch.HoldTicks = impl.GetHoldTicks(in latch, in c);
                Latch(InteractState.Holding, in latch);
                break;
            default:
                Latch(InteractState.Channel, in latch);
                break;
        }
    }

    private void Latch(InteractState state, in InteractionLatch latch)
    {
        _interaction.State = state;
        _interaction.Latch = latch;
    }

    private void Cancel(CancelReason reason, StringId key)
    {
        _interaction.LastCancel = reason;
        _interaction.LastCancelReason = key;
    }
}
