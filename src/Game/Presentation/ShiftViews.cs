using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Throwing;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hud;
using PerformativeMail.Game.Views.Inventory;
using PerformativeMail.Game.Views.Throwing;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Boot step 7's shift views (M-1a wave-3 assembly): the HUD read model over step 5's wallet and run mirrors, then the persistent HUD,
/// inventory and aim layers on the Hud presenter and the flight view under <c>World/Flights</c>, all before the presenters'
/// <c>Initialize</c> so their subtree walk injects them (WP-F).
/// The aim ribbon reads the client's throw preview and the deliver prompt the client's interaction snapshot; the inventory layer
/// keeps the empty panel (its source is the panel stand-in PR's, card t316s1406; TD-260, TD-270). Without a client loop (a smoke's
/// boot), an idle arc and an idle interaction.
/// Main thread only.
/// </summary>
/// <example>
/// <code>
/// IHudState hud = ShiftViews.ComposeHud(inputs);
/// string installed = ShiftViews.Install(world, hudPresenter, inputs);   // before world.Initialize(deps)
/// </code>
/// </example>
internal static class ShiftViews
{
    /// <summary>The gameplay camera under <c>Main/World</c>.</summary>
    public const string CameraPath = "CameraRig/Boom/Camera3D";

    /// <summary>Builds the HUD read model from the published mirrors.</summary>
    /// <param name="inputs">Step 7's inputs.</param>
    /// <returns>The HUD state.</returns>
    /// <exception cref="InvalidOperationException">Step 5 did not publish the wallet or run mirror.</exception>
    public static IHudState ComposeHud(PresentationInputs inputs)
    {
        PublishedStates published = inputs.Published;
        IInteractionQuery interaction = published.Types.Contains(typeof(IInteractionQuery))
            ? published.Get<IInteractionQuery>()
            : IdleInteraction.Instance;
        return new HudState(HudSources.Wallet(published.Get<WalletMirror>()), HudSources.Run(published.Get<RunMirror>()), interaction, inputs.Clock);
    }

    /// <summary>Adds the shift's layers and the flight view; call before the presenters are initialised.</summary>
    /// <param name="world">The World presenter.</param>
    /// <param name="hud">The Hud presenter.</param>
    /// <param name="inputs">Step 7's inputs.</param>
    /// <returns>A summary for the step-7 log line.</returns>
    public static string Install(WorldPresenter world, HudPresenter hud, PresentationInputs inputs)
    {
        HudLayers.Install(hud);
        PublishedStates published = inputs.Published;
        // The inventory panel stays the empty one: its real source is the stand-in PR's (card t316s1406), not WP-F's. The hotbar keys
        // are the client loop's sinks (InputBoot), so they select through the own-panel mirror without feeding the panel.
        InventoryLayers.Install(hud, EmptyInventoryPanel.Instance, NoInventoryActions.Instance, ClosedIntakeGate.Instance, router: null);
        bool previewed = published.TryGet(out IAimArcSource? arc);
        ThrowLayers.InstallAim(hud, arc ?? IdleAim.Instance, () => world.GetNodeOrNull<Camera3D>(CameraPath));

        string flights = "none (no World/Flights)";
        if (world.GetNodeOrNull<Node3D>("Flights") is { } parent)
        {
            var source = new SampledFlights(inputs.Published.Get<FlightMirrorConsumer>(), inputs.Clock);
            world.Register(source);   // samples before the flight view presents
            parent.AddChild(ThrowLayers.CreateFlights(source));
            flights = "World/Flights";
        }

        return $"hud=Persistent inventory=EmptyPanel aim={(previewed ? "ThrowPreview" : "Idle")} flights={flights}";
    }

    // No client loop published: nothing focused, the deliver prompt stays hidden.
    private sealed class IdleInteraction : IInteractionQuery
    {
        public static readonly IdleInteraction Instance = new();
        private readonly InteractionSnapshot _snapshot;

        public ref readonly InteractionSnapshot Current => ref _snapshot;
    }

    // No client loop published: nothing to aim.
    private sealed class IdleAim : IAimArcSource
    {
        public static readonly IdleAim Instance = new();

        public bool Aiming => false;

        public AimEnd End => AimEnd.Neutral;

        public int PointCount => 0;

        public Vec3Mm Point(int i) => throw new ArgumentOutOfRangeException(nameof(i));
    }

    private sealed class NoInventoryActions : IInventoryActions
    {
        public static readonly NoInventoryActions Instance = new();

        public bool SelectHotbar(int slot) => false;

        public bool QuickMove(uint fromContainer, uint entryId) => false;
    }

    private sealed class ClosedIntakeGate : IIntakeRangeGate
    {
        public static readonly ClosedIntakeGate Instance = new();

        public InteractionCheckKind Check() => InteractionCheckKind.NoTarget;
    }

    // Samples the flight mirror once per frame and lists the in-flight ones for the flight view. Allocation-free.
    private sealed class SampledFlights(FlightMirrorConsumer consumer, IRenderClock clock) : IFlightSource, IPresentedView
    {
        private readonly int[] _visible = new int[FlightPlacements.Cap];

        public int RenderTick { get; private set; }

        public int Count { get; private set; }

        public FlightRender Flight(int i)
        {
            ref readonly FlightRenderState f = ref consumer.Flights[_visible[i]];
            return new FlightRender(f.Facts.Id.Value, f.Frame.From, f.Frame.To, f.Frame.AlphaPermille);
        }

        public void Present(int alphaPermille)
        {
            consumer.Present(clock);
            RenderTick = (int)clock.HostTick;
            IFlightRead flights = consumer.Flights;
            int n = 0;
            for (int i = 0; i < flights.Count && n < _visible.Length; i++)
            {
                if (flights[i].Frame.Phase == FlightPhase.InFlight)
                {
                    _visible[n++] = i;
                }
            }

            Count = n;
        }
    }
}
