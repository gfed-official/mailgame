namespace PerformativeMail.Sim.Core;

/// <summary>
/// Canonical dump section ids (ADR-0006 section 4). Append-only: an id is allocated once, never reused for a different layout
/// and never renumbered, because dumps are compared across builds. A system claims the next free value here when it binds its
/// <see cref="ISimStateContributor"/>. Id 0 is invalid. Dump order is bind order, not id order.
/// </summary>
/// <example>
/// <code>
/// public ushort SectionId =&gt; SectionIds.SimCore;   // Sim Core's own section (Story 019)
/// </code>
/// </example>
public static class SectionIds
{
    /// <summary>Sim Core's own section: tick, id counters, <c>opSeq</c>, RNG streams, queued ops, sample latch, slot table.</summary>
    public const ushort SimCore = 1;

    /// <summary>Players' section: per present slot, ascending, the player id, life state and deadlines, aim and every <c>CarrierState</c> field.</summary>
    public const ushort Players = 2;

    /// <summary>Mail's section (ADR-0017): <c>u32 liveCount</c>, then live rows in ascending <c>mailId</c>. The destination registry (story 002) appends its fields to the same section.</summary>
    public const ushort Mail = 3;

    /// <summary>Grid Inventory's section (ADR-0009): the next container id, the live container count, then per live container in ascending <c>containerId</c> its header fields.</summary>
    public const ushort Inventory = 4;

    /// <summary>
    /// The world tables' section (ADR-0026 section 9): bounds (4 x i64), <c>TilesX</c>, <c>TilesZ</c> (i32), corner heights (u32 count + i32 each, row-major),
    /// flags (u32 count + u8 each), solids (u32 count + per solid index u16, owner u32, box 6 x i32, kind u8), lots (u32 count + every
    /// <c>LotRecord</c> field in declaration order) and the Post Office record. Immutable, so it is encoded once at build.
    /// </summary>
    public const ushort World = 5;
}
