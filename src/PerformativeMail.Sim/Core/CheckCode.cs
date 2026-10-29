namespace PerformativeMail.Sim.Core;

/// <summary>
/// Identifies an always-on check recorded in the <see cref="SimCheckRing"/>. Explicit,
/// append-only values: never renumber or reuse. Value 0 is reserved (a zeroed record is not a
/// real check). Codes outside the defined range are still recorded; see <see cref="SimCheckRing.Record"/>.
/// </summary>
/// <example><code>ring.Record(CheckCode.TickHitch, tick, elapsedMs, 0);</code></example>
public enum CheckCode : ushort
{
    /// <summary>Reserved; never recorded by the Sim.</summary>
    None = 0,

    /// <summary>A frame ran longer than the tick budget (written by FrameRunner).</summary>
    TickHitch = 1,

    /// <summary>An id space was exhausted.</summary>
    IdExhausted = 2,

    /// <summary>A system was busy when work arrived.</summary>
    BusySystem = 3,

    /// <summary>The operation sequence counter was exhausted.</summary>
    OpSeqExhausted = 4,

    /// <summary>A tick value was out of range.</summary>
    BadTick = 5,

    /// <summary>An entity id was requested for a reserved class (0x00, 0x09 to 0xEF) or a test class without <c>allowTestClasses</c>; a = class.</summary>
    IdReservedClass = 6,

    /// <summary>The mail table grew (ADR-0017 section 2). a = kind: 1 = the empty page pool doubled (b = new total page count), 2 = the page map doubled because ids ran past it (b = new map length).</summary>
    MailTableGrow = 7,

    /// <summary>The <c>InputLatch</c> held more than 32 edges for one frame; a = edges dropped (newest presses), b = presses coalesced. Written once per overflow (ADR-0012 section 4).</summary>
    InputEdgeOverflow = 8,

    /// <summary>Grid Inventory: a container could not be created because the container table is full. a = player slot (0xFF for a world container), b = table capacity.</summary>
    InventoryContainerCapacity = 9,

    /// <summary>Players: a second verb was registered on one anchor in one context (<c>RegisterResult.DuplicateVerb</c>, PCM-P10-1; ADR-0014 section 1). a = entity id, b = slot. Recorded with tick 0 (the registry has no tick source).</summary>
    AnchorDuplicateVerb = 10,

    /// <summary>Grid Inventory: an entry could not be allocated because the entry pool is full (story grid-inventory/002). a = pool capacity, b = live entries.</summary>
    InventoryEntryPoolExhausted = 11,

    /// <summary>Grid Inventory: the mail-ids arena could not give a block because the fitting class and every larger class are empty, or a run would exceed 256 ids (story grid-inventory/003; the validator maps it to <c>capacity_exhausted</c>). a = units requested, b = the fitting class index (9 when no class fits).</summary>
    InventoryArenaExhausted = 12,

    /// <summary>Grid Inventory: the live WorldItem count went over <c>inventory.worldItemCap</c> because no eviction candidate qualified (every WorldItem young mail, GDD R12 <c>worldItemCapExceeded</c>; story grid-inventory/013). Written once when the count first goes over, then not again until it is back at or under the cap. a = live count, b = cap.</summary>
    InventoryWorldItemCapExceeded = 13,

    /// <summary>Grid Inventory: a WorldItem could not be created because the WorldItem table (<c>inventory.worldItemCapacity</c>) is full (story grid-inventory/013); the entry stays with the caller. a = table capacity, b = live count.</summary>
    InventoryWorldItemCapacity = 14,

    /// <summary>Grid Inventory: the tick-end conservation check found a ledger row out of balance (story grid-inventory/017; ADR-0009 section 6; GDD R19). a = the ledger row (0 = mail, itemId + 1 for an item) with, for a negative live counter, the state ordinal + 1 (<c>LedgerState</c>) in bits 24-31 (0 for the Σ check); b = Σ live − (spawned + granted − consumed − destroyed), or the negative counter's value. One record per finding, subject to the ring's per-code rate limit; <c>CounterId.InventoryConservationMismatches</c> counts every finding (Mail Lead planning default pending Evan, TD-160 item 5).</summary>
    InventoryConservationMismatch = 15,

    // 16 is reserved for player-character 017.

    /// <summary>Throwing: the tick-end conservation check found <c>launched != resolved + live</c> or a <c>FlightId</c> resolved twice in the run (throwing-catching story 008; ADR-0015 section 5). a = launched − resolved − live; b = the twice-resolved <c>FlightId</c> value, or 0. Recorded on every tick the finding holds, subject to the ring's per-code rate limit; <c>CounterId.ThrowingConservationMismatches</c> counts every one. Id 17: Mail Lead planning default, pending Evan (TD-195 item 3).</summary>
    ThrowingConservationMismatch = 17,
}
