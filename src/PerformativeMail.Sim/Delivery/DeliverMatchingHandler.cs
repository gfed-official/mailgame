using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// The host <c>deliver_matching</c> handler (Manual Delivery MD6; ADR-0009 errata 2026-10-16, section 2 batch rules; ADR-0014 section 5;
/// ADR-0017 section 5). <see cref="Apply"/> runs, in order:
/// <list type="number">
/// <item><see cref="IPlayersApi.CheckInteraction"/> on <c>AnchorRef(mailboxId, 0)</c> with the claim; the first failure is the reply;</item>
/// <item>the anchor check: <c>mailboxId</c> names a destination whose type registers a Deliver anchor, else <c>no_target</c>;</item>
/// <item>the clamp: <c>holdStartTick</c> into <c>[applyTick − (maxHoldTicks + closeGraceTicks), applyTick]</c>, never replaced;</item>
/// <item>the scan of the actor's own cursor and panel through <see cref="DeliverScan"/> over <see cref="IInventoryApi.HostPanelView"/>
/// (the client preview's helper): the entries <c>MailRules.Evaluate</c> accepts at an open destination form the MATCH list; when it is
/// empty and <c>selectedEntryId</c> names a mail entry on the actor's hotbar, the list is that entry; otherwise <c>no_target</c>;</item>
/// <item><see cref="IMailApi.ConsumeHand"/> with the clamped tick (Mail decides the state under its closing grace, pay, penalty, floor);</item>
/// <item>the reply: <c>Ok</c> with <see cref="InvReplyExtra"/>'s requested/got entries and units when a unit was consumed, else the first
/// reason (<c>wallet_floor</c>, then Mail's reject reason, else <c>no_target</c>).</item>
/// </list>
/// No throttle, no <c>reqId</c> dedupe, no state between calls. Allocation-free.
/// </summary>
/// <example><code>builder.BindOp(DeliveryDescriptors.DeliverMatching, handler);</code></example>
internal sealed class DeliverMatchingHandler : IOpHandler
{
    private readonly IPlayersApi _players;
    private readonly IMailApi _mail;
    private readonly IInventoryApi _inventory;
    private readonly ISlotView _slots;
    private readonly DestinationRegistry _registry;
    private readonly int[] _holdWindows;

    /// <summary>Creates the handler.</summary>
    /// <param name="players">Player Character's API.</param>
    /// <param name="mail">Mail's API.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="slots">The player slot view.</param>
    /// <param name="registry">Delivery's destination registry.</param>
    /// <param name="holdWindows">Per registry row, the clamp bound in ticks; 0 where the type has no Deliver anchor.</param>
    public DeliverMatchingHandler(
        IPlayersApi players, IMailApi mail, IInventoryApi inventory, ISlotView slots, DestinationRegistry registry, int[] holdWindows)
    {
        _players = players;
        _mail = mail;
        _inventory = inventory;
        _slots = slots;
        _registry = registry;
        _holdWindows = holdWindows;
    }

    /// <inheritdoc />
    public OpKind Kind => DeliveryOpKinds.DeliverMatching;

    /// <inheritdoc />
    public Reply Apply(in Op op, in TickContext ctx)
    {
        DeliverMatchingPayload p = op.PayloadAs<DeliverMatchingPayload>();
        var dest = new EntityId(p.MailboxId);
        InteractionCheck check = _players.CheckInteraction(op.Actor, new AnchorRef(dest, 0), in p.Claim);
        if (check.Kind != InteractionCheckKind.Ok)
        {
            return Reject(in op, ReasonOf(check));
        }

        PlayerSlot slot = _slots.SlotOf(new EntityId(op.Actor.Value));
        if (!_registry.TryGetOrdinal(dest, out DestinationOrdinal o) || _holdWindows[o.Value] == 0 || slot.IsNone
            || !_mail.TryGetDestinationFacts(dest, out DestinationFacts facts))
        {
            return Reject(in op, DeliveryReason.NoTarget);
        }

        uint holdStart = ClampHoldStart(p.HoldStartTick, ctx.Tick, _holdWindows[o.Value]);
        IPanelView panel = _inventory.HostPanelView(slot);
        if (panel is not IHostPanelContainers containers)
        {
            return Reject(in op, DeliveryReason.NoTarget);
        }

        // The match test ignores the state: Mail judges CLOSED / LOCKED at the hold's start under its grace, inside ConsumeHand.
        var open = new DestinationFacts(facts.Id, facts.Address, facts.Type, DestState.Open, facts.AcceptsMask, facts.RequiresUnit);
        Span<EntryRef> list = stackalloc EntryRef[DeliverScan.MaxEntries];
        int n = 0;
        int requestedUnits = 0;
        DeliverScan.Enumerator scan = DeliverScan.Enumerate(panel);
        while (scan.MoveNext() && n < list.Length)
        {
            PanelEntryFacts e = scan.Current;
            UnitFacts unit = UnitOf(in e);
            if (MailRules.Evaluate(in open, in unit).Result == EvalResult.Accept)
            {
                list[n++] = new EntryRef(containers.ContainerOf(scan.Section), e.EntryId);
                requestedUnits += e.Units;
            }
        }

        if (n == 0 && p.SelectedEntryId != 0 && TryFindHotbarMail(panel, EntryId.FromValue(p.SelectedEntryId)))
        {
            list[n++] = new EntryRef(containers.ContainerOf(PanelSection.Hotbar), EntryId.FromValue(p.SelectedEntryId));
            requestedUnits = 1;
        }

        if (n == 0)
        {
            return Reject(in op, DeliveryReason.NoTarget);
        }

        HandConsumeSummary got = _mail.ConsumeHand(dest, list[..n], slot, holdStart);
        if (got.UnitsConsumed == 0)
        {
            return Reject(in op, got.StoppedAtFloor ? DeliveryReason.WalletFloor : ReasonOf(got.FirstReject));
        }

        var extra = new InvReplyExtra
        {
            RequestedEntries = InvReplyExtra.Saturate(n),
            GotEntries = got.EntriesGot,
            RequestedUnits = InvReplyExtra.Saturate(requestedUnits),
            GotUnits = got.UnitsConsumed,
            SkippedEntries = InvReplyExtra.Saturate(n - got.EntriesGot),
        };
        return Reply.Ok(in op, in extra);
    }

    /// <summary>
    /// The MD6 consistency clamp: <paramref name="holdStartTick"/> into <c>[applyTick − window, applyTick]</c> (a value inside is kept;
    /// the bound never goes below tick 0).
    /// </summary>
    /// <param name="holdStartTick">The request's hold start, host tick domain.</param>
    /// <param name="applyTick">The tick the request is applied.</param>
    /// <param name="window">The destination's bound, <c>maxHoldTicks + closeGraceTicks</c> (150 for a house by default).</param>
    /// <returns>The clamped start tick.</returns>
    /// <example><code>uint s = DeliverMatchingHandler.ClampHoldStart(840, 1000, 150);   // 850</code></example>
    internal static uint ClampHoldStart(uint holdStartTick, uint applyTick, int window)
    {
        uint earliest = applyTick > (uint)window ? applyTick - (uint)window : 0u;
        return Math.Clamp(holdStartTick, earliest, applyTick);
    }

    private static Reply Reject(in Op op, DeliveryReason reason) => Reply.Reject(in op, (ushort)reason);

    // The ADR-0014 section 5 chain onto Delivery's codes; an Unavailable carries its key (MD2 rows 2, 4, 5 on the host).
    private static DeliveryReason ReasonOf(in InteractionCheck check) => check.Kind switch
    {
        InteractionCheckKind.Dead => DeliveryReason.Dead,
        InteractionCheckKind.Frozen => DeliveryReason.Frozen,
        InteractionCheckKind.NoTarget => DeliveryReason.NoTarget,
        InteractionCheckKind.PositionStale => DeliveryReason.PositionStale,
        InteractionCheckKind.OutOfRange => DeliveryReason.OutOfRange,
        _ when check.Reason == PlayerReasons.DryLand || check.Reason == DeliverPromptKeys.DryLand => DeliveryReason.DryLand,
        _ when check.Reason == DeliverPromptKeys.MailRejectClosed => DeliveryReason.Closed,
        _ when check.Reason == DeliverPromptKeys.MailRejectNotOnRoute => DeliveryReason.NotOnRoute,
        _ => DeliveryReason.Unavailable,
    };

    private static DeliveryReason ReasonOf(MailRejectReason reason) => reason switch
    {
        MailRejectReason.Closed => DeliveryReason.Closed,
        MailRejectReason.NotOnRoute => DeliveryReason.NotOnRoute,
        MailRejectReason.WontFit => DeliveryReason.WontFit,
        _ => DeliveryReason.NoTarget,
    };

    // The host keeps no hotbar selection, so the fallback accepts the named entry anywhere on the actor's own hotbar.
    private static bool TryFindHotbarMail(IPanelView panel, EntryId selected)
    {
        int slots = panel.EntryCount(PanelSection.Hotbar);
        for (int i = 0; i < slots; i++)
        {
            PanelEntryFacts e = panel.Entry(PanelSection.Hotbar, i);
            if (e.IsEntry && e.EntryId == selected)
            {
                return e.Category == ItemCategory.Mail;
            }
        }

        return false;
    }

    // The unit Evaluate reads for the match test: kind, category, address and flags (the host view carries no value runs).
    private static UnitFacts UnitOf(in PanelEntryFacts e) =>
        new(DefIndex.FromValue(e.KindKey), e.Category, e.Address, 0, 0, (MailFlags)e.Flags);
}
