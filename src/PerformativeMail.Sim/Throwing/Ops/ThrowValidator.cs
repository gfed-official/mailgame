using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The pure <c>Validate</c> half of the <c>ThrowRelease</c> handler (ADR-0015 section 1; ADR-0007; GDD T1, T4 "Host validation";
/// throwing-catching story 009). The checks run in the ADR's fixed order and the first failure is the reply:
/// <code>
/// 1  malformed, or the entry is not in the sender's hotbar     bad_request (8)
/// 2  entry unknown, not the selected slot, or count != units   stale_entry (64)
/// 3  actor not Alive (and not Frozen)                          dead (65)
/// 4  actor Frozen ("on break")                                 frozen (66)
/// 5  swimming                                                  swimming (67)
/// 6  not on foot                                               unavailable (68)
/// 7  leaky bucket                                              throw_cooldown (69): accepts every request at M-1a (E5; story 028)
/// 8  direction norm or elevation out of bounds                 bad_direction (70): LaunchMath.ValidateDir
/// 9  claimed feet not corroborated                             never a rejection: the newest sample, originClamped
/// 10 every flight slot in use                                  flight_cap (71)
/// </code>
/// Nothing is mutated, no id is allocated, no RNG is drawn and no event is raised; the only writes are the diagnostic counters
/// (<see cref="CounterId.ThrowingReleaseRejections"/> on every rejection, <see cref="CounterId.ThrowingFlightCapRefusals"/> on check 10),
/// which are not canonical state. Consistency checks, not anti-cheat (spec 13 section 4.3). O(1), allocation-free.
/// </summary>
/// <example>
/// <code>
/// ThrowCheck c = ThrowValidator.Validate(in op, applyTick, in facts, out ThrowPlan plan);
/// if (!c.IsOk) { return Reply.Reject(in op, c.Reason); }   // else story 010 commits plan
/// </code>
/// </example>
internal static class ThrowValidator
{
    /// <summary>
    /// Validates a framed <c>ThrowRelease</c> op: a payload length other than the descriptor's 28 B is <c>bad_request</c> (never an
    /// exception), else the payload is read and validated with the transport's <see cref="Op.Actor"/>.
    /// </summary>
    /// <param name="op">The op (its actor is the transport peer, never a payload field).</param>
    /// <param name="applyTick">The apply tick <c>T</c>.</param>
    /// <param name="facts">The host state.</param>
    /// <param name="plan">The launch plan when valid; <c>default</c> otherwise.</param>
    /// <returns>The first failing check's reason, or ok.</returns>
    public static ThrowCheck Validate(in Op op, uint applyTick, in ThrowHostFacts facts, out ThrowPlan plan)
    {
        if (op.PayloadLength != ThrowDescriptors.ThrowRelease.PayloadLength)
        {
            plan = default;
            return Reject(new ThrowCheck(ThrowReasons.BadRequest), in facts);
        }

        ThrowReleasePayload p = op.PayloadAs<ThrowReleasePayload>();
        return Validate(in p, op.Actor, applyTick, in facts, out plan);
    }

    /// <summary>Runs the validation chain over a read payload (see the type remarks for the order).</summary>
    /// <param name="p">The payload.</param>
    /// <param name="actor">The requesting player (<see cref="Op.Actor"/>).</param>
    /// <param name="applyTick">The apply tick <c>T</c>.</param>
    /// <param name="facts">The host state.</param>
    /// <param name="plan">The launch plan when valid; <c>default</c> otherwise.</param>
    /// <returns>The first failing check's reason, or ok.</returns>
    public static ThrowCheck Validate(in ThrowReleasePayload p, ActorId actor, uint applyTick, in ThrowHostFacts facts, out ThrowPlan plan)
    {
        plan = default;
        ThrowCheck check = Chain(in p, actor, applyTick, in facts, out plan);
        if (check.IsOk)
        {
            return check;
        }

        plan = default;
        return Reject(check, in facts);
    }

    // Check 7, the leaky bucket (ADR-0015 section 6), keeps its slot and its throw_cooldown code but accepts unconditionally at M-1a
    // (erratum E5). Story 028 (M0) replaces this body with ThrowCooldown.TryAccept; the chain around it does not move.
    private static bool CooldownAccepts(uint applyTick, uint tat) => true;

    private static ThrowCheck Chain(in ThrowReleasePayload p, ActorId actor, uint applyTick, in ThrowHostFacts facts, out ThrowPlan plan)
    {
        plan = default;

        // 1. Malformed: the fields the payload layout fixes (the wire codec refuses them too; a loopback op skips the codec).
        if ((p.Side != 1 && p.Side != -1) || p.Flags != 0 || p.Reserved != 0 || p.Pad != 0)
        {
            return new ThrowCheck(ThrowReasons.BadRequest);
        }

        // 1-2. Inventory's own detach decision: bad_request for no hotbar, a malformed id or an entry not in the sender's hotbar;
        // stale_entry for an unknown entry or count != units. A non-player actor has no slot, so no hotbar.
        PlayerSlot slot = facts.Slots.SlotOf(new EntityId(actor.Value));
        DetachCheck detach = facts.Inventory.CheckDetachForThrow(slot, p.EntryId, p.Units, out byte hotbarIndex);
        if (detach == DetachCheck.BadRequest)
        {
            return new ThrowCheck(ThrowReasons.BadRequest);
        }

        // 2. ... or not the selected slot. From here the id is a live real id (Ok), so EntryId.Real below cannot assert.
        if (detach == DetachCheck.Stale || facts.Hotbar.SelectedIndex(slot) != hotbarIndex)
        {
            return ThrowCheck.Of(ThrowReason.StaleEntry);
        }

        // 3-4. Frozen is its own reply; every other non-Alive state (Dead, DeadAway, Joining) is dead, as CheckInteraction maps them.
        LifeState life = facts.Players.GetLifeState(slot);
        if (life != LifeState.Alive && life != LifeState.Frozen)
        {
            return ThrowCheck.Of(ThrowReason.Dead);
        }

        if (life == LifeState.Frozen)
        {
            return ThrowCheck.Of(ThrowReason.Frozen);
        }

        // 5-6. The carrier facts: the stored sample's swimming bit, and on foot. Spawn protection and support state are never read.
        if (facts.Players.IsSwimming(slot))
        {
            return ThrowCheck.Of(ThrowReason.Swimming);
        }

        if (facts.Players.IsMounted(slot))
        {
            return ThrowCheck.Of(ThrowReason.Unavailable);
        }

        // 7. The leaky bucket (stub at M-1a).
        uint tat = facts.Tat[slot.Value];
        if (!CooldownAccepts(applyTick, tat))
        {
            return ThrowCheck.Of(ThrowReason.ThrowCooldown);
        }

        // 8. The F1 host bounds, the one implementation.
        if (!LaunchMath.ValidateDir(p.DirX, p.DirY, p.DirZ, out _))
        {
            return ThrowCheck.Of(ThrowReason.BadDirection);
        }

        // 9. Claim-and-corroborate: never a rejection. An uncorroborated claim or no history uses the host's position.
        var claim = new Vec3Mm(p.FeetX, p.FeetY, p.FeetZ);
        Corroboration c = facts.Corroborator.Check(slot, in claim, facts.ClaimToleranceMm, out Vec3Mm newest);
        bool clamped = c != Corroboration.Corroborated;

        // 10. Every flight slot in use: exactly FlightTable.TryAppend's refusal, so story 010's commit cannot fail on it.
        if (facts.Flights.Count == facts.Flights.Capacity)
        {
            facts.Counters.Add(CounterId.ThrowingFlightCapRefusals);
            return ThrowCheck.Of(ThrowReason.FlightCap);
        }

        plan = new ThrowPlan(slot, EntryId.Real(p.EntryId), p.Units, clamped ? newest : claim, clamped, p.DirX, p.DirY, p.DirZ, p.Side, tat);
        return ThrowCheck.Ok;
    }

    private static ThrowCheck Reject(ThrowCheck check, in ThrowHostFacts facts)
    {
        facts.Counters.Add(CounterId.ThrowingReleaseRejections);
        return check;
    }
}
