using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 005 AC-1 (M-R7-2c, FX-W; ADR-0017 section 8, ADR-0008): the M-1a <see cref="StubWallet"/> behind
/// <see cref="IWalletGate"/>. Each applied call raises exactly one <c>WalletChanged</c> through the world's event facade; a refused
/// debit changes nothing and raises nothing. The calls run inside a phase of a real world so the raises reach the log.
/// </summary>
[Trait("Id", "M-R7-2c")]
public class StubWalletTests
{
    private sealed class ScriptPhase : ISimPhase
    {
        private readonly Action _body;

        public ScriptPhase(Action body)
        {
            _body = body;
        }

        public void Run(in TickContext ctx) => _body();
    }

    [Fact]
    public void FromZero_DebitsToTheFloor_RefusesBelowIt_ThenCredits_RaisingOneWalletChangedPerAppliedCall()
    {
        // Arrange: starting wallet 0, floor -500 (a Cents argument; there is no walletFloor key).
        var counters = new SimCounters();
        var events = new EventLogFacade(counters);
        var wallet = new StubWallet(startingWalletCents: 0, events, new SlotTable(), counters);
        SimCounterSink sink = counters.PrimitiveSink();
        Cents floor = Cents.FromCents(-500, ref sink);
        Cents four = Cents.FromCents(4, ref sink);
        Cents one = Cents.FromCents(1, ref sink);
        Cents nine = Cents.FromCents(9, ref sink);
        var applied = new List<bool>();
        var balances = new List<long>();
        bool refused = true;
        long balanceAfterRefusal = 0;
        var builder = new SimWorldBuilder(new SimCoreSettings(), counters, new SimCheckRing());
        builder.Bind(PhaseId.PlayersStep, new ScriptPhase(() =>
        {
            for (int i = 0; i < 125; i++)
            {
                applied.Add(wallet.TryDebitPenalty(four, floor)); // 124 calls reach -496, the 125th lands exactly on -500
                balances.Add(wallet.Balance.Value);
            }

            refused = !wallet.TryDebitPenalty(one, floor);
            balanceAfterRefusal = wallet.Balance.Value;
            wallet.Credit(nine, CreditReason.DeliveryPay);
        }));
        wallet.Bind(builder);
        var capture = new CaptureSink();
        builder.BindEventSink(capture);
        SimWorld world = builder.Freeze();
        events.Attach(world.Events);

        // Act
        world.Tick();

        // Assert: balances after each debit, the refusal and the credit.
        Assert.All(applied, Assert.True);
        Assert.Equal(Enumerable.Range(1, 125).Select(n => -4L * n), balances);
        Assert.Equal(-496L, balances[123]);
        Assert.True(refused);
        Assert.Equal(-500L, balanceAfterRefusal);
        Assert.Equal(-491L, wallet.Balance.Value);

        // One WalletChanged per applied call (125 debits + 1 credit), none for the refusal.
        WalletChangedPayload[] raised = capture.Last
            .Where(e => e.Kind == EconEventKinds.WalletChanged)
            .Select(e =>
            {
                Assert.Equal(ReplicationClass.Replicated, e.Class);
                Assert.Equal(24, e.PayloadLength);
                return e.PayloadAs<WalletChangedPayload>();
            })
            .ToArray();
        Assert.Equal(126, raised.Length);
        AssertChanged(raised[0], -4, -4, CreditReason.None);
        AssertChanged(raised[123], -496, -4, CreditReason.None);
        AssertChanged(raised[124], -500, -4, CreditReason.None);
        AssertChanged(raised[125], -491, 9, CreditReason.DeliveryPay);

        // The payload: long Balance, long Delta, byte Reason, 7 explicit pad bytes = 24 B with no implicit padding.
        Assert.Equal(24, Unsafe.SizeOf<WalletChangedPayload>());
        Assert.All(raised, p => Assert.True(p.Pad0 == 0 && p.Pad1 == 0 && p.Pad2 == 0));
    }

    private static void AssertChanged(in WalletChangedPayload p, long balance, long delta, CreditReason reason)
    {
        Assert.Equal(balance, p.Balance);
        Assert.Equal(delta, p.Delta);
        Assert.Equal((byte)reason, p.Reason);
    }
}
