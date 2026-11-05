namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// How mail arrived, the <c>via</c> byte of <see cref="DeliveryOutcomePayload"/> (ADR-0017 section 9). TD-208 item 5, approved by Evan
/// 2026-11-04 9:23 AM PT by card click (card t271s1234, listing t271s1233), in the listing's words: "numbers for how mail arrived. None is
/// 0, Hand is 1, Throw is 2, Feeder (a machine) is 3, and Npc (computer-driven) is 4." The enum name and type are uncredited (Mail Lead
/// planning detail, TD-208 item 5). Append-only.
/// </summary>
/// <example><code>var call = new ConsumeCall(DeliveryVia.Hand, d, entry, actor, holdStartTick, MailConsumer.HandThrownPm, now);</code></example>
public enum DeliveryVia : byte
{
    /// <summary>No path.</summary>
    None = 0,

    /// <summary>A hand delivery (<see cref="IMailApi.ConsumeHand"/>).</summary>
    Hand = 1,

    /// <summary>A thrown landing (story 008).</summary>
    Throw = 2,

    /// <summary>A feeder (M-1b).</summary>
    Feeder = 3,

    /// <summary>An NPC drop (later).</summary>
    Npc = 4,
}
