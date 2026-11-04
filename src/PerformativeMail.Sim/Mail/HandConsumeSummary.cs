namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The receipt <see cref="IMailApi.ConsumeHand"/> returns (ADR-0017 Key Interfaces erratum 2026-11-04). TD-208 item 3 (A3), approved by
/// Evan 2026-11-04 6:47 AM PT by card click (card t265s1214, listing t265s1213): "Approve A3 (TD-208 item 3): delivery returns the receipt
/// with an ADR-0017 erratum, as listed in your message above"; as listed: the number of stacks handed over (right or wrong house),
/// letters used, the first refusal reason, whether the −500 ¢ wallet floor stopped a wrong delivery, and the net change in cents.
/// The type, its member names and widths are uncredited (Mail Lead planning default, TD-208 item 3). Delivery's handler builds its
/// reply from it.
/// </summary>
/// <param name="EntriesGot">Entries that had at least one unit consumed (Accept or Misdeliver).</param>
/// <param name="UnitsConsumed">Units consumed over the call.</param>
/// <param name="FirstReject">The first Reject's reason, in scan order; <see cref="MailRejectReason.None"/> when nothing was rejected.</param>
/// <param name="StoppedAtFloor">True when the wallet floor refused a misdelivery penalty.</param>
/// <param name="CentsDelta">Income minus penalties, cents (a penalty that stays after a refused removal included).</param>
/// <example><code>HandConsumeSummary r = mail.ConsumeHand(dest, entries, actor, holdStartTick);   // 4 one-letter stacks on time: (4, 4, None, false, 32)</code></example>
public readonly record struct HandConsumeSummary(
    ushort EntriesGot, ushort UnitsConsumed, MailRejectReason FirstReject, bool StoppedAtFloor, long CentsDelta);
