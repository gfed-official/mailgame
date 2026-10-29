using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations.Ports;

/// <summary>
/// The Mail-owned wallet port (ADR-0017 section 8, TR-mail-007). Mail reaches the wallet only through this port and never reads
/// the balance; the Economy implements it (the <c>StubWallet</c> at M-1a) and the composition root injects it into Mail's
/// constructor (<c>SimSystemContext.WalletGate</c>, Mail &amp; Destinations only). Both calls apply immediately and synchronously
/// in the caller's phase, one call per unit, so the next unit's floor check sees the previous unit's debit.
/// </summary>
/// <example>
/// <code>
/// if (!wallet.TryDebitPenalty(penalty, floor)) { /* stoppedAtFloor */ }
/// wallet.Credit(pay, CreditReason.DeliveryPay);
/// </code>
/// </example>
public interface IWalletGate
{
    /// <summary>Debits <paramref name="amount"/> iff <c>balance − amount ≥ floor</c> (equality allowed).</summary>
    /// <param name="amount">The penalty, at least 0.</param>
    /// <param name="floor">The lowest balance allowed after the debit (−500 ¢ at M-1a).</param>
    /// <returns>True when applied; false with nothing changed otherwise.</returns>
    bool TryDebitPenalty(Cents amount, Cents floor);

    /// <summary>Credits <paramref name="amount"/>, applied immediately.</summary>
    /// <param name="amount">The credit, at least 0.</param>
    /// <param name="reason">Why.</param>
    void Credit(Cents amount, CreditReason reason);
}
