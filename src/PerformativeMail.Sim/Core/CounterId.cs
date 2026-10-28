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

    /// <summary>Inventory AutoPlace PASS M: one candidate stack visited in a target's StackKey non-full list (one increment per list node walked, never a container scan; GDD R8, ADR-0009 section 5; key <c>inventoryMergeCandidates</c>). A probe counter (H-PERF-6), compiled into Release, not in the canonical dump.</summary>
    InventoryMergeCandidates = 42,

    /// <summary>Inventory AutoPlace PASS U / PASS R: one increment per cell examined (GDD H-PERF-6 "one integer increment per cell probe"; per cell per Evan 2026-10-21). A grid candidate anchor tests all <c>fh</c> of its footprint's occupancy row masks, each covering <c>fw</c> cells, so it adds <c>fw × fh</c>; a slots-layout candidate slot adds 1; a negative-fit cache hit examines nothing. A scan therefore costs (anchors visited) × area, which matches GDD D3's per-shape <c>C × a</c> term (GDD R8, ADR-0009 section 5; key <c>inventoryPlacementProbes</c>). A probe counter, compiled into Release, not in the canonical dump.</summary>
    InventoryPlacementProbes = 43,

    /// <summary>Inventory outfeed: one per candidate visit = one FIFO node examined (GDD H-PERF-6 unit: one per cell probe or candidate visit; never per call, per anchor or per batch). <c>PeekOutfeed</c> / <c>TakeUnit</c> count the live head and each stale-head tombstone popped once; a compaction or a rebuild counts each node it walks; an append counts nothing; GDD R14, ADR-0009 section 5; key <c>inventoryOutfeedCandidates</c>). A probe counter (H-PERF-6a: at most 4 per call amortised), compiled into Release, not in the canonical dump.</summary>
    InventoryOutfeedCandidates = 44,

    /// <summary>A mutating <c>I…Api</c> method was called outside a tick phase (ADR-0003 section 5: legal only while <c>Stage == InPhase</c> and <c>Phase</c> is at most <c>RunStep</c>); the call was refused with nothing mutated and no event. Asserts under <c>SIM_ASSERT</c> (<c>TickGuard</c>).</summary>
    MutatingApiOutOfPhase = 45,

    /// <summary>Inventory WorldItem cap eviction: one per candidate visit = one expiry-FIFO node inspected by the eviction path (GDD H-PERF-6d; ADR-0009 section 5): a head read that was not already verified since it last changed, and each stale tombstone popped on the way. A head the <c>ItemsExpire</c> pass or an earlier eviction already verified is not read again and counts nothing; comparing two known heads counts nothing (story grid-inventory/013; key <c>inventoryWorldItemCapCandidates</c>). A probe counter (H-PERF-6d: at most items created + 1 per tick), compiled into Release, not in the canonical dump.</summary>
    InventoryWorldItemCapCandidates = 46,

    /// <summary>Retired by story grid-inventory/021 (TD-138): no longer incremented. It counted story 013's interim rule (a mail WorldItem due to return to a full or not-accepting Intake was kept and retried); the backlog now takes such an entry, so a mail WorldItem always leaves. The append-only id stays pinned and is never reused (key <c>inventoryWorldItemReturnRetries</c>).</summary>
    InventoryWorldItemReturnRetries = 47,

    /// <summary>Inventory WorldItem: a creation left the live WorldItem count over <c>inventory.worldItemCap</c> because no eviction candidate qualified (GDD R12 <c>worldItemCapExceeded</c>; one per such creation; the first one of an episode also writes <c>CheckCode.InventoryWorldItemCapExceeded</c>; key <c>inventoryWorldItemCapExceeded</c>).</summary>
    InventoryWorldItemCapExceeded = 48,

    /// <summary>Inventory <c>CountItem</c>: one per table slot read in the per-container <c>itemId → units</c> map (GDD R17 "never a scan", H-R17-1; ADR-0009 section 5; story grid-inventory/015; key <c>inventoryItemCountProbes</c>): a hit counts the slots up to and including its key, a miss up to and including the empty slot that ends it. Depends on the map's collisions, never on how many entries a container holds. A probe counter, compiled into Release, not in the canonical dump.</summary>
    InventoryItemCountProbes = 49,

    /// <summary>Inventory <c>IntakeDrain</c>: one per backlog ring node read by a drain pass (the H-PERF-6d visit unit; story grid-inventory/021; key <c>inventoryBacklogVisits</c>). A candidate visit under ADR-0009 l.232, so H-PERF-6c's drain gate counts it with the placement probes (<c>inventoryPlacementProbes</c> + merge candidates + these visits at most 8,500 per pass); kept as its own counter for diagnostics, with a second test gate of at most entries walked + 1 per pass (TD-157 item 4). An idle pass (empty backlog, full Intake, or no change since the last pass) reads no node. A probe counter, compiled into Release, not in the canonical dump.</summary>
    InventoryBacklogVisits = 50,

    /// <summary>Inventory tick-end conservation check: one per finding, a ledger row whose Σ live differs from spawned + granted − consumed − destroyed or a negative live counter (story grid-inventory/017; ADR-0009 section 6; GDD R19, H-R19-2 reads it; key <c>inventoryConservationMismatches</c>). Unthrottled, unlike its <c>CheckCode.InventoryConservationMismatch</c> record (Mail Lead planning default pending Evan, TD-160 item 5). Zero in a correct world.</summary>
    InventoryConservationMismatches = 51,

    /// <summary>Focus (player-character story 014; ADR-0014 section 2): the most candidates one client-tick focus pass scored (key <c>focusCandidates</c>; D3, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164), i.e. anchors past the context filter and within reach; a running maximum, not a sum (Mail Lead planning default, pending Evan (TD-170 item 2)). A probe counter on the ClientLayer's counters, not in the canonical dump.</summary>
    FocusCandidates = 52,

    /// <summary>Focus: the most anchor-grid cells one client-tick focus pass walked (budget 25 at the default reach, ADR-0014 l.62) (key <c>focusCellsVisited</c>; D3, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164); a running maximum, not a sum (Mail Lead planning default, pending Evan (TD-170 item 2)). A probe counter on the ClientLayer's counters, not in the canonical dump.</summary>
    FocusCellsVisited = 53,

    /// <summary>Press resolution (player-character story 016; ADR-0014 section 3, input-controls H-R4-6): one per Interact press edge ignored because a hold or channel was already in progress (no second latch) (key <c>interactPressIgnored</c>; D5 — Mail Lead default, pending Evan (TD-171)). On the ClientLayer's counters, not in the canonical dump.</summary>
    InteractPressIgnored = 54,

    /// <summary>Focus occlusion (player-character story 015; ADR-0014 section 2): the most chest-to-anchor occlusion tests one client-tick focus pass ran (key <c>focusOcclusionTests</c>; PCM-PERF-1 "occlusion tests &lt;= candidates in range"); a running maximum like <see cref="FocusCandidates"/>, never above it (TD-175 item 2, approved by Evan 2026-10-28 7:26 AM PT). A probe counter on the ClientLayer's counters, not in the canonical dump.</summary>
    FocusOcclusionTests = 55,

    /// <summary>Mail <c>MailStep</c> (phase 8): one per mail-table row visited by the step (key <c>mailRowsVisited</c>; mail-destinations story 005, TD-177 item 4; M-PERF-1c: an idle tick visits 0). A test seam: at M-1a the step has no row-visiting path, so it stays 0.</summary>
    MailRowsVisited = 56,

    /// <summary>Mail <c>MailStep</c> (phase 8): one per complaint bucket visited by the step (key <c>mailBucketsVisited</c>; mail-destinations story 005, TD-177 item 4; M-PERF-1c: an idle tick visits 0; buckets refill lazily on touch, ADR-0017 section 7). A test seam: at M-1a the step visits no bucket, so it stays 0.</summary>
    MailBucketsVisited = 57,
}
