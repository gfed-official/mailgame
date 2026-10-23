using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>A scripted <see cref="ICatchPredictor"/>: returns <see cref="Next"/> and counts calls.</summary>
internal sealed class FakeCatchPredictor : ICatchPredictor
{
    public CatchPrediction Next { get; set; }

    public int Calls { get; private set; }

    public CatchPrediction Evaluate(in CatchQuery q)
    {
        Calls++;
        return Next;
    }
}

/// <summary>An in-memory <see cref="IRequestSink"/>: keeps the sent ops; <see cref="Accept"/> false is the local busy.</summary>
internal sealed class FakeRequestSink : IRequestSink
{
    private readonly Op[] _sent = new Op[64];

    public bool Accept { get; set; } = true;

    public int Attempts { get; private set; }

    public int Sent { get; private set; }

    public Op Last => _sent[(Sent - 1) & 63];

    public bool TrySend(in Op op)
    {
        Attempts++;
        if (!Accept)
        {
            return false;
        }

        _sent[Sent++ & 63] = op;
        return true;
    }
}

/// <summary>A counting <see cref="ICatchArmSender"/>.</summary>
internal sealed class FakeCatchArmSender : ICatchArmSender
{
    public bool Accept { get; set; } = true;

    public int Downs { get; private set; }

    public int Ups { get; private set; }

    public bool Send(bool down)
    {
        if (down)
        {
            Downs++;
        }
        else
        {
            Ups++;
        }

        return Accept;
    }
}

/// <summary>A mutable <see cref="IClientInputSettings"/> (M-MD9-1: 400 then 200).</summary>
internal sealed class MutableInputSettings : IClientInputSettings
{
    public int HoldSettingMs { get; set; } = DefaultClientInputSettings.DefaultHoldSettingMs;
}

/// <summary>A stub <see cref="IStatSheet"/> answering <see cref="Stat.DeliverInsertPm"/> only.</summary>
internal sealed class StubInsertSheet : IStatSheet
{
    public int InsertPm { get; set; }

    public uint Version => 0;

    public int Read(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0) =>
        stat == Stat.DeliverInsertPm ? InsertPm : 0;

    public StatExplain Explain(Stat stat, ushort qualifier = StatSheet.NoQualifier, PlayerSlot? slot = null, int transientAdd = 0) =>
        throw new NotSupportedException();

    public bool Has(RuleFlag flag, PlayerSlot? slot = null) => false;
}

/// <summary>
/// The owner fixture: <c>OnPress</c> writes <see cref="PayloadMark"/> into the payload; <c>GetHoldTicks</c> reads the stat and the
/// hold setting on every call (as <c>DeliverInteractable</c> does); <c>TryBuildRequest</c> builds an <see cref="InvOpKinds.Open"/>
/// with the latch's claim. <see cref="Order"/> logs the hook calls as digits (1 OnPress, 2 GetHoldTicks, 3 TryBuildRequest).
/// <see cref="Branch"/> is the owner state <see cref="PureHookGuard"/> watches; no hook writes it.
/// </summary>
internal sealed class PressOwner : IInteractable
{
    public const byte PayloadMark = 0xA5;

    /// <summary>The <see cref="Order"/> of a Tap: OnPress (1), then TryBuildRequest (3).</summary>
    public const long OnPressThenTryBuildRequest = 13;

    private readonly IStatSheet _stats;

    public PressOwner(IStatSheet stats)
    {
        _stats = stats;
    }

    public int Branch { get; set; } = 3;

    public long Order { get; private set; }

    public int HoldCalls { get; private set; }

    public InteractionLatch BuiltFrom { get; private set; }

    public bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return true;
    }

    public int GetHoldTicks(in InteractionLatch latch, in InteractionContext c)
    {
        Order = (Order % 100_000_000_000L * 10) + 2;
        HoldCalls++;
        HoldSettings s = c.HoldSettings;
        return Hold.HoldTicks(c.HoldBaseMs, _stats.Read(Stat.DeliverInsertPm, slot: c.Actor), c.HoldSettingMs, in s);
    }

    public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
    {
        reason = StringId.None;
        return false;
    }

    public void WritePrompt(in InteractionContext c, ref PromptArgs args)
    {
    }

    public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op)
    {
        Order = (Order % 100_000_000_000L * 10) + 3;
        BuiltFrom = latch;
        op.Kind = InvOpKinds.Open;
        op.SetPayload(new OpenPayload { Container = latch.Anchor.Entity.Value, ClaimedX = latch.Claimed.X, ClaimedY = latch.Claimed.Y, ClaimedZ = latch.Claimed.Z });
        return true;
    }

    public void OnPress(in InteractionContext c, ref InteractionLatch latch)
    {
        Order = (Order % 100_000_000_000L * 10) + 1;
        latch.OwnerPayload[0] = PayloadMark;
        latch.OwnerPayload[1] = (byte)Branch;
    }
}

/// <summary>
/// The press fixture: the <see cref="FocusRig"/> geometry (feet at <see cref="FocusRig.Feet"/>, chest 1000 mm up, anchors at chest
/// height <c>d</c> mm ahead) with a mailbox (Deliver, Hold), a container (Open, Tap), an unavailable anchor and a Dismount seat
/// behind the carrier, a <see cref="FocusStep"/> at 4a and the <see cref="EdgeResolveStep"/> at 4d. The two <see cref="PressOwner"/>s
/// are wrapped in <see cref="PureHookGuard"/>.
/// </summary>
internal sealed class PressRig
{
    public static readonly AnchorRef Mailbox = FocusRig.Ref(1);
    public static readonly AnchorRef Container = FocusRig.Ref(2);
    public static readonly AnchorRef Closed = FocusRig.Ref(3);
    public static readonly AnchorRef Seat = FocusRig.Ref(4);
    public static readonly AnchorRef Unregistered = FocusRig.Ref(99);
    public static readonly StringId ClosedReason = new(StringNamespace.Run, 903);
    public static readonly PlayerSlot Self = PlayerSlot.FromValue(1);
    private static readonly AnchorRef[] _scriptTargets = { Container, Mailbox, AnchorRef.None, Closed, Unregistered };

    private ushort _seq;

    public PressRig()
    {
        Counters = new SimCounters();
        Settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        Anchors = new AnchorRegistry(new MapBounds(0, 0, 80_000, 80_000), 64, 2500, 400, new SimCheckRing(), Counters);
        Gate = new InteractionGate(new FrameworkReasons(FocusRig.DryLand, FocusRig.ClosePanel));
        Predictor = new FakeCatchPredictor();
        Resolver = new PressResolver(Anchors, Gate, Predictor, Settings);
        Source = new ScriptedFocusSource { Feet = FocusRig.Feet };
        Focus = new FocusStep(Source, Anchors, Gate, Settings, Counters);
        Step = new EdgeResolveStep(Source, Anchors, Gate, Settings, Input, Predictor, CatchArm, Requests, Counters, Self);
        var builder = new ClientLayerBuilder(new NoClock(), Counters);
        builder.BindPreview(PreviewSubStep.Focus, Focus);
        builder.BindPreview(PreviewSubStep.EdgeResolve, Step);
        Layer = builder.Build();
        Layer.InputWriter.SetState(new InputState { FocusCosMinPm = 635 });
        MailboxOwner = new PressOwner(Stats);
        ContainerOwner = new PressOwner(Stats);
        Add(Mailbox, FocusRig.Ahead(2000), Verb.Deliver, new PureHookGuard(MailboxOwner, () => MailboxOwner.Branch));
        Add(Container, FocusRig.Ahead(1500, 500), Verb.Open, new PureHookGuard(ContainerOwner, () => ContainerOwner.Branch));
        Add(Closed, FocusRig.Ahead(1200, -500), Verb.Open, new CountingInteractable { Available = false, UnavailableReason = ClosedReason });
        Add(Seat, new Vec3Mm(FocusRig.Feet.X + 2000, 1000, FocusRig.Feet.Z + 1000), Verb.Dismount, new PressOwner(Stats), AnchorFlags.SeatOf);
    }

    public SimCounters Counters { get; }

    public PlayerSettings Settings { get; }

    public AnchorRegistry Anchors { get; }

    public InteractionGate Gate { get; }

    public FakeCatchPredictor Predictor { get; }

    public PressResolver Resolver { get; }

    public ScriptedFocusSource Source { get; }

    public FocusStep Focus { get; }

    public EdgeResolveStep Step { get; }

    public ClientLayerSut Layer { get; }

    public FakeRequestSink Requests { get; } = new();

    public FakeCatchArmSender CatchArm { get; } = new();

    public MutableInputSettings Input { get; } = new();

    public StubInsertSheet Stats { get; } = new();

    public PressOwner MailboxOwner { get; }

    public PressOwner ContainerOwner { get; }

    public ref InteractionStatus Interaction => ref Step.Interaction;

    public static Vec3Mm Chest => new(FocusRig.Feet.X, 1000, FocusRig.Feet.Z);

    public static CarrierStatus Status(BodyContext body = BodyContext.OnFoot, bool panel = false, bool swimming = false, AnchorRef seat = default) =>
        new() { Life = body == BodyContext.Dead ? LifeState.Dead : LifeState.Alive, Body = body, PanelOpen = panel, Swimming = swimming, OccupiedSeat = seat };

    public static CatchPrediction Lob(AnchorRef receiver = default) => new() { Predicted = 1, FlightId = 7, Units = 1, PredictedStaticReceiver = receiver };

    public void Add(AnchorRef a, Vec3Mm at, Verb verb, IInteractable? impl = null, AnchorFlags flags = AnchorFlags.None)
    {
        AnchorDef d = AnchorDef.For(a, at, verb);
        d.Flags = flags;
        Assert.Equal(RegisterResult.Ok, Anchors.Register(in d, impl ?? new CountingInteractable()));
    }

    public PressResolution Resolve(AnchorRef presented, CarrierStatus status, InteractState state = InteractState.Idle)
    {
        var q = new CatchQuery { Tick = 5, Self = Self, Chest = Chest, Presented = presented };
        var press = new InteractionContext { Tick = 5, Actor = Self, ChestMm = Chest };
        SimCounterSink sink = Counters.PrimitiveSink();
        return Resolver.Resolve(state, in status, in q, in press, ref sink);
    }

    /// <summary>Latches one Interact press edge presenting <paramref name="target"/> (<see cref="AnchorRef.None"/>: <c>TargetEntity</c> 0).</summary>
    public void Press(AnchorRef target) =>
        Layer.InputWriter.PushEdge(new InputEdge(FrameAction.Interact, EdgeKind.Press, ++_seq, 1, targetEntity: target.Entity.Value, targetSlot: target.Slot));

    /// <summary>Runs one client tick (the first of its batch); returns the tick it ran.</summary>
    public uint Tick()
    {
        uint t = Layer.ClientTick;
        Layer.Step(true);
        return t;
    }

    /// <summary>
    /// One tick of the mixed 300-tick press script (the determinism twin and the 0 B test): a press every other tick cycling the
    /// container (Tap), the mailbox (Hold), no target, the unavailable anchor and an unregistered ref, a second press now and then
    /// (ignored during a hold), a lob every 7th tick, a busy sink every 11th, the panel every 50th tick and a swim spell. Every
    /// third tick a held interaction returns to Idle, standing in for story 017's release.
    /// </summary>
    public void ScriptTick(int t)
    {
        Source.SetStatus(Status(panel: t % 50 == 49, swimming: t % 70 >= 60));
        Predictor.Next = t % 7 == 0 ? Lob() : default;
        Requests.Accept = t % 11 != 0;
        if (t % 3 == 0)
        {
            Interaction.State = InteractState.Idle;
        }

        if (t % 2 == 0)
        {
            Press(_scriptTargets[t / 2 % _scriptTargets.Length]);
            if (t % 10 == 4)
            {
                Press(Mailbox);
            }
        }

        Tick();
    }

    private sealed class NoClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => false;
    }
}

/// <summary>
/// Player Character story 016 (ADR-0014 section 3; input-controls R4.3, G1, H-R4-2/4/6; GDD P11, P12): press resolution, the
/// latch and Tap requests. D1–D6 are Mail Lead defaults, pending Evan (TD-171).
/// </summary>
public class PressResolutionTests
{
    // ---------------------------------------------------------------- resolution order (PressResolver)

    [Theory]
    [Trait("Id", "H-R4-2")]
    [Trait("Id", "PCM-P11-2b")]
    [InlineData("a: lob reaches the carrier, not the focused mailbox", "mailbox", "elsewhere", PressOutcome.Catch)]
    [InlineData("a′: lob predicted into the presented mailbox (D3 gate)", "mailbox", "mailbox", PressOutcome.Verb)]
    [InlineData("b: container", "container", "none", PressOutcome.Verb)]
    [InlineData("c: mailbox", "mailbox", "none", PressOutcome.Verb)]
    [InlineData("d: no target", "none", "none", PressOutcome.CatchArmed)]
    [InlineData("lob with no target and no static receiver", "none", "free", PressOutcome.Catch)]
    public void Press_ResolvesCatch_ThenThePresentedVerb_ThenCatchArmed(string row, string presented, string lob, PressOutcome expected)
    {
        // Arrange
        var rig = new PressRig();
        AnchorRef target = presented switch { "mailbox" => PressRig.Mailbox, "container" => PressRig.Container, _ => AnchorRef.None };
        rig.Predictor.Next = lob switch
        {
            "elsewhere" => PressRig.Lob(FocusRig.Ref(50)),
            "mailbox" => PressRig.Lob(PressRig.Mailbox),
            "free" => PressRig.Lob(),
            _ => default,
        };

        // Act
        PressResolution r = rig.Resolve(target, PressRig.Status());

        // Assert
        Assert.True(expected == r.Outcome, row);
        Assert.Equal(expected == PressOutcome.Verb, r.Index >= 0 && rig.Anchors.DefAt(r.Index).Ref == target);
    }

    [Theory]
    [Trait("Id", "H-F2-1")]
    [InlineData(2500, PressOutcome.Verb)]
    [InlineData(2501, PressOutcome.CatchArmed)]
    public void StartRange_IsRangeMmFromTheChest_WithNoIncumbentSlack(int distance, PressOutcome expected)
    {
        // Arrange
        var rig = new PressRig();
        rig.Anchors.Move(PressRig.Mailbox, FocusRig.Ahead(distance));

        // Act + Assert
        Assert.Equal(expected, rig.Resolve(PressRig.Mailbox, PressRig.Status()).Outcome);
    }

    [Theory]
    [Trait("Id", "H-F2-1")]
    [InlineData("unregistered")]
    [InlineData("TargetEntity 0")]
    [InlineData("registered only in Build")]
    public void NoValidTarget_FallsToCatchArmed(string row)
    {
        // Arrange
        var rig = new PressRig();
        AnchorRef target = row switch { "unregistered" => PressRig.Unregistered, "TargetEntity 0" => AnchorRef.None, _ => FocusRig.Ref(5) };
        rig.Add(FocusRig.Ref(5), FocusRig.Ahead(1000), Verb.Deliver, flags: AnchorFlags.BuildModeOnly);

        // Act + Assert
        Assert.Equal(PressOutcome.CatchArmed, rig.Resolve(target, PressRig.Status()).Outcome);
    }

    [Theory]
    [Trait("Id", "H-R4-4")]
    [InlineData("owner IsAvailable false", false)]
    [InlineData("gate dry_land while swimming", true)]
    public void UnavailableTarget_ResolvesUnavailableWithTheReason_AndNeverCatchArms(string row, bool swimming)
    {
        // Arrange: a lob is predicted into the target too, so only "no fall-through" can give Unavailable.
        var rig = new PressRig();
        AnchorRef target = swimming ? PressRig.Mailbox : PressRig.Closed;
        rig.Predictor.Next = PressRig.Lob(target);

        // Act
        PressResolution r = rig.Resolve(target, PressRig.Status(swimming: swimming));

        // Assert
        Assert.True((PressOutcome.Unavailable, swimming ? FocusRig.DryLand : PressRig.ClosedReason) == (r.Outcome, r.Reason), row);
    }

    [Theory]
    [Trait("Id", "PCM-P11-2b")]
    [InlineData(InteractState.Holding)]
    [InlineData(InteractState.Channel)]
    public void Press_WhileAHoldOrChannelIsInProgress_IsIgnored_EvenWithAPredictedCatch(InteractState state)
    {
        var rig = new PressRig();
        rig.Predictor.Next = PressRig.Lob();

        Assert.Equal(PressOutcome.Ignored, rig.Resolve(PressRig.Container, PressRig.Status(), state).Outcome);
    }

    [Theory]
    [InlineData("Dead, no target, lob inbound", "dead", "none", PressOutcome.Refused)]
    [InlineData("Panel, container presented", "panel", "container", PressOutcome.Refused)]
    [InlineData("Mounted, a container presented", "mounted", "container", PressOutcome.Refused)]
    [InlineData("Mounted, own-seat Dismount presented", "mounted", "seat", PressOutcome.Verb)]
    public void DeadPanelMounted_RefuseTheEdge_ExceptTheOwnSeatDismount(string row, string context, string presented, PressOutcome expected)
    {
        // Arrange: D6 — Mail Lead default, pending Evan (TD-171).
        var rig = new PressRig();
        rig.Predictor.Next = PressRig.Lob();
        CarrierStatus status = context switch
        {
            "dead" => PressRig.Status(BodyContext.Dead),
            "panel" => PressRig.Status(panel: true),
            _ => PressRig.Status(BodyContext.Mounted, seat: PressRig.Seat),
        };
        AnchorRef target = presented switch { "container" => PressRig.Container, "seat" => PressRig.Seat, _ => AnchorRef.None };

        // Act + Assert
        Assert.True(expected == rig.Resolve(target, status).Outcome, row);
    }

    // ---------------------------------------------------------------- EdgeResolveStep

    [Theory]
    [Trait("Id", "H-R4-2")]
    [Trait("Id", "PC016-SWIM-CATCH")]
    [InlineData("a: Catch on land", true, false)]
    [InlineData("d: catch-armed on land", false, false)]
    [InlineData("Catch while swimming", true, true)]
    [InlineData("catch-armed while swimming", false, true)]
    public void CatchAndCatchArmed_SendOneCatchArmDown_AndStartAnAnchorlessChannel_SwimmingOrNot(string row, bool lob, bool swimming)
    {
        // Arrange: Evan's swim-catch ruling (2026-10-20 22:05 PT): no client-side suppression.
        var rig = new PressRig();
        rig.Source.SetStatus(PressRig.Status(swimming: swimming));
        rig.Predictor.Next = lob ? PressRig.Lob(FocusRig.Ref(50)) : default;
        rig.Press(lob ? PressRig.Mailbox : AnchorRef.None);

        // Act
        uint tick = rig.Tick();

        // Assert
        InteractionLatch l = rig.Interaction.Latch;
        Assert.True((1, 0, 0) == (rig.CatchArm.Downs, rig.CatchArm.Ups, rig.Requests.Attempts), row);
        Assert.Equal((InteractState.Channel, AnchorRef.None, Verb.Catch, InteractMode.Channel, tick), (rig.Interaction.State, l.Anchor, l.Verb, l.Mode, l.PressTick));
    }

    [Theory]
    [Trait("Id", "H-R4-2")]
    [Trait("Id", "PCM-P12-1")]
    [InlineData(true)]
    [InlineData(false)]
    public void TapPress_SendsExactlyOneRequestThatTick_CarryingTheFeet_OrRecordsBusy(bool accept)
    {
        // Arrange: H-R4-2 (b), the container's Open Tap.
        var rig = new PressRig();
        rig.Source.Feet = new Vec3Mm(FocusRig.Feet.X + 7, -3, FocusRig.Feet.Z + 11);
        rig.Requests.Accept = accept;
        rig.Press(PressRig.Container);

        // Act
        uint tick = rig.Tick();
        rig.Tick();

        // Assert: one attempt; the transient latch had PressTick, Claimed and the OnPress payload before TryBuildRequest.
        InteractionLatch built = rig.ContainerOwner.BuiltFrom;
        Assert.Equal((1, accept ? 1 : 0, PressOwner.OnPressThenTryBuildRequest), (rig.Requests.Attempts, rig.Requests.Sent, rig.ContainerOwner.Order));
        Assert.Equal((tick, PressOwner.PayloadMark), (built.PressTick, built.OwnerPayload[0]));
        Assert.Equal((InteractState.Idle, default(InteractionLatch).Anchor, accept ? CancelReason.None : CancelReason.Busy), (rig.Interaction.State, rig.Interaction.Latch.Anchor, rig.Interaction.LastCancel));
        if (accept)
        {
            OpenPayload p = rig.Requests.Last.PayloadAs<OpenPayload>();
            Assert.Equal((InvOpKinds.Open, PressRig.Container.Entity.Value), (rig.Requests.Last.Kind, p.Container));
            Assert.Equal((FocusRig.Feet.X + 7, -3, FocusRig.Feet.Z + 11), (p.ClaimedX, p.ClaimedY, p.ClaimedZ));
        }
    }

    [Fact]
    [Trait("Id", "H-R4-2")]
    [Trait("Id", "H-R4-6")]
    public void HoldPress_LatchesTheEdgesPresentedMailbox_NotTheFocusedContainer_OnPressThenGetHoldTicksOnce()
    {
        // Arrange: the container is focused on the press tick; the edge's presented snapshot still names the mailbox (G1).
        var rig = new PressRig();
        rig.Anchors.Move(PressRig.Container, FocusRig.Ahead(700));
        rig.Tick();
        rig.Press(PressRig.Mailbox);

        // Act
        uint tick = rig.Tick();

        // Assert
        InteractionLatch l = rig.Interaction.Latch;
        Assert.Equal((PressRig.Container, false), (rig.Focus.State.Focused, rig.Focus.State.Latched));   // 016 leaves Latched to 017
        Assert.Equal((InteractState.Holding, PressRig.Mailbox, Verb.Deliver, InteractMode.Hold, tick), (rig.Interaction.State, l.Anchor, l.Verb, l.Mode, l.PressTick));
        Assert.Equal((FocusRig.Feet.X, FocusRig.Feet.Y, FocusRig.Feet.Z), (l.Claimed.X, l.Claimed.Y, l.Claimed.Z));
        Assert.Equal((PressOwner.PayloadMark, 12, 12L), (l.OwnerPayload[0], l.HoldTicks, rig.MailboxOwner.Order));
        Assert.Equal((0, 0, 0), (rig.Requests.Attempts, rig.CatchArm.Downs, rig.ContainerOwner.HoldCalls));
    }

    [Theory]
    [Trait("Id", "M-MD9-1")]
    [InlineData("hold setting 400 to 200")]
    [InlineData("Quick Hands: DeliverInsertPm 0 to 500")]
    public void LatchedHoldTicks_IgnoreASettingOrStatChangeAfterThePress(string row)
    {
        // Arrange: a Deliver hold at the default setting, no perk, latches 12.
        var rig = new PressRig();
        rig.Press(PressRig.Mailbox);
        rig.Tick();

        // Act
        if (row.StartsWith("hold", StringComparison.Ordinal))
        {
            rig.Input.HoldSettingMs = 200;
        }
        else
        {
            rig.Stats.InsertPm = 500;
        }

        var heldTicks = new int[10];
        for (int i = 0; i < heldTicks.Length; i++)
        {
            rig.Tick();
            heldTicks[i] = rig.Interaction.Latch.HoldTicks;
        }

        // Assert: still 12, GetHoldTicks never called again, while a fresh call now gives a different length.
        Assert.All(heldTicks, t => Assert.Equal(12, t));
        Assert.Equal(1, rig.MailboxOwner.HoldCalls);
        InteractionLatch l = rig.Interaction.Latch;
        var c = new InteractionContext { Actor = PressRig.Self, HoldBaseMs = 400, HoldSettingMs = rig.Input.HoldSettingMs, HoldSettings = HoldSettings.FromPlayerSettings(rig.Settings) };
        Assert.True(rig.MailboxOwner.GetHoldTicks(in l, in c) != 12, row);
    }

    [Fact]
    [Trait("Id", "PCM-P11-2b")]
    [Trait("Id", "H-R4-6")]
    public void EdgesInOneFrame_ApplyInArrivalOrder_AndPressesDuringTheHoldAreIgnoredAndCounted_EvenWithACatchInbound()
    {
        // Arrange: Tap the container, then press the mailbox (hold), then the container again, all in one frame.
        var rig = new PressRig();
        rig.Press(PressRig.Container);
        rig.Press(PressRig.Mailbox);
        rig.Press(PressRig.Container);

        // Act
        rig.Tick();
        InteractionLatch latched = rig.Interaction.Latch;
        rig.Predictor.Next = PressRig.Lob();   // a lob arrives during the latched Deliver hold
        rig.Press(AnchorRef.None);
        rig.Tick();

        // Assert: one request, the hold on the mailbox kept, no catch, two ignored presses.
        Assert.Equal((1, 0), (rig.Requests.Sent, rig.CatchArm.Downs));
        Assert.Equal((InteractState.Holding, PressRig.Mailbox, latched.PressTick), (rig.Interaction.State, rig.Interaction.Latch.Anchor, rig.Interaction.Latch.PressTick));
        Assert.Equal((PressOwner.OnPressThenTryBuildRequest, 2L), (rig.ContainerOwner.Order, rig.Counters[CounterId.InteractPressIgnored]));
    }

    [Theory]
    [Trait("Id", "H-R4-4")]
    [InlineData("unavailable target", "")]
    [InlineData("Dead", "dead")]
    public void UnavailableOrRefusedPress_FiresNothing_UnavailableRecordsTheReason(string row, string context)
    {
        // Arrange: a lob is inbound (into the unavailable target, so the D3 gate blocks rule 1); a fall-through would catch-arm.
        var rig = new PressRig();
        rig.Predictor.Next = PressRig.Lob(context.Length == 0 ? PressRig.Closed : AnchorRef.None);
        rig.Source.SetStatus(context.Length == 0 ? PressRig.Status() : PressRig.Status(BodyContext.Dead));
        rig.Press(context.Length == 0 ? PressRig.Closed : PressRig.Container);

        // Act
        rig.Tick();

        // Assert
        (CancelReason, StringId) expected = context.Length == 0 ? (CancelReason.Unavailable, PressRig.ClosedReason) : (CancelReason.None, StringId.None);
        Assert.True((0, 0, 0L) == (rig.Requests.Attempts, rig.CatchArm.Downs, rig.ContainerOwner.Order), row);
        Assert.Equal((InteractState.Idle, 0L), (rig.Interaction.State, rig.Counters[CounterId.InteractPressIgnored]));
        Assert.Equal(expected, (rig.Interaction.LastCancel, rig.Interaction.LastCancelReason));
    }

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void TwoFreshSteps_OverTheSame300TickScript_GiveIdenticalStateLatchAndCallsPerTick()
    {
        (InteractState, AnchorRef, uint, int, CancelReason, int, int, long)[] first = Run();
        (InteractState, AnchorRef, uint, int, CancelReason, int, int, long)[] second = Run();

        Assert.Equal(first, second);
        var last = first[^1];
        Assert.True(last.Item6 > 0 && last.Item7 > 0 && last.Item8 > 0, "the script should send requests, arm catches and ignore presses");
        Assert.Contains(first, s => s.Item5 == CancelReason.Unavailable);
        Assert.Contains(first, s => s.Item5 == CancelReason.Busy);
    }

    private static (InteractState, AnchorRef, uint, int, CancelReason, int, int, long)[] Run()
    {
        var rig = new PressRig();
        var seen = new (InteractState, AnchorRef, uint, int, CancelReason, int, int, long)[300];
        for (int t = 0; t < seen.Length; t++)
        {
            rig.ScriptTick(t);
            ref InteractionStatus s = ref rig.Interaction;
            seen[t] = (s.State, s.Latch.Anchor, s.Latch.PressTick, s.Latch.HoldTicks, s.LastCancel, rig.Requests.Sent, rig.CatchArm.Downs, rig.Counters[CounterId.InteractPressIgnored]);
        }

        return seen;
    }
}
