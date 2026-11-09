namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The shift spawner's inputs (GDD mail-destinations R10; ADR-0017 section 10; mail-destinations story 013), handed to
/// <see cref="IMailApi.PrepareShift"/> by Run Structure (the M-1a shift stub) at the start of each Prep. Immutable, 0 B; value
/// equality is what decides "the profile changed, re-roll". <see cref="FoundValueBudgetCents"/> is unused at M-1a.
/// </summary>
/// <param name="Shift">The shift number (1-based).</param>
/// <param name="SpawnBudgetCents">V, the spawn budget in cents (quota x spawnOverhead, already scaled by the player count).</param>
/// <param name="DeliverySeconds">D, the Delivery length in seconds.</param>
/// <param name="RaidWindowSeconds">The raid overlap at the end of Delivery (0 on shift 1).</param>
/// <param name="CursedSharePm">The chance a unit is Cursed, permille.</param>
/// <param name="PlayerCount">The player count (R9).</param>
/// <param name="PlayerScalePm">The team <c>playerScale</c>, permille (stored for the team complaint cap).</param>
/// <param name="FoundValueBudgetCents">The Found mail budget (R15; unused at M-1a).</param>
/// <example><code>var p = new SpawnProfile(1, 960, 240, 0, 0, 1, 1000, 0); mail.PrepareShift(in p);</code></example>
public readonly record struct SpawnProfile(
    int Shift,
    long SpawnBudgetCents,
    int DeliverySeconds,
    int RaidWindowSeconds,
    int CursedSharePm,
    int PlayerCount,
    int PlayerScalePm,
    long FoundValueBudgetCents);
