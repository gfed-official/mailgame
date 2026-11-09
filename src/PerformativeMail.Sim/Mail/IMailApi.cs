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
    /// Run Structure's downward call that sets the deliveries state (ADR-0017 section 3). Story 009 implements it; until then it is a
    /// no-op that asserts <see cref="SimAssertCode.MailApiNotImplemented"/>.
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
    /// The throw path into the consume routine (ADR-0017 section 5 throw row; GDD R13; mail-destinations story 008): Throwing's step,
    /// phase 4, in flight order, after phase 0's hand ops (R6 tick order is phase order). <paramref name="e"/> is the flight's entry in
    /// <c>IInventoryApi.ConsumeUnit</c>'s Flight form (<c>new EntryRef(ContainerId.None, entry)</c>). One <c>MailConsumer.Consume</c> call
    /// with <c>actionStartTick = releaseTick</c> (the closing grace judges the release) and the snapshotted <paramref name="thrownPm"/>
    /// (Cannon Arm applies even when the thrower has left; the live stat sheet's thrown multiplier is never read): an Accept pays every
    /// unit; a Misdeliver misdelivers every unit, oldest first, until the wallet floor; a Reject consumes nothing. Lateness is read at the
    /// apply tick's shift. Mail never creates the bounce: the caller turns a Reject or the remainder into one WorldItem (ADR-0015).
    /// Out of phase or at an unregistered destination it changes nothing and returns <see cref="LandingKind.Rejected"/> with the whole
    /// entry remaining. 0 B after warm-up.
    /// </summary>
    /// <param name="dest">The destination entity whose catch volume the flight entered.</param>
    /// <param name="e">The flight's entry (Flight form).</param>
    /// <param name="thrower">The thrower's slot (the outcome's actor).</param>
    /// <param name="thrownPm">The thrower's thrown pay multiplier at release, permille.</param>
    /// <param name="releaseTick">The tick the throw was released.</param>
    /// <returns>What was consumed and what remains on the entry.</returns>
    /// <exception cref="SimAssertException"><see cref="SimAssertCode.MailConsumeUnitRefused"/> under <c>SIM_ASSERT</c> when Inventory refuses a unit Mail has just read.</exception>
    LandingResult ResolveThrownLanding(EntityId dest, in EntryRef e, PlayerSlot thrower, int thrownPm, uint releaseTick);
}
