using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The built-in <see cref="ClientStepId.InputLatch"/> step (ADR-0002 section 2, ADR-0012 section 4, Input and
/// Controls GDD R11). The router pushes ordered edges, steps and the current state through
/// <see cref="IInputLatchWriter"/> (reach it with <see cref="ClientLayer.InputWriter"/>; the push members are
/// explicit interface implementations). On a tick with <c>IsFirstTickInBatch</c> the latch emits an
/// <see cref="InputFrame"/> holding the latched edges (at most 32) and all latched steps, in arrival order, and clears
/// what it emitted. On later ticks of the batch the frame has no edges or steps, only state. If no tick runs, nothing
/// is cleared. The latch only latches and emits: it never dispatches a verb and never resolves an edge.
/// <para>
/// Storage: a preallocated 256-entry edge buffer behind the frame's 32-edge window, a state slot and four step
/// accumulators. Nothing is allocated after construction. Not thread-safe: push and step on the same thread.
/// </para>
/// <para>
/// Overflow policy (more than 32 undelivered edges at emit, or more than 256 at push). First, same-action level
/// presses are coalesced: a Press of an action in the level mask (see <see cref="ClientLayerBuilder.SetLevelActionMask"/>:
/// Tap and Held-state actions) with an earlier Press of that action, no Release or Cancel of it and no change of edge
/// <see cref="InputEdge.RouterStateId"/> in between is removed, keeping the first. Actions outside the mask (Hold, Aim and Interact grammar) are never merged. If the
/// latch is still over capacity, the newest Press edges are dropped. A Release or Cancel is never dropped; edges that
/// do not fit the 32-edge frame stay latched, in order, for the next frame that runs a first tick. Only a buffer of 257
/// non-press edges (a router bug) drops the newest edge of any kind. Survivors keep their order. Each drop adds to
/// <see cref="InputFrame.DroppedEdges"/> and <see cref="CounterId.InputEdgeOverflow"/>; each overflow writes one
/// <see cref="CheckCode.InputEdgeOverflow"/> record (when a check ring was supplied).
/// </para>
/// <para>
/// Steps: <c>PushSteps</c> accumulates per action in a signed byte, saturating at +/-127. At most four distinct
/// actions per frame; a fifth is dropped and counted in <see cref="CounterId.InputStepDropped"/>. An action whose
/// steps net to 0 is not emitted.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ClientLayer layer = new ClientLayerBuilder(clock).Build();
/// IInputLatchWriter w = layer.InputWriter;
/// w.SetState(new InputState { HeldBits = 0b1, MoveX = 16384, RouterStateId = 1 });
/// w.PushEdge(new InputEdge(FrameAction.Interact, EdgeKind.Press, arrivalSeq: 1, routerStateId: 1));
/// layer.Step(isFirstTickInBatch: true);    // layer.InputLatch.Frame has 1 edge
/// layer.Step(isFirstTickInBatch: false);   // no edges, held bit 0 still set
/// </code>
/// </example>
public sealed class InputLatch : IClientStep, IInputLatchWriter
{
    /// <summary>Capacity of the edge buffer behind the 32-edge frame window (ADR-0012 section 4: a preallocated 256-entry ring).</summary>
    public const int RingCapacity = 256;

    private readonly SimCounters _counters;
    private readonly SimCheckRing? _checks;
    private readonly ulong _levelMask;
    private readonly InputEdge[] _ring = new InputEdge[RingCapacity + 1];   // +1: the pushed edge lands here before the overflow policy runs
    private readonly ulong[] _dropMask = new ulong[(RingCapacity + 1 + 63) / 64];
    private int _count;
    private InputStepBuffer _steps;
    private int _stepCount;
    private int _droppedSinceEmit;
    private InputFrame _frame;
    private InputState _state;
    private ushort _lastArrivalSeq;
    private bool _hasArrivalSeq;
    private uint _lastTick;

    internal InputLatch(SimCounters counters, SimCheckRing? checks = null, ulong levelActionMask = 0)
    {
        _counters = counters;
        _checks = checks;
        _levelMask = levelActionMask;
    }

    /// <summary>The frame emitted by the latest <see cref="Step"/> (default before the first tick). Read by steps through <see cref="ClientStepContext.Input"/>.</summary>
    public ref readonly InputFrame Frame => ref _frame;

    /// <summary>Edges latched and not yet emitted.</summary>
    public int PendingEdgeCount => _count;

    /// <summary>Distinct actions with steps latched and not yet emitted.</summary>
    public int PendingStepCount => _stepCount;

    /// <inheritdoc />
    void IInputLatchWriter.PushEdge(in InputEdge e)
    {
        if (_hasArrivalSeq)
        {
            SimAssert.That(!SerialU16.IsNewer(_lastArrivalSeq, e.ArrivalSeq), SimAssertCode.InputEdgeOutOfOrder);
        }

        _lastArrivalSeq = e.ArrivalSeq;
        _hasArrivalSeq = true;
        _ring[_count++] = e;
        if (_count > RingCapacity)
        {
            Resolve(RingCapacity, lastResort: true);
        }
    }

    /// <inheritdoc />
    void IInputLatchWriter.PushSteps(FrameAction a, int n)
    {
        if (n == 0)
        {
            return;
        }

        for (int i = 0; i < _stepCount; i++)
        {
            if (_steps[i].Action == a)
            {
                _steps[i] = new InputStep(a, Saturate((long)_steps[i].N + n));
                return;
            }
        }

        if (_stepCount >= InputFrame.MaxSteps)
        {
            _counters.Add(CounterId.InputStepDropped);
            return;
        }

        _steps[_stepCount++] = new InputStep(a, Saturate(n));
    }

    /// <inheritdoc />
    void IInputLatchWriter.SetState(in InputState s) => _state = s;

    /// <summary>Emits this tick's frame. Called by <see cref="ClientLayer"/> as the first step of <see cref="ClientStepId.InputLatch"/>.</summary>
    /// <param name="ctx">The tick's context.</param>
    public void Step(in ClientStepContext ctx)
    {
        _lastTick = ctx.ClientTick;
        ClearFrameStorage();
        EmitState(ctx.ClientTick);
        if (!ctx.IsFirstTickInBatch)
        {
            return;
        }

        _frame.Flags |= InputFrameFlags.FirstInBatch;
        EmitEdges();
        EmitSteps();
        _frame.DroppedEdges = (ushort)Math.Min(_droppedSinceEmit, ushort.MaxValue);
        _droppedSinceEmit = 0;
    }

    private void EmitState(uint tick)
    {
        ref InputFrame f = ref _frame;
        f.Tick = tick;
        f.MoveX = _state.MoveX;
        f.MoveY = _state.MoveY;
        f.LookYaw = _state.LookYaw;
        f.LookPitch = _state.LookPitch;
        f.FocusCosMinPm = _state.FocusCosMinPm;
        f.RouterStateId = _state.RouterStateId;
        f.HeldBits = _state.HeldBits;
        f.Flags = _state.Flags & ~InputFrameFlags.FirstInBatch;
        f.DroppedEdges = 0;
    }

    private void EmitEdges()
    {
        ref InputFrame f = ref _frame;
        if (_count > InputFrame.MaxEdges)
        {
            Resolve(InputFrame.MaxEdges, lastResort: false);
        }

        int n = Math.Min(_count, InputFrame.MaxEdges);
        Span<InputEdge> edges = f.EdgeStorage;
        _ring.AsSpan(0, n).CopyTo(edges);
        f.EdgeCount = (byte)n;
        if (n < _count)
        {
            // Releases and cancels beyond the 32-edge window stay latched, in order, for the next first tick.
            Array.Copy(_ring, n, _ring, 0, _count - n);
        }

        _count -= n;
    }

    private void EmitSteps()
    {
        ref InputFrame f = ref _frame;
        Span<InputStep> steps = f.StepStorage;
        int emitted = 0;
        for (int i = 0; i < _stepCount; i++)
        {
            if (_steps[i].N != 0)
            {
                steps[emitted++] = _steps[i];
            }

            _steps[i] = default;
        }

        f.StepCount = (byte)emitted;
        _stepCount = 0;
    }

    private static sbyte Saturate(long value) => (sbyte)Math.Clamp(value, -127L, 127L);

    private void ClearFrameStorage()
    {
        if (_frame.EdgeCount != 0)
        {
            _frame.EdgeStorage[.._frame.EdgeCount].Clear();
            _frame.EdgeCount = 0;
        }

        if (_frame.StepCount != 0)
        {
            _frame.StepStorage[.._frame.StepCount].Clear();
            _frame.StepCount = 0;
        }
    }

    /// <summary>Brings the latched count down to <paramref name="target"/> if it can: coalesce level presses, then drop the newest presses.</summary>
    private void Resolve(int target, bool lastResort)
    {
        int coalesced = Coalesce();
        int dropped = 0;
        if (_count > target)
        {
            dropped = DropNewest(_count - target, lastResort);
        }

        if (dropped > 0)
        {
            _droppedSinceEmit += dropped;
            _counters.Add(CounterId.InputEdgeOverflow, dropped);
        }

        if (dropped + coalesced > 0)
        {
            _checks?.Record(CheckCode.InputEdgeOverflow, _lastTick, dropped, coalesced);
        }
    }

    /// <summary>Removes level-action presses that repeat an open press of the same action. Returns how many were removed.</summary>
    private int Coalesce()
    {
        if (_levelMask == 0)
        {
            return 0;
        }

        ulong open = 0;
        int write = 0;
        ushort runState = _count > 0 ? _ring[0].RouterStateId : (ushort)0;
        for (int read = 0; read < _count; read++)
        {
            InputEdge e = _ring[read];
            if (e.RouterStateId != runState)
            {
                // A router state change between two presses ends every run: presses gated against different
                // states are never merged (AC-3 "no state change between them").
                open = 0;
                runState = e.RouterStateId;
            }

            ulong bit = FrameActions.Bit(e.Action) & _levelMask;
            if (bit != 0)
            {
                if (e.Kind == EdgeKind.Press)
                {
                    if ((open & bit) != 0)
                    {
                        continue;
                    }

                    open |= bit;
                }
                else
                {
                    open &= ~bit;
                }
            }

            _ring[write++] = e;
        }

        int removed = _count - write;
        _count = write;
        return removed;
    }

    /// <summary>Drops up to <paramref name="excess"/> of the newest Press edges (any newest edges when <paramref name="lastResort"/>). Returns how many were dropped.</summary>
    private int DropNewest(int excess, bool lastResort)
    {
        Array.Clear(_dropMask);
        int marked = 0;
        for (int i = _count - 1; i >= 0 && marked < excess; i--)
        {
            if (_ring[i].Kind == EdgeKind.Press)
            {
                _dropMask[i >> 6] |= 1UL << (i & 63);
                marked++;
            }
        }

        for (int i = _count - 1; lastResort && i >= 0 && marked < excess; i--)
        {
            if ((_dropMask[i >> 6] & (1UL << (i & 63))) == 0)
            {
                _dropMask[i >> 6] |= 1UL << (i & 63);
                marked++;
            }
        }

        if (marked == 0)
        {
            return 0;
        }

        int write = 0;
        for (int read = 0; read < _count; read++)
        {
            if ((_dropMask[read >> 6] & (1UL << (read & 63))) == 0)
            {
                _ring[write++] = _ring[read];
            }
        }

        _count = write;
        return marked;
    }
}
