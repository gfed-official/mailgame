namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// What <see cref="IMailApi.ResolveThrownLanding"/> returns to Throwing (ADR-0017 Key Interfaces; ADR-0015 <c>MAILBOX_*</c> rows;
/// mail-destinations story 008): a <see cref="LandingKind.Rejected"/> entry, or any <see cref="UnitsRemaining"/> (the wallet-floor
/// remainder), becomes one WorldItem that Throwing creates. The member set is the story's; widths and the <see cref="Reason"/> name are
/// uncredited implementation detail.
/// </summary>
/// <param name="Kind">Accepted, misdelivered or rejected.</param>
/// <param name="UnitsConsumed">Units Mail consumed.</param>
/// <param name="UnitsRemaining">Units still on the flight's entry.</param>
/// <param name="StoppedAtFloor">True when the wallet floor refused a misdelivery penalty (<c>wallet_floor</c>).</param>
/// <param name="Reason">The Reject reason; <see cref="MailRejectReason.None"/> otherwise, and for a landing Mail could not resolve.</param>
/// <example><code>LandingResult r = mail.ResolveThrownLanding(dest, in flying, thrower, thrownPm, releaseTick);   // wrong house, −440 ¢, 20 × 11 ¢: (Misdelivered, 5, 15, true, None)</code></example>
public readonly record struct LandingResult(
    LandingKind Kind, byte UnitsConsumed, byte UnitsRemaining, bool StoppedAtFloor, MailRejectReason Reason);
