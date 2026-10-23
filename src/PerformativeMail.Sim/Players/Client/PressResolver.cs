using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>What one Interact press resolves to (<see cref="PressResolver.Resolve"/>). Explicit, append-only values.</summary>
/// <example><code>bool fires = r.Outcome is PressOutcome.Catch or PressOutcome.Verb or PressOutcome.CatchArmed;</code></example>
public enum PressOutcome : byte
{
    /// <summary>The edge is refused entirely: Dead, Panel, or Mounted without an own-seat Dismount (D6 — Mail Lead default, pending Evan (TD-171)).</summary>
    Refused = 0,

    /// <summary>A hold or channel is already in progress: no second latch (H-R4-6; counted <see cref="CounterId.InteractPressIgnored"/>).</summary>
    Ignored = 1,

    /// <summary>Rule 1: a predicted catch passes the D3 gate; a <c>Channel</c> start with <c>CatchArm{down}</c>.</summary>
    Catch = 2,

    /// <summary>Rule 2: the presented target's verb (<see cref="PressResolution.Index"/>); Tap sends, Hold latches, Channel starts.</summary>
    Verb = 3,

    /// <summary>Rule 2 refused by availability: nothing fires and nothing falls through (ADR-0014 l.81; <see cref="PressResolution.Reason"/>).</summary>
    Unavailable = 4,

    /// <summary>Rule 3, no valid target: catch-armed while held (<c>CatchArm{down}</c>).</summary>
    CatchArmed = 5,
}

/// <summary>The result of <see cref="PressResolver.Resolve"/>: the outcome, the anchor index for <see cref="PressOutcome.Verb"/> and the unavailable reason.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Index">The registry index of the presented anchor for <see cref="PressOutcome.Verb"/>, else -1.</param>
/// <param name="Reason">The gate's or owner's reason key for <see cref="PressOutcome.Unavailable"/>, else <see cref="StringId.None"/>.</param>
/// <example><code>PressResolution r = resolver.Resolve(state, in status, in query, in press, ref sink);</code></example>
public readonly record struct PressResolution(PressOutcome Outcome, int Index, StringId Reason);

/// <summary>
/// Interact press resolution (ADR-0014 section 3; input-controls R4.3, G1, H-R4-2/4/6; GDD P11, P12; TR-movement-012). Pure: it
/// reads the registry, the gate and the predictor and calls only the owner's pure <c>IsAvailable</c>; it writes nothing. Order:
/// <list type="number">
/// <item>Dead or Panel refuses the edge; Mounted refuses it unless the presented target is the own-seat Dismount (the gate's
/// <see cref="GateKind.NoCandidate"/> is a refusal there, not "no valid target"), and then only rule 2 applies (D6 — Mail Lead
/// default, pending Evan (TD-171); ADR-0015 section 7 "Alive, on foot").</item>
/// <item>A hold or channel in progress ignores the press.</item>
/// <item>Catch iff the predictor predicts and the predicted static receiver is not the presented target (the D3 gate).</item>
/// <item>The presented target (the edge's snapshot, never the focus) if registered in this context, within its <c>RangeMm</c>
/// from the chest (<see cref="FocusScore.Measure"/>, <see cref="FocusScore.InRange"/> with <c>incumbent: false</c>) and not a
/// <see cref="GateKind.NoCandidate"/>: a gate <see cref="GateKind.Unavailable"/> or an owner <c>IsAvailable == false</c> is
/// <see cref="PressOutcome.Unavailable"/>, else <see cref="PressOutcome.Verb"/>.</item>
/// <item>Otherwise catch-armed (ADR-0014 l.78–80: unregistered, out of start range or no target is "no valid target").</item>
/// </list>
/// Allocation free; not thread-safe only in that the ports it reads are not.
/// </summary>
/// <example>
/// <code>
/// var resolver = new PressResolver(anchors, gate, predictor, settings);
/// PressResolution r = resolver.Resolve(InteractState.Idle, in status, in query, in press, ref sink);
/// </code>
/// </example>
public sealed class PressResolver
{
    private readonly AnchorRegistry _anchors;
    private readonly InteractionGate _gate;
    private readonly ICatchPredictor _predictor;
    private readonly int _nearAxisMm;

    /// <summary>Creates the resolver.</summary>
    /// <param name="anchors">The anchor registry (read only).</param>
    /// <param name="gate">The framework context gate.</param>
    /// <param name="predictor">The catch predictor (<see cref="NullCatchPredictor"/> until Throwing's exists).</param>
    /// <param name="settings">The players settings (<c>NearAxisMm</c> for the distance metric).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public PressResolver(AnchorRegistry anchors, InteractionGate gate, ICatchPredictor predictor, PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(predictor);
        ArgumentNullException.ThrowIfNull(settings);
        _anchors = anchors;
        _gate = gate;
        _predictor = predictor;
        _nearAxisMm = settings.NearAxisMm;
    }

    /// <summary>
    /// The hook context for the anchor at <paramref name="index"/>: <paramref name="press"/>'s tick, actor, chest and hold settings
    /// plus the anchor's fields.
    /// </summary>
    /// <param name="press">The press-wide fields.</param>
    /// <param name="def">The anchor's definition.</param>
    /// <param name="contextMask">The anchor's context mask.</param>
    /// <returns>The context.</returns>
    /// <example><code>InteractionContext c = PressResolver.ForAnchor(in press, in anchors.DefAt(i), anchors.ContextMaskAt(i));</code></example>
    public static InteractionContext ForAnchor(in InteractionContext press, in AnchorDef def, byte contextMask) => press with
    {
        Anchor = def.Ref,
        Verb = def.Verb,
        Mode = def.Mode,
        HoldBaseMs = def.HoldBaseMs,
        RangeMm = def.RangeMm,
        ContextMask = contextMask,
    };

    /// <summary>Resolves one press.</summary>
    /// <param name="state">The interaction state before the press.</param>
    /// <param name="status">The carrier's status.</param>
    /// <param name="query">The catch query: also the chest and the edge's presented target (<see cref="AnchorRef.None"/> for <c>TargetEntity == 0</c>).</param>
    /// <param name="press">The press-wide hook context fields (tick, actor, chest, hold settings).</param>
    /// <param name="sink">Arithmetic counters for the distance.</param>
    /// <returns>The resolution.</returns>
    public PressResolution Resolve(InteractState state, in CarrierStatus status, in CatchQuery query, in InteractionContext press, ref SimCounterSink sink)
    {
        if (status.Body == BodyContext.Dead || status.PanelOpen)
        {
            return new PressResolution(PressOutcome.Refused, -1, StringId.None);
        }

        if (state is InteractState.Holding or InteractState.Channel or InteractState.ToggleArmed)
        {
            return new PressResolution(PressOutcome.Ignored, -1, StringId.None);
        }

        AnchorRef presented = query.Presented;
        int index = ValidTarget(presented, in status, query.Chest, ref sink, out GateResult gate);
        if (status.Body == BodyContext.Mounted)
        {
            return index < 0 ? new PressResolution(PressOutcome.Refused, -1, StringId.None) : TargetVerb(index, gate, in press);
        }

        CatchPrediction p = _predictor.Evaluate(in query);
        if (p.Predicted != 0 && (presented.IsNone || p.PredictedStaticReceiver != presented))
        {
            return new PressResolution(PressOutcome.Catch, -1, StringId.None);
        }

        return index < 0 ? new PressResolution(PressOutcome.CatchArmed, -1, StringId.None) : TargetVerb(index, gate, in press);
    }

    /// <summary>The presented anchor's index when registered in this context, within its start range and a gate candidate; else -1.</summary>
    private int ValidTarget(AnchorRef presented, in CarrierStatus status, in Vec3Mm chest, ref SimCounterSink sink, out GateResult gate)
    {
        gate = default;
        byte mask = status.Body == BodyContext.Build ? AnchorRegistry.ContextBuild : AnchorRegistry.ContextWorld;
        if (presented.IsNone || !_anchors.TryGet(presented, mask, out int index))
        {
            return -1;
        }

        ref readonly AnchorDef def = ref _anchors.DefAt(index);

        // The focus distance metric; only D is read (the cone is not a start condition).
        long d = FocusScore.Measure(in chest, in def.Point, 0, 0, _nearAxisMm, ref sink).D;
        if (!FocusScore.InRange(d, def.RangeMm, incumbent: false, 0))
        {
            return -1;
        }

        gate = _gate.Evaluate(in def, in status);
        return gate.Kind == GateKind.NoCandidate ? -1 : index;
    }

    private PressResolution TargetVerb(int index, GateResult gate, in InteractionContext press)
    {
        if (gate.Kind == GateKind.Unavailable)
        {
            return new PressResolution(PressOutcome.Unavailable, -1, gate.Reason);
        }

        InteractionContext c = ForAnchor(in press, in _anchors.DefAt(index), _anchors.ContextMaskAt(index));
        return _anchors.ImplAt(index).IsAvailable(in c, out StringId reason)
            ? new PressResolution(PressOutcome.Verb, index, StringId.None)
            : new PressResolution(PressOutcome.Unavailable, -1, reason);
    }
}
