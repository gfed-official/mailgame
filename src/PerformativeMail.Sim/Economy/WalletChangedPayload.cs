using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Economy;

/// <summary>
/// <see cref="EconEventKinds.WalletChanged"/> (24 B, ADR-0017 section 8: at most 24 B; ADR-0008 padding-free): the balance after the
/// change, the signed change and its reason. A penalty debit carries reason 0 (<c>CreditReason.None</c>) and a negative
/// <see cref="Delta"/>; a credit carries its <c>CreditReason</c>.
/// </summary>
/// <example><code>var p = entry.PayloadAs&lt;WalletChangedPayload&gt;(); long cents = p.Balance;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WalletChangedPayload
{
    /// <summary>The balance after the change, in cents.</summary>
    public long Balance;

    /// <summary>The signed change, in cents.</summary>
    public long Delta;

    /// <summary>The <c>CreditReason</c> value (0 for a penalty debit).</summary>
    public byte Reason;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad1;

    /// <summary>Explicit padding, zero.</summary>
    public uint Pad2;
}
