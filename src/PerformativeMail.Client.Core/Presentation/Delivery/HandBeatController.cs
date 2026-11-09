using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Presentation.Delivery;

/// <summary>
/// One hand beat's cursor data (manual-delivery story 006): the hold's key, the <see cref="HandResponseTable"/> response and the riffle,
/// all read from <see cref="PresentationEventStream"/>. <see cref="HandBeatDriver"/> stamps it into a <see cref="HandBeatStart"/>.
/// </summary>
/// <param name="Actor">The acting player.</param>
/// <param name="Destination">The destination.</param>
/// <param name="ApplyTick">The host tick the hold applied on (the batch's <c>sourceTick</c>).</param>
/// <param name="Response">The hand response; a late reply's <see cref="HandResponse.SlotIn"/> is false (MD7).</param>
/// <param name="Riffle">GDD F4 for the entries posted (<see cref="RiffleTiming.Stacks"/> 0 for none).</param>
public readonly record struct HandBeatOutcome(PlayerSlot Actor, EntityId Destination, uint ApplyTick, HandResponse Response, RiffleTiming Riffle);

/// <summary>
/// The hand beat (GDD MD7; ADR-0016 section 5; TR-delivery-017). An outcome beat: every beat starts from its cursor on
/// <see cref="PresentationEventStream"/>, reading <see cref="MailEventKinds.DeliveryOutcome"/> and own <c>deliver_matching</c> replies,
/// and plays once per hold, keyed <c>(actorSlot, destinationId, applyTick)</c> through a 16-entry recent-key ring, however many
/// outcomes the hold produced. Another player's hold starts on its first outcome; an own hold starts on its reply, read after the
/// hold's outcomes in the same batch, and maps it through <see cref="HandResponseTable"/>. <see cref="OnHoldCompleted"/> and
/// <see cref="OnResync"/> never start a beat: with no reply the hand goes idle 30 ticks (1 s) after completion, a reply read after
/// that plays without the slot-in, and a reply after a resync plays none. It emits cursor data only (<see cref="HandBeatOutcome"/>);
/// the non-<c>[OutcomeBeat]</c> <see cref="HandBeatDriver"/> stamps the start time and the prompt attach. Main thread only.
/// </summary>
/// <example>
/// <code>
/// var hand = new HandBeatController(deps.Events, self);
/// hand.Update(clientTick);
/// foreach (HandBeatOutcome beat in hand.Started) { /* HandBeatDriver stamps it */ }
/// </code>
/// </example>
[OutcomeBeat]
public sealed class HandBeatController
{
    /// <summary>Recent hold keys kept (ADR-0016 section 5).</summary>
    public const int RingSize = 16;

    /// <summary>Client ticks from completion until the hand returns to idle with no reply (MD7: 1 s).</summary>
    public const uint HandIdleTicks = 30;

    private readonly EventCursor _cursor;
    private readonly PlayerSlot _self;
    private readonly HoldKey[] _ring = new HoldKey[RingSize];
    private readonly List<HandBeatOutcome> _started = [];
    private int _ringNext;
    private bool _awaiting;
    private uint _completedTick;
    private EntityId _awaitedDestination;
    private HoldKey _ownOutcome;
    private int _ownOutcomeCount;
    private bool _ownMisdelivered;
    private HoldKey _remote;
    private int _remoteK;

    /// <summary>Creates the controller and opens its cursor (it sees only events published from now on).</summary>
    /// <param name="events">The outcome-beat feed.</param>
    /// <param name="self">The local player's slot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public HandBeatController(PresentationEventStream events, PlayerSlot self)
    {
        ArgumentNullException.ThrowIfNull(events);
        _cursor = events.OpenCursor(EventKindSet.Of(MailEventKinds.DeliveryOutcome, EventKind.OpReply));
        _self = self;
    }

    /// <summary>The local player's slot: outcomes with another actor are remote holds.</summary>
    public PlayerSlot Self => _self;

    /// <summary>The beats the last <see cref="Update"/> started, in stream order.</summary>
    public IReadOnlyList<HandBeatOutcome> Started => _started;

    /// <summary>The destination of the own hold awaiting a reply, or default when none.</summary>
    public EntityId AwaitedDestination => _awaiting ? _awaitedDestination : EntityId.None;

    /// <summary>True when no own hold is in the slot: no reply awaited, or <see cref="HandIdleTicks"/> passed since completion.</summary>
    /// <param name="clientTick">The current client tick.</param>
    /// <returns>True when the hand is idle.</returns>
    public bool IsHandIdle(uint clientTick) => !_awaiting || clientTick - _completedTick >= HandIdleTicks;

    /// <summary>An own hold completed and sent its <c>deliver_matching</c> (MD5): the hand rests in the slot until the reply.</summary>
    /// <param name="clientTick">The completion tick.</param>
    /// <param name="destination">The latched destination.</param>
    public void OnHoldCompleted(uint clientTick, EntityId destination)
    {
        _awaiting = true;
        _completedTick = clientTick;
        _awaitedDestination = destination;
    }

    /// <summary>A resync snapshot cleared the pending queue: a reply for the cleared request plays no hand beat.</summary>
    public void OnResync() => _awaiting = false;

    /// <summary>Reads every event published since the last call and replaces <see cref="Started"/> with the beats they start.</summary>
    /// <param name="clientTick">The current client tick (late replies, MD7).</param>
    public void Update(uint clientTick)
    {
        _started.Clear();
        _remoteK = 0;
        while (_cursor.TryRead(out PresentedEvent e))
        {
            if (e.Kind == MailEventKinds.DeliveryOutcome)
            {
                OnOutcome(in e);
            }
            else
            {
                OnReply(in e, clientTick);
            }
        }
    }

    private void OnOutcome(in PresentedEvent e)
    {
        DeliveryOutcomePayload p = e.PayloadAs<DeliveryOutcomePayload>();
        var key = new HoldKey(PlayerSlot.FromValue(p.Actor), new EntityId(p.Destination), e.SourceTick);
        bool misdelivered = p.Result == (byte)EvalResult.Misdeliver;
        if (key.Actor == _self)
        {
            // The own hold waits for its reply; its outcomes give the destination and the latched branch.
            _ownOutcomeCount = key == _ownOutcome ? _ownOutcomeCount + 1 : 1;
            _ownMisdelivered = (key == _ownOutcome && _ownMisdelivered) || misdelivered;
            _ownOutcome = key;
            return;
        }

        if (_remoteK > 0 && _remote == key)
        {
            // A further outcome of the hold that started this update: the riffle grows, the beat does not repeat.
            _remoteK++;
            HandBeatOutcome started = _started[^1];
            HandBeat beat = started.Response.Beat == HandBeat.SlotIn ? HandBeat.SlotInRiffle : started.Response.Beat;
            _started[^1] = started with { Response = started.Response with { Beat = beat }, Riffle = RiffleTiming.For(_remoteK) };
            return;
        }

        _remoteK = 0;
        if (!TryClaim(key))
        {
            return;
        }

        bool floor = (p.Flags & DeliveryOutcomePayload.FlagStoppedAtFloor) != 0;
        HandResponse response = floor
            ? new HandResponse(HandBeat.PiecesOut, StringId.None, false)
            : new HandResponse(misdelivered ? HandBeat.CarefulHandover : HandBeat.SlotIn, StringId.None, true);
        _remote = key;
        _remoteK = floor ? 0 : 1;
        _started.Add(new HandBeatOutcome(key.Actor, key.Destination, key.ApplyTick, response, RiffleTiming.For(_remoteK)));
    }

    private void OnReply(in PresentedEvent e, uint clientTick)
    {
        _remoteK = 0;
        Reply reply = e.PayloadAs<Reply>();
        if (reply.Kind != DeliveryOpKinds.DeliverMatching || reply.Reason == CoreReason.Busy || !_awaiting)
        {
            return;
        }

        bool late = IsHandIdle(clientTick);
        _awaiting = false;
        bool outcomes = _ownOutcome.ApplyTick == e.SourceTick && _ownOutcome.Actor == _self && _ownOutcomeCount > 0;
        EntityId destination = outcomes ? _ownOutcome.Destination : _awaitedDestination;
        var key = new HoldKey(_self, destination, e.SourceTick);
        if (!TryClaim(key))
        {
            return;
        }

        HandResponse response = HandResponseTable.For(in reply, outcomes && _ownMisdelivered ? DeliverBranchKind.MisdeliverOne : DeliverBranchKind.Match);
        if (response.Beat == HandBeat.None)
        {
            return;
        }

        int k = reply.IsOk ? reply.ExtraAs<InvReplyExtra>().GotEntries : 0;
        _started.Add(new HandBeatOutcome(_self, destination, e.SourceTick, late ? response with { SlotIn = false } : response, RiffleTiming.For(k)));
    }

    // False when the key already fired; otherwise records it, overwriting the oldest of the 16.
    private bool TryClaim(HoldKey key)
    {
        for (int i = 0; i < RingSize; i++)
        {
            if (_ring[i] == key)
            {
                return false;
            }
        }

        _ring[_ringNext] = key;
        _ringNext = (_ringNext + 1) % RingSize;
        return true;
    }

    private readonly record struct HoldKey(PlayerSlot Actor, EntityId Destination, uint ApplyTick);
}
