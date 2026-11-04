using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The one consume routine (ADR-0017 section 5, TR-mail-008; mail-destinations story 007): every path's <see cref="IMailApi"/> entry point
/// calls <see cref="Consume"/> once per entry, in its caller's phase. Per entry: build the facts with the effective state (open, or within
/// the closing grace from <see cref="ConsumeCall.ActionStartTick"/>), <see cref="MailRules.Evaluate"/> once on the oldest unit (all units
/// of an entry share kind and address), then per unit, oldest first: <b>Accept</b> consumes the whole entry, each unit
/// <c>ConsumeUnit</c>, <c>Credit</c>, retire; <b>Misdeliver</b> (one unit on the hand path) <c>TryDebitPenalty</c> against
/// <see cref="MailSystem.WalletFloorCents"/>, stopping at the floor, else <c>ConsumeUnit</c>, retire; <b>Reject</b> consumes nothing.
/// Each consumed unit raises <see cref="MailEventKinds.MailConsumed"/>; after the units, the op's complaint (one misdelivery charge, or
/// one late charge per call) raises <see cref="MailEventKinds.ComplaintRaised"/>, then one <see cref="MailEventKinds.DeliveryOutcome"/>.
/// <para>
/// It owns the outcome state (TD-208 item 10, approved by Evan 2026-11-04 9:56 AM PT by card click (card t271s1241): "Approve TD-208
/// item 10 as listed, including the story 013 zeroing step"): the running outcome number, the zero-unit floor-stop throttle and the
/// shift's wrong-delivery count, dumped by <see cref="WriteCanonical"/> in that order. Field names and widths are uncredited (Mail Lead
/// planning detail). Integer-only, 0 B per call after warm-up. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example><code>ConsumeResult r = consumer.Consume(new ConsumeCall(DeliveryVia.Hand, d, entry, actor, holdStart, MailConsumer.HandThrownPm, now, false));</code></example>
internal sealed class MailConsumer
{
    /// <summary>The thrown multiplier of a hand delivery (no throw), permille.</summary>
    public const int HandThrownPm = 1000;

    private readonly MailSystem _owner;
    private readonly EventLogFacade _events;
    private readonly IInventoryApi _inventory;
    private readonly IWalletGate _wallet;
    private readonly IStatSheet _stats;
    private readonly ISlotView _slots;
    private readonly SimCounters _counters;
    private readonly SimCheckRing _checks;
    private readonly MailTable _table;
    private readonly DestinationRegistry _registry;
    private readonly ComplaintBuckets _complaints;
    private readonly MailBalance _balance;
    private readonly int[] _complaintByKind;
    private readonly long[] _lastZeroFloorTick;
    private uint _outcomeSeq;
    private ushort _misdeliveriesThisShift;

    /// <summary>Creates the routine over its owner's table, registry and buckets.</summary>
    /// <param name="owner">The Mail module (its <see cref="MailSystem.CurrentShift"/> and <see cref="MailSystem.RaiseComplaint"/>).</param>
    /// <param name="events">The world's event-log writer facade.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="wallet">The wallet port.</param>
    /// <param name="stats">The world's stat sheet.</param>
    /// <param name="slots">The player slot view (the removal record's actor).</param>
    /// <param name="counters">The world's counters.</param>
    /// <param name="checks">The world's check ring.</param>
    /// <param name="table">The mail table.</param>
    /// <param name="registry">The destination registry.</param>
    /// <param name="complaints">The F9 buckets.</param>
    /// <param name="content">The accepted content set (the mail balance and each kind's misdelivery complaint).</param>
    public MailConsumer(
        MailSystem owner,
        EventLogFacade events,
        IInventoryApi inventory,
        IWalletGate wallet,
        IStatSheet stats,
        ISlotView slots,
        SimCounters counters,
        SimCheckRing checks,
        MailTable table,
        DestinationRegistry registry,
        ComplaintBuckets complaints,
        ContentSet content)
    {
        _owner = owner;
        _events = events;
        _inventory = inventory;
        _wallet = wallet;
        _stats = stats;
        _slots = slots;
        _counters = counters;
        _checks = checks;
        _table = table;
        _registry = registry;
        _complaints = complaints;
        _balance = content.Mail;

        ReadOnlySpan<MailKindDef> kinds = content.MailKinds.AsSpan();
        _complaintByKind = new int[kinds.Length];
        for (int k = 0; k < kinds.Length; k++)
        {
            _complaintByKind[k] = kinds[k].ComplaintOnMisdeliveryMilliPoints;
        }

        // lots x 8, index ordinal x 8 + slot; -1 is "never" (uncredited Mail Lead planning detail, TD-208 item 10).
        _lastZeroFloorTick = new long[registry.Count * PlayerSlot.Count];
        Array.Fill(_lastZeroFloorTick, -1L);
    }

    /// <summary>The last <c>outcomeSeq</c> raised (0 before the first; the first outcome is 1).</summary>
    public uint OutcomeSeq => _outcomeSeq;

    /// <summary>The shift's wrong-delivery count: +1 per op that consumed at least one Misdeliver unit, saturating at 65,535.</summary>
    public ushort MisdeliveriesThisShift
    {
        get => _misdeliveriesThisShift;
        set => _misdeliveriesThisShift = value;
    }

    /// <summary>
    /// Consumes from one entry per the class remarks. At an open door, an entry that cannot be read (a stale ref, an item entry, or a unit
    /// with no live row) is skipped: Reject with no reason, nothing consumed, nothing raised (uncredited implementation detail). 0 B.
    /// </summary>
    /// <param name="c">The call.</param>
    /// <returns>What happened.</returns>
    public ConsumeResult Consume(in ConsumeCall c)
    {
        // Evaluate checks the state first, so a closed or locked door rejects before the entry is read: a Reject makes no
        // IInventoryApi call (AC-3). Same rule, no second copy.
        DestinationFacts d = EffectiveFacts(in c);
        if (d.State != DestState.Open)
        {
            UnitFacts none = default;
            return new ConsumeResult(EvalResult.Reject, MailRules.Evaluate(in d, in none).Reason, 0, 0, false, false, false);
        }

        if (!_inventory.TryReadEntry(c.Entry, out EntryView view) || !_table.TryGetRow(view.LowestMailId, out MailRow row))
        {
            return default;
        }

        UnitFacts u = FactsOf(in row);
        DeliverEval e = MailRules.Evaluate(in d, in u);
        if (e.Result == EvalResult.Reject)
        {
            return new ConsumeResult(EvalResult.Reject, e.Reason, 0, 0, false, false, false);
        }

        bool misdeliver = e.Result == EvalResult.Misdeliver;

        // Hand misdeliver = exactly one unit per op (GDD R6 table, OQ4); Accept and the thrown path take the whole entry.
        int target = misdeliver && c.Via == DeliveryVia.Hand ? 1 : view.Units;
        SimCounterSink sink = _counters.PrimitiveSink();
        ActorId actor = ActorId.FromEntity(_slots.PlayerOf(c.Actor));
        Cents floor = Cents.FromCents(MailSystem.WalletFloorCents, ref sink);
        int shift = _owner.CurrentShift;
        int lateRatioPm = MailPay.LateValueRatioPm(_stats);
        int deadShifts = _balance.DeadLetterShifts;
        int units = 0;
        int lateUnits = 0;
        int deadUnits = 0;
        int rawPm = 0;
        long cents = 0;
        bool stopped = false;
        bool aborted = false;
        for (int i = 0; i < target; i++)
        {
            if (i > 0)
            {
                // Read into a temporary: a failed read zeroes its out value, and the outcome below still needs the last good view.
                if (!_inventory.TryReadEntry(c.Entry, out EntryView next) || !_table.TryGetRow(next.LowestMailId, out row))
                {
                    break;
                }

                view = next;
            }

            u = FactsOf(in row);

            // TD-208 item 7: overdue = current shift minus deadline shift; late at 1 or more but under the dead-letter limit, dead at or
            // past it (uncredited: lag against balance.DeadLetterShifts).
            MailPay.Overdue(shift, row.DeadlineShift, deadShifts, out bool late, out bool dead);
            long unitCents;
            if (misdeliver)
            {
                Cents penalty = MailPay.UnitPenalty(in u, _balance.MisdeliveryPenaltyRatio, _balance.MinPenaltyCents, _stats, ref sink);
                if (!_wallet.TryDebitPenalty(penalty, floor))
                {
                    stopped = true;
                    break;
                }

                // Charge, then remove (ADR-0017 section 5 l.129): IWalletGate has no refund, so a refused removal leaves the penalty.
                cents -= penalty.Value;
                if (!_inventory.ConsumeUnit(c.Entry, ConsumeReason.Misdelivered, actor))
                {
                    Refused(in c, row.Id);
                    aborted = true;
                    break;
                }

                unitCents = -penalty.Value;
                rawPm += _complaintByKind[row.Kind.Value];
            }
            else
            {
                int timelinessPm = MailPay.Timeliness(shift, row.DeadlineShift, lateRatioPm, deadShifts);
                Cents pay = MailPay.UnitPay(in u, timelinessPm, c.ThrownPm, _stats, c.Actor, ref sink);
                if (!_inventory.ConsumeUnit(c.Entry, ConsumeReason.Delivered, actor))
                {
                    Refused(in c, row.Id);
                    aborted = true;
                    break;
                }

                _wallet.Credit(pay, CreditReason.DeliveryPay);
                cents += pay.Value;
                unitCents = pay.Value;
            }

            _ = _table.Retire(row.Id);
            units++;
            lateUnits += late ? 1 : 0;
            deadUnits += dead ? 1 : 0;
            RaiseConsumed(row.Id, unitCents, e.Result, late, dead, c.Actor);
        }

        int complaintPm = 0;
        bool lateCharged = false;
        if (misdeliver && rawPm > 0)
        {
            complaintPm = _complaints.ChargeMisdelivery(c.Destination, c.ApplyTick, rawPm, _stats, ref sink);
            _owner.RaiseComplaint(c.Destination, ComplaintSource.Misdelivery, complaintPm);
        }
        else if (!misdeliver && lateUnits > 0 && !c.LateChargedInCall)
        {
            // AC-10: two late entries in one hold count once; a dead-letter-only Accept and a Misdeliver op make no late call (AC-8, AC-9).
            complaintPm = _complaints.ChargeLate(c.ApplyTick, _stats.Has(RuleFlag.SuppressLateComplaint));
            lateCharged = true;
            _owner.RaiseComplaint(c.Destination, ComplaintSource.Late, complaintPm);
        }

        if (misdeliver && units > 0 && _misdeliveriesThisShift < ushort.MaxValue)
        {
            _misdeliveriesThisShift++;
        }

        if (units > 0 || (stopped && FloorBroadcastDue(c.Destination, c.Actor, c.ApplyTick)))
        {
            RaiseOutcome(in c, e.Result, view, units, cents, lateUnits, deadUnits, complaintPm, stopped);
        }

        return new ConsumeResult(e.Result, MailRejectReason.None, units, cents, stopped, aborted, lateCharged);
    }

    /// <summary>
    /// Writes the outcome state after the team complaint state (AC-12; TD-208 item 10: "the number first, then the floor times, then the
    /// count"): <c>outcomeSeq u32</c>, <c>lastZeroFloorTick i64 × lots × 8</c> (ordinal-major), <c>misdeliveriesThisShift u16</c>.
    /// </summary>
    /// <param name="w">The writer, inside the Mail section.</param>
    public void WriteCanonical(ref CanonicalWriter w)
    {
        w.WriteU32(_outcomeSeq);
        long[] last = _lastZeroFloorTick;
        for (int i = 0; i < last.Length; i++)
        {
            w.WriteI64(last[i]);
        }

        w.WriteU16(_misdeliveriesThisShift);
    }

    /// <summary>
    /// Loads the outcome state from its <see cref="WriteCanonical"/> bytes (the dump round-trip, AC-12; the Sim has no full load path
    /// yet, so this reads this block only). Uncredited implementation detail.
    /// </summary>
    /// <param name="src">Exactly <see cref="CanonicalBytes"/> bytes, little-endian.</param>
    /// <exception cref="ArgumentException"><paramref name="src"/> is not <see cref="CanonicalBytes"/> long.</exception>
    public void ReadCanonical(ReadOnlySpan<byte> src)
    {
        if (src.Length != CanonicalBytes)
        {
            throw new ArgumentException("The outcome state is " + CanonicalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes.", nameof(src));
        }

        _outcomeSeq = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(src);
        for (int i = 0; i < _lastZeroFloorTick.Length; i++)
        {
            _lastZeroFloorTick[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(src.Slice(4 + (i * 8)));
        }

        _misdeliveriesThisShift = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4 + (_lastZeroFloorTick.Length * 8)));
    }

    /// <summary>The length of the <see cref="WriteCanonical"/> block, bytes.</summary>
    public int CanonicalBytes => 4 + (_lastZeroFloorTick.Length * 8) + 2;

    private DestinationFacts EffectiveFacts(in ConsumeCall c)
    {
        DestinationOrdinal o = c.Destination;
        _ = _registry.TryGetDestinationFacts(_registry.EntityIdOf(o), out DestinationFacts raw);
        DeliveriesState s = _registry.Deliveries;
        DestState state = DeliveriesStateRules.Effective(
            s.IsDistrictUnlocked(_registry.DistrictOf(o)), s.Open, s.CloseTick, c.ActionStartTick, c.ApplyTick, _balance.CloseGraceTicks);
        return new DestinationFacts(raw.Id, raw.Address, raw.Type, state, raw.AcceptsMask, raw.RequiresUnit);
    }

    private static UnitFacts FactsOf(in MailRow row) =>
        new(row.Kind, ItemCategory.Mail, row.Address, row.SpawnValue, row.DeadlineShift, row.Flags);

    private bool FloorBroadcastDue(DestinationOrdinal d, PlayerSlot actor, uint now)
    {
        if (actor.IsNone || actor.Value >= PlayerSlot.Count)
        {
            return true;
        }

        // At most once per (destination, player) per 30 ticks (TD-208 item 10: "The 30 is fixed in code, not a tuning setting").
        ref long last = ref _lastZeroFloorTick[(d.Value * PlayerSlot.Count) + actor.Value];
        if (last >= 0 && now - last < MailSystem.ZeroFloorBroadcastTicks)
        {
            return false;
        }

        last = now;
        return true;
    }

    // TD-208 item 12 and open point (iv): the always-on record first, so it is present in every configuration, then the assert.
    private void Refused(in ConsumeCall c, MailId unit)
    {
        _checks.Record(CheckCode.MailConsumeUnitRefused, c.ApplyTick, _registry.EntityIdOf(c.Destination).Value, unit.Value);
        SimAssert.Fail(SimAssertCode.MailConsumeUnitRefused);
    }

    private void RaiseConsumed(MailId id, long cents, EvalResult result, bool late, bool dead, PlayerSlot actor)
    {
        var payload = new MailConsumedPayload
        {
            MailId = id.Value,
            Cents = cents,
            Result = (byte)result,
            Late = late ? (byte)1 : (byte)0,
            Dead = dead ? (byte)1 : (byte)0,
            Actor = actor.Value,
        };
        _ = _events.TryRaise(MailEventKinds.MailConsumed, 0, in payload);
    }

    private void RaiseOutcome(
        in ConsumeCall c, EvalResult result, EntryView view, int units, long cents, int lateUnits, int deadUnits, int complaintPm, bool stopped)
    {
        int flags = (stopped ? DeliveryOutcomePayload.FlagStoppedAtFloor : 0)
            | ((view.Flags & (byte)MailFlags.Cursed) != 0 ? DeliveryOutcomePayload.FlagCursed : 0);
        var payload = new DeliveryOutcomePayload
        {
            OutcomeSeq = ++_outcomeSeq,
            Destination = _registry.EntityIdOf(c.Destination).Value,
            Actor = c.Actor.Value,
            Via = (byte)c.Via,
            Result = (byte)result,
            Kind = view.MailKind.Value,
            Units = (ushort)units,
            Cents = cents,
            LateUnits = (ushort)lateUnits,
            DeadUnits = (ushort)deadUnits,
            ComplaintPermille = (ushort)Math.Min(complaintPm, ushort.MaxValue),
            Flags = (byte)flags,
            IntendedAddress = result == EvalResult.Misdeliver ? view.AddressId : 0u,
        };
        _ = _events.TryRaise(MailEventKinds.DeliveryOutcome, MailEventDescriptors.AudienceAll, in payload);
    }
}
