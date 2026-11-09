using System.Runtime.InteropServices;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery.Client;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run.Events;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>The scripted <see cref="IDeliverClientSource"/>: a selected slot, a shift and a set of pending own op kinds.</summary>
internal sealed class ScriptedDeliverSource : IDeliverClientSource
{
    public int SelectedSlot { get; set; }

    public int CurrentShift { get; set; } = DeliverClientRig.Shift;

    public HashSet<OpKind> Pending { get; } = new();

    public bool IsPending(OpKind kind) => Pending.Contains(kind);
}

/// <summary>A host clock whose estimate is <c>clientTick + Offset</c>, set by <see cref="DeliverClientRig.Tick"/> before each step.</summary>
internal sealed class OffsetHostClock : IHostClock
{
    public uint EstimatedHostTick { get; set; }

    public bool IsValid => true;
}

/// <summary>
/// The client half of Manual Delivery on one ClientLayer (manual-delivery stories 003, 004, 010): Players' <see cref="FocusStep"/> (4a),
/// the <see cref="DeliverPreviewStep"/> (4b), <see cref="EdgeResolveStep"/> (4d), <see cref="HoldAdvanceStep"/> and the
/// <see cref="InteractionSnapshotPublisher"/> (4e), over the <see cref="FocusRig"/> geometry. Two Deliver anchors stand for the first two
/// M-1a street lots: <see cref="Home"/> 2 m straight ahead and <see cref="Next"/> 2 m ahead, 1.5 m to the side; the destination mirror
/// is OPEN with District 1 unlocked, the wallet 0. The client hooks are wrapped in <see cref="PureHookGuard"/> over the published state.
/// </summary>
internal sealed class DeliverClientRig
{
    public const int Shift = 3;
    public const int HoldBaseMs = 400;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();
    private static readonly WorldTables _street = BuildStreet();
    private ushort _seq;

    public DeliverClientRig(int offset = 0)
    {
        Offset = offset;
        Counters = new SimCounters();
        PlayerSettings settings = PlayerSettings.FromBalance(_content.Player);
        Anchors = new AnchorRegistry(new MapBounds(0, 0, 80_000, 80_000), 64, 2500, 400, new SimCheckRing(), Counters);
        var gate = new InteractionGate(new FrameworkReasons(FocusRig.DryLand, FocusRig.ClosePanel));
        Carrier = new ScriptedFocusSource { Feet = FocusRig.Feet };
        Focus = new FocusStep(Carrier, FocusRig.FlatWorld(), Anchors, gate, settings, Counters);
        Press = new EdgeResolveStep(Carrier, Anchors, gate, settings, new MutableInputSettings(), new FakeCatchPredictor(), new FakeCatchArmSender(), Requests, Counters, new SimCheckRing(), Self);
        var hold = new HoldAdvanceStep(Press, Focus, Carrier, Anchors, settings, new MutableInputSettings(), new FakeCatchArmSender(), Requests, Counters, Self);
        Publisher = new InteractionSnapshotPublisher(Focus, Press, Carrier, Anchors, settings, new MutableInputSettings(), Self);
        Destinations = new DestinationMirror(_street, _content);
        SetDeliveries(open: true, unlockedMask: 1UL);
        Preview = new DeliverPreviewStep(Focus, Press, Anchors, Carrier, Panel, Source, Destinations, Wallet, _street, _content, StatSheet.Build(_content, new StatSources(ReadOnlySpan<Modifier>.Empty)));
        var builder = new ClientLayerBuilder(Clock, Counters);
        builder.BindPreview(PreviewSubStep.Focus, Focus);
        builder.BindPreview(PreviewSubStep.DeliverPreview, Preview);
        builder.BindPreview(PreviewSubStep.EdgeResolve, Press);
        builder.BindPreview(PreviewSubStep.HoldAdvance, hold);
        builder.BindPreview(PreviewSubStep.HoldAdvance, Publisher);
        Layer = builder.Build();
        Layer.InputWriter.SetState(new InputState { FocusCosMinPm = 635 });
        Home = new AnchorRef(Destinations.DestinationAt(0), 0);
        Next = new AnchorRef(Destinations.DestinationAt(1), 0);
        Hooks = new PureHookGuard(Preview.Interactable, StateHash);
        Register(Home, FocusRig.Ahead(2000));
        Register(Next, FocusRig.Ahead(2000, 1500));
    }

    public static PlayerSlot Self { get; } = PlayerSlot.FromValue(1);

    public int Offset { get; set; }

    public SimCounters Counters { get; }

    public AnchorRegistry Anchors { get; }

    public ScriptedFocusSource Carrier { get; }

    public FocusStep Focus { get; }

    public EdgeResolveStep Press { get; }

    public InteractionSnapshotPublisher Publisher { get; }

    public DeliverPreviewStep Preview { get; }

    public IInteractable Hooks { get; }

    public DestinationMirror Destinations { get; }

    public WalletMirror Wallet { get; } = new();

    public InMemoryPanelView Panel { get; } = new();

    public ScriptedDeliverSource Source { get; } = new();

    public FakeRequestSink Requests { get; } = new();

    public OffsetHostClock Clock { get; } = new();

    public ClientLayerSut Layer { get; }

    public AnchorRef Home { get; }

    public AnchorRef Next { get; }

    public AddressId HomeAddress => _street.Lots[0].Address;

    public AddressId NextAddress => _street.Lots[1].Address;

    /// <summary>The address of a third lot, wrong for both <see cref="Home"/> and <see cref="Next"/>.</summary>
    public AddressId FarAddress => _street.Lots[2].Address;

    /// <summary>The display string of lot <paramref name="i"/>.</summary>
    public StringId DisplayOf(int i) => _street.Lots[i].Display;

    public ref readonly DeliverPreviewState State => ref Preview.Current;

    public ref InteractionStatus Interaction => ref Press.Interaction;

    /// <summary>A one-unit letter (value 8, deadline <see cref="Shift"/>) addressed to <paramref name="address"/>.</summary>
    public PanelEntryFacts Letter(AddressId address) => Panel.Mail(DeliverPreviewTests.KindIndex("letter"), address, 0, (Shift, 1, 8));

    /// <summary>Applies a <c>DeliveriesState</c> to the destination mirror, as <c>MirrorDrain</c> would.</summary>
    public void SetDeliveries(bool open, ulong unlockedMask, uint closeTick = 0)
    {
        var p = new DeliveriesStatePayload { Open = open ? (byte)1 : (byte)0, UnlockedMask = unlockedMask, CloseTick = closeTick };
        var e = new EventEntry { Kind = RunEventKinds.DeliveriesState, PayloadLength = (byte)Marshal.SizeOf<DeliveriesStatePayload>() };
        MemoryMarshal.Write(e.Payload, in p);
        Destinations.Apply(in e, default);
    }

    /// <summary>Applies a <c>WalletChanged</c> with <paramref name="cents"/> to the wallet mirror.</summary>
    public void SetWallet(long cents)
    {
        var p = new WalletChangedPayload { Balance = cents };
        var e = new EventEntry { Kind = EconEventKinds.WalletChanged, PayloadLength = (byte)Marshal.SizeOf<WalletChangedPayload>() };
        MemoryMarshal.Write(e.Payload, in p);
        Wallet.Apply(in e, default);
    }

    /// <summary><paramref name="e"/> with the same entry id and address, new flags and a new one-unit value (a payload change, not a key change).</summary>
    public static PanelEntryFacts Restyle(in PanelEntryFacts e, byte flags, ushort value) =>
        new(e.EntryId, e.KindKey, e.Category, e.Address, flags, e.Units, new[] { new PanelHintRun(e.Run(0).DeadlineShift, 1, value) });

    public void SetStatus(bool panel = false, bool swimming = false) =>
        Carrier.SetStatus(new CarrierStatus { Life = LifeState.Alive, Body = BodyContext.OnFoot, PanelOpen = panel, Swimming = swimming });

    /// <summary>Latches an Interact press presenting <paramref name="target"/> for the next tick.</summary>
    public void PressEdge(AnchorRef target) =>
        Layer.InputWriter.PushEdge(new InputEdge(FrameAction.Interact, EdgeKind.Press, ++_seq, 1, EdgeReason.None, targetEntity: target.Entity.Value, targetSlot: target.Slot));

    /// <summary>Runs one client tick with the host estimate at <c>clientTick + Offset</c>; returns the tick it ran.</summary>
    public uint Tick()
    {
        uint t = Layer.ClientTick;
        Clock.EstimatedHostTick = unchecked(t + (uint)Offset);
        Layer.Step(true);
        return t;
    }

    /// <summary>Runs ticks until <see cref="State"/> is about <paramref name="anchor"/> (the focus dwell), at most 10.</summary>
    public void FocusOn(AnchorRef anchor)
    {
        for (int i = 0; i < 10 && !(State.Active && State.Destination == anchor.Entity); i++)
        {
            Tick();
        }

        Assert.Equal(anchor.Entity, State.Destination);
    }

    /// <summary><c>IsAvailable</c> on <paramref name="anchor"/> through the guarded hooks with this tick's host estimate.</summary>
    public bool IsAvailable(AnchorRef anchor, out StringId reason)
    {
        var c = new InteractionContext { Tick = Clock.EstimatedHostTick, Actor = Self, Anchor = anchor, Verb = Verb.Deliver, Mode = InteractMode.Hold };
        return Hooks.IsAvailable(in c, out reason);
    }

    private static WorldTables BuildStreet()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private void Register(AnchorRef anchor, Vec3Mm at)
    {
        AnchorDef d = AnchorDef.For(anchor, at, Verb.Deliver);
        d.HoldBaseMs = HoldBaseMs;
        Assert.Equal(RegisterResult.Ok, Anchors.Register(in d, Hooks));
    }

    private int StateHash()
    {
        ref readonly DeliverPreviewState s = ref Preview.Current;
        return HashCode.Combine(s.Tick, s.Destination, s.Branch.Kind, s.Branch.K, s.Row, s.DisplayedTicks, s.CanStartHoldNext, Preview.Scratch.Evaluations);
    }
}
