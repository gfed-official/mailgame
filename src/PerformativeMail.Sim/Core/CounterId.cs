namespace PerformativeMail.Sim.Core;

/// <summary>
/// Index of each always-on counter in a world's <see cref="SimCounters"/>. Values are explicit and
/// append-only: never renumber, reuse or reorder one (a test pins the primitive block and the
/// later values). Values 0 to 5 are the numerics block and equal
/// <see cref="Primitives.PrimitiveCounter"/>; <see cref="SimCounters.PrimitiveSink"/> exposes them.
/// </summary>
/// <example>
/// <code>
/// var counters = new SimCounters();
/// counters.Add(CounterId.BadTick);
/// long n = counters[CounterId.BadTick];   // 1
/// </code>
/// </example>
public enum CounterId : ushort
{
    /// <summary>Numerics block: a Fix helper got a zero divisor (equals PrimitiveCounter.FixDivZero).</summary>
    FixDivZero = 0,

    /// <summary>Numerics block: a Fix helper clamped its result (equals PrimitiveCounter.FixClamped).</summary>
    FixClamped = 1,

    /// <summary>Numerics block: a money result was clamped (equals PrimitiveCounter.MoneyClamped).</summary>
    MoneyClamped = 2,

    /// <summary>Numerics block: money add/sub saturated (equals PrimitiveCounter.MoneySaturated).</summary>
    MoneySaturated = 3,

    /// <summary>Numerics block: an Fx16 result was clamped (equals PrimitiveCounter.FxClamped).</summary>
    FxClamped = 4,

    /// <summary>Numerics block: a negative permille ratio was treated as 0 (equals PrimitiveCounter.MoneyBadPermille).</summary>
    MoneyBadPermille = 5,

    /// <summary>A tick or millisecond input was out of range and was handled.</summary>
    BadTick = 6,

    /// <summary>A player command was refused because the target was busy.</summary>
    BusyPlayer = 7,

    /// <summary>A system command was refused because the target was busy.</summary>
    BusySystem = 8,

    /// <summary>An enqueue was attempted in an illegal state.</summary>
    EnqueueBadState = 9,

    /// <summary>An enqueue was attempted while the queue was flushing.</summary>
    EnqueueDuringFlush = 10,

    /// <summary>Drain backlog level (a level, not a monotonic count; see Story 020).</summary>
    DrainBacklog = 11,

    /// <summary>Events were dropped.</summary>
    EventsDropped = 12,

    /// <summary>Events were published outside a tick.</summary>
    EventsOutsideTick = 13,

    /// <summary>Events were published at tick end.</summary>
    EventsAtTickEnd = 14,

    /// <summary>Events were published during a flush.</summary>
    EventsDuringFlush = 15,

    /// <summary>A coalescing request arrived late.</summary>
    CoalesceLate = 16,

    /// <summary>A state dump was requested during a tick.</summary>
    DumpDuringTick = 17,

    /// <summary>An illegal id allocation was attempted.</summary>
    IdAllocIllegal = 18,

    /// <summary>An RNG stream index was out of range.</summary>
    RngIndexOutOfRange = 19,

    /// <summary>A stop was requested out of phase.</summary>
    StopOutOfPhase = 20,

    /// <summary>An RNG stream request was refused: the name is not owned by the requesting view, or is not registered.</summary>
    RngRefused = 21,

    /// <summary>A second stop request, or one on an already stopped or faulted world, was ignored (ADR-0003 section 5).</summary>
    StopIgnored = 22,

    /// <summary>An enqueue was refused because the <c>opSeq</c> counter was exhausted (ADR-0007 section 2); the counter never wraps.</summary>
    OpSeqExhausted = 23,

    /// <summary>A call scope was still open after a phase run or at tick end (a discarded <c>log.BeginCall();</c>); the depth was reset (ADR-0008 risks).</summary>
    ScopeLeak = 24,

    /// <summary>A handler returned a <see cref="Reply"/> whose <c>Result</c> disagreed with its <c>Reason</c> (<c>Ok</c> iff <c>Reason == 0</c>); the drain normalised <c>Result</c> from <c>Reason</c> and appended it (ADR-0007 section 5).</summary>
    ReplyInconsistent = 25,

    /// <summary>A <c>QueryStaticSolids</c> call had more hits than its caller span and returned the lowest-index ones (counted once per truncating call; ADR-0026 section 5).</summary>
    WorldSolidQueryOverflow = 26,

    /// <summary>A <c>QueryStaticSolids</c> region touched more 4 m buckets than the merge buffer holds and used the ascending linear scan of all solids (correct, but O(solids)).</summary>
    WorldSolidQueryFallback = 27,

    /// <summary>A transform sample was discarded at the tick prologue because its per-sender sequence number was older than or equal to the last accepted one (ADR-0003 section 2).</summary>
    SamplesStale = 28,

    /// <summary>A transform sample write was refused: the ring was full, the slot was none, or the world was not Ready/Running. Never grows, never overwrites.</summary>
    SamplesRefused = 29,

    /// <summary>An input edge was dropped because the <c>InputLatch</c> already held its fixed capacity of undelivered edges (ClientLayer; a client-side counter, never part of the host world).</summary>
    InputEdgeOverflow = 30,

    /// <summary>Carrier solver: the most sub-steps one carrier step took in a tick; a running maximum, not a sum (ADR-0013 section 4; key <c>carrierSubSteps</c>).</summary>
    CarrierSubSteps = 31,

    /// <summary>Carrier solver: the most slide iterations in one sub-step; a running maximum, not a sum (key <c>carrierSlideIterations</c>).</summary>
    CarrierSlideIterations = 32,

    /// <summary>Carrier solver: a move was clamped by the sub-step or per-tick cap (key <c>carrierStepClamped</c>).</summary>
    CarrierStepClamped = 33,

    /// <summary>Carrier solver: a depenetration correction was applied (key <c>carrierDepenetrations</c>).</summary>
    CarrierDepenetrations = 34,

    /// <summary>Carrier solver: a candidate was dropped because a fixed-capacity scratch buffer was full (key <c>carrierCandidateOverflow</c>).</summary>
    CarrierCandidateOverflow = 35,

    /// <summary>A <c>PushSteps</c> call named a fifth distinct action in one frame (the latch holds at most 4) and was dropped (ClientLayer; a client-side counter; ADR-0012 section 4).</summary>
    InputStepDropped = 36,

    /// <summary>A stat-source <c>Push</c> was refused because the owner's fixed modifier pool (or its source table) was full (content-data story 015; ADR-0010 section 7). The sheet and its version are unchanged; the pool never grows.</summary>
    StatPoolFull = 37,

    /// <summary>A stat-source <c>Push</c> or <c>Remove</c> was refused for a reason other than capacity: an invalid source or modifier, a stack index at or beyond the source's maximum, a duplicate identity, or a removal of a source never pushed (content-data story 015).</summary>
    StatWriteRefused = 38,

    /// <summary><c>Hold.HoldTicks</c> received an <c>insertPm</c> outside 0 to maxInsertPm and clamped it (ADR-0014 section 4; key <c>holdInputClamped</c>; defensive - the StatSheet already clamps DeliverInsertPm).</summary>
    HoldInputClamped = 39,

    /// <summary>An anchor registration was refused because the registry already held <c>maxAnchors</c> anchors (ADR-0014 section 1; key <c>anchorRegistryFull</c>).</summary>
    AnchorRegistryFull = 40,

    /// <summary>An anchor query had more hits than its result span and kept the first ones (counted once per truncating call; key <c>anchorQueryOverflow</c>).</summary>
    AnchorQueryOverflow = 41,
}
