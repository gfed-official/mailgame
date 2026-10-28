namespace PerformativeMail.Sim.MailDestinations.Ports;

/// <summary>
/// Why Mail credits the wallet (ADR-0017 section 8). Mail owns 0 to 31; the Economy defines its own reasons elsewhere. A penalty
/// debit carries <see cref="None"/> in <c>WalletChanged</c>. Values are append-only.
/// </summary>
/// <example><code>wallet.Credit(pay, CreditReason.DeliveryPay);</code></example>
public enum CreditReason : byte
{
    /// <summary>No reason: the reason a penalty debit carries.</summary>
    None = 0,

    /// <summary>Pay for one accepted unit (ADR-0017 section 5, Accept).</summary>
    DeliveryPay = 1,

    // 2 to 31 are reserved for Mail.
}
