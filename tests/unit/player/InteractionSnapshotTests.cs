using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 018 (ADR-0014 section 7 with the 2026-11-04 erratum; ADR-0016 section 2; GDD P11, UI Requirements;
/// TR-movement-011, TR-movement-012): the <see cref="InteractionSnapshot"/> that <see cref="InteractionSnapshotPublisher"/> publishes
/// at the end of 4e, on the <see cref="PressRig"/> with the hold step and the publisher bound. One test per acceptance criterion.
/// The 0 B publish is measured by <see cref="HoldAdvanceAllocationTests"/>, whose rig binds the publisher.
/// </summary>
public class InteractionSnapshotTests
{
    private static readonly StringId _closedKey = new(StringNamespace.Run, 931);

    private static PressRig MailboxOnly()
    {
        var rig = new PressRig(withHold: true);
        rig.Anchors.Unregister(PressRig.Container);
        rig.Anchors.Unregister(PressRig.Closed);
        return rig;
    }

    private static ref readonly InteractionSnapshot Snap(PressRig rig) => ref rig.Publisher!.Current;

    [Fact]
    [Trait("Id", "PCM-UI-1")]
    public void Fields_FocusedAvailableDeliverAnchor_PublishesItsRefVerbModePromptAndTheTick()
    {
        // Arrange
        PressRig rig = MailboxOnly();

        // Act
        uint tick = rig.Tick();

        // Assert
        InteractionSnapshot s = Snap(rig);
        Assert.Equal((tick, (ushort)1, PressRig.Mailbox, Verb.Deliver, InteractMode.Hold), (s.Tick, s.Seq, s.Focused, s.Verb, s.Mode));
        Assert.Equal(((byte)1, StringId.None, InteractState.Idle), (s.Available, s.Reason, s.State));
        PromptArgs prompt = s.PromptArgs;
        Assert.Equal((PressOwner.PromptMark, (byte)rig.MailboxOwner.Branch, (byte)0), (prompt[0], prompt[1], prompt[31]));
        Assert.Equal(1, rig.MailboxOwner.PromptCalls);   // once per tick, focused anchor only
    }

    [Theory]
    [Trait("Id", "PCM-UI-1")]
    [InlineData("owner unavailable")]
    [InlineData("panel")]
    [InlineData("dead mid-hold")]
    [InlineData("unregistered mid-hold")]
    public void UnavailableTarget_StillPublishedWithOneReason_DeadPublishesNoTargetThatTick(string row)
    {
        // Arrange
        PressRig rig = MailboxOnly();
        rig.Tick();
        int prompts = rig.MailboxOwner.PromptCalls;
        switch (row)
        {
            case "owner unavailable":
                rig.MailboxOwner.Unavailable = _closedKey;
                break;
            case "panel":
                rig.Source.SetStatus(PressRig.Status(panel: true));
                break;
            default:
                rig.Press(PressRig.Mailbox);
                rig.Tick();
                rig.Tick();
                prompts = rig.MailboxOwner.PromptCalls;
                if (row == "dead mid-hold")
                {
                    rig.Source.SetStatus(PressRig.Status(BodyContext.Dead));
                }
                else
                {
                    rig.Anchors.Unregister(PressRig.Mailbox);
                }

                break;
        }

        // Act
        rig.Tick();

        // Assert
        InteractionSnapshot s = Snap(rig);
        PromptArgs prompt = s.PromptArgs;
        if (row == "dead mid-hold")
        {
            // TD-209 item 6: no target and no reason on the Dead tick itself, though 4a still holds the latched incumbent.
            Assert.Equal(PressRig.Mailbox, rig.Focus.State.Focused);
            Assert.Equal((default(AnchorRef), StringId.None, (byte)0, (byte)0), (s.Focused, s.Reason, s.Available, prompt[0]));
            Assert.Equal(prompts, rig.MailboxOwner.PromptCalls);
        }
        else if (row == "unregistered mid-hold")
        {
            // TD-209 items 1-2: still focused (4a keeps the latched ref), unusable with no reason, no prompt; verb and mode from the latch (B2).
            Assert.Equal((PressRig.Mailbox, StringId.None, (byte)0, Verb.Deliver, InteractMode.Hold), (s.Focused, s.Reason, s.Available, s.Verb, s.Mode));
            Assert.Equal(((byte)0, prompts), (prompt[0], rig.MailboxOwner.PromptCalls));
        }
        else
        {
            StringId expected = row == "panel" ? FocusRig.ClosePanel : _closedKey;
            Assert.Equal((PressRig.Mailbox, expected, (byte)0), (s.Focused, s.Reason, s.Available));
            Assert.Equal((PressOwner.PromptMark, prompts + 1), (prompt[0], rig.MailboxOwner.PromptCalls));   // still written, dimmed
        }
    }

    [Fact]
    [Trait("Id", "PCM-F4-2")]
    [Trait("Id", "H-G1-1")]
    public void Publish_PressAndHold_MatchesTheTickTable()
    {
        // Arrange: the container is focused; the press presents the mailbox (a 12-tick Deliver hold, cancel range 2900 mm).
        var rig = new PressRig(withHold: true);
        rig.Anchors.Move(PressRig.Container, FocusRig.Ahead(700));
        rig.Tick();
        ushort seq = Snap(rig).Seq;
        rig.Press(PressRig.Mailbox);

        // Each row: state, latched, held, hold, progress, last cancel, whether Seq moved; Focused stays the container (G1).
        (InteractState State, AnchorRef Latched, short Held, short Hold, ushort Pm, CancelReason Cancel, bool Moved)[] table =
        {
            (InteractState.Holding, PressRig.Mailbox, 0, 12, 0, CancelReason.None, true),       // k 0: the press tick
            (InteractState.Holding, PressRig.Mailbox, 1, 12, 83, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 2, 12, 166, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 3, 12, 250, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 4, 12, 333, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 5, 12, 416, CancelReason.None, true),     // PCM-F4-2: 5 of 12 → 416
            (InteractState.Holding, PressRig.Mailbox, 6, 12, 500, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 7, 12, 583, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 8, 12, 666, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 9, 12, 750, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 10, 12, 833, CancelReason.None, true),
            (InteractState.Holding, PressRig.Mailbox, 11, 12, 916, CancelReason.None, true),
            (InteractState.Complete, PressRig.Mailbox, 12, 12, 1000, CancelReason.None, true),  // k 12: the request tick
            (InteractState.Complete, PressRig.Mailbox, 12, 12, 1000, CancelReason.None, false), // the ring stays full
            (InteractState.Idle, default, 0, 0, 0, CancelReason.None, true),                    // k 14: released
            (InteractState.Holding, PressRig.Mailbox, 0, 12, 0, CancelReason.None, true),       // k 15: pressed again
            (InteractState.Idle, default, 0, 0, 0, CancelReason.OutOfRange, true),              // k 16: walked to 3000 mm
            (InteractState.Idle, default, 0, 0, 0, CancelReason.OutOfRange, true),              // k 17: a panel press keeps it (the panel dims the focus)
        };

        for (int k = 0; k < table.Length; k++)
        {
            switch (k)
            {
                case 14: rig.Release(); break;
                case 15: rig.Press(PressRig.Mailbox); break;
                case 16: rig.Source.Feet = new Vec3Mm(FocusRig.Feet.X, FocusRig.Feet.Y, FocusRig.Feet.Z + 1000); break;
                case 17:
                    rig.Source.SetStatus(PressRig.Status(panel: true));
                    rig.Press(PressRig.Mailbox);
                    break;
                default: break;
            }

            // Act
            uint tick = rig.Tick();

            // Assert
            InteractionSnapshot s = Snap(rig);
            var row = table[k];
            Assert.True(
                (s.Tick, s.State, s.Latched, s.HeldTicks, s.HoldTicks, s.ProgressPm, s.LastCancel) == (tick, row.State, row.Latched, row.Held, row.Hold, row.Pm, row.Cancel),
                $"k {k}: {(s.State, s.Latched, s.HeldTicks, s.HoldTicks, s.ProgressPm, s.LastCancel)}");
            Assert.True(row.Moved == (s.Seq != seq), $"k {k}: Seq {seq} -> {s.Seq}");
            seq = s.Seq;
            if (k < 16)
            {
                Assert.True(s.Focused == PressRig.Container, $"k {k}: focused {s.Focused}");
            }
        }

        Assert.Equal(1, rig.Requests.Sent);
    }

    [Fact]
    public void Seq_StaysPutAcrossThousandsOfSteadyTicks_AndMovesOnlyWhenContentChanges()
    {
        // Arrange: nothing registered, then a steady focused prompt.
        var empty = new PressRig(withHold: true);
        foreach (AnchorRef a in new[] { PressRig.Mailbox, PressRig.Container, PressRig.Closed, PressRig.Seat })
        {
            empty.Anchors.Unregister(a);
        }

        PressRig steady = MailboxOnly();

        // Act
        for (int t = 0; t < 3000; t++)
        {
            empty.Tick();
            steady.Tick();
        }

        // Assert: TD-209 item 7, an idle player looking at nothing stays at 0; a steady prompt keeps the Seq of its first tick.
        Assert.Equal((ushort)0, Snap(empty).Seq);
        Assert.Equal((ushort)1, Snap(steady).Seq);

        // Act + Assert: one content change, one step.
        steady.MailboxOwner.Unavailable = _closedKey;
        steady.Tick();
        steady.Tick();
        Assert.Equal((ushort)2, Snap(steady).Seq);
    }

    [Fact]
    [Trait("Id", "PCM-UI-1")]
    public void Layout_Is84UnmanagedBytesAtTheTd209Offsets_AndCurrentIsOneReadOnlyReference()
    {
        // Arrange
        var focused = new AnchorRef(new EntityId(0x0100_0011u), 2);
        var latched = new AnchorRef(new EntityId(0x0100_0022u), 3);
        var beyond = new AnchorRef(new EntityId(0x0100_0033u), 4);
        var reason = new StringId(StringNamespace.Run, 0x1234);
        PromptArgs prompt = default;
        prompt[0] = 0xC1;
        prompt[31] = 0xC2;
        var s = new InteractionSnapshot
        {
            Tick = 0x0A0B0C0D, Seq = 0xBEEF, State = InteractState.Complete, LastCancel = CancelReason.OutOfRange,
            Focused = focused, Latched = latched, NearestBeyond = beyond, NearestBeyondRangeMm = 5999,
            Verb = Verb.Deliver, Mode = InteractMode.Hold, CatchPredicted = 1, Available = 1, Reason = reason,
            CatchFlightId = 0x7777, HeldTicks = 5, HoldTicks = 12, ProgressPm = 416, PromptArgs = prompt,
        };

        // Act
        ReadOnlySpan<byte> b = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref s, 1));

        // Assert
        Assert.Equal(84, Unsafe.SizeOf<InteractionSnapshot>());
        Assert.Equal((0x0A0B0C0Du, (ushort)0xBEEF, (byte)InteractState.Complete, (byte)CancelReason.OutOfRange), (BinaryPrimitives.ReadUInt32LittleEndian(b), BinaryPrimitives.ReadUInt16LittleEndian(b[4..]), b[6], b[7]));
        Assert.Equal((focused, latched, beyond, 5999), (MemoryMarshal.Read<AnchorRef>(b[8..]), MemoryMarshal.Read<AnchorRef>(b[16..]), MemoryMarshal.Read<AnchorRef>(b[24..]), BinaryPrimitives.ReadInt32LittleEndian(b[32..])));
        Assert.Equal(((byte)Verb.Deliver, (byte)InteractMode.Hold, (byte)1, (byte)1, reason), (b[36], b[37], b[38], b[39], MemoryMarshal.Read<StringId>(b[40..])));
        Assert.Equal(((ushort)0x7777, (short)5, (short)12, (ushort)416), (BinaryPrimitives.ReadUInt16LittleEndian(b[44..]), BinaryPrimitives.ReadInt16LittleEndian(b[46..]), BinaryPrimitives.ReadInt16LittleEndian(b[48..]), BinaryPrimitives.ReadUInt16LittleEndian(b[50..])));
        Assert.Equal(((byte)0xC1, (byte)0xC2), (b[52], b[83]));

        // The port hands out ref readonly only (compile-time); a read through it sees the last publish.
        PressRig rig = MailboxOnly();
        rig.Tick();
        IInteractionQuery q = rig.Publisher!;
        Assert.Equal(PressRig.Mailbox, q.Current.Focused);
    }

    [Theory]
    [InlineData("4 m ahead", 4000, false, 4000)]
    [InlineData("6 m ahead", 6000, false, 6000)]
    [InlineData("past 6 m", 6001, false, 0)]
    [InlineData("4 m behind", -4000, false, 0)]
    [InlineData("tick starting focused", 4000, true, 0)]
    public void TooFar_NearestVisibleOutOfReachAnchorWithin6m_OnlyOnATickStartingUnfocused(string row, int distance, bool focusFirst, int expectedMm)
    {
        // Arrange: TD-209 item 4's example: a mailbox (reach 2500 mm) 4 m ahead with nothing in reach.
        _ = row;
        var rig = new PressRig(withHold: true);
        rig.Anchors.Unregister(PressRig.Closed);
        if (focusFirst)
        {
            rig.Anchors.Move(PressRig.Container, FocusRig.Ahead(700));
            rig.Tick();
        }
        else
        {
            rig.Anchors.Unregister(PressRig.Container);
        }

        rig.Anchors.Move(PressRig.Mailbox, FocusRig.Ahead(distance));

        // Act
        rig.Tick();

        // Assert
        InteractionSnapshot s = Snap(rig);
        AnchorRef expected = expectedMm == 0 ? default : PressRig.Mailbox;
        Assert.Equal((expected, expectedMm), (s.NearestBeyond, s.NearestBeyondRangeMm));
    }

    [Theory]
    [InlineData("lob on the press", 1, 7)]
    [InlineData("no prediction", 0, 0)]
    [InlineData("two presses, the last predicts nothing", 0, 0)]
    [InlineData("two presses, the last predicts", 1, 9)]
    public void CatchPrompt_CopiesThisTicksLastPressPrediction_EmptyOnTheNextTick(string row, byte predicted, ushort flight)
    {
        // Arrange: Tap presses on the container (a predicted receiver equal to the presented target is no catch, so both resolve).
        var rig = new PressRig(withHold: true);
        var ownLob = new CatchPrediction { Predicted = 1, FlightId = 9, Units = 1, PredictedStaticReceiver = PressRig.Container };
        switch (row)
        {
            case "lob on the press":
                rig.Predictor.Next = PressRig.Lob();
                rig.Press(AnchorRef.None);
                break;
            case "no prediction":
                rig.Press(PressRig.Container);
                break;
            case "two presses, the last predicts nothing":
                rig.Predictor.Then.Enqueue(PressRig.Lob(PressRig.Container));
                rig.Predictor.Then.Enqueue(default);
                rig.Press(PressRig.Container);
                rig.Press(PressRig.Container);
                break;
            default:
                rig.Predictor.Then.Enqueue(default);
                rig.Predictor.Then.Enqueue(ownLob);
                rig.Press(PressRig.Container);
                rig.Press(PressRig.Container);
                break;
        }

        // Act
        rig.Tick();
        InteractionSnapshot pressTick = Snap(rig);
        rig.Predictor.Next = default;
        rig.Tick();

        // Assert
        Assert.Equal((predicted, flight), (pressTick.CatchPredicted, pressTick.CatchFlightId));
        Assert.Equal(((byte)0, (ushort)0), (Snap(rig).CatchPredicted, Snap(rig).CatchFlightId));
    }
}
