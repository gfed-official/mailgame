using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Delivery.Client;

/// <summary>
/// Manual Delivery's client step at <see cref="PreviewSubStep.DeliverPreview"/> (4b) (ADR-0016 sections 2 and 4; Manual Delivery MD2,
/// MD3, MD4; TR-delivery-002 to 005). Each tick it picks the Deliver anchor to judge: the latched one while a Deliver hold is in
/// progress, else the target of this tick's first Interact press edge when that is a Deliver anchor (the press-tick recompute; the
/// target stays the edge's, ADR-0014 section 3), else the focus from 4a. For a Deliver anchor it builds the client facts
/// (<see cref="MailClientFacts"/>; during a hold, the destination state at the hold's start tick: closing grace), runs
/// <see cref="DeliverPreview.Preview"/> once with no cache, evaluates MD2 (<see cref="DeliverAvailability"/>), advances the
/// <see cref="DeliverPreviewTracker"/> and publishes <see cref="Current"/>. Anything else publishes IDLE and evaluates nothing. It
/// dispatches no press: edges resolve only at 4d.
/// <para>
/// <see cref="Interactable"/> is the client's Deliver <see cref="IInteractable"/>: its pure hooks read this step's published state
/// (and MD2 rows 1 to 5 fresh from the ports), so a hook never evaluates a preview. Reads only its constructor ports and mirror
/// parts, never <c>SimWorld</c>. Allocation free; not thread-safe.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var preview = new DeliverPreviewStep(focus, edgeResolve, anchors, carrier, panel, source, destinations, wallet, world, content, stats, branchSettleTicks: 6);
/// builder.BindPreview(PreviewSubStep.DeliverPreview, preview);
/// anchors.Register(in deliverDef, preview.Interactable);
/// </code>
/// </example>
public sealed class DeliverPreviewStep : IClientStep, IDeliverPreviewRead
{
    /// <summary>GDD F5 <c>branchSettleTicks</c> (6 ticks, 200 ms, PROPOSED) until the Delivery balance carries the key.</summary>
    public const int DefaultBranchSettleTicks = 6;

    private readonly FocusStep _focus;
    private readonly EdgeResolveStep _press;
    private readonly AnchorRegistry _anchors;
    private readonly ICarrierFocusSource _carrier;
    private readonly IPanelView _panel;
    private readonly IDeliverClientSource _source;
    private readonly DestinationMirror _destinations;
    private readonly WalletMirror _wallet;
    private readonly MailBalance _balance;
    private readonly IStatSheet _stats;
    private readonly Cents _walletFloor;
    private readonly AddressId[] _lotAddress;
    private readonly StringId[] _lotDisplay;
    private DeliverPreviewTracker _tracker;
    private DeliverPreviewScratch _scratch;
    private DeliverPreviewState _state;

    /// <summary>Creates the step and its client hooks.</summary>
    /// <param name="focus">Players' 4a step (the focused anchor).</param>
    /// <param name="press">Players' 4d step (the interaction state and the latch while holding).</param>
    /// <param name="anchors">The client anchor registry (read only: is an anchor a Deliver anchor).</param>
    /// <param name="carrier">The local carrier's status (rows 1 and 2).</param>
    /// <param name="panel">The displayed own panel.</param>
    /// <param name="source">The selected slot, the current shift and the own pending requests (row 3).</param>
    /// <param name="destinations">The destination mirror (rows 4 and 5, the preview's destination facts).</param>
    /// <param name="wallet">The wallet mirror (the predicted floor refusal).</param>
    /// <param name="world">The world tables (only the lots' display strings are read, here).</param>
    /// <param name="content">The content set (the Mail balance).</param>
    /// <param name="stats">The client stat sheet.</param>
    /// <param name="branchSettleTicks">GDD F5 <c>branchSettleTicks</c>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    public DeliverPreviewStep(
        FocusStep focus,
        EdgeResolveStep press,
        AnchorRegistry anchors,
        ICarrierFocusSource carrier,
        IPanelView panel,
        IDeliverClientSource source,
        DestinationMirror destinations,
        WalletMirror wallet,
        IWorldQuery world,
        ContentSet content,
        IStatSheet stats,
        int branchSettleTicks = DefaultBranchSettleTicks)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(press);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        _focus = focus;
        _press = press;
        _anchors = anchors;
        _carrier = carrier;
        _panel = panel;
        _source = source;
        _destinations = destinations;
        _wallet = wallet;
        _balance = content.Mail;
        _stats = stats;
        var sink = default(SimCounterSink);
        _walletFloor = Cents.FromCents(MailSystem.WalletFloorCents, ref sink);
        ReadOnlySpan<LotRecord> lots = world.Lots;
        _lotAddress = new AddressId[lots.Length];
        _lotDisplay = new StringId[lots.Length];
        for (int i = 0; i < lots.Length; i++)
        {
            _lotAddress[i] = lots[i].Address;
            _lotDisplay[i] = lots[i].Display;
        }

        _tracker = new DeliverPreviewTracker(branchSettleTicks);
        Interactable = new DeliverInteractable(stats, null, this);
    }

    /// <inheritdoc />
    public ref readonly DeliverPreviewState Current => ref _state;

    /// <summary>The client's Deliver hooks over this step's state; register it for every client Deliver anchor.</summary>
    public IInteractable Interactable { get; }

    /// <summary>The preview's test seams: evaluations and scanned entries (counted in test builds only, MD3).</summary>
    public ref readonly DeliverPreviewScratch Scratch => ref _scratch;

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        uint hostTick = ctx.HostClock.IsValid ? ctx.HostClock.EstimatedHostTick : 0u;
        uint? holdStart = null;
        AnchorRef anchor;
        ref readonly InteractionStatus s = ref _press.Interaction;
        if (s.State == InteractState.Holding && s.Latch.Verb == Verb.Deliver)
        {
            InteractionLatch latch = s.Latch;
            anchor = latch.Anchor;
            holdStart = DeliverLatchPayload.Read(in latch.OwnerPayload).HoldStartTick;
        }
        else
        {
            anchor = PressedDeliverAnchor(in ctx.Input);
            if (anchor.IsNone)
            {
                anchor = _focus.State.Focused;
            }
        }

        if (!IsDeliverAnchor(anchor)
            || !MailClientFacts.TryDestination(_destinations, anchor.Entity, holdStart, hostTick, _balance.CloseGraceTicks, out DestinationFacts d))
        {
            _tracker.Reset();
            _state = new DeliverPreviewState { Tick = ctx.ClientTick };
            return;
        }

        int slot = _source.SelectedSlot;
        DeliverBranch b = DeliverPreview.Preview(in d, _panel, slot, _source.CurrentShift, _balance, _stats, _wallet.Balance, _walletFloor, ref _scratch);
        DeliverAvailabilityFacts f = PortFacts(d.State, b.Kind == DeliverBranchKind.Nothing);
        DeliverRow row = DeliverAvailability.Evaluate(in f);
        _tracker.Update(ctx.ClientTick, anchor.Entity, in b, row, out uint displayed, out bool canStart);
        _state = new DeliverPreviewState
        {
            Active = true,
            Tick = ctx.ClientTick,
            Destination = anchor.Entity,
            Branch = b,
            Row = row,
            SelectedSlot = slot,
            DisplayedTicks = displayed,
            CanStartHoldNext = canStart,
        };
    }

    /// <summary>
    /// The client <c>IsAvailable</c>: MD2 rows 1 to 5 fresh from the ports and the mirror (rows 1 to 3 only during this anchor's hold:
    /// rows 4 and 5 gate the start of a hold, and branch changes are <see cref="ShouldCancel"/>'s), then row 6 from the published
    /// preview of this anchor, then the settle (MD4: a MISDELIVER_ONE press before <c>branchSettleTicks</c> reports
    /// <c>deliver.cancel.settle</c>; <c>OnPress</c> cannot refuse, TD-171 D3).
    /// </summary>
    internal bool IsAvailable(in InteractionContext c, out StringId reason)
    {
        bool holding = IsHolding(c.Anchor);
        DestState state = DestState.Open;
        if (!holding && _destinations.TryGetOrdinal(c.Anchor.Entity, out int o))
        {
            state = _destinations.EffectiveStateAt(o, null, c.Tick, _balance.CloseGraceTicks);
        }

        bool mine = IsFor(c.Anchor);
        bool nothing = !holding && mine && _state.Branch.Kind == DeliverBranchKind.Nothing;
        DeliverAvailabilityFacts f = PortFacts(state, nothing);
        DeliverRow row = DeliverAvailability.Evaluate(in f);
        if (row != DeliverRow.None)
        {
            reason = DeliverAvailability.Key(row);
            return false;
        }

        if (!holding && mine && _state.Settling)
        {
            reason = DeliverPromptKeys.CancelSettle;
            return false;
        }

        reason = StringId.None;
        return true;
    }

    /// <summary>The client <c>OnPress</c> payload: this tick's branch, slot and entry, and <c>holdStartTick = c.Tick</c> (the host-tick estimate at latch).</summary>
    internal DeliverLatchPayload LatchPayload(in InteractionContext c)
    {
        DeliverBranchKind kind = IsFor(c.Anchor) ? _state.Branch.Kind : DeliverBranchKind.Nothing;
        EntryId entry = kind == DeliverBranchKind.MisdeliverOne ? _state.Branch.SelectedEntry : EntryId.None;
        ushort ordinal = _destinations.TryGetOrdinal(c.Anchor.Entity, out int o) ? (ushort)o : (ushort)0;
        return new DeliverLatchPayload(kind, (byte)Math.Clamp(_state.SelectedSlot, 0, byte.MaxValue), entry, c.Tick, ordinal);
    }

    /// <summary>
    /// The client <c>ShouldCancel</c> (MD4), on this tick's recompute (destination state at the hold's start): the branch kind differs
    /// from the latched one (<c>deliver.cancel.prompt_changed</c>), else on a MISDELIVER_ONE hold the selected slot or its entry differs
    /// from the latched pair at the tick boundary (<c>deliver.cancel.selection_changed</c>). MD2 rows 1 to 3 cancel through
    /// <see cref="IsAvailable"/>.
    /// </summary>
    internal bool ShouldCancel(in InteractionLatch latch, out StringId reason)
    {
        DeliverLatchPayload p = DeliverLatchPayload.Read(in latch.OwnerPayload);
        if (!IsFor(latch.Anchor) || _state.Branch.Kind != p.Kind)
        {
            reason = DeliverPromptKeys.CancelPromptChanged;
            return true;
        }

        if (p.Kind == DeliverBranchKind.MisdeliverOne && (_state.SelectedSlot != p.Slot || _state.Branch.SelectedEntry != p.SelectedEntry))
        {
            reason = DeliverPromptKeys.CancelSelectionChanged;
            return true;
        }

        reason = StringId.None;
        return false;
    }

    /// <summary>The client <c>WritePrompt</c> input for <paramref name="anchor"/>; false when this tick's state is not about it.</summary>
    internal bool TryPromptInput(AnchorRef anchor, out DeliverPromptInput input)
    {
        if (!IsFor(anchor))
        {
            input = default;
            return false;
        }

        _destinations.TryGetOrdinal(anchor.Entity, out int o);
        input = new DeliverPromptInput
        {
            Branch = _state.Branch,
            Unavailable = DeliverAvailability.PromptRow(_state.Row),
            Settling = _state.Settling,
            DestinationAddress = (uint)o < (uint)_lotDisplay.Length ? _lotDisplay[o] : StringId.None,
            SelectedAddress = _state.Branch.Kind == DeliverBranchKind.MisdeliverOne ? DisplayOf(_state.Branch.SelectedAddress) : StringId.None,
        };
        return true;
    }

    private bool IsFor(AnchorRef anchor) => _state.Active && _state.Destination == anchor.Entity && anchor.Slot == 0;

    private bool IsHolding(AnchorRef anchor)
    {
        ref readonly InteractionStatus s = ref _press.Interaction;
        return s.State == InteractState.Holding && s.Latch.Anchor == anchor;
    }

    private bool IsDeliverAnchor(AnchorRef anchor) =>
        !anchor.IsNone && _anchors.TryGet(anchor, out int index) && _anchors.DefAt(index).Verb == Verb.Deliver;

    private AnchorRef PressedDeliverAnchor(in InputFrame frame)
    {
        ReadOnlySpan<InputEdge> edges = frame.Edges;
        for (int i = 0; i < edges.Length; i++)
        {
            ref readonly InputEdge e = ref edges[i];
            if (e.Action == FrameAction.Interact && e.Kind == EdgeKind.Press && e.TargetEntity != 0)
            {
                var target = new AnchorRef(new EntityId(e.TargetEntity), e.TargetSlot);
                return IsDeliverAnchor(target) ? target : AnchorRef.None;
            }
        }

        return AnchorRef.None;
    }

    // Rows 1 to 3 from the carrier and the pending requests, rows 4 to 6 from the caller.
    private DeliverAvailabilityFacts PortFacts(DestState state, bool previewNothing)
    {
        ref readonly CarrierStatus status = ref _carrier.Status;
        return new DeliverAvailabilityFacts
        {
            PanelOpen = status.PanelOpen,
            Swimming = status.Swimming,
            Pending = _source.IsPending(DeliveryOpKinds.DeliverMatching) || _source.IsPending(ThrowOpKinds.ThrowRelease)
                || _source.IsPending(InvOpKinds.Drop) || _source.IsPending(InvOpKinds.TakeAll),
            SortPending = _source.IsPending(InvOpKinds.Sort),
            State = state,
            PreviewNothing = previewNothing,
        };
    }

    private StringId DisplayOf(AddressId address)
    {
        for (int i = 0; i < _lotAddress.Length; i++)
        {
            if (_lotAddress[i] == address)
            {
                return _lotDisplay[i];
            }
        }

        return StringId.None;
    }
}
