namespace PerformativeMail.Sim.Content;

/// <summary>
/// A closed behaviour switch (content-data CD18, TR-content-018 enum half): a perk effect that changes behaviour rather
/// than a number. A flag is on while at least one live source holds it (no stack count) and the owning system checks it.
/// Timed effects are not flags or modifiers: the owning system passes its live term as <c>transientAdd</c> (the sheet
/// has no timers). Values 1 to 14 follow the CD18 order and are never renumbered, reordered or reused (they are part of
/// the content hash, frozen by <c>StatDefTableTests</c>). 0 is never a valid loaded value. Data spellings are in
/// <see cref="RuleFlagSpelling"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = RuleFlagSpelling.TryParse("postal_sprint"u8, out RuleFlag f);   // ok, f == RuleFlag.PostalSprint
/// </code>
/// </example>
public enum RuleFlag : byte
{
    /// <summary>KeepHotbarOnDeath (Second Wind). Data spelling <c>keep_hotbar_on_death</c>.</summary>
    KeepHotbarOnDeath = 1,

    /// <summary>PresortOutfeed (Presort). Data spelling <c>presort_outfeed</c>.</summary>
    PresortOutfeed = 2,

    /// <summary>InsuredRebuild (Insured). Data spelling <c>insured_rebuild</c>.</summary>
    InsuredRebuild = 3,

    /// <summary>SuppressLateComplaint (Grace Period). Data spelling <c>suppress_late_complaint</c>.</summary>
    SuppressLateComplaint = 4,

    /// <summary>PriorityMail (Priority Post). Data spelling <c>priority_mail</c>.</summary>
    PriorityMail = 5,

    /// <summary>FreeForecast (Forecast Desk). Data spelling <c>free_forecast</c>.</summary>
    FreeForecast = 6,

    /// <summary>SharedBuild (Shared Build). Data spelling <c>shared_build</c>.</summary>
    SharedBuild = 7,

    /// <summary>LocalKnowledgeArrow (Local Knowledge). Data spelling <c>local_knowledge_arrow</c>.</summary>
    LocalKnowledgeArrow = 8,

    /// <summary>BeltMomentum (Bunny Hop). Data spelling <c>belt_momentum</c>.</summary>
    BeltMomentum = 9,

    /// <summary>EarlyDistrictUnlock (Zoning Board). Data spelling <c>early_district_unlock</c>.</summary>
    EarlyDistrictUnlock = 10,

    /// <summary>EnemiesMarked (Early Warning). Data spelling <c>enemies_marked</c>.</summary>
    EnemiesMarked = 11,

    /// <summary>PostalSprint (Postal Sprint). Data spelling <c>postal_sprint</c>.</summary>
    PostalSprint = 12,

    /// <summary>CursedMail (Cursed Mail stamp). Data spelling <c>cursed_mail</c>.</summary>
    CursedMail = 13,

    /// <summary>RushHour (Rush Hour stamp). Data spelling <c>rush_hour</c>.</summary>
    RushHour = 14,
}
