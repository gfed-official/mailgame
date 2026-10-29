using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// What Mail &amp; Destinations provides to the systems above it (ADR-0017 Key Interfaces; ADR-0003 section 5): synchronous calls made
/// by in-tick callers inside their own phase. At story 005 it declares only <see cref="ConsumeHand"/>, <see cref="SetDeliveriesState"/>
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
    /// The hand path into the consume routine (ADR-0017 section 5): Delivery's <c>deliver_matching</c> handler, phase 0. Story 007
    /// implements it; until then it is a no-op that asserts <see cref="SimAssertCode.MailApiNotImplemented"/>.
    /// </summary>
    /// <param name="dest">The destination entity.</param>
    /// <param name="inScanOrder">The candidate entries, in scan order.</param>
    /// <param name="actor">The delivering player.</param>
    /// <param name="holdStartTick">The tick the hold started.</param>
    void ConsumeHand(EntityId dest, ReadOnlySpan<EntryRef> inScanOrder, PlayerSlot actor, uint holdStartTick);

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
}
