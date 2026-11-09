using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail's canonical dump section, <see cref="SectionIds.Mail"/> (ADR-0006 section 4, ADR-0017 section 2). Layout: the mail
/// table rows (<see cref="MailTable.WriteCanonicalRows"/>: <c>u32 liveCount</c>, then live rows in ascending <c>mailId</c>,
/// each <c>mailId u32, kind u16, addressId u32, spawnValue u16, spawnShift u8, deadlineShift u8, flags u8</c>).
/// Then <see cref="DestinationRegistry.WriteCanonical"/> (story 002: <c>u16 destinationCount</c> and the per-destination columns), then,
/// when built with the complaint buckets, <see cref="ComplaintBuckets.WriteCanonical"/> (story 006: <c>teamTokensPm i64, teamLastTick i64,
/// lastLateTick i64, lateCharged u8</c>; Evan approved 2026-10-29 (TD-187 item 3)), then, when built with the consume routine,
/// <see cref="MailConsumer.WriteCanonical"/> (story 007: <c>outcomeSeq u32</c>, <c>lastZeroFloorTick i64 × lots × 8</c>,
/// <c>misdeliveriesThisShift u16</c>; TD-208 item 10), then the deliveries state (story 009: <c>open u8, closeTick u32,
/// unlockedDistrictMask u64</c>; TD-208 item 10, "story 009 appends after these"). Later stories only append; never reorder what is here.
/// </summary>
internal sealed class MailStateContributor : ISimStateContributor
{
    private readonly MailTable _table;
    private readonly DestinationRegistry _registry;
    private readonly ComplaintBuckets? _complaints;
    private readonly MailConsumer? _consumer;

    /// <summary>Creates the contributor for one table, its destination registry and, optionally, the complaint buckets.</summary>
    /// <param name="table">The mail table to dump.</param>
    /// <param name="registry">The destination registry, dumped after the rows.</param>
    /// <param name="complaints">
    /// The complaint buckets, dumped after the registry. The save/compare dump includes the 25 bytes of team complaint data only when
    /// it's given the buckets; the real game always gives them (Mail Dev default, approved by Evan 2026-11-03 9:15 PM PT by card click
    /// (TD-193 item 6)). Implementation detail (Mail Dev, not part of Evan's ruling): null writes no team state (the table and
    /// registry tests' layout).
    /// </param>
    /// <param name="consumer">The consume routine, whose outcome state is dumped after the team complaint state; null writes none.</param>
    public MailStateContributor(MailTable table, DestinationRegistry registry, ComplaintBuckets? complaints = null, MailConsumer? consumer = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(registry);
        _table = table;
        _registry = registry;
        _complaints = complaints;
        _consumer = consumer;
    }

    /// <inheritdoc />
    public ushort SectionId => SectionIds.Mail;

    /// <inheritdoc />
    public void Write(ref CanonicalWriter w)
    {
        _table.WriteCanonicalRows(ref w);

        // Story 002: the destination registry's columns, after the mail rows (layout in DestinationRegistry.WriteCanonical).
        _registry.WriteCanonical(ref w);

        // Story 006: the team bucket and late window, after the registry (Evan approved 2026-10-29 (TD-187 item 3)).
        _complaints?.WriteCanonical(ref w);

        // Story 007: the outcome state, after the team complaint state (TD-208 item 10: "the number first, then the floor times, then
        // the count").
        _consumer?.WriteCanonical(ref w);

        // Story 009: the deliveries state, after the outcome state: open u8, closeTick u32, unlockedDistrictMask u64.
        DeliveriesState deliveries = _registry.Deliveries;
        w.WriteBool(deliveries.Open);
        w.WriteU32(deliveries.CloseTick);
        w.WriteU64(deliveries.UnlockedDistrictMask);
    }
}
