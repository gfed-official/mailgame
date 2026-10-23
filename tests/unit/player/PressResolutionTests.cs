using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

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

/// <summary>
/// The press fixture: the <see cref="FocusRig"/> geometry (feet at <see cref="FocusRig.Feet"/>, chest 1000 mm up, anchors at chest
/// height <c>d</c> mm ahead) with a mailbox (Deliver, Hold), a container (Open, Tap), an unavailable anchor and an own-seat Dismount.
/// </summary>
internal sealed class PressRig
{
    public static readonly AnchorRef Mailbox = FocusRig.Ref(1);
    public static readonly AnchorRef Container = FocusRig.Ref(2);
    public static readonly AnchorRef Closed = FocusRig.Ref(3);
    public static readonly AnchorRef Seat = FocusRig.Ref(4);
    public static readonly AnchorRef Unregistered = FocusRig.Ref(99);
    public static readonly StringId ClosedReason = new(StringNamespace.Run, 903);

    public PressRig()
    {
        Counters = new SimCounters();
        Settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        Anchors = new AnchorRegistry(new MapBounds(0, 0, 80_000, 80_000), 64, 2500, 400, new SimCheckRing(), Counters);
        Gate = new InteractionGate(new FrameworkReasons(FocusRig.DryLand, FocusRig.ClosePanel));
        Predictor = new FakeCatchPredictor();
        Resolver = new PressResolver(Anchors, Gate, Predictor, Settings);
        Add(Mailbox, FocusRig.Ahead(2000), Verb.Deliver);
        Add(Container, FocusRig.Ahead(1500, 500), Verb.Open);
        Add(Closed, FocusRig.Ahead(1200, -500), Verb.Open, new CountingInteractable { Available = false, UnavailableReason = ClosedReason });
        Add(Seat, FocusRig.Ahead(600), Verb.Dismount, flags: AnchorFlags.SeatOf);
    }

    public SimCounters Counters { get; }

    public PlayerSettings Settings { get; }

    public AnchorRegistry Anchors { get; }

    public InteractionGate Gate { get; }

    public FakeCatchPredictor Predictor { get; }

    public PressResolver Resolver { get; }

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
        var q = new CatchQuery { Tick = 5, Self = PlayerSlot.FromValue(1), Chest = Chest, Presented = presented };
        var press = new InteractionContext { Tick = 5, Actor = PlayerSlot.FromValue(1), ChestMm = Chest };
        SimCounterSink sink = Counters.PrimitiveSink();
        return Resolver.Resolve(state, in status, in q, in press, ref sink);
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
    [InlineData("Mounted, no target, lob inbound", "mounted", "none", PressOutcome.Refused)]
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
}
