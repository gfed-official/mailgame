using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Economy;

/// <summary>
/// The Economy's <see cref="EventKind"/> values (ADR-0008 section 3: kinds mirror the <see cref="OpKindBlock"/> ranges, Economy
/// <c>0x0180-0x01BF</c>, ADR-0007 section 4 amendment 2026-10-28; mail-destinations story 005, TD-177 item 3). Append-only, never
/// renumbered or reused.
/// </summary>
/// <example><code>if (entry.Kind == EconEventKinds.WalletChanged) { var p = entry.PayloadAs&lt;WalletChangedPayload&gt;(); }</code></example>
public static class EconEventKinds
{
    /// <summary><c>WalletChanged</c>: the wallet balance changed (one per applied debit or credit).</summary>
    public const EventKind WalletChanged = (EventKind)0x0180;
}

/// <summary>
/// The descriptors of <see cref="EconEventKinds"/>, bound with the <see cref="StubWallet"/>. <c>WalletChanged</c> is
/// <see cref="ReplicationClass.Replicated"/> (audience: the connected slots), not coalescible, resync scope 2 (Mail's scope in ADR-0008's
/// order Players 0, Inventory 1, Mail 2: at M-1a the wallet lives with Mail and there is no Economy system), wire codec 0 until
/// story 015 assigns one.
/// </summary>
/// <example><code>builder.BindEventKind(EconEventDescriptors.WalletChanged);</code></example>
public static class EconEventDescriptors
{
    /// <summary>The resync scope bit of <c>WalletChanged</c>.</summary>
    public const byte ResyncScope = 2;

    /// <summary><see cref="EconEventKinds.WalletChanged"/>: 24 B, replicated.</summary>
    public static EventDescriptor WalletChanged { get; } = EventDescriptor.For<WalletChangedPayload>(
        EconEventKinds.WalletChanged, ReplicationClass.Replicated, coalescible: false, ResyncScope, wireCodecId: 0, wireSize: 0);
}
