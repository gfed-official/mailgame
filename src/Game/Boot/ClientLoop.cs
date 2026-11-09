using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Inventory;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition.Client;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery.Client;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The host client's interaction loop (WP-F), composed at boot step 5 next to the carrier step and the mirrors: the own-panel mirror
/// (<see cref="OwnPanelMirror"/>, the local player's panel ids from <see cref="ContainerIdPlan"/>), the destination mirror, the
/// pending-request tracker in front of the request port, the client interaction steps (<see cref="ClientInteractionBinder"/>), the
/// Intake pickup sequence and the hotbar selection sender. Everything it reads arrives through the client adapter (records, replies) or
/// is the client's own state; it never reads <c>SimWorld</c> or a host table. <see cref="Attach"/> binds the session's request port and
/// clock once the session exists; <see cref="Publish"/> hands the router's ports, the aim source and the verb sinks to the later boot
/// steps. The mirror never feeds <c>ClientPorts.Panel</c> or the inventory panel view (those are the panel stand-in PR's).
/// </summary>
/// <example>
/// <code>
/// var loop = new ClientLoop(clientWorld, content, carrier.View, stats, inputSettings, mirrors.Wallet);
/// options.ConfigureClient = b => loop.BindTo(b);
/// var session = new HostSession(...);
/// loop.Attach(session, mirrors.FlightMirror);
/// loop.Publish(ctx.Published);
/// </code>
/// </example>
public sealed class ClientLoop
{
    /// <summary>The host's own client is the first to join (HostSession admits it into slot 0).</summary>
    public const int LocalJoinIndex = 0;

    private readonly ContentSet _content;
    private readonly CarrierView _carrier;
    private HostSession? _session;
    private ThrowReleaseSender? _release;
    private FlightMirror? _flights;
    private readonly AimView _aimView;

    /// <summary>Creates the parts.</summary>
    /// <param name="world">The client's world tables.</param>
    /// <param name="content">The content set.</param>
    /// <param name="carrier">The owned carrier's view (bound by the carrier step).</param>
    /// <param name="stats">The client stat sheet.</param>
    /// <param name="input">The client input settings (hold length).</param>
    /// <param name="wallet">The wallet mirror (bound by <see cref="ClientMirrors"/>).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ClientLoop(IWorldQuery world, ContentSet content, CarrierView carrier, IStatSheet stats, IClientInputSettings input, WalletMirror wallet)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(carrier);
        _content = content;
        _carrier = carrier;
        Panel = new OwnPanelMirror(
            content, ContainerIdPlan.Hotbar(LocalJoinIndex), ContainerIdPlan.PlayerInventory(LocalJoinIndex), ContainerIdPlan.Cursor(LocalJoinIndex));
        Destinations = new DestinationMirror(world, content);
        Requests = new PendingRequests();
        Hotbar = new HotbarSelector(Panel, Requests);
        uint district = world.Lots.Length > 0 ? world.Lots[0].Address.District : 1u;
        Pickup = new IntakePickup(Panel, Requests, Hotbar, ContainerIdPlan.Intake, district);
        Interaction = new ClientInteractionBinder(
            world, content, PlayerSettings.FromBalance(content.Player), stats, carrier, Panel, Panel, new DeliverSource(Panel, Requests),
            Destinations, wallet, Requests, input, PlayerSlot.FromValue(LocalJoinIndex), ContainerIdPlan.Intake);
        _aimView = new AimView(Interaction.Throw);
    }

    /// <summary>The own-panel mirror (deliver preview, aim preview, hotbar strip).</summary>
    public OwnPanelMirror Panel { get; }

    /// <summary>The destination mirror (deliver and aim previews).</summary>
    public DestinationMirror Destinations { get; }

    /// <summary>The request port with the pending-by-kind count; its target is the session's sink after <see cref="Attach"/>.</summary>
    public PendingRequests Requests { get; }

    /// <summary>The hotbar selection sender (keys 1 to 8, the pickup's staging).</summary>
    public HotbarSelector Hotbar { get; }

    /// <summary>The Intake pickup sequence.</summary>
    public IntakePickup Pickup { get; }

    /// <summary>The client interaction steps.</summary>
    public ClientInteractionBinder Interaction { get; }

    /// <summary>The aim preview as the ribbon's arc (and the router's aim gate).</summary>
    public IAimArcSource Aim => _aimView;

    /// <summary>The <c>ThrowRelease</c> sender; null before <see cref="Attach"/>.</summary>
    public ThrowReleaseSender? Release => _release;

    /// <summary>Binds the mirror parts and the steps on the client layer being built.</summary>
    /// <param name="builder">The builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public void BindTo(ClientLayerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindMirrorPart(OwnPanelMirror.Kinds, Panel);
        builder.BindMirrorPart(DestinationMirror.Kinds, Destinations);
        builder.BindMirrorPart(EventKindSet.Of(), Requests);
        builder.BindMirrorPart(EventKindSet.Of(), Pickup);
        Interaction.BindTo(builder);
        builder.Bind(ClientStepId.Previews, Pickup);
    }

    /// <summary>
    /// Points the request port at the session's client adapter and creates the release sender over its clock; each sent release is
    /// noted on <paramref name="flights"/> (its launch verdict, G9).
    /// </summary>
    /// <param name="session">The booted session.</param>
    /// <param name="flights">The client's flight mirror.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Attach(HostSession session, FlightMirror flights)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(flights);
        _session = session;
        _flights = flights;
        Requests.Target = new ClientAdapterRequestSink(session.ClientAdapter, session.OpCodecs);
        _release = new ThrowReleaseSender(Interaction.Throw, Requests, session.ClientAdapter.Clock, _content.Throw.ThrowCooldownTicks);
    }

    /// <summary>Publishes the router's ports, the views' sources and the verb sinks for steps 6 to 9.</summary>
    /// <param name="published">The boot's published states.</param>
    /// <exception cref="ArgumentNullException"><paramref name="published"/> is null.</exception>
    public void Publish(PublishedStates published)
    {
        ArgumentNullException.ThrowIfNull(published);
        published.Add<IInteractionQuery>(Interaction.Publisher);
        published.Add<ICarrierStatusSource>(new CarrierPort(_carrier));
        published.Add<IThrowAimView>(_aimView);
        published.Add<IAimArcSource>(_aimView);
        published.Add<IAnchorPoints>(new AnchorPoints(Interaction.Anchors));
        published.Add(new ClientVerbSinks(new ThrowSink(this), new HotbarKeys(Hotbar)));
    }

    // The deliver preview's selected slot, shift and pending rows.
    private sealed class DeliverSource(OwnPanelMirror panel, PendingRequests pending) : IDeliverClientSource
    {
        public int SelectedSlot => panel.SelectedIndex;

        public int CurrentShift => 1; // M-1a runs one shift; mail hint runs (the only reader) do not travel yet

        public bool IsPending(OpKind kind) => pending.IsPending(kind);
    }

    // The router's carrier port: the owned carrier's published status.
    private sealed class CarrierPort(CarrierView carrier) : ICarrierStatusSource
    {
        public ref readonly CarrierStatus Current => ref carrier.Status;
    }

    // The aim preview as the router's aim gate and the ribbon's arc.
    private sealed class AimView(ThrowPreviewStep preview) : IThrowAimView, IAimArcSource
    {
        public byte NotThrowable => (byte)preview.Current.NotThrowable;

        public bool EntryStale => preview.Current.EntryStale != 0;

        public bool Aiming => preview.Current.Active != 0;

        public AimEnd End => preview.Current.EndClass switch
        {
            EndClass.Accept => AimEnd.Accept,
            EndClass.Misdeliver => AimEnd.Misdeliver,
            _ => AimEnd.Neutral,
        };

        public int PointCount => preview.Polyline.Length;

        public Vec3Mm Point(int i) => preview.Polyline[i];
    }

    // The focus feed's anchor points: the client registry's.
    private sealed class AnchorPoints(AnchorRegistry anchors) : IAnchorPoints
    {
        public bool TryGetPoint(AnchorRef anchor, out Vec3Mm point)
        {
            if (anchors.TryGet(anchor, out int index))
            {
                point = anchors.DefAt(index).Point;
                return true;
            }

            point = default;
            return false;
        }
    }

    // Keys 1 to 8 select hotbar slots 0 to 7 (slot 0 is the hands) through the mirror and HotbarSelect; the panel is not involved.
    private sealed class HotbarKeys(HotbarSelector selector) : IImmediateActionSink
    {
        public void OnImmediate(in ImmediateAction a)
        {
            if (a.EdgeKind == EdgeKind.Press && a.ActionId is >= ActionId.HotbarSlot1 and <= ActionId.HotbarSlot8)
            {
                _ = selector.SelectHotbar(a.ActionId - ActionId.HotbarSlot1);
            }
        }
    }

    // Throw's commit: the release frame's immediate sends the ThrowRelease built from the last published preview.
    private sealed class ThrowSink(ClientLoop loop) : IImmediateActionSink
    {
        public void OnImmediate(in ImmediateAction a)
        {
            if (a.EdgeKind == EdgeKind.Release && loop._release is { } release && loop._session is { } session
                && release.Commit(session.Client.ClientTick) == ThrowSendResult.Sent)
            {
                loop._flights?.NoteOwnRelease(in release.Newest);
            }
        }
    }
}

/// <summary>The verb sinks the client loop owns, registered on the router when it attaches (WP-F).</summary>
/// <param name="throwCommit">The Throw action's commit sink.</param>
/// <param name="hotbarKeys">The sink of the eight hotbar-slot actions.</param>
/// <example><code>router.RegisterSink(ActionId.Throw, sinks.Throw);</code></example>
public sealed class ClientVerbSinks(IImmediateActionSink throwCommit, IImmediateActionSink hotbarKeys)
{
    /// <summary>The Throw action's commit sink.</summary>
    public IImmediateActionSink Throw { get; } = throwCommit ?? throw new ArgumentNullException(nameof(throwCommit));

    /// <summary>The hotbar-slot actions' sink.</summary>
    public IImmediateActionSink HotbarKeys { get; } = hotbarKeys ?? throw new ArgumentNullException(nameof(hotbarKeys));
}
