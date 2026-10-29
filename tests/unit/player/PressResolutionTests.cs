using System.Runtime.InteropServices;
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
/// <see cref="Branch"/> is the owner state <see cref="PureHookGuard"/> watches; no hook writes it. Story 017's switches:
/// <see cref="Unavailable"/> (<c>IsAvailable</c> false with that key) and <see cref="CancelWith"/> (<c>ShouldCancel</c> true with that key).
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

    public InteractionContext Pressed { get; private set; }

    public StringId Unavailable { get; set; }

    public StringId CancelWith { get; set; }

    public int Builds { get; private set; }

    public bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        reason = Unavailable;
        return Unavailable.IsNone;
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
        reason = CancelWith;
        return !CancelWith.IsNone;
    }

    public void WritePrompt(in InteractionContext c, ref PromptArgs args)
    {
    }

    public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op)
    {
        Order = (Order % 100_000_000_000L * 10) + 3;
        Builds++;
        BuiltFrom = latch;
        op.Kind = InvOpKinds.Open;
        op.SetPayload(new OpenPayload { Container = latch.Anchor.Entity.Value, ClaimedX = latch.Claimed.X, ClaimedY = latch.Claimed.Y, ClaimedZ = latch.Claimed.Z });
        return true;
    }

    public void OnPress(in InteractionContext c, ref InteractionLatch latch)
    {
        Order = (Order % 100_000_000_000L * 10) + 1;
        Pressed = c;
        latch.OwnerPayload[0] = PayloadMark;
        latch.OwnerPayload[1] = (byte)Branch;
    }
}

/// <summary>
/// The press fixture: the <see cref="FocusRig"/> geometry (feet at <see cref="FocusRig.Feet"/>, chest 1000 mm up, anchors at chest
/// height <c>d</c> mm ahead) with a mailbox (Deliver, Hold), a container (Open, Tap), an unavailable anchor and a Dismount seat
/// behind the carrier, a <see cref="FocusStep"/> at 4a and the <see cref="EdgeResolveStep"/> at 4d, plus story 017's
/// <see cref="HoldAdvanceStep"/> at 4e when <c>withHold</c>. The two <see cref="PressOwner"/>s are wrapped in <see cref="PureHookGuard"/>.
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

    public PressRig(bool withHold = false)
    {
        Counters = new SimCounters();
        Settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        Anchors = new AnchorRegistry(new MapBounds(0, 0, 80_000, 80_000), 64, 2500, 400, Checks, Counters);
        Gate = new InteractionGate(new FrameworkReasons(FocusRig.DryLand, FocusRig.ClosePanel));
        Predictor = new FakeCatchPredictor();
        Resolver = new PressResolver(Anchors, Gate, Predictor, Settings);
        Source = new ScriptedFocusSource { Feet = FocusRig.Feet };
        Focus = new FocusStep(Source, FocusRig.FlatWorld(), Anchors, Gate, Settings, Counters);
        Step = new EdgeResolveStep(Source, Anchors, Gate, Settings, Input, Predictor, CatchArm, Requests, Counters, Checks, Self);
        var builder = new ClientLayerBuilder(Clock, Counters);
        builder.BindPreview(PreviewSubStep.Focus, Focus);
        builder.BindPreview(PreviewSubStep.EdgeResolve, Step);
        if (withHold)
        {
            Hold = new HoldAdvanceStep(Step, Focus, Source, Anchors, Settings, Input, CatchArm, Requests, Counters, Self);
            builder.BindPreview(PreviewSubStep.HoldAdvance, Hold);
        }

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

    public HoldAdvanceStep? Hold { get; }

    public SimCheckRing Checks { get; } = new();

    public ClientLayerSut Layer { get; }

    public FakeRequestSink Requests { get; } = new();

    public RigClock Clock { get; } = new();

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

    public void Add(AnchorRef a, Vec3Mm at, Verb verb, IInteractable? impl = null, AnchorFlags flags = AnchorFlags.None, int rangeMm = 0)
    {
        AnchorDef d = AnchorDef.For(a, at, verb);
        d.Flags = flags;
        d.RangeMm = rangeMm;
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
    public void Press(AnchorRef target) => Edge(FrameAction.Interact, EdgeKind.Press, target);

    /// <summary>Latches one edge of any action and kind carrying <paramref name="target"/>.</summary>
    public void Edge(FrameAction action, EdgeKind kind, AnchorRef target, EdgeReason reason = EdgeReason.None) =>
        Layer.InputWriter.PushEdge(new InputEdge(action, kind, ++_seq, 1, reason, targetEntity: target.Entity.Value, targetSlot: target.Slot));

    /// <summary>Latches one Interact release edge (<see cref="EdgeReason.ToggleSecondPress"/> is the router's hold-as-toggle second press).</summary>
    public void Release(EdgeReason reason = EdgeReason.None) => Edge(FrameAction.Interact, EdgeKind.Release, AnchorRef.None, reason);

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

    /// <summary>The host clock: invalid (hook <c>Tick</c> 0) until a test sets it.</summary>
    public sealed class RigClock : IHostClock
    {
        public uint EstimatedHostTick { get; set; }

        public bool IsValid { get; set; }
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
    [InlineData("a′: lob predicted into the presented mailbox (ADR-0014 §3 catch gate)", "mailbox", "mailbox", PressOutcome.Verb)]
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
    [InlineData(0, 2500, true, false)]
    [InlineData(0, 2501, false, false)]
    [InlineData(1000, 1000, true, false)]
    [InlineData(1000, 1001, false, false)]
    [InlineData(0, 2500, true, true)]
    [InlineData(0, 2501, false, true)]
    public void StartRange_IsTheAnchorsRangeMmFromTheChest_WithNoIncumbentSlack(int rangeMm, int distance, bool starts, bool throughTheStep)
    {
        // Arrange: a Deliver hold at chest height (rangeMm 0 is the registry's 2500); the feet are 1000 mm below the chest.
        var rig = new PressRig();
        AnchorRef a = FocusRig.Ref(7);
        rig.Add(a, FocusRig.Ahead(distance), Verb.Deliver, rangeMm: rangeMm);

        // Act + Assert
        if (!throughTheStep)
        {
            Assert.Equal(starts ? PressOutcome.Verb : PressOutcome.CatchArmed, rig.Resolve(a, PressRig.Status()).Outcome);
            return;
        }

        rig.Press(a);
        rig.Tick();
        Assert.Equal(starts ? (InteractState.Holding, a, 0) : (InteractState.Channel, AnchorRef.None, 1), (rig.Interaction.State, rig.Interaction.Latch.Anchor, rig.CatchArm.Downs));
    }

    [Theory]
    [Trait("Id", "H-F2-1")]
    [InlineData("unregistered", BodyContext.OnFoot, PressOutcome.CatchArmed)]
    [InlineData("TargetEntity 0", BodyContext.OnFoot, PressOutcome.CatchArmed)]
    [InlineData("registered only in Build", BodyContext.OnFoot, PressOutcome.CatchArmed)]
    [InlineData("registered only in Build, pressed in Build", BodyContext.Build, PressOutcome.Verb)]
    public void NoValidTarget_FallsToCatchArmed_ABuildOnlyAnchorResolvesInBuild(string row, BodyContext body, PressOutcome expected)
    {
        // Arrange
        var rig = new PressRig();
        AnchorRef target = row switch { "unregistered" => PressRig.Unregistered, "TargetEntity 0" => AnchorRef.None, _ => FocusRig.Ref(5) };
        rig.Add(FocusRig.Ref(5), FocusRig.Ahead(1000), Verb.Deliver, flags: AnchorFlags.BuildModeOnly);

        // Act + Assert
        Assert.True(expected == rig.Resolve(target, PressRig.Status(body)).Outcome, row);
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
    [InlineData(InteractState.ToggleArmed)]
    public void Press_WhileAHoldChannelOrToggleIsInProgress_IsIgnored_EvenWithAPredictedCatch(InteractState state)
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
        rig.Tick();
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
        rig.Clock.EstimatedHostTick = 900;
        rig.Clock.IsValid = true;
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
        Assert.Equal((PressRig.Self, 900u), (rig.MailboxOwner.Pressed.Actor, rig.MailboxOwner.Pressed.Tick));   // hooks: the host-tick estimate (TD-170 item 4)
    }

    [Fact]
    [Trait("Id", "H-R4-6")]
    public void Press_LatchesTheEdgesTargetSlot_NotSlot0OfTheSameEntity_AndAChannelVerbStartsAChannel()
    {
        // Arrange: one entity, slot 0 a Deliver hold and slot 1 a Push (Channel, VerbCatalogue); the edge presents slot 1.
        var rig = new PressRig();
        var slot0 = new PressOwner(rig.Stats);
        var slot1 = new PressOwner(rig.Stats);
        rig.Add(FocusRig.Ref(6, 0), FocusRig.Ahead(1000), Verb.Deliver, slot0);
        rig.Add(FocusRig.Ref(6, 1), FocusRig.Ahead(1000, 300), Verb.Push, slot1);
        rig.Press(FocusRig.Ref(6, 1));

        // Act
        rig.Tick();

        // Assert: slot 1's OnPress only (no GetHoldTicks for a channel); slot 0 untouched.
        InteractionLatch l = rig.Interaction.Latch;
        Assert.Equal((InteractState.Channel, FocusRig.Ref(6, 1), (byte)1, Verb.Push, InteractMode.Channel), (rig.Interaction.State, l.Anchor, l.Anchor.Slot, l.Verb, l.Mode));
        Assert.Equal((1L, 0L, 0, 0), (slot1.Order, slot0.Order, rig.Requests.Attempts, rig.CatchArm.Downs));
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
        // Arrange: Tap the container, then press the mailbox (hold) twice, all in one frame. Not a palindrome: reversed, the
        // container press would come after the hold and send nothing.
        var rig = new PressRig();
        rig.Press(PressRig.Container);
        rig.Press(PressRig.Mailbox);
        rig.Press(PressRig.Mailbox);

        // Act
        rig.Tick();
        InteractionLatch latched = rig.Interaction.Latch;
        rig.Predictor.Next = PressRig.Lob();   // a lob arrives during the latched Deliver hold
        rig.Press(AnchorRef.None);
        rig.Tick();

        // Assert: one request, the hold on the mailbox kept, no catch, two ignored presses.
        Assert.Equal((1, 0), (rig.Requests.Sent, rig.CatchArm.Downs));
        Assert.Equal((InteractState.Holding, PressRig.Mailbox, latched.PressTick), (rig.Interaction.State, rig.Interaction.Latch.Anchor, rig.Interaction.Latch.PressTick));
        Assert.Equal((PressOwner.OnPressThenTryBuildRequest, 12L, 2L), (rig.ContainerOwner.Order, rig.MailboxOwner.Order, rig.Counters[CounterId.InteractPressIgnored]));
    }

    [Theory]
    [Trait("Id", "PCM-P12-1")]
    [InlineData("Interact: Press(container), Release, Cancel", FrameAction.Interact, "container", 1, CancelReason.None)]
    [InlineData("Aim: Press(container)", FrameAction.Aim, "container", 0, CancelReason.None)]
    [InlineData("Interact: Press(mailbox) Hold, Release, Cancel (story 017)", FrameAction.Interact, "mailbox", 0, CancelReason.Released)]
    public void OnlyInteractPressEdgesResolve_ReleaseCancelAndOtherActionsResolveNothing(string row, FrameAction action, string target, int sent, CancelReason cancel)
    {
        // Arrange: after a Tap the release and cancel find Idle and do nothing; after a Hold the release cancels it at 4d (story
        // 017, Evan approved 2026-10-29 (TD-186 item 1)) and the cancel edge then finds Idle. Resolving a press again would send.
        var rig = new PressRig();
        AnchorRef a = target == "mailbox" ? PressRig.Mailbox : PressRig.Container;
        rig.Edge(action, EdgeKind.Press, a);
        rig.Edge(action, EdgeKind.Release, a);
        rig.Edge(action, EdgeKind.Cancel, a);

        // Act
        rig.Tick();

        // Assert
        Assert.True((sent, 0, InteractState.Idle, cancel) == (rig.Requests.Attempts, rig.CatchArm.Downs, rig.Interaction.State, rig.Interaction.LastCancel), row);
    }

    [Theory]
    [Trait("Id", "H-R4-6")]
    [Trait("Id", "PC017-ONE-COMMIT")]
    [InlineData(true)]
    [InlineData(false)]
    public void PressReleasePress_InOneFrame_EndsTheHoldBeforeTheSecondPressResolves(bool release)
    {
        // Arrange: TD-171 A9, closed by Evan approved 2026-10-29 (TD-186 item 1): "Press A, Release, Press B" in arrival order.
        // Without the release, B is ignored behind the hold.
        var rig = new PressRig();
        rig.Press(PressRig.Mailbox);
        if (release)
        {
            rig.Release();
        }

        rig.Press(PressRig.Container);

        // Act
        rig.Tick();

        // Assert: with the release the Tap sends (and clears the hold's Released: Evan approved 2026-10-29 (TD-186 item 5)); without it the hold stays.
        Assert.Equal(release ? (1, InteractState.Idle, 0L, CancelReason.None) : (0, InteractState.Holding, 1L, CancelReason.None),
            (rig.Requests.Sent, rig.Interaction.State, rig.Counters[CounterId.InteractPressIgnored], rig.Interaction.LastCancel));
        Assert.Equal((1L, release ? 1L : 0L), (rig.Counters[CounterId.HoldsStarted], rig.Counters[CounterId.HoldsCancelled]));
    }

    [Theory]
    [Trait("Id", "H-R4-4")]
    [InlineData("unavailable target", "")]
    [InlineData("Dead", "dead")]
    public void UnavailableOrRefusedPress_FiresNothing_UnavailableRecordsTheReason(string row, string context)
    {
        // Arrange: a lob is inbound (into the unavailable target, so the ADR-0014 §3 catch gate blocks rule 1); a fall-through would catch-arm.
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
        TickSnapshot[] first = Run();
        TickSnapshot[] second = Run();

        Assert.Equal(first, second);
        TickSnapshot last = first[^1];
        Assert.True(last.Sent > 0 && last.Downs > 0 && last.Ignored > 0, "the script should send requests, arm catches and ignore presses");
        Assert.Contains(first, s => s.LastCancel == CancelReason.Unavailable);
        Assert.Contains(first, s => s.LastCancel == CancelReason.Busy);
    }

    private static TickSnapshot[] Run()
    {
        var rig = new PressRig();
        var seen = new TickSnapshot[300];
        for (int t = 0; t < seen.Length; t++)
        {
            rig.ScriptTick(t);
            ref InteractionStatus s = ref rig.Interaction;
            InteractionLatch l = s.Latch;
            OwnerPayload16 payload = l.OwnerPayload;
            Op op = rig.Requests.Last;
            seen[t] = new TickSnapshot(
                s.State, l.Anchor, l.Verb, l.Mode, l.PressTick, l.HoldTicks, l.Claimed, Convert.ToHexString(payload), s.LastCancel, s.LastCancelReason,
                rig.Requests.Attempts, rig.Requests.Sent, Convert.ToHexString(MemoryMarshal.AsBytes(new ReadOnlySpan<Op>(in op))), rig.CatchArm.Downs, rig.CatchArm.Ups,
                rig.Counters[CounterId.InteractPressIgnored]);
        }

        return seen;
    }

    /// <summary>One tick of the twin: the whole status and latch, the sink's attempts, sends and last op bytes, the sender's ups and downs.</summary>
    private readonly record struct TickSnapshot(
        InteractState State, AnchorRef Anchor, Verb Verb, InteractMode Mode, uint PressTick, int HoldTicks, ClaimedPosition Claimed, string OwnerPayload,
        CancelReason LastCancel, StringId LastCancelReason, int Attempts, int Sent, string LastOp, int Downs, int Ups, long Ignored);
}
