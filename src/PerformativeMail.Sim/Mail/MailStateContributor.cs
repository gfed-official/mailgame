using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail's canonical dump section, <see cref="SectionIds.Mail"/> (ADR-0006 section 4, ADR-0017 section 2). Layout: the mail
/// table rows (<see cref="MailTable.WriteCanonicalRows"/>: <c>u32 liveCount</c>, then live rows in ascending <c>mailId</c>,
/// each <c>mailId u32, kind u16, addressId u32, spawnValue u16, spawnShift u8, deadlineShift u8, flags u8</c>).
/// Then <see cref="DestinationRegistry.WriteCanonical"/> (story 002: <c>u16 destinationCount</c> and the per-destination columns). Later stories only append; never reorder what is here.
/// </summary>
internal sealed class MailStateContributor : ISimStateContributor
{
    private readonly MailTable _table;
    private readonly DestinationRegistry _registry;

    /// <summary>Creates the contributor for one table and its destination registry.</summary>
    /// <param name="table">The mail table to dump.</param>
    /// <param name="registry">The destination registry, dumped after the rows.</param>
    public MailStateContributor(MailTable table, DestinationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(registry);
        _table = table;
        _registry = registry;
    }

    /// <inheritdoc />
    public ushort SectionId => SectionIds.Mail;

    /// <inheritdoc />
    public void Write(ref CanonicalWriter w)
    {
        _table.WriteCanonicalRows(ref w);

        // Story 002: the destination registry's columns, after the mail rows (layout in DestinationRegistry.WriteCanonical).
        _registry.WriteCanonical(ref w);
        // Story 009 appends the deliveries state here, after the registry.
    }
}
