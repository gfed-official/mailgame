using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// What Mail &amp; Destinations provides to the systems above it (ADR-0017 Key Interfaces; ADR-0003 section 5): synchronous calls made
/// by in-tick callers inside their own phase. At story 005 it declared only <see cref="ConsumeHand"/>, <see cref="SetDeliveriesState"/>
/// and <see cref="TryGetDestinationFacts"/> (TD-177 item 6): <c>ResolveThrownLanding</c> arrives with story 008 and <c>ConsumeFed</c>
/// with M-1b. Allocation-free; never throws on the tick path. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// IMailApi mail = context.Api&lt;IMailApi&gt;(SystemId.MailDestinations);
/// if (mail.TryGetDestinationFacts(dest, out DestinationFacts f)) { /* f.State */ }
/// </code>
/// </example>
public interface IMailApi
{
    /// <summary>
    /// The hand path into the consume routine (ADR-0017 section 5; mail-destinations story 007): Delivery's <c>deliver_matching</c>
    /// handler, phase 0, in <c>opSeq</c> order, with <c>actionStartTick = holdStartTick</c>. Each entry in turn goes through
    /// <c>MailConsumer.Consume</c>: an Accept consumes the whole entry, a Misdeliver exactly one unit, a Reject nothing. One call scope;
    /// the events (Inventory's records, <c>MailConsumed</c> per unit, <c>ComplaintRaised</c>, one <c>DeliveryOutcome</c> per entry) come
    /// before the handler's reply. A wallet-floor stop, or a refused removal with asserts off, ends the hand-over. Returns the receipt
    /// (ADR-0017 Key Interfaces erratum 2026-11-04, TD-208 item 3); <c>default</c> for an unregistered destination or a call outside a
    /// tick phase (counted by <c>TickGuard</c>), with nothing changed. 0 B after warm-up.
    /// </summary>
    /// <param name="dest">The destination entity.</param>
    /// <param name="inScanOrder">The candidate entries, in scan order.</param>
    /// <param name="actor">The delivering player.</param>
    /// <param name="holdStartTick">The tick the hold started.</param>
    /// <returns>The receipt.</returns>
    /// <exception cref="SimAssertException"><see cref="SimAssertCode.MailConsumeUnitRefused"/> under <c>SIM_ASSERT</c> when Inventory refuses a unit Mail has just read.</exception>
    HandConsumeSummary ConsumeHand(EntityId dest, ReadOnlySpan<EntryRef> inScanOrder, PlayerSlot actor, uint holdStartTick);

    /// <summary>
    /// Run Structure's downward call that sets the deliveries state (ADR-0017 section 3). Called by the Run shift stub in phase 12 of tick T − 1, so
    /// the state is effective from tick T's phase 0. Stores the three values; Mail raises no event. Illegal outside a phase (the
    /// ADR-0003 section 5 guard): nothing is stored.
    /// </summary>
    /// <param name="open">Whether deliveries are open.</param>
    /// <param name="closeTick">The tick they closed.</param>
    /// <param name="unlockedDistrictMask">The unlocked-district mask (bit <c>districtId − 1</c>).</param>
    void SetDeliveriesState(bool open, uint closeTick, ulong unlockedDistrictMask);

    /// <summary>Fills the facts of a registered destination; false and <c>default</c> for any other entity. 0 B.</summary>
    /// <param name="dest">The destination entity.</param>
    /// <param name="f">The facts when found.</param>
    /// <returns>True when registered.</returns>
    bool TryGetDestinationFacts(EntityId dest, out DestinationFacts f);

    /// <summary>
    /// The number of registered destinations (registry rows; ordinals 0 to <c>DestinationCount - 1</c>). Read by Throwing's catch-volume
    /// adapter at run start (throwing-catching story 003). Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-199 item 1).
    /// Mail Dev default, approved by Evan 2026-11-01 12:05 PM PT (TD-202 item 6): the three member names, and an out-of-range ordinal throws (run start only, never the tick path).
    /// </summary>
    int DestinationCount { get; }

    /// <summary>The destination entity of registry row <paramref name="ordinal"/> (lot order, so ascending ids). 0 B (TD-199 item 1).</summary>
    /// <param name="ordinal">The row, 0 to <see cref="DestinationCount"/> - 1.</param>
    /// <returns>The destination's entity id.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is outside the registry.</exception>
    EntityId DestinationAt(int ordinal);

    /// <summary>The world catch box of registry row <paramref name="ordinal"/> (inclusive corners). 0 B (TD-199 item 1).</summary>
    /// <param name="ordinal">The row, 0 to <see cref="DestinationCount"/> - 1.</param>
    /// <returns>The catch volume as a world AABB.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is outside the registry.</exception>
    AabbMm CatchVolumeAt(int ordinal);

    /// <summary>
    /// Run Structure's shift-start call (GDD R10; ADR-0017 section 10; mail-destinations story 013): stores the profile and marks the
    /// manifest dirty; Mail's phase 8 rolls it once that tick, so several changes in one tick roll once and an unchanged profile changes
    /// nothing. A new shift number sets Mail's current shift and zeroes the shift's wrong-delivery count. Locked from Delivery start: a
    /// call for the same shift after deliveries opened, or any call while they are open, is ignored and counted. Allocates no mailId.
    /// Illegal outside a phase (the ADR-0003 section 5 guard): nothing is stored. 0 B.
    /// </summary>
    /// <param name="p">The shift's spawn profile.</param>
    void PrepareShift(in SpawnProfile p);
}
