using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// Interaction focus for the local carrier at <see cref="PreviewSubStep.Focus"/> (4a) (ADR-0014 section 2 as placed by l.114 and
/// ADR-0016 section 2; GDD P11, F4; TR-movement-011). Each tick it scores the anchors of the grid cells around the chest
/// (<see cref="FocusScore"/>), keeps the context-filtered eligible ones (<see cref="AnchorRegistry.ContextMaskAt"/>, then
/// <see cref="InteractionGate.Evaluate"/>: <see cref="GateKind.NoCandidate"/> excludes, <see cref="GateKind.Unavailable"/> stays a
/// candidate), sorts the scored ones by score with the <see cref="AnchorRef.CompareTo"/> tie-break (<see cref="StableSort"/>),
/// tests line of sight lazily in that order until one passes (<see cref="FocusOcclusion"/>, story 015) and applies the
/// <c>focusDwellTicks</c> dwell to that winner. The owner's <c>IsAvailable</c> is asked for the focused anchor only.
/// <para>
/// Occlusion (ADR-0014 section 2): an unlatched incumbent that is not the first passing candidate is tested too (at most one extra
/// test), so an occluded incumbent loses focus on that tick (TD-175 item 1, approved by Evan 2026-10-28 7:26 AM PT); a latched
/// incumbent is never tested. <see cref="FocusCounters.OcclusionTests"/> is therefore at most <see cref="FocusCounters.Candidates"/>.
/// </para>
/// <para>
/// Reads only its constructor ports and <see cref="ClientStepContext.Input"/> (<c>LookYaw</c>, <c>FocusCosMinPm</c>), never
/// <c>SimWorld</c>. Allocation free; not thread-safe.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var focus = new FocusStep(carrierSource, world, players.Anchors, gate, settings, layerCounters);
/// builder.BindPreview(PreviewSubStep.Focus, focus);
/// AnchorRef target = focus.State.Focused;
/// </code>
/// </example>
public sealed class FocusStep : IClientStep
{
    /// <summary>The candidate span length; more anchors in the queried cells count <see cref="CounterId.AnchorQueryOverflow"/>.</summary>
    public const int CandidateCapacity = 128;

    private readonly ICarrierFocusSource _source;
    private readonly IWorldQuery _world;
    private readonly AnchorRegistry _anchors;
    private readonly InteractionGate _gate;
    private readonly PlayerSettings _settings;
    private readonly SimCounters _counters;
    private readonly int _solidLimit;
    private FocusState _state;
    private FocusCounters _probes;

    /// <summary>Creates the step. The world port is the second parameter (Evan approved 2026-10-28 (TD-182 item 3)).</summary>
    /// <param name="source">The local carrier's feet and status.</param>
    /// <param name="world">The read-only world for occlusion (terrain, blocked tiles, static solids; TD-175 item 4, approved by Evan 2026-10-28 7:26 AM PT).</param>
    /// <param name="anchors">The anchor registry (read only).</param>
    /// <param name="gate">The framework context gate.</param>
    /// <param name="settings">The focus knobs (<c>ChestMm</c>, <c>IncumbentSlackMm</c>, <c>NearMm</c>, <c>NearAxisMm</c>, weights, dwell) and <c>MaxCarrierCandidates</c> (the occlusion solid scratch).</param>
    /// <param name="counters">The ClientLayer's counters (arithmetic counts and the <see cref="FocusCounters"/> maxima (TD-170 items 1-2, Mail Lead planning default, pending Evan)).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public FocusStep(ICarrierFocusSource source, IWorldQuery world, AnchorRegistry anchors, InteractionGate gate, PlayerSettings settings, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(counters);
        _source = source;
        _world = world;
        _anchors = anchors;
        _gate = gate;
        _settings = settings;
        _counters = counters;
        _solidLimit = Math.Clamp(settings.MaxCarrierCandidates, 0, CarrierScratch.SolidCapacity);
    }

    /// <summary>The focus after the last <see cref="Step"/>; story 017 sets <see cref="FocusState.Latched"/> through it.</summary>
    public ref FocusState State => ref _state;

    /// <summary>The last <see cref="Step"/>'s budget probes.</summary>
    public ref readonly FocusCounters Probes => ref _probes;

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        ref readonly InputFrame frame = ref ctx.Input;
        ref readonly CarrierStatus status = ref _source.Status;
        Vec3Mm chest = FocusScore.Chest(_source.Feet, _settings.ChestMm);
        FocusScore.Forward(frame.LookYaw, out int fx, out int fz);
        byte mask = status.Body == BodyContext.Build ? AnchorRegistry.ContextBuild : AnchorRegistry.ContextWorld;
        AnchorRef incumbent = _state.Focused;
        bool latched = _state.Latched;
        SimCounterSink sink = _counters.PrimitiveSink();

        Span<int> found = stackalloc int[CandidateCapacity];
        Span<Candidate> scored = stackalloc Candidate[CandidateCapacity];
        int n = _anchors.QueryCells(in chest, found, out int cellsVisited);
        int candidates = 0;
        int eligible = 0;
        int incumbentIndex = -1;
        GateResult incumbentGate = default;
        bool incumbentEligible = false;
        for (int k = 0; k < n; k++)
        {
            int i = found[k];
            if (_anchors.ContextMaskAt(i) != mask)
            {
                continue;
            }

            ref readonly AnchorDef def = ref _anchors.DefAt(i);
            bool isIncumbent = !incumbent.IsNone && def.Ref == incumbent;
            GateResult gate = _gate.Evaluate(in def, in status);
            if (isIncumbent)
            {
                incumbentIndex = i;
                incumbentGate = gate;
            }

            if (latched || gate.Kind == GateKind.NoCandidate)
            {
                continue;
            }

            FocusGeometry g = FocusScore.Measure(in chest, in def.Point, fx, fz, _settings.NearAxisMm, ref sink);
            if (!FocusScore.InRange(g.D, def.RangeMm, isIncumbent, _settings.IncumbentSlackMm))
            {
                continue;
            }

            candidates++;
            if (!FocusScore.InCone(g.D, g.CosPm, frame.FocusCosMinPm, _settings.NearMm))
            {
                continue;
            }

            incumbentEligible |= isIncumbent;
            long score = FocusScore.Score(g.D, g.CosPm, def.RangeMm, def.Priority, isIncumbent, _settings);
            scored[eligible++] = new Candidate(score, def.Ref, i, gate);
        }

        _probes.Candidates = candidates;
        _probes.CellsVisited = cellsVisited;
        _probes.OcclusionTests = 0;
        if (latched)
        {
            _probes.RaiseInto(_counters);
            // D2 (TD-164): the incumbent is kept unconditionally and the dwell is held at zero; a ref not registered in this
            // tick's context (unregistered, or only in the other context) is kept, not read, and shows as unavailable (TD-170).
            _state.LeaderRef = AnchorRef.None;
            _state.LeadTicks = 0;
            if (!incumbent.IsNone && incumbentIndex < 0 && _anchors.TryGet(incumbent, mask, out incumbentIndex))
            {
                // Outside the queried cells or the kept prefix: the rare O(maxAnchors) lookup.
                incumbentGate = _gate.Evaluate(in _anchors.DefAt(incumbentIndex), in status);
            }

            if (incumbentIndex >= 0)
            {
                WriteAvailability(incumbentIndex, incumbentGate, in chest, in ctx);
            }
            else
            {
                _state.Available = false;
                _state.Reason = StringId.None;
            }

            return;
        }

        // Lazy occlusion in ascending score order: the first unoccluded candidate wins.
        Span<Candidate> ordered = scored[..eligible];
        Span<Candidate> sortScratch = stackalloc Candidate[CandidateCapacity];
        StableSort.Sort(ordered, sortScratch[..eligible], default(CandidateOrder));
        Span<ushort> solids = stackalloc ushort[CarrierScratch.SolidCapacity];
        solids = solids[.._solidLimit];
        int bestIndex = -1;
        AnchorRef best = AnchorRef.None;
        GateResult bestGate = default;
        bool incumbentTested = false;
        for (int k = 0; k < ordered.Length; k++)
        {
            ref readonly Candidate cand = ref ordered[k];
            bool occluded = FocusOcclusion.IsOccluded(_world, in chest, in _anchors.DefAt(cand.Index).Point, cand.Ref.Entity, solids, ref _probes);
            if (cand.Ref == incumbent)
            {
                incumbentTested = true;
                incumbentEligible = !occluded;
            }

            if (!occluded)
            {
                bestIndex = cand.Index;
                best = cand.Ref;
                bestGate = cand.Gate;
                break;
            }
        }

        if (incumbentEligible && !incumbentTested
            && FocusOcclusion.IsOccluded(_world, in chest, in _anchors.DefAt(incumbentIndex).Point, incumbent.Entity, solids, ref _probes))
        {
            // TD-175 item 1: the incumbent ranks behind the winner, so the dwell would keep it; one extra test lets occlusion drop it.
            incumbentEligible = false;
        }

        _probes.RaiseInto(_counters);
        if (incumbent.IsNone || !incumbentEligible)
        {
            // No incumbent, or it became ineligible: the best candidate (or none) is focused on this tick.
            Focus(best);
        }
        else if (best == incumbent)
        {
            _state.LeaderRef = AnchorRef.None;
            _state.LeadTicks = 0;
        }
        else
        {
            _state.LeadTicks = best == _state.LeaderRef ? _state.LeadTicks + 1 : 1;
            _state.LeaderRef = best;
            if (_state.LeadTicks >= _settings.FocusDwellTicks)
            {
                Focus(best);
            }
        }

        if (_state.Focused.IsNone)
        {
            _state.Available = false;
            _state.Reason = StringId.None;
        }
        else if (_state.Focused == best)
        {
            WriteAvailability(bestIndex, bestGate, in chest, in ctx);
        }
        else
        {
            WriteAvailability(incumbentIndex, incumbentGate, in chest, in ctx);
        }
    }

    /// <summary>One scored, eligible anchor: its score, ref, registry index and gate verdict.</summary>
    private readonly record struct Candidate(long Score, AnchorRef Ref, int Index, GateResult Gate);

    /// <summary>Score ascending, then the <see cref="AnchorRef.CompareTo"/> tie-break (a strict total order over distinct refs).</summary>
    private readonly struct CandidateOrder : IComparer<Candidate>
    {
        public int Compare(Candidate x, Candidate y)
        {
            int c = x.Score.CompareTo(y.Score);
            return c != 0 ? c : x.Ref.CompareTo(y.Ref);
        }
    }

    private void Focus(AnchorRef anchor)
    {
        _state.Focused = anchor;
        _state.LeaderRef = AnchorRef.None;
        _state.LeadTicks = 0;
    }

    /// <summary>A gate <see cref="GateKind.Unavailable"/> reason wins (the owner is not asked); otherwise the owner's <c>IsAvailable</c>.</summary>
    private void WriteAvailability(int index, GateResult gate, in Vec3Mm chest, in ClientStepContext ctx)
    {
        if (gate.Kind == GateKind.Unavailable)
        {
            _state.Available = false;
            _state.Reason = gate.Reason;
            return;
        }

        ref readonly AnchorDef def = ref _anchors.DefAt(index);
        var c = new InteractionContext
        {
            Tick = ctx.HostClock.IsValid ? ctx.HostClock.EstimatedHostTick : 0u,
            ChestMm = chest,
            Anchor = def.Ref,
            Verb = def.Verb,
            Mode = def.Mode,
            HoldBaseMs = def.HoldBaseMs,
            RangeMm = def.RangeMm,
            ContextMask = _anchors.ContextMaskAt(index),
            IsHost = 0,
        };
        bool available = _anchors.ImplAt(index).IsAvailable(in c, out StringId reason);
        _state.Available = available;
        _state.Reason = available ? StringId.None : reason;
    }
}
