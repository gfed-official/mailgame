using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail's canonical dump section, <see cref="SectionIds.Mail"/> (ADR-0006 section 4, ADR-0017 section 2). Layout: the mail
/// table rows (<see cref="MailTable.WriteCanonicalRows"/>: <c>u32 liveCount</c>, then live rows in ascending <c>mailId</c>,
/// each <c>mailId u32, kind u16, addressId u32, spawnValue u16, spawnShift u8, deadlineShift u8, flags u8</c>).
/// Then <see cref="DestinationRegistry.WriteCanonical"/> (story 002: <c>u16 destinationCount</c> and the per-destination columns), then,
/// when built with the complaint buckets, <see cref="ComplaintBuckets.WriteCanonical"/> (story 006: <c>teamTokensPm i64, teamLastTick i64,
/// lastLateTick i64, lateCharged u8</c>; Evan approved 2026-10-29 (TD-187 item 3)). Later stories only append; never reorder what is here.
/// </summary>
internal sealed class MailStateContributor : ISimStateContributor
{
    private readonly MailTable _table;
    private readonly DestinationRegistry _registry;
    private readonly ComplaintBuckets? _complaints;

    /// <summary>Creates the contributor for one table, its destination registry and, optionally, the complaint buckets.</summary>
    /// <param name="table">The mail table to dump.</param>
    /// <param name="registry">The destination registry, dumped after the rows.</param>
    /// <param name="complaints">
    /// The complaint buckets, dumped after the registry; null writes no team state (the table and registry tests' layout).
    /// Mail Dev default, pending Evan (TD-193 item 6).
    /// </param>
    public MailStateContributor(MailTable table, DestinationRegistry registry, ComplaintBuckets? complaints = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(registry);
        _table = table;
        _registry = registry;
        _complaints = complaints;
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
        // Story 009 appends the deliveries state here, after the team complaint state.
    }
}
