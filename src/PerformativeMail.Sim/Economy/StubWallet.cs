using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Economy;

/// <summary>
/// The M-1a wallet (ADR-0017 section 8; mail-destinations story 005): a <see cref="Cents"/> balance starting at
/// <c>SimSettings.StartingWalletCents</c>, behind the Mail-owned <see cref="IWalletGate"/>. Each applied call raises exactly one
/// <see cref="EconEventKinds.WalletChanged"/> to the connected slots; a refused debit raises none. It is not a <c>[SimSystem]</c> and
/// binds no phase: <c>SimComposition.Build</c> constructs it after the world tables and before the systems, binds it (its event kind
/// and its <see cref="SectionIds.Wallet"/> section, <c>balance i64</c>) and injects it into Mail only. The future Economy replaces it
/// behind the same port. Money arithmetic counts into the world's numerics block (the <see cref="SimCounterSink"/> is taken per
/// call: a ref struct cannot be a field). 0 B per call. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var wallet = new StubWallet(content.Settings.StartingWalletCents, events, slots, counters);
/// wallet.Bind(builder);
/// bool applied = wallet.TryDebitPenalty(penalty, floor);   // inside a phase
/// </code>
/// </example>
public sealed class StubWallet : IWalletGate, ISimStateContributor
{
    private readonly EventLogFacade _events;
    private readonly ISlotView _slots;
    private readonly SimCounters _counters;
    private Cents _balance;

    /// <summary>Creates the wallet.</summary>
    /// <param name="startingWalletCents">The starting balance (<c>sim.startingWalletCents</c>).</param>
    /// <param name="events">The world's event-log writer facade.</param>
    /// <param name="slots">The slot view (the <c>WalletChanged</c> audience is its connected mask).</param>
    /// <param name="counters">The world's counters (money clamps and saturations count into their numerics block).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public StubWallet(int startingWalletCents, EventLogFacade events, ISlotView slots, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(counters);
        _events = events;
        _slots = slots;
        _counters = counters;
        SimCounterSink sink = counters.PrimitiveSink();
        _balance = Cents.FromCents(startingWalletCents, ref sink);
    }

    /// <summary>The current balance (the wallet's own state; Mail never reads it).</summary>
    public Cents Balance => _balance;

    /// <inheritdoc />
    public ushort SectionId => SectionIds.Wallet;

    /// <summary>Binds the <c>WalletChanged</c> kind and the <see cref="SectionIds.Wallet"/> section. Called once, by the composition root.</summary>
    /// <param name="builder">The world's builder, before <c>Freeze</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindEventKind(EconEventDescriptors.WalletChanged);
        builder.BindStateContributor(this);
    }

    /// <inheritdoc />
    public bool TryDebitPenalty(Cents amount, Cents floor)
    {
        SimCounterSink sink = _counters.PrimitiveSink();
        Cents next = Cents.Sub(_balance, amount, ref sink);
        if (next.CompareTo(floor) < 0)
        {
            return false;
        }

        Apply(next, CreditReason.None);
        return true;
    }

    /// <inheritdoc />
    public void Credit(Cents amount, CreditReason reason)
    {
        SimCounterSink sink = _counters.PrimitiveSink();
        Apply(Cents.Add(_balance, amount, ref sink), reason);
    }

    /// <inheritdoc />
    public void Write(ref CanonicalWriter w) => w.WriteI64(_balance.Value);

    private void Apply(Cents next, CreditReason reason)
    {
        // Both balances lie in ±2^62 and a debit or a credit moves one way, so the difference fits a long.
        var payload = new WalletChangedPayload { Balance = next.Value, Delta = next.Value - _balance.Value, Reason = (byte)reason };
        _balance = next;
        _ = _events.TryRaise(EconEventKinds.WalletChanged, _slots.ConnectedMask, in payload);
    }
}
