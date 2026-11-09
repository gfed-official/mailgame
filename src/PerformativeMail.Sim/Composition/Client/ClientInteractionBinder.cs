using PerformativeMail.Sim.ClientLayer;
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
using PerformativeMail.Sim.Players.Ops;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Composition.Client;

/// <summary>
/// The client half of the interaction loop on one <see cref="ClientLayerBuilder"/> (WP-F; ADR-0014 section 2, ADR-0016 section 2): the
/// client's own <see cref="AnchorRegistry"/> over its world copy, with the Intake console's Open anchor (<see cref="OpenInteractable"/>,
/// container <paramref name="intake"/>) and one Deliver anchor per destination (the host's <c>DeliverAnchors</c> rule: house hold time,
/// lot anchor point), then the Previews rows in ADR order: Players' <see cref="FocusStep"/> (4a), Delivery's
/// <see cref="DeliverPreviewStep"/> (4b), Throwing's <see cref="ThrowPreviewStep"/> (4c), Players' <see cref="EdgeResolveStep"/> (4d),
/// <see cref="HoldAdvanceStep"/> and the <see cref="InteractionSnapshotPublisher"/> (4e). Everything it reads is client state: the
/// published carrier view, the own-panel mirror, the destination and wallet mirrors, the client's stat sheet and settings; never
/// <c>SimWorld</c>. No catch predictor or catch-arm sender exists yet, so presses never arm a catch.
/// </summary>
/// <example>
/// <code>
/// var binder = new ClientInteractionBinder(world, content, settings, stats, carrier, panel, selected, deliver, destinations, wallet,
///     requests, inputSettings, self, ContainerIdPlan.Intake);
/// binder.BindTo(builder);
/// IInteractionQuery focus = binder.Publisher;
/// </code>
/// </example>
public sealed class ClientInteractionBinder
{
    private readonly DestinationMirror _destinations;
    private readonly WalletMirror _wallet;
    private readonly HoldAdvanceStep _hold;

    /// <summary>Creates the steps and registers the client anchors.</summary>
    /// <param name="world">The client's world tables.</param>
    /// <param name="content">The content set.</param>
    /// <param name="settings">The <c>player.*</c> settings.</param>
    /// <param name="stats">The client stat sheet.</param>
    /// <param name="carrier">The owned carrier's published view (feet, status, look).</param>
    /// <param name="panel">The displayed own panel.</param>
    /// <param name="selected">The selected hotbar entry.</param>
    /// <param name="deliver">The selected slot, shift and own pending requests the deliver preview reads.</param>
    /// <param name="destinations">The destination mirror (bound as a mirror part by the caller).</param>
    /// <param name="wallet">The wallet mirror (bound as a mirror part by the caller).</param>
    /// <param name="requests">The request port the press and hold steps send through.</param>
    /// <param name="input">The client input settings (hold length).</param>
    /// <param name="self">The local player's slot.</param>
    /// <param name="intake">The Intake container (the Open anchor's entity).</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="InvalidOperationException">An anchor could not be registered.</exception>
    public ClientInteractionBinder(
        IWorldQuery world,
        ContentSet content,
        PlayerSettings settings,
        IStatSheet stats,
        CarrierView carrier,
        IPanelView panel,
        ISelectedEntryView selected,
        IDeliverClientSource deliver,
        DestinationMirror destinations,
        WalletMirror wallet,
        IRequestSink requests,
        IClientInputSettings input,
        PlayerSlot self,
        ContainerId intake)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(wallet);
        _destinations = destinations;
        _wallet = wallet;
        Counters = new SimCounters();
        var checks = new SimCheckRing();
        Anchors = new AnchorRegistry(world.Bounds, settings.MaxAnchors, settings.RangeMm, settings.IncumbentSlackMm, checks, Counters);
        var gate = new InteractionGate(PlayerReasons.Framework);
        Focus = new FocusStep(carrier, world, Anchors, gate, settings, Counters);
        Press = new EdgeResolveStep(carrier, Anchors, gate, settings, input, NullCatchPredictor.Instance, NoCatchArm.Instance, requests, Counters, checks, self);
        _hold = new HoldAdvanceStep(Press, Focus, carrier, Anchors, settings, input, NoCatchArm.Instance, requests, Counters, self);
        Publisher = new InteractionSnapshotPublisher(Focus, Press, carrier, Anchors, settings, input, self);
        Deliver = new DeliverPreviewStep(Focus, Press, Anchors, carrier, panel, deliver, destinations, wallet, world, content, stats);
        var volumes = new ClientCatchVolumes(world, content);
        Throw = new ThrowPreviewStep(
            new ThrowCarrier(carrier), selected, new ThrowMirror(this, content.Mail.CloseGraceTicks), new DefaultThrowerProfile(),
            new ReceiverViewBuilder(world, volumes, Counters), new ReceiverRanks(content.Throw), new ThrowSettings(content.Throw),
            content.Throw.LoftDeg, new ChestOriginProbe(world));
        Register(AnchorDef.For(OpenOp.AnchorOf(intake), world.PostOffice.IntakeAnchor, Verb.Open), new OpenInteractable());
        for (int i = 0; i < volumes.Count; i++)
        {
            AnchorDef d = AnchorDef.For(new AnchorRef(volumes.DestinationAt(i), 0), volumes.AnchorAt(i), Verb.Deliver);
            d.HoldBaseMs = volumes.HoldMsAt(i);
            if (d.HoldBaseMs > 0)
            {
                Register(d, Deliver.Interactable);
            }
        }
    }

    /// <summary>The client anchors (read by the focus feed's anchor points).</summary>
    public AnchorRegistry Anchors { get; }

    /// <summary>4a.</summary>
    public FocusStep Focus { get; }

    /// <summary>4b.</summary>
    public DeliverPreviewStep Deliver { get; }

    /// <summary>4c: the aim preview the ribbon draws and the release sender reads.</summary>
    public ThrowPreviewStep Throw { get; }

    /// <summary>4d.</summary>
    public EdgeResolveStep Press { get; }

    /// <summary>4e: the published interaction snapshot (the router's and the HUD's <see cref="IInteractionQuery"/>).</summary>
    public InteractionSnapshotPublisher Publisher { get; }

    /// <summary>The counters the steps write.</summary>
    public SimCounters Counters { get; }

    /// <summary>Binds the steps in the ADR-0016 Previews order.</summary>
    /// <param name="builder">The client layer builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public void BindTo(ClientLayerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindPreview(PreviewSubStep.Focus, Focus);
        builder.BindPreview(PreviewSubStep.DeliverPreview, Deliver);
        Throw.BindTo(builder);
        builder.BindPreview(PreviewSubStep.EdgeResolve, Press);
        builder.BindPreview(PreviewSubStep.HoldAdvance, _hold);
        builder.BindPreview(PreviewSubStep.HoldAdvance, Publisher);
    }

    private void Register(in AnchorDef def, IInteractable hooks)
    {
        RegisterResult r = Anchors.Register(in def, hooks);
        if (r != RegisterResult.Ok)
        {
            throw new InvalidOperationException($"Client anchor {def.Ref.Entity.Value}/{def.Ref.Slot} ({def.Verb}) was refused: {r}.");
        }
    }

    // Throwing's carrier port over the owned carrier's view: the predicted feet, body yaw and camera look.
    private sealed class ThrowCarrier(CarrierView carrier) : IThrowCarrierSource
    {
        public Vec3Mm Feet => carrier.Feet;

        public ushort BodyYaw => carrier.Current.BodyYaw;

        public ushort AimYaw => carrier.LookYaw;

        public short AimPitch => carrier.LookPitch;

        public ref readonly CarrierStatus Status => ref carrier.Status;
    }

    // Throwing's mirror port over the destination and wallet mirrors (mail-destinations 010's facts helper).
    private sealed class ThrowMirror(ClientInteractionBinder owner, int closeGraceTicks) : IThrowPreviewMirror
    {
        public uint DestinationVersion => owner._destinations.Version;

        public Cents Wallet => owner._wallet.Balance;

        public bool TryGetDestination(uint destinationKey, uint actionStartTick, uint applyTick, out DestinationFacts facts) =>
            MailClientFacts.TryDestination(owner._destinations, new EntityId(destinationKey), actionStartTick, applyTick, closeGraceTicks, out facts);
    }

    // No catch at M-1a: the null predictor never asks for an arm, so nothing is ever sent.
    private sealed class NoCatchArm : ICatchArmSender
    {
        public static readonly NoCatchArm Instance = new();

        public bool Send(bool down) => false;
    }
}

/// <summary>
/// The destinations' catch volumes and Deliver anchors as the client builds them from its own world copy and content (WP-F): the same
/// <c>DestinationRegistry</c> rows the host composes (lot order, the house type's catch volume and hold time), so the aim preview's
/// mailbox receivers and the client's Deliver anchors match the host's. Immutable after construction.
/// </summary>
/// <example><code>var volumes = new ClientCatchVolumes(world, content); AabbMm box = volumes.CatchVolumeAt(0);</code></example>
public sealed class ClientCatchVolumes : ICatchVolumeSource
{
    private readonly DestinationRegistry _registry;
    private readonly ContentSet _content;

    /// <summary>Builds the rows.</summary>
    /// <param name="world">The client's world tables.</param>
    /// <param name="content">The content set.</param>
    public ClientCatchVolumes(IWorldQuery world, ContentSet content)
    {
        _registry = new DestinationRegistry(world, content);
        _content = content;
    }

    /// <inheritdoc />
    public int Count => _registry.Count;

    /// <inheritdoc />
    public EntityId DestinationAt(int ordinal) => _registry.EntityIdOf(new DestinationOrdinal((ushort)ordinal));

    /// <inheritdoc />
    public AabbMm CatchVolumeAt(int ordinal) => _registry.CatchVolumeOf(new DestinationOrdinal((ushort)ordinal));

    /// <summary>The Deliver anchor point of row <paramref name="ordinal"/> (the lot's anchor).</summary>
    /// <param name="ordinal">The row.</param>
    /// <returns>The point.</returns>
    public Vec3Mm AnchorAt(int ordinal) => _registry.AnchorOf(new DestinationOrdinal((ushort)ordinal));

    /// <summary>The manual insert hold of row <paramref name="ordinal"/>'s type, ms (0: no Deliver anchor).</summary>
    /// <param name="ordinal">The row.</param>
    /// <returns>The hold base, ms.</returns>
    public int HoldMsAt(int ordinal) => _content.DestinationTypes[_registry.TypeOf(new DestinationOrdinal((ushort)ordinal))].ManualInsertHoldMs;
}
