using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The Sim <see cref="ICatchVolumeSource"/>: reads Mail's registry columns through the three read-only <see cref="IMailApi"/>
/// members (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-199 item 1)). Throwing already holds
/// <see cref="IMailApi"/> (a declared Throwing → Mail edge), so nothing new is injected. Allocation-free; Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var builder = new ReceiverViewBuilder(world, new MailCatchVolumeSource(mail), counters);
/// </code>
/// </example>
public sealed class MailCatchVolumeSource : ICatchVolumeSource
{
    private readonly IMailApi _mail;

    /// <summary>Creates the adapter.</summary>
    /// <param name="mail">Mail &amp; Destinations' API.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mail"/> is null.</exception>
    public MailCatchVolumeSource(IMailApi mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        _mail = mail;
    }

    /// <inheritdoc />
    public int Count => _mail.DestinationCount;

    /// <inheritdoc />
    public EntityId DestinationAt(int ordinal) => _mail.DestinationAt(ordinal);

    /// <inheritdoc />
    public AabbMm CatchVolumeAt(int ordinal) => _mail.CatchVolumeAt(ordinal);
}
