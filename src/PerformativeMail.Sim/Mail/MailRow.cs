using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// An immutable copy of one live mail table row (ADR-0017 section 2). It has no setters: a row's <see cref="SpawnValue"/>
/// is fixed at creation (GDD mail-destinations R2), so nothing, a later StatSheet change included, can reach it. There is no
/// location field; where a unit is lives in Inventory's <c>UnitLedger</c>.
/// </summary>
/// <example>
/// <code>
/// if (table.TryGetRow(id, out MailRow row)) { ushort cents = row.SpawnValue; }
/// </code>
/// </example>
internal readonly struct MailRow
{
    /// <summary>Creates a row copy; used by <see cref="MailTable"/>.</summary>
    /// <param name="id">The mail id.</param>
    /// <param name="kind">The mail kind def.</param>
    /// <param name="address">The packed destination address.</param>
    /// <param name="spawnValue">The value in cents fixed at creation.</param>
    /// <param name="spawnShift">The shift the row was created in.</param>
    /// <param name="deadlineShift">The shift the row is due by.</param>
    /// <param name="flags">The row flags.</param>
    public MailRow(MailId id, DefIndex kind, AddressId address, ushort spawnValue, byte spawnShift, byte deadlineShift, MailFlags flags)
    {
        Id = id;
        Kind = kind;
        Address = address;
        SpawnValue = spawnValue;
        SpawnShift = spawnShift;
        DeadlineShift = deadlineShift;
        Flags = flags;
    }

    /// <summary>The row's mail id (lower = older).</summary>
    public MailId Id { get; }

    /// <summary>The mail kind def.</summary>
    public DefIndex Kind { get; }

    /// <summary>The packed destination address.</summary>
    public AddressId Address { get; }

    /// <summary>The value in cents, fixed at creation and never changed.</summary>
    public ushort SpawnValue { get; }

    /// <summary>The shift the row was created in.</summary>
    public byte SpawnShift { get; }

    /// <summary>The shift the row is due by (equal to <see cref="SpawnShift"/> while every v1 <c>deadlineOffsetShifts</c> is 0).</summary>
    public byte DeadlineShift { get; }

    /// <summary>The row flags.</summary>
    public MailFlags Flags { get; }
}
