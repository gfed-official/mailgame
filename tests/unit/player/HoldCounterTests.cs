using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 017 (ADR-0014 sections 3-4; ADR-0016 section 2; GDD P11, P12; input-controls H-R3-1, H-F2-1, H-G1-1,
/// H-R4-4): the hold counter at 4e on the <see cref="PressRig"/> with <see cref="HoldAdvanceStep"/> bound. The mailbox is a
/// 400 ms Deliver hold, 12 ticks at the default setting; its reach is 2500 mm, so the cancel range is 2900 mm.
/// </summary>
public class HoldCounterTests
{
    private static readonly StringId _ownerReason = new(StringNamespace.Run, 917);

    /// <summary>Presses the mailbox and runs the press tick; returns the press tick.</summary>
    private static uint PressMailbox(PressRig rig)
    {
        rig.Press(PressRig.Mailbox);
        return rig.Tick();
    }

    /// <summary>Runs ticks until <paramref name="tick"/> has run; returns the request tick seen on the way, or null.</summary>
    private static uint? RunTo(PressRig rig, uint tick, Action<uint>? before = null)
    {
        uint? sentAt = null;
        while (rig.Layer.ClientTick <= tick)
        {
            before?.Invoke(rig.Layer.ClientTick);
            int sent = rig.Requests.Sent;
            uint t = rig.Tick();
            if (rig.Requests.Sent != sent)
            {
                Assert.Null(sentAt);
                sentAt = t;
            }
        }

        return sentAt;
    }

    // ---------------------------------------------------------------- PCM-P12-1, H-R3-1, H-R4-4

    [Fact]
    [Trait("Id", "PCM-P12-1")]
    [Trait("Id", "PC017-ONE-COMMIT")]
    public void Hold_PressedOnTick0_SendsOneRequestOnTick12_NoneOnTick11_AndCompleteNeverSendsAgain()
    {
        // Arrange
        var rig = new PressRig(withHold: true);
        uint press = PressMailbox(rig);

        // Act
        uint? byTick11 = RunTo(rig, press + 11);
        uint? sentAt = RunTo(rig, press + 60);
        InteractState held = rig.Interaction.State;
        rig.Release();
        rig.Tick();

        // Assert
        Assert.Equal((0u, null, press + 12), (press, byTick11, sentAt));
        Assert.Equal((1, InteractState.Complete, InteractState.Idle), (rig.Requests.Attempts, held, rig.Interaction.State));
        Assert.Equal((1L, 1L, 0L), (rig.Counters[CounterId.HoldsStarted], rig.Counters[CounterId.HoldsCompleted], rig.Counters[CounterId.HoldsCancelled]));
    }

    [Theory]
    [Trait("Id", "PCM-P12-1")]
    [Trait("Id", "H-R3-1")]
    [InlineData(1, EdgeKind.Release, EdgeReason.None, false)]
    [InlineData(6, EdgeKind.Release, EdgeReason.ToggleSecondPress, false)]
    [InlineData(11, EdgeKind.Cancel, EdgeReason.StateChange, false)]
    [InlineData(12, EdgeKind.Release, EdgeReason.None, false)]
    [InlineData(12, EdgeKind.Release, EdgeReason.ToggleSecondPress, false)]
    [InlineData(13, EdgeKind.Release, EdgeReason.None, true)]
    [InlineData(60, EdgeKind.Release, EdgeReason.ToggleSecondPress, true)]
    public void Release_BeforeOrOnTheCompletionTick_Cancels_AfterItChangesNothing(int at, EdgeKind kind, EdgeReason reason, bool sends)
    {
        // Arrange: a hold-as-toggle is a plain hold here; the router's second press arrives as a release (ADR-0012).
        var rig = new PressRig(withHold: true);
        uint press = PressMailbox(rig);

        // Act: the edge on tick press + at is resolved at 4d, before 4e's accrual.
        uint? before = RunTo(rig, press + (uint)at - 1);
        rig.Edge(FrameAction.Interact, kind, AnchorRef.None, reason);
        uint? sentAt = RunTo(rig, press + (uint)at + 20) ?? before;

        // Assert
        Assert.Equal(sends ? press + 12 : null, sentAt);
        Assert.Equal((sends ? 1 : 0, InteractState.Idle), (rig.Requests.Attempts, rig.Interaction.State));
        Assert.Equal(sends ? CancelReason.None : CancelReason.Released, rig.Interaction.LastCancel);
    }

    [Fact]
    [Trait("Id", "H-R4-4")]
    [Trait("Id", "H-R3-1")]
    public void PressAndReleaseInOneFrame_SendsNothing_AndReportsReleased()
    {
        // Arrange
        var rig = new PressRig(withHold: true);
        rig.Press(PressRig.Mailbox);
        rig.Release();

        // Act
        uint? sentAt = RunTo(rig, 30);

        // Assert
        Assert.Equal((null, 0, InteractState.Idle, CancelReason.Released), (sentAt, rig.Requests.Attempts, rig.Interaction.State, rig.Interaction.LastCancel));
        Assert.Equal((1L, 1L, false), (rig.Counters[CounterId.HoldsStarted], rig.Counters[CounterId.HoldsCancelled], rig.Focus.State.Latched));
    }

    [Theory]
    [Trait("Id", "PCM-P12-1")]
    [Trait("Id", "PCM-F4-2")]
    [InlineData(400, 0, 400)]
    [InlineData(400, 500, 400)]
    [InlineData(400, 500, 1000)]
    [InlineData(400, 0, 200)]
    [InlineData(400, 0, 1000)]
    [InlineData(400, 1000, 400)]
    [InlineData(2000, 0, 1000)]
    public void RequestTick_IsPressTickPlusHoldTicks_ForEveryHoldEffRow(int baseMs, int insertPm, int settingMs)
    {
        // Arrange: the PCM-F4-2 rows of HoldTicksTests; the expected length comes from the shared function.
        var rig = new PressRig(withHold: true);
        AnchorRef a = FocusRig.Ref(8);
        AnchorDef d = AnchorDef.For(a, FocusRig.Ahead(1000), Verb.Deliver);
        d.HoldBaseMs = baseMs;
        Assert.Equal(RegisterResult.Ok, rig.Anchors.Register(in d, new PressOwner(rig.Stats)));
        rig.Stats.InsertPm = insertPm;
        rig.Input.HoldSettingMs = settingMs;
        HoldSettings s = HoldSettings.FromPlayerSettings(rig.Settings);
        int holdTicks = Hold.HoldTicks(baseMs, insertPm, settingMs, in s);
        rig.Tick();
        rig.Press(a);
        uint press = rig.Tick();

        // Act
        uint? sentAt = RunTo(rig, press + (uint)holdTicks + 5);

        // Assert
        Assert.Equal(press + (uint)holdTicks, sentAt);
    }

    // ---------------------------------------------------------------- PCM-P12-2, H-F2-1

    [Theory]
    [Trait("Id", "PCM-P12-2")]
    [Trait("Id", "H-F2-1")]
    [InlineData("release", CancelReason.Released)]
    [InlineData("distance 2901 mm", CancelReason.OutOfRange)]
    [InlineData("distance 2900 mm", CancelReason.None)]
    [InlineData("IsAvailable false", CancelReason.Unavailable)]
    [InlineData("unregistered", CancelReason.Unregistered)]
    [InlineData("ShouldCancel", CancelReason.OwnerCancel)]
    [InlineData("panel", CancelReason.Panel)]
    [InlineData("mounted", CancelReason.Mounted)]
    [InlineData("dead", CancelReason.Dead)]
    [InlineData("swim depth", CancelReason.Swim)]
    [InlineData("knockdown", CancelReason.Knockdown)]
    [InlineData("walking at full speed, Alive, unrelated status changes", CancelReason.None)]
    public void HoldInProgress_EachCancelEndsItWithNoRequestAndItsReason_WalkingDoesNot(string row, CancelReason expected)
    {
        // Arrange: the condition starts on held tick 6 (feet moved straight back: the mailbox is 2000 mm ahead of the chest).
        var rig = new PressRig(withHold: true);
        uint press = PressMailbox(rig);
        RunTo(rig, press + 5);
        Vec3Mm feet = FocusRig.Feet;
        switch (row)
        {
            case "release": rig.Release(); break;
            case "distance 2901 mm": rig.Source.Feet = new Vec3Mm(feet.X, feet.Y, feet.Z + 901); break;
            case "distance 2900 mm": rig.Source.Feet = new Vec3Mm(feet.X, feet.Y, feet.Z + 900); break;
            case "IsAvailable false": rig.MailboxOwner.Unavailable = _ownerReason; break;
            case "unregistered": rig.Anchors.Unregister(PressRig.Mailbox); break;
            case "ShouldCancel": rig.MailboxOwner.CancelWith = _ownerReason; break;
            case "panel": rig.Source.SetStatus(PressRig.Status(panel: true)); break;
            case "mounted": rig.Source.SetStatus(PressRig.Status(BodyContext.Mounted)); break;
            case "dead": rig.Source.SetStatus(new CarrierStatus { Life = LifeState.Frozen, Body = BodyContext.Dead }); break;
            case "swim depth": rig.Source.SetStatus(PressRig.Status(swimming: true)); break;
            case "knockdown": rig.Source.SetStatus(PressRig.Status(BodyContext.Dead)); break;
            default: break;
        }

        // Act: walking sidesteps 150 mm a tick (4.5 m/s) and flips unrelated status bits, the life state staying Alive.
        uint? sentAt = RunTo(rig, press + 20, t =>
        {
            if (row.StartsWith("walking", StringComparison.Ordinal))
            {
                rig.Source.Feet = new Vec3Mm(feet.X + (int)(t % 8 * 150), feet.Y, feet.Z);
                rig.Source.SetStatus(new CarrierStatus { Life = LifeState.Alive, Body = BodyContext.OnFoot, CursorHolding = t % 2 == 0, Aiming = t % 3 == 0, Seq = (ushort)t });
            }
        });

        // Assert
        bool keeps = expected == CancelReason.None;
        StringId key = expected is CancelReason.Unavailable or CancelReason.OwnerCancel ? _ownerReason : StringId.None;
        Assert.True((keeps ? press + 12 : (uint?)null, keeps ? 1 : 0) == (sentAt, rig.Requests.Attempts), row);
        Assert.True((expected, key, InteractState.Idle == rig.Interaction.State) == (rig.Interaction.LastCancel, rig.Interaction.LastCancelReason, !keeps), row);
        Assert.Equal(keeps ? (1L, 0L) : (0L, 1L), (rig.Counters[CounterId.HoldsCompleted], rig.Counters[CounterId.HoldsCancelled]));
    }

    // ---------------------------------------------------------------- H-G1-1, PCM-P11-2c, PCM-P11-2b

    [Fact]
    [Trait("Id", "H-G1-1")]
    [Trait("Id", "PCM-P11-2c")]
    public void HoldLatchedOnA_FocusChallengerBFromTick6_SendsForA_AndFocusStaysOnA()
    {
        // Arrange: the container is moved aside so the mailbox (A) is focused when it is pressed.
        var rig = new PressRig(withHold: true);
        rig.Anchors.Move(PressRig.Container, FocusRig.Ahead(2400, 2000));
        RunTo(rig, 5);
        Assert.Equal(PressRig.Mailbox, rig.Focus.State.Focused);
        uint press = PressMailbox(rig);
        var latched = new List<(AnchorRef Latch, AnchorRef Focus, bool Latched)>();

        // Act: from held tick 6 the container (B) sits 700 mm dead ahead, far better than A, leading for the rest of the hold.
        uint? sentAt = RunTo(rig, press + 12, t =>
        {
            latched.Add((rig.Interaction.Latch.Anchor, rig.Focus.State.Focused, rig.Focus.State.Latched));   // after tick t - 1
            if (t == press + 6)
            {
                rig.Anchors.Move(PressRig.Container, FocusRig.Ahead(700));
            }
        });

        // Assert: the request is A's, built from the latch (payload, claim); A stayed latched and focused; Latched clears on Complete.
        InteractionLatch built = rig.MailboxOwner.BuiltFrom;
        Assert.Equal((press + 12, PressRig.Mailbox, PressOwner.PayloadMark, 12), (sentAt, built.Anchor, built.OwnerPayload[0], built.HoldTicks));
        Assert.Equal(PressRig.Mailbox.Entity.Value, rig.Requests.Last.PayloadAs<OpenPayload>().Container);
        Assert.Equal(12, latched.Count);
        Assert.All(latched, l => Assert.Equal((PressRig.Mailbox, PressRig.Mailbox, true), l));
        Assert.Equal((InteractState.Complete, false), (rig.Interaction.State, rig.Focus.State.Latched));
        RunTo(rig, press + 12 + (uint)rig.Settings.FocusDwellTicks);
        Assert.Equal(PressRig.Container, rig.Focus.State.Focused);   // once unlatched, the dwell hands focus to B
    }

    [Fact]
    [Trait("Id", "PCM-P11-2c")]
    [Trait("Id", "PC017-CATCH-CHANNEL")]
    public void Channel_LatchesFocusFromPressToRelease_AChallengerLeadingFor30TicksNeverTakesIt()
    {
        // Arrange: catch-armed (no target) while the mailbox is focused.
        var rig = new PressRig(withHold: true);
        rig.Anchors.Move(PressRig.Container, FocusRig.Ahead(2400, 2000));
        RunTo(rig, 5);
        rig.Press(AnchorRef.None);
        uint press = rig.Tick();
        bool latchedOnPressTick = rig.Focus.State.Latched;
        rig.Anchors.Move(PressRig.Container, FocusRig.Ahead(700));

        // Act
        var focus = new List<AnchorRef>();
        RunTo(rig, press + 30, _ => focus.Add(rig.Focus.State.Focused));
        bool latchedBeforeRelease = rig.Focus.State.Latched;
        rig.Release();
        rig.Tick();

        // Assert
        Assert.All(focus, f => Assert.Equal(PressRig.Mailbox, f));
        Assert.Equal((true, true, false), (latchedOnPressTick, latchedBeforeRelease, rig.Focus.State.Latched));
        Assert.Equal((1, 1, InteractState.Idle, CancelReason.None), (rig.CatchArm.Downs, rig.CatchArm.Ups, rig.Interaction.State, rig.Interaction.LastCancel));
        RunTo(rig, press + 31 + (uint)rig.Settings.FocusDwellTicks);
        Assert.Equal(PressRig.Container, rig.Focus.State.Focused);   // the control: unlatched, the challenger wins
    }

    [Fact]
    [Trait("Id", "PCM-P11-2b")]
    public void CatchEnteringTheWindowAtTick6_OfALatchedDeliverHold_DoesNotConvertIt()
    {
        // Arrange
        var rig = new PressRig(withHold: true);
        uint press = PressMailbox(rig);

        // Act: from held tick 6 a lob is predicted to reach the carrier, and the player presses Interact again.
        uint? sentAt = RunTo(rig, press + 14, t =>
        {
            rig.Predictor.Next = t >= press + 6 ? PressRig.Lob() : default;
            if (t == press + 6)
            {
                rig.Press(AnchorRef.None);
            }
        });

        // Assert
        Assert.Equal((press + 12, 0, 0, InteractState.Complete), (sentAt, rig.CatchArm.Downs, rig.CatchArm.Ups, rig.Interaction.State));
        Assert.Equal(PressRig.Mailbox.Entity.Value, rig.Requests.Last.PayloadAs<OpenPayload>().Container);
    }

    // ---------------------------------------------------------------- one commit

    [Fact]
    [Trait("Id", "PC017-ONE-COMMIT")]
    public void Completion_BuildsFromTheLatch_ClaimIsThePressFeet_NotTheCompletionFeet()
    {
        // Arrange
        var rig = new PressRig(withHold: true);
        uint press = PressMailbox(rig);
        rig.Source.Feet = new Vec3Mm(FocusRig.Feet.X + 300, FocusRig.Feet.Y, FocusRig.Feet.Z);

        // Act
        RunTo(rig, press + 12);

        // Assert
        OpenPayload p = rig.Requests.Last.PayloadAs<OpenPayload>();
        Assert.Equal((FocusRig.Feet.X, FocusRig.Feet.Y, FocusRig.Feet.Z), (p.ClaimedX, p.ClaimedY, p.ClaimedZ));
        Assert.Equal((press, 12, 1), (rig.MailboxOwner.BuiltFrom.PressTick, rig.MailboxOwner.BuiltFrom.HoldTicks, rig.MailboxOwner.Builds));
    }

    [Fact]
    [Trait("Id", "PC017-ONE-COMMIT")]
    public void RefusedSendAtCompletion_IsBusy_Idle_AndNeverRetried()
    {
        // Arrange: Evan approved 2026-10-29 (TD-186 item 2).
        var rig = new PressRig(withHold: true);
        uint press = PressMailbox(rig);
        rig.Requests.Accept = false;

        // Act
        RunTo(rig, press + 12);
        rig.Requests.Accept = true;
        RunTo(rig, press + 40);

        // Assert
        Assert.Equal((1, 0, InteractState.Idle, CancelReason.Busy), (rig.Requests.Attempts, rig.Requests.Sent, rig.Interaction.State, rig.Interaction.LastCancel));
        Assert.Equal((0L, 1L, false), (rig.Counters[CounterId.HoldsCompleted], rig.Counters[CounterId.HoldsCancelled], rig.Focus.State.Latched));
    }

    [Fact]
    [Trait("Id", "PC017-ONE-COMMIT")]
    public void LastCancel_ClearsOnTheNextLatch()
    {
        // Arrange: Evan approved 2026-10-29 (TD-186 item 5); a cancelled hold, then a new press.
        var rig = new PressRig(withHold: true);
        rig.Press(PressRig.Mailbox);
        rig.Release();
        rig.Tick();
        CancelReason cancelled = rig.Interaction.LastCancel;

        // Act
        PressMailbox(rig);

        // Assert
        Assert.Equal((CancelReason.Released, CancelReason.None, InteractState.Holding), (cancelled, rig.Interaction.LastCancel, rig.Interaction.State));
    }

    // ---------------------------------------------------------------- catch channel

    [Theory]
    [Trait("Id", "PC017-CATCH-CHANNEL")]
    [InlineData("release", CancelReason.None)]
    [InlineData("cancel edge", CancelReason.None)]
    [InlineData("swim depth", CancelReason.Swim)]
    [InlineData("knockdown", CancelReason.Knockdown)]
    [InlineData("panel", CancelReason.Panel)]
    public void CatchChannel_SendsDownOnStart_AndUpOnTheReleaseOrAContextCancel(string row, CancelReason expected)
    {
        // Arrange: a predicted catch (rule 1) starts the Channel.
        var rig = new PressRig(withHold: true);
        rig.Predictor.Next = PressRig.Lob();
        rig.Press(AnchorRef.None);
        rig.Tick();
        rig.Predictor.Next = default;
        RunTo(rig, 8);
        (int, int, InteractState) held = (rig.CatchArm.Downs, rig.CatchArm.Ups, rig.Interaction.State);

        // Act
        switch (row)
        {
            case "release": rig.Release(); break;
            case "cancel edge": rig.Edge(FrameAction.Interact, EdgeKind.Cancel, AnchorRef.None, EdgeReason.StateChange); break;
            case "swim depth": rig.Source.SetStatus(PressRig.Status(swimming: true)); break;
            case "knockdown": rig.Source.SetStatus(PressRig.Status(BodyContext.Dead)); break;
            default: rig.Source.SetStatus(PressRig.Status(panel: true)); break;
        }

        RunTo(rig, 20);

        // Assert
        Assert.Equal((1, 0, InteractState.Channel), held);
        Assert.True((1, 1, InteractState.Idle, expected, false) == (rig.CatchArm.Downs, rig.CatchArm.Ups, rig.Interaction.State, rig.Interaction.LastCancel, rig.Focus.State.Latched), row);
        Assert.Equal((0L, 0L), (rig.Counters[CounterId.HoldsStarted], rig.Counters[CounterId.HoldsCancelled]));
    }

    [Fact]
    [Trait("Id", "PC017-CATCH-CHANNEL")]
    public void RefusedCatchArmDown_NeverEntersChannel_SoTheReleaseOwesNoUp()
    {
        // Arrange
        var rig = new PressRig(withHold: true);
        rig.CatchArm.Accept = false;
        rig.Press(AnchorRef.None);
        rig.Tick();

        // Act
        rig.Release();
        rig.Tick();

        // Assert
        Assert.Equal((1, 0, InteractState.Idle, CancelReason.Busy), (rig.CatchArm.Downs, rig.CatchArm.Ups, rig.Interaction.State, rig.Interaction.LastCancel));
    }

    [Fact]
    [Trait("Id", "PC017-CATCH-CHANNEL")]
    public void ChannelAnchorPress_AtM1a_LatchesSendsNothing_AndRecordsChannelAnchorNoBuilder()
    {
        // Arrange: Evan approved 2026-10-29 (TD-186 item 3); a Push (Channel) anchor.
        var rig = new PressRig(withHold: true);
        AnchorRef push = FocusRig.Ref(6, 1);
        rig.Add(push, FocusRig.Ahead(1000), Verb.Push);
        rig.Press(push);

        // Act
        uint tick = rig.Tick();
        bool latched = rig.Focus.State.Latched;
        rig.Release();
        rig.Tick();

        // Assert
        SimCheckRecord r = rig.Checks[rig.Checks.Count - 1];
        Assert.Equal(((ushort)CheckCode.ChannelAnchorNoBuilder, tick, (long)push.Entity.Value, 1L), (r.Code, r.Tick, r.A, r.B));
        Assert.Equal((0, 0, 0, true, InteractState.Idle), (rig.Requests.Attempts, rig.CatchArm.Downs, rig.CatchArm.Ups, latched, rig.Interaction.State));
    }

    // ---------------------------------------------------------------- counters and the fuzz

    [Fact]
    [Trait("Id", "PC017-COUNTERS")]
    [Trait("Id", "PC017-ONE-COMMIT")]
    public void Fuzz_10000MixedPresses_NeverSendTwiceForOnePress_AndTheCountersBalance()
    {
        // Arrange
        var fuzz = new HoldFuzz();
        var built = new HashSet<uint>();
        int refused = 0;

        // Act + Assert: per tick, the mailbox hold built at most once, never twice for one press tick, and the counters balance.
        while (fuzz.Presses < 10_000)
        {
            int before = fuzz.Rig.MailboxOwner.Builds;
            int attempts = fuzz.Rig.Requests.Attempts - fuzz.Rig.ContainerOwner.Builds;
            fuzz.Step();
            int builds = fuzz.Rig.MailboxOwner.Builds - before;
            Assert.InRange(builds, 0, 1);
            Assert.Equal(attempts + builds, fuzz.Rig.Requests.Attempts - fuzz.Rig.ContainerOwner.Builds);   // one send per build
            if (builds == 1)
            {
                Assert.True(built.Add(fuzz.Rig.MailboxOwner.BuiltFrom.PressTick), "a second request for one press");
                refused += fuzz.Rig.Interaction.State == InteractState.Complete ? 0 : 1;   // a refused send is Busy and Idle
            }

            SimCounters c = fuzz.Rig.Counters;
            long inProgress = fuzz.Rig.Interaction.State == InteractState.Holding ? 1 : 0;
            Assert.Equal(c[CounterId.HoldsStarted], c[CounterId.HoldsCompleted] + c[CounterId.HoldsCancelled] + inProgress);
            Assert.Equal(fuzz.Rig.Interaction.State is InteractState.Holding or InteractState.Channel, fuzz.Rig.Focus.State.Latched);
        }

        // The script exercised completions, every cancel kind it can produce, and refused sends.
        SimCounters counters = fuzz.Rig.Counters;
        Assert.True(counters[CounterId.HoldsCompleted] > 100 && counters[CounterId.HoldsCancelled] > 100, "completions and cancels");
        Assert.Equal(counters[CounterId.HoldsCompleted], built.Count - refused);
        Assert.True(refused > 0, "refused sends at completion");
        Assert.All(new[] { CancelReason.Released, CancelReason.OutOfRange, CancelReason.Busy, CancelReason.Swim, CancelReason.Panel, CancelReason.Knockdown }, r => Assert.True(fuzz.HasSeen(r), r.ToString()));
    }
}

/// <summary>
/// The seeded PCG fuzz for story 017 (the counters AC and <see cref="HoldAdvanceAllocationTests"/>): each tick 0-3 Interact edges
/// drawn from presses (mailbox hold, container Tap, no target, unregistered), releases and cancels; feet jitter that sometimes
/// leaves the 2900 mm cancel range; a lob, a busy sink, the panel, a swim spell and a knockdown now and then. Allocation free
/// after construction.
/// </summary>
internal sealed class HoldFuzz
{
    private static readonly AnchorRef[] _targets = { PressRig.Mailbox, PressRig.Mailbox, PressRig.Container, AnchorRef.None, PressRig.Unregistered };
    private Pcg32 _rng = new(0x017_401DUL, 17UL);
    private int _seen;

    public PressRig Rig { get; } = new(withHold: true);

    public int Presses { get; private set; }

    /// <summary>True when some tick ended with <paramref name="reason"/> as the last cancel.</summary>
    public bool HasSeen(CancelReason reason) => (_seen & (1 << (int)reason)) != 0;

    public void Step()
    {
        uint r = _rng.Next32();
        int edges = r % 4 == 0 ? (int)((r >> 2) % 3) + 1 : 0;
        for (int i = 0; i < edges; i++)
        {
            uint e = _rng.Next32();
            if (e % 16 < 2)
            {
                Rig.Edge(FrameAction.Interact, e % 16 == 0 ? EdgeKind.Release : EdgeKind.Cancel, AnchorRef.None);
            }
            else if (e % 16 < 8)
            {
                Rig.Press(_targets[(e >> 8) % (uint)_targets.Length]);
                Presses++;
            }
        }

        uint s = _rng.Next32();
        Rig.Source.Feet = new Vec3Mm(FocusRig.Feet.X + (int)(s % 600) - 300, FocusRig.Feet.Y, FocusRig.Feet.Z + (int)((s >> 10) % 1000) - 90);
        uint k = (s >> 20) % 64;
        Rig.Source.SetStatus(k switch
        {
            0 => PressRig.Status(panel: true),
            1 or 2 => PressRig.Status(swimming: true),
            3 => PressRig.Status(BodyContext.Dead),
            _ => PressRig.Status(),
        });
        Rig.Predictor.Next = (s >> 26) % 16 == 0 ? PressRig.Lob() : default;
        Rig.Requests.Accept = (s >> 28) % 8 != 0;
        Rig.Tick();
        _seen |= 1 << (int)Rig.Interaction.LastCancel;
    }
}
