using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Run.Events;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 009 (ADR-0017 section 3, ADR-0003 sections 1 and 5): the M-1a shift stub drives Mail's deliveries state
/// from phase 12 of tick T − 1, so it is effective from T's phase 0. One world per test over the real Inventory, Mail and the stub on
/// the 10-lot M-1a street, a short shift (Prep 6 ticks, Delivery 1 s = 30 ticks: Delivery runs ticks 6 to 35, closed from 36), and a
/// phase-0 probe op that runs a body (a hand-over included) in a real tick.
/// </summary>
public class ShiftStubDeliveriesStateTests
{
    private const int PrepTicks = 6;
    private const uint DeliveryStart = 6;
    private const uint CloseTick = 36;

    // ---- AC-1 ----

    [Fact]
    [Trait("Id", "M-R4-1a")]
    public void Ac1_PrepDeliveryClosed_District1IsClosedOpenClosed_AndDistrict2IsLockedOnlyWhileOpen()
    {
        // Arrange
        var rig = new Rig(PrepTicks, mask: 0b01);
        DeliveriesState store = rig.Mail.Registry.Deliveries;

        // Act + Assert: after tick T-1 the store holds the flag tick T's phase 0 will see.
        for (uint t = rig.Host.World.CurrentTick; t < 45; t++)
        {
            Assert.Equal(t, rig.Host.World.CurrentTick);
            bool open = t >= DeliveryStart && t < CloseTick;
            Assert.True(rig.Mail.TryGetDestinationFacts(rig.Dest(0), out DestinationFacts d1));
            Assert.Equal(open ? DestState.Open : DestState.Closed, d1.State);
            DestState d2 = DeliveriesStateRules.Effective(store.IsDistrictUnlocked(2), store.Open, store.CloseTick, null, t, 0);
            Assert.Equal(open ? DestState.Locked : DestState.Closed, d2);
            Assert.True(store.IsDistrictUnlocked(1));
            rig.Host.World.Tick();
        }
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "M-R4-2a")]
    [Trait("Id", "M-R4-2b")]
    [Trait("Id", "M-R4-2f")]
    public void Ac2_AHandOverAppliedAtTheLastOpenTickIsAccepted_AndOneStartedAtTheCloseTickIsClosed()
    {
        // Arrange: one single-letter entry per accepted hand-over (an accept takes the whole entry) and one kept for the closed rejects.
        var rig = new Rig(PrepTicks);
        EntryRef atStartEntry = default;
        EntryRef lastOpenEntry = default;
        EntryRef kept = default;
        rig.Run(() =>
        {
            atStartEntry = rig.Give(0, "postcard");
            lastOpenEntry = rig.Give(0, "letter");
            kept = rig.Give(0, "small");
        });

        // Act: hand over in phase 0 of S-1, S, closeTick-1 and closeTick.
        MailRejectReason beforeStart = rig.HandAt(DeliveryStart - 1, kept);
        MailRejectReason atStart = rig.HandAt(DeliveryStart, atStartEntry);
        MailRejectReason lastOpen = rig.HandAt(CloseTick - 1, lastOpenEntry);
        MailRejectReason atClose = rig.HandAt(CloseTick, kept);

        // Assert
        Assert.Equal(
            new[] { MailRejectReason.Closed, MailRejectReason.None, MailRejectReason.None, MailRejectReason.Closed },
            new[] { beforeStart, atStart, lastOpen, atClose });
        Assert.True(rig.Inventory.TryReadEntry(kept, out EntryView v));
        Assert.Equal(1, v.Units);
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "M-R4-3")]
    public void Ac3_OneDeliveriesStateEventPerChange_NoneOnOtherTicks_WithTheDeadlineSetAtDeliveryStart()
    {
        // Arrange
        var rig = new Rig(PrepTicks);

        // Act
        for (int t = 0; t < 60; t++)
        {
            rig.Host.World.Tick();
        }

        // Assert: raised in S-1 (Delivery) and closeTick-1 (Closed) and in no other tick.
        Assert.Equal(new uint[] { DeliveryStart - 1, CloseTick - 1 }, rig.Raised.Select(x => x.Tick).ToArray());
        DeliveriesStatePayload start = rig.Raised[0].Payload;
        Assert.Equal((CloseTick, CloseTick, 1UL, (byte)ShiftPhase.Delivery, (byte)1), (start.CloseTick, start.DeadlineTick, start.UnlockedMask, start.Phase, start.Open));
        DeliveriesStatePayload end = rig.Raised[1].Payload;
        Assert.Equal((CloseTick, CloseTick, 1UL, (byte)ShiftPhase.Closed, (byte)0), (end.CloseTick, end.DeadlineTick, end.UnlockedMask, end.Phase, end.Open));
    }

    // ---- AC-4 ----

    [Fact]
    [Trait("Id", "M-R4-4")]
    public void Ac4_TwoIdenticalRuns_GiveIdenticalDumps_AndTheStubStateIsInTheDump()
    {
        // Arrange + Act
        var a = new Rig(PrepTicks);
        var b = new Rig(PrepTicks);
        var differentPrep = new Rig(PrepTicks + 1);
        for (int t = 0; t < 40; t++)
        {
            a.Host.World.Tick();
            b.Host.World.Tick();
            differentPrep.Host.World.Tick();
            if (t == 3)
            {
                // In Prep only the stub's phaseEndTick differs: Mail's state is the same in all three worlds.
                Assert.NotEqual(DumpHash.Sha256(a.Host.World), DumpHash.Sha256(differentPrep.Host.World));
            }
        }

        // Assert
        Assert.Equal(DumpHash.Bytes(a.Host.World), DumpHash.Bytes(b.Host.World));
    }

    private sealed class Rig : ISimSystem, IOpHandler
    {
        private static readonly ContentSet _content = BuiltInContent.CreateM1a();
        private static readonly OpKind _probeOp = (OpKind)0x0100;
        private Action? _next;
        private uint _req = 1;
        private MailSystem? _mail;
        private InventorySystem? _inventory;

        public Rig(int prepTicks, ulong mask = 1UL)
        {
            var settings = new ShiftStubSettings { PrepTicks = prepTicks, DeliverySeconds = 1, UnlockedMask = mask };
            SimProfile profile = SimProfile.ForTest(
                "md009",
                null,
                new SimSystemEntry(SystemId.GridInventory, ctx => _inventory = new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
                new SimSystemEntry(SystemId.MailDestinations, ctx => _mail = new MailSystem(
                    ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), ctx.WalletGate, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots)),
                new SimSystemEntry(SystemId.ManualDelivery, _ => this),
                new SimSystemEntry(SystemId.RunStructure, ctx => new ShiftStub(ctx.Api<IMailApi>(SystemId.MailDestinations), ctx.RunControl, ctx.Events, settings)));
            Sink = new Capture(this);
            Host = SimComposition.Build(new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance, EventSink = Sink });
            var join = new Op { Kind = OpKind.PlayerJoin, ReqId = _req++ };
            join.SetPayload(new PlayerJoinPayload { Slot = 0 });
            Assert.True(Host.World.EnqueueOp(ActorId.System, in join));
            Host.World.Tick();
        }

        public SimHost Host { get; }

        public MailSystem Mail => _mail!;

        public InventorySystem Inventory => _inventory!;

        public Capture Sink { get; }

        public List<(uint Tick, DeliveriesStatePayload Payload)> Raised { get; } = new();

        public OpKind Kind => _probeOp;

        public void Bind(SimWorldBuilder builder) => builder.BindOp(OpDescriptor.Empty(_probeOp, true, 0, 0), this);

        public Reply Apply(in Op op, in TickContext ctx)
        {
            _next?.Invoke();
            return Reply.Ok(in op);
        }

        public EntityId Dest(int ordinal) => Mail.Registry.EntityIdOf(new DestinationOrdinal((ushort)ordinal));

        /// <summary>Ticks until <paramref name="body"/> has run once, in phase 0 of the next tick.</summary>
        public void Run(Action body)
        {
            _next = body;
            var op = new Op { Kind = _probeOp, ReqId = _req++ };
            Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
            Host.World.Tick();
            _next = null;
        }

        /// <summary>In a tick: one unit of <paramref name="kind"/> for registry row 0, in slot 0's hotbar.</summary>
        public EntryRef Give(byte slot, string kind)
        {
            DefIndex k = KindOf(kind);
            ushort value = (ushort)_content.MailKinds[k].BaseValueCents;
            AddressId address = Mail.Registry.AddressOf(new DestinationOrdinal(0));
            ContainerId hotbar = Inventory.HotbarOf(PlayerSlot.FromValue(slot));
            ActorId actor = ActorId.FromEntity(Host.Services.Slots.PlayerOf(PlayerSlot.FromValue(slot)));
            MailId id = Mail.Table.Create(k, address, value, (byte)Mail.CurrentShift, MailFlags.None);
            Assert.Equal(1, Inventory.Spawn(Inventory.IntakeContainer, k, address.Value, 0, new[] { id }, MergePolicy.NoMerge).Placed);
            Assert.True(Inventory.TakeUnit(Inventory.IntakeContainer, Selector.Fifo, ActorId.System, out UnitRef u));
            Assert.True(Inventory.TryInsert(hotbar, in u, actor));
            return new EntryRef(hotbar, EntryId.Real(Inventory.Entries.LastId));
        }

        /// <summary>Ticks to <paramref name="tick"/>, then hands one unit of <paramref name="e"/> over in that tick's phase 0 (started that tick).</summary>
        public MailRejectReason HandAt(uint tick, EntryRef e)
        {
            while (Host.World.CurrentTick < tick)
            {
                Host.World.Tick();
            }

            Assert.Equal(tick, Host.World.CurrentTick);
            MailRejectReason reason = MailRejectReason.None;
            Run(() => reason = Mail.ConsumeHand(Dest(0), new[] { e }, PlayerSlot.FromValue(0), tick).FirstReject);
            return reason;
        }

        private static DefIndex KindOf(string id)
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

    private sealed class Capture : IEventSink
    {
        private readonly Rig _rig;

        public Capture(Rig rig)
        {
            _rig = rig;
        }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            foreach (EventEntry x in entries)
            {
                if (x.Kind == RunEventKinds.DeliveriesState)
                {
                    _rig.Raised.Add((tick, x.PayloadAs<DeliveriesStatePayload>()));
                }
            }
        }
    }
}
