using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Run.Events;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 015 (ADR-0011 sections 3 and 6, ADR-0017 sections 1, 3 and 9; M-NET-2): the Mail event codecs through the
/// event codec registry over the M-1a street's id maps, and a hand consume over the host loopback session. Fuzzing is dropped (greybox
/// scope): one truncated-input row per codec.
/// </summary>
public class MailEventCodecTests
{
    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    // ---- AC-1 ----

    [Theory]
    [Trait("Id", "M-NET-2")]
    [InlineData(2u, 0u, (byte)1, 3, 24L, 0, 13)]                              // an Accept, small cents
    [InlineData(0x1_0003u, 0xFFFEu, (byte)2, 255, (long)int.MinValue, 2, 19)] // a Misdeliver at maximum values, outcomeSeq past 2^16
    [InlineData(70_000u, 69_990u, (byte)1, 255, (long)int.MaxValue, 0, 17)]   // an Accept at maximum cents
    public void Ac1_DeliveryOutcome_IsAtMost19BytesAtMaximumValues_AndRoundTripsAfterNarrowing(
        uint outcomeSeq, uint lastSeen, byte result, int units, long cents, byte flags, int expectedBytes)
    {
        // Arrange
        (WorldIdMaps maps, IWorldQuery world) = Maps();
        EventCodecRegistry registry = Registry(maps);
        bool misdeliver = result == 2;
        var sim = new DeliveryOutcomePayload
        {
            OutcomeSeq = outcomeSeq, Destination = world.Lots[^1].Destination.Value, Actor = 7, Via = (byte)DeliveryVia.Npc, Result = result,
            Kind = 255, Units = (ushort)units, Cents = cents, LateUnits = (ushort)units, DeadUnits = (ushort)units, ComplaintPermille = ushort.MaxValue,
            Flags = flags, IntendedAddress = misdeliver ? world.Lots[^1].Address.Value : 0u,
        };
        byte[] wire = new byte[64];

        // Act
        IEventCodec codec = registry.CodecFor(MailEventKinds.DeliveryOutcome)!;
        int n = codec.Encode(Entry(MailEventKinds.DeliveryOutcome, sim), wire);
        EventEntry decoded = default;
        bool ok = codec.TryDecode(wire.AsSpan(0, n), ref decoded, out int consumed);

        // Assert
        Assert.Equal(expectedBytes, n);
        Assert.InRange(n, 1, 19);
        Assert.True(ok);
        Assert.Equal(n, consumed);
        DeliveryOutcomePayload back = decoded.PayloadAs<DeliveryOutcomePayload>();
        Assert.Equal(outcomeSeq, MailEventCodecs.ExpandOutcomeSeq(lastSeen, unchecked((ushort)back.OutcomeSeq)));
        Assert.Equal(sim with { OutcomeSeq = 0 }, back with { OutcomeSeq = 0 });
    }

    // ---- AC-2 ----

    [Theory]
    [InlineData("ComplaintRaised")]
    [InlineData("BatchArrived")]
    [InlineData("DeliveriesState")]
    [InlineData("WalletChanged")]
    public void Ac2_OtherEvents_RoundTripThroughTheirRegisteredCodec(string kind)
    {
        // Arrange
        (WorldIdMaps maps, IWorldQuery world) = Maps();
        EventCodecRegistry registry = Registry(maps);
        EventEntry entry = kind switch
        {
            "ComplaintRaised" => Entry(MailEventKinds.ComplaintRaised, new ComplaintRaisedPayload { Destination = world.Lots[3].Destination.Value, AmountPermille = 40_000, Source = (byte)ComplaintSource.Late }),
            "BatchArrived" => Entry(MailEventKinds.BatchArrived, new BatchArrivedPayload { Units = 64 }),
            "DeliveriesState" => Entry(RunEventKinds.DeliveriesState, new DeliveriesStatePayload { CloseTick = 7_236, DeadlineTick = 7_236, UnlockedMask = 0b101, Phase = (byte)ShiftPhase.Delivery, Open = 1 }),
            _ => Entry(EconEventKinds.WalletChanged, new WalletChangedPayload { Balance = -480, Delta = -20, Reason = 0 }),
        };
        byte[] wire = new byte[64];

        // Act
        IEventCodec codec = registry.CodecFor(entry.Kind)!;
        int n = codec.Encode(in entry, wire);
        EventEntry decoded = default;
        bool ok = codec.TryDecode(wire.AsSpan(0, n), ref decoded, out _);

        // Assert
        Assert.True(n > 0);
        Assert.True(ok);
        Assert.Equal(entry.PayloadLength, decoded.PayloadLength);
        Assert.Equal(((ReadOnlySpan<byte>)entry.Payload).ToArray(), ((ReadOnlySpan<byte>)decoded.Payload).ToArray());
    }

    [Fact]
    public void Ac2_DeliveriesStateAndDeliveryOutcome_AreReplicatedNonCoalescibleKindsWithACodec_SoTheyRideTheReliableOrderedBatch()
    {
        // Arrange
        (WorldIdMaps maps, _) = Maps();
        EventCodecRegistry registry = Registry(maps);

        // Act + Assert
        foreach (EventDescriptor d in new[] { RunEventDescriptors.DeliveriesState, MailEventDescriptors.DeliveryOutcome })
        {
            Assert.Equal((ReplicationClass.Replicated, false), (d.Class, d.Coalescible));
            Assert.Equal(d.WireCodecId, registry.CodecFor(d.Kind)!.CodecId);
        }
    }

    // ---- AC-3 ----

    [Fact]
    public void Ac3_AHandConsumeOverLoopback_DeliversItsOutcomesDecodedEqualToTheSimPayloads_InLogOrder_AtTheNextClientTick()
    {
        // Arrange: a right-house letter and a wrong-house letter in slot 0's hotbar.
        var rig = new LoopbackRig();
        rig.RunFrame(); // tick 0: the local join
        Assert.True(rig.Session.IsLocalJoined);

        // Act: the probe op hands both over in tick 1; its batch is applied at the next client tick.
        rig.EnqueueProbe();
        rig.RunFrame();
        uint clientTickAfterApply = rig.Session.Client.ClientTick;
        Assert.Empty(rig.Received);
        rig.RunFrame();

        // Assert
        Assert.Equal(2, rig.Raised.Count);
        Assert.Equal(rig.Raised.Count, rig.Received.Count);
        uint last = 0;
        for (int i = 0; i < rig.Raised.Count; i++)
        {
            (uint clientTick, EventEntry entry) = rig.Received[i];
            Assert.Equal(clientTickAfterApply, clientTick);
            DeliveryOutcomePayload got = entry.PayloadAs<DeliveryOutcomePayload>();
            last = MailEventCodecs.ExpandOutcomeSeq(last, unchecked((ushort)got.OutcomeSeq));
            Assert.Equal(rig.Raised[i], got with { OutcomeSeq = last });
        }

        Assert.True(rig.Received[0].Entry.Ordinal < rig.Received[1].Entry.Ordinal);
        Assert.Equal(new[] { (byte)EvalResult.Accept, (byte)EvalResult.Misdeliver }, rig.Raised.Select(p => p.Result));
    }

    // ---- AC-4 ----

    [Theory]
    [InlineData("DeliveryOutcome")]
    [InlineData("ComplaintRaised")]
    [InlineData("BatchArrived")]
    [InlineData("DeliveriesState")]
    [InlineData("WalletChanged")]
    public void Ac4_EachDecoderReturnsFalseOnTruncatedInput_WithoutThrowing(string kind)
    {
        // Arrange
        (WorldIdMaps maps, IWorldQuery world) = Maps();
        EventCodecRegistry registry = Registry(maps);
        EventEntry entry = kind switch
        {
            "DeliveryOutcome" => Entry(MailEventKinds.DeliveryOutcome, new DeliveryOutcomePayload { OutcomeSeq = 1, Destination = world.Lots[0].Destination.Value, Via = 1, Result = 2, Units = 1, Cents = -4, IntendedAddress = world.Lots[1].Address.Value }),
            "ComplaintRaised" => Entry(MailEventKinds.ComplaintRaised, new ComplaintRaisedPayload { Destination = world.Lots[0].Destination.Value, AmountPermille = 1, Source = 1 }),
            "BatchArrived" => Entry(MailEventKinds.BatchArrived, new BatchArrivedPayload { Units = 3 }),
            "DeliveriesState" => Entry(RunEventKinds.DeliveriesState, new DeliveriesStatePayload { Open = 1 }),
            _ => Entry(EconEventKinds.WalletChanged, new WalletChangedPayload { Balance = 5 }),
        };
        IEventCodec codec = registry.CodecFor(entry.Kind)!;
        byte[] wire = new byte[64];
        int n = codec.Encode(in entry, wire);

        // Act
        EventEntry decoded = default;
        bool ok = codec.TryDecode(wire.AsSpan(0, n - 1), ref decoded, out int consumed);

        // Assert
        Assert.False(ok);
        Assert.Equal(0, consumed);
    }

    private static EventEntry Entry<T>(EventKind kind, T payload)
        where T : unmanaged
    {
        var e = new EventEntry { Kind = kind, PayloadLength = (byte)Marshal.SizeOf<T>() };
        MemoryMarshal.Write(e.Payload, in payload);
        return e;
    }

    private static EventCodecRegistry Registry(WorldIdMaps maps) =>
        new(new[]
        {
            MailEventDescriptors.DeliveryOutcome, MailEventDescriptors.ComplaintRaised, MailEventDescriptors.BatchArrived,
            RunEventDescriptors.DeliveriesState, EconEventDescriptors.WalletChanged,
        }, MailEventCodecs.Create(maps));

    // The M-1a street's world tables and id maps (built the way every peer builds them).
    private static (WorldIdMaps Maps, IWorldQuery World) Maps()
    {
        IWorldQuery? world = null;
        SimProfile profile = SimProfile.ForTest(
            "md015-maps",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx => new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
            new SimSystemEntry(SystemId.MailDestinations, ctx =>
            {
                world = ctx.World;
                return new MailSystem(
                    ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), ctx.WalletGate, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots);
            }));
        _ = SimComposition.Build(new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance });
        return (new WorldIdMaps(world!), world!);
    }

    private sealed class FakeClock : IMonotonicClock
    {
        public long Now { get; set; }

        public long Timestamp => Now;

        public long Frequency => 10_000_000;
    }

    /// <summary>
    /// A <see cref="HostSession"/> over Inventory, Mail and a probe system whose op handler opens deliveries and hands two letters over
    /// (ConsumeHand). It records the Sim's <c>DeliveryOutcome</c> payloads and the decoded entries MirrorDrain applies.
    /// </summary>
    private sealed class LoopbackRig : ISimSystem, IOpHandler, IEventSink, IMirrorConsumer
    {
        private static readonly OpKind _probeOp = (OpKind)0x0100;
        private readonly FakeClock _clock = new();
        private InventorySystem? _inventory;
        private MailSystem? _mail;

        public LoopbackRig()
        {
            (WorldIdMaps maps, _) = Maps();
            SimProfile profile = SimProfile.ForTest(
                "md015-loopback",
                null,
                new SimSystemEntry(SystemId.GridInventory, ctx => _inventory = new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
                new SimSystemEntry(SystemId.MailDestinations, ctx => _mail = new MailSystem(
                    ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), ctx.WalletGate, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots)),
                new SimSystemEntry(SystemId.ManualDelivery, _ => this));
            var args = new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance };
            Session = new HostSession(args, _clock, null, MailEventCodecs.Create(maps));
            Session.AddEventSink(this);
            Session.MirrorDrain.Register(this);
        }

        public HostSession Session { get; }

        public List<DeliveryOutcomePayload> Raised { get; } = new();

        public List<(uint ClientTick, EventEntry Entry)> Received { get; } = new();

        public OpKind Kind => _probeOp;

        public void Bind(SimWorldBuilder builder) => builder.BindOp(OpDescriptor.Empty(_probeOp, true, 0, 0), this);

        public void RunFrame()
        {
            _clock.Now += 333_334;
            Session.RunFrame();
        }

        public void EnqueueProbe()
        {
            var op = new Op { Kind = _probeOp, ReqId = 1 };
            Assert.True(Session.World.EnqueueOp(ActorId.System, in op));
        }

        public Reply Apply(in Op op, in TickContext ctx)
        {
            MailSystem mail = _mail!;
            mail.SetDeliveriesState(true, 0, 1UL);
            EntryRef right = Give(new DestinationOrdinal(0));
            EntryRef wrong = Give(new DestinationOrdinal(1));
            _ = mail.ConsumeHand(mail.Registry.EntityIdOf(new DestinationOrdinal(0)), new[] { right, wrong }, PlayerSlot.FromValue(0), ctx.Tick);
            return Reply.Ok(in op);
        }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            foreach (EventEntry e in entries)
            {
                if (e.Kind == MailEventKinds.DeliveryOutcome)
                {
                    Raised.Add(e.PayloadAs<DeliveryOutcomePayload>());
                }
            }
        }

        public void OnEntry(uint tick, in EventEntry entry)
        {
            if (entry.Kind == MailEventKinds.DeliveryOutcome)
            {
                Received.Add((Session.Client.ClientTick, entry));
            }
        }

        public void OnReply(uint tick, in Reply reply)
        {
        }

        // One letter for registry row `to`, in slot 0's hotbar.
        private EntryRef Give(DestinationOrdinal to)
        {
            InventorySystem inventory = _inventory!;
            DefIndex letter = DefIndex.FromValue((ushort)_content.MailKinds.AsSpan().ToArray().ToList().FindIndex(k => k.Id == "letter"));
            AddressId address = _mail!.Registry.AddressOf(to);
            ContainerId hotbar = inventory.HotbarOf(PlayerSlot.FromValue(0));
            ActorId actor = ActorId.FromEntity(Session.Host.Services.Slots.PlayerOf(PlayerSlot.FromValue(0)));
            MailId id = _mail.Table.Create(letter, address, 8, 1, MailFlags.None);
            Assert.Equal(1, inventory.Spawn(inventory.IntakeContainer, letter, address.Value, 0, new[] { id }, MergePolicy.NoMerge).Placed);
            Assert.True(inventory.TakeUnit(inventory.IntakeContainer, Selector.Fifo, ActorId.System, out UnitRef u));
            Assert.True(inventory.TryInsert(hotbar, in u, actor));
            return new EntryRef(hotbar, EntryId.Real(inventory.Entries.LastId));
        }
    }
}
