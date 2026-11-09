using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Run.Events;
using PerformativeMail.Sim.World;
using ClientLayerShell = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 010 (ADR-0016 sections 1 and 3): the client mirror parts fed through <see cref="MirrorDrainStep"/> from a
/// recorded host stream, and the client facts constructors against the host's. One world per test over the real Inventory, Mail and the
/// shift stub on the M-1a street (Prep 6 ticks, Delivery 1 s), plus a phase-0 probe op that credits the wallet.
/// </summary>
public class ClientMirrorFactsTests
{
    // ---- AC-1 ----

    [Theory]
    [InlineData(0b01UL)] // District 1 unlocked: CLOSED, OPEN, CLOSED
    [InlineData(0b10UL)] // District 1 locked: CLOSED, LOCKED, CLOSED
    public void Ac1_MirrorsFedThroughMirrorDrain_ReportTheHostStateWalletAndRunPhaseAtEveryTick(ulong mask)
    {
        // Arrange
        var rig = new Rig(mask);
        var inbox = new ClientInbox();
        var drain = new MirrorDrainStep(inbox);
        var destinations = new DestinationMirror(rig.World, rig.Host.Content);
        var wallet = new WalletMirror();
        var run = new RunMirror();
        var builder = new ClientLayerBuilder(new NoClock());
        drain.BindTo(builder);
        builder.BindMirrorPart(DestinationMirror.Kinds, destinations);
        builder.BindMirrorPart(WalletMirror.Kinds, wallet);
        builder.BindMirrorPart(RunMirror.Kinds, run);
        ClientLayerShell client = builder.Build();
        var credits = new Dictionary<uint, long> { [3] = 125, [20] = 40 };
        var states = new HashSet<DestState>();

        // Act + Assert: after host tick T the client applies T's batch and agrees with the host.
        for (int t = 0; t < 45; t++)
        {
            uint tick = rig.Host.World.CurrentTick;
            if (credits.TryGetValue(tick, out long cents))
            {
                rig.Credit(cents);
            }
            else
            {
                rig.Host.World.Tick();
            }

            foreach (EventEntry e in rig.TakeBatch())
            {
                Assert.True(inbox.TryAddEntry(tick, in e));
            }

            client.Step(isFirstTickInBatch: true);
            for (int o = 0; o < destinations.Count; o++)
            {
                Assert.True(rig.Mail.TryGetDestinationFacts(destinations.DestinationAt(o), out DestinationFacts host));
                Assert.Equal(host.State, destinations.StateAt(o));
                states.Add(host.State);
            }

            Assert.Equal(rig.Wallet.Balance.Value, wallet.BalanceCents);
            Assert.Equal((rig.Stub.Phase, rig.Stub.DeadlineTick, rig.Stub.CloseTick), (run.Phase, run.DeadlineTick, run.CloseTick));
        }

        // The stream really went through every state the theory row names, and both credits.
        DestState middle = mask == 0b01UL ? DestState.Open : DestState.Locked;
        Assert.Equal(new[] { DestState.Closed, middle }.OrderBy(s => s), states.OrderBy(s => s));
        Assert.Equal(rig.Host.Content.Settings.StartingWalletCents + 165L, wallet.BalanceCents);
        Assert.Equal(ShiftPhase.Closed, run.Phase);
    }

    // ---- AC-2 ----

    [Fact]
    public void Ac2_ClientAndHostFactsConstructors_AgreeOnEveryStateKindAndAddressCase_AndSoDoesEvaluate()
    {
        // Arrange: the host registry and the client mirror over the same world; deliveries closed at tick 100, grace from the balance.
        var rig = new Rig(0b01UL);
        DestinationRegistry registry = rig.Mail.Registry;
        var mirror = new DestinationMirror(rig.World, rig.Host.Content);
        int grace = rig.Host.Content.Mail.CloseGraceTicks;
        const uint CloseTick = 100;
        var o = new DestinationOrdinal(0);
        AddressId own = registry.AddressOf(o);
        AddressId other = registry.AddressOf(new DestinationOrdinal(1));
        DefIndex letter = rig.KindOf("letter");
        DefIndex unaccepted = DefIndex.FromValue(63);

        // (open, mask, actionStart, apply): OPEN, CLOSED, CLOSED within the grace (effective OPEN), CLOSED past the grace, LOCKED.
        var states = new (bool Open, ulong Mask, uint? Start, uint Apply)[]
        {
            (true, 0b01UL, null, 50),
            (false, 0b01UL, null, 120),
            (false, 0b01UL, CloseTick - 1, CloseTick + (uint)grace),
            (false, 0b01UL, CloseTick - 1, CloseTick + (uint)grace + 1),
            (true, 0b10UL, null, 50),
        };
        var results = new HashSet<(EvalResult, MailRejectReason)>();

        foreach ((bool open, ulong mask, uint? start, uint apply) in states)
        {
            registry.Deliveries.Set(open, CloseTick, mask);
            mirror.Apply(Entry(new DeliveriesStatePayload { CloseTick = CloseTick, UnlockedMask = mask, Open = open ? (byte)1 : (byte)0 }), default);
            foreach (DefIndex kind in new[] { letter, unaccepted })
            {
                foreach (AddressId address in new[] { own, other })
                {
                    var row = new MailRow(new MailId(7), kind, address, spawnValue: 9, spawnShift: 1, deadlineShift: 2, MailFlags.Cursed);
                    var entry = new PanelEntryFacts(
                        EntryId.Real(3), kind.Value, ItemCategory.Mail, address, (byte)MailFlags.Cursed, 1, new[] { new PanelHintRun(2, 1, 9) });

                    // Act
                    DestinationFacts hostDest = MailHostFacts.Destination(registry, o, start, apply, grace);
                    UnitFacts hostUnit = MailHostFacts.Unit(in row);
                    Assert.True(MailClientFacts.TryDestination(mirror, registry.EntityIdOf(o), start, apply, grace, out DestinationFacts clientDest));
                    UnitFacts clientUnit = MailClientFacts.Unit(in entry, 0);

                    // Assert
                    Assert.Equal(hostDest, clientDest);
                    Assert.Equal(hostUnit, clientUnit);
                    DeliverEval hostEval = MailRules.Evaluate(in hostDest, in hostUnit);
                    Assert.Equal(hostEval, MailRules.Evaluate(in clientDest, in clientUnit));
                    results.Add((hostEval.Result, hostEval.Reason));
                }
            }
        }

        // The fixture reaches every Evaluate outcome: Accept, Misdeliver and the three reject reasons.
        Assert.Equal(5, results.Count);
    }

    private static EventEntry Entry(DeliveriesStatePayload payload)
    {
        var e = new EventEntry { Kind = RunEventKinds.DeliveriesState, PayloadLength = (byte)Marshal.SizeOf<DeliveriesStatePayload>() };
        MemoryMarshal.Write(e.Payload, in payload);
        return e;
    }

    private sealed class NoClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => false;
    }

    private sealed class Rig : ISimSystem, IOpHandler, IEventSink
    {
        private static readonly ContentSet _content = BuiltInContent.CreateM1a();
        private static readonly OpKind _probeOp = (OpKind)0x0100;
        private readonly List<EventEntry> _batch = new();
        private long _credit;
        private uint _req = 1;
        private MailSystem? _mail;
        private ShiftStub? _stub;

        public Rig(ulong mask)
        {
            var settings = new ShiftStubSettings { PrepTicks = 6, DeliverySeconds = 1, UnlockedMask = mask };
            SimProfile profile = SimProfile.ForTest(
                "md010",
                null,
                new SimSystemEntry(SystemId.GridInventory, ctx => new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
                new SimSystemEntry(SystemId.MailDestinations, ctx =>
                {
                    World = ctx.World!;
                    return _mail = new MailSystem(
                        ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), ctx.WalletGate, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots);
                }),
                new SimSystemEntry(SystemId.ManualDelivery, _ => this),
                new SimSystemEntry(SystemId.RunStructure, ctx => _stub = new ShiftStub(ctx.Api<IMailApi>(SystemId.MailDestinations), ctx.RunControl, ctx.Events, settings)));
            Host = SimComposition.Build(new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance, EventSink = this });
        }

        public SimHost Host { get; }

        public IWorldQuery World { get; private set; } = null!;

        public MailSystem Mail => _mail!;

        public ShiftStub Stub => _stub!;

        public StubWallet Wallet => Host.Wallet!;

        public OpKind Kind => _probeOp;

        public void Bind(SimWorldBuilder builder) => builder.BindOp(OpDescriptor.Empty(_probeOp, true, 0, 0), this);

        public Reply Apply(in Op op, in TickContext ctx)
        {
            var sink = default(SimCounterSink);
            Wallet.Credit(Cents.FromCents(_credit, ref sink), CreditReason.DeliveryPay);
            return Reply.Ok(in op);
        }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            foreach (EventEntry e in entries)
            {
                if (e.Class != ReplicationClass.Internal)
                {
                    _batch.Add(e);
                }
            }
        }

        /// <summary>Runs one tick whose phase 0 credits <paramref name="cents"/>.</summary>
        public void Credit(long cents)
        {
            _credit = cents;
            var op = new Op { Kind = _probeOp, ReqId = _req++ };
            Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
            Host.World.Tick();
        }

        /// <summary>The replicated entries of the last tick, then cleared.</summary>
        public EventEntry[] TakeBatch()
        {
            EventEntry[] batch = _batch.ToArray();
            _batch.Clear();
            return batch;
        }

        public DefIndex KindOf(string id)
        {
            ReadOnlySpan<MailKindDef> kinds = _content.MailKinds.AsSpan();
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i].Id == id)
                {
                    return DefIndex.FromValue((ushort)i);
                }
            }

            throw new ArgumentException(id);
        }
    }
}
