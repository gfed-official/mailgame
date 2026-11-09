using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Presentation.Delivery;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Delivery.Client;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using EventCursor = PerformativeMail.Client.Core.Presentation.EventCursor;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 006 (GDD MD7, M-EC-1; ADR-0016 section 5): <see cref="HandBeatController"/> driven through a
/// <see cref="PresentationEventStream"/> fed the batches a hold produces (its <c>DeliveryOutcome</c>s, then the own reply), and the
/// <see cref="ReachEcho"/> gate. Timeout/rejoin and dead/frozen rows are out of the greybox (Mail Lead regroup 2026-11-08).
/// </summary>
public class HandBeatControllerTests
{
    private const uint Completed = 100;
    private static PlayerSlot Self => PlayerSlot.FromValue(0);
    private static PlayerSlot Other => PlayerSlot.FromValue(1);
    private static EntityId House => new(0x0100_0007);

    [Theory]
    [Trait("Id", "M-MD7-2")]
    [Trait("Id", "M-EC-1")]
    [InlineData("own hold, 3 outcomes", true, 3, HandBeat.SlotInRiffle, 3, 180)]
    [InlineData("own hold, 26 entries (M-EC-1)", true, 26, HandBeat.SlotInRiffle, 5, 300)]
    [InlineData("other player's hold, 3 outcomes", false, 3, HandBeat.SlotInRiffle, 3, 180)]
    public void Hold_WithKOutcomes_FiresOneHandBeat(string row, bool own, int k, HandBeat beat, int stacks, int riffleMs)
    {
        // Arrange
        var rig = new Rig();
        rig.Hand.OnHoldCompleted(Completed, House);

        // Act
        rig.Batch(Completed + 3, own ? Self : Other, k, reply: own ? OkReply(k) : null);
        rig.Hand.Update(Completed + 4);
        int firstUpdate = rig.Hand.Started.Count;
        HandBeatStart started = firstUpdate == 1 ? rig.Hand.Started[0] : default;
        rig.Hand.Update(Completed + 5);

        // Assert
        Assert.True(firstUpdate == 1, row + ": " + firstUpdate + " beats");
        Assert.Empty(rig.Hand.Started);
        Assert.Equal((own ? Self : Other, House, Completed + 3, beat), (started.Actor, started.Destination, started.ApplyTick, started.Response.Beat));
        Assert.Equal(new RiffleTiming(stacks, riffleMs), started.Riffle);
    }

    [Fact]
    [Trait("Id", "M-MD7-2")]
    public void NoReply_HandIsNotIdleAtTick29AfterCompletion_IsIdleAtTick30WhilePromptKeepsDelivering()
    {
        // Arrange: the completed hold's deliver_matching stays in the pending queue (no reply), which the hand neither reads nor clears.
        var rig = new Rig();
        var prompt = new DeliverClientRig();
        prompt.Panel.Inventory.Add(prompt.Letter(prompt.HomeAddress));
        prompt.FocusOn(prompt.Home);
        prompt.Source.Pending.Add(DeliveryOpKinds.DeliverMatching);

        // Act
        rig.Hand.OnHoldCompleted(Completed, prompt.Home.Entity);
        prompt.Tick();
        bool available = prompt.IsAvailable(prompt.Home, out StringId reason);

        // Assert: idle at 30 while the published state keeps "Delivering…".
        Assert.False(rig.Hand.IsHandIdle(Completed + 29));
        Assert.True(rig.Hand.IsHandIdle(Completed + 30));
        Assert.False(available);
        Assert.Equal(DeliverPromptKeys.Pending, reason);
    }

    [Theory]
    [Trait("Id", "M-MD7-2")]
    [InlineData("reply at tick 10", 10u, false, true, 1, true, true)]
    [InlineData("reply at tick 40 (after the 1 s idle)", 40u, false, true, 1, false, true)]
    [InlineData("reply after a resync", 10u, true, true, 0, false, false)]
    [InlineData("reply beyond 2.9 m", 10u, false, false, 1, true, false)]
    public void OwnReply_PlaysTheVerdictAsMd7Says(string row, uint replyTick, bool resync, bool withinReach, int beats, bool slotIn, bool attached)
    {
        // Arrange
        var rig = new Rig { WithinReach = withinReach };
        EventCursor feed = rig.Events.OpenCursor(EventKindSet.Of(MailEventKinds.DeliveryOutcome, EventKind.OpReply));
        rig.Hand.OnHoldCompleted(Completed, House);
        if (resync)
        {
            rig.Hand.OnResync();
        }

        // Act
        rig.Batch(Completed + replyTick - 1, Self, 1, OkReply(1));
        rig.Hand.Update(Completed + replyTick);

        // Assert
        Assert.True(beats == rig.Hand.Started.Count, row + ": " + rig.Hand.Started.Count + " beats");
        if (beats == 1)
        {
            HandBeatStart started = rig.Hand.Started[0];
            Assert.Equal((HandBeat.SlotIn, slotIn, attached, 4_000L), (started.Response.Beat, started.Response.SlotIn, started.AttachedToPrompt, started.StartMs));
        }

        Assert.True(feed.TryRead(out PresentedEvent outcome) && outcome.Kind == MailEventKinds.DeliveryOutcome, row + ": feed outcome");
        Assert.True(feed.TryRead(out PresentedEvent reply) && reply.Kind == EventKind.OpReply, row + ": feed reply");
    }

    [Theory]
    [Trait("Id", "TR-delivery-018")]
    [InlineData(false)]
    [InlineData(true)]
    public void Press_StartsTheReachEcho_OnlyWhenTheLastPublishedStateAllowedAHold(bool canStartHoldNext)
    {
        // Arrange
        var preview = new FakePreview(new DeliverPreviewState { Active = true, Destination = House, CanStartHoldNext = canStartHoldNext });
        var echo = new ReachEcho(preview, new FakeRenderClock());

        // Act
        bool started = echo.TryStart(out ReachEchoStart start);

        // Assert
        Assert.Equal(canStartHoldNext, started);
        Assert.Equal(canStartHoldNext ? new ReachEchoStart(House, 4_000L) : default, start);
    }

    private static Reply OkReply(int k)
    {
        var op = new Op { Kind = DeliveryOpKinds.DeliverMatching, ReqId = 7 };
        var extra = new InvReplyExtra { RequestedEntries = (ushort)k, GotEntries = (ushort)k };
        return Reply.Ok(in op, in extra);
    }

    private static EventEntry Entry<T>(EventKind kind, uint ordinal, T payload)
        where T : unmanaged
    {
        var e = new EventEntry { Ordinal = ordinal, Kind = kind, PayloadLength = (byte)Marshal.SizeOf<T>() };
        MemoryMarshal.Write(e.Payload, in payload);
        return e;
    }

    private sealed class FakeRenderClock : IRenderClock
    {
        public uint HostTick => 120;

        public int AlphaPermille => 0;

        public long RenderSimTimeMs => 4_000;

        public bool IsValid => true;
    }

    private sealed class FakeReach(Rig rig) : IDeliverReachRead
    {
        public bool IsWithinHoldReach(EntityId destination) => rig.WithinReach && destination == House;
    }

    private sealed class FakePreview(DeliverPreviewState state) : IDeliverPreviewRead
    {
        private readonly DeliverPreviewState _state = state;

        public ref readonly DeliverPreviewState Current => ref _state;
    }

    private sealed class Rig
    {
        public Rig()
        {
            Hand = new HandBeatController(Events, new FakeRenderClock(), new FakeReach(this), Self);
        }

        public PresentationEventStream Events { get; } = new();

        public HandBeatController Hand { get; }

        public bool WithinReach { get; init; } = true;

        // One host batch as the client adapter publishes it: the hold's k outcomes, then the own reply (Sim Core appends it last).
        public void Batch(uint tick, PlayerSlot actor, int k, Reply? reply)
        {
            uint ordinal = 0;
            for (int i = 0; i < k; i++)
            {
                var outcome = new DeliveryOutcomePayload
                {
                    OutcomeSeq = (uint)i + 1, Destination = House.Value, Actor = actor.Value, Result = (byte)EvalResult.Accept, Units = 1, Cents = 4,
                };
                Events.OnDecoded(tick, Entry(MailEventKinds.DeliveryOutcome, ordinal++, outcome));
            }

            if (reply is { } r)
            {
                Events.OnDecoded(tick, Entry(EventKind.OpReply, ordinal, r));
            }
        }
    }
}
