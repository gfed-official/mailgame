namespace PerformativeMail.Sim.Content;

/// <summary>
/// The snake_case data spelling table for <see cref="RuleFlag"/> (content-data CD18). Hand-written and compile-time, in
/// the same discipline as <see cref="WeightClassSpelling"/>: no reflection, no Enum parsing, no static state, ordinal
/// UTF-8 comparison with no numeric or case-insensitive fallback. Every member has exactly one spelling and every
/// spelling maps back to its member (checked by <c>StatDefTableTests</c>). Stateless, so safe to call from any thread.
/// </summary>
/// <example>
/// <code>
/// bool ok = RuleFlagSpelling.TryParse("rush_hour"u8, out RuleFlag f);   // ok, f == RuleFlag.RushHour
/// ReadOnlySpan&lt;byte&gt; back = RuleFlagSpelling.ToSpelling(RuleFlag.CursedMail);   // "cursed_mail"u8
/// </code>
/// </example>
public static class RuleFlagSpelling
{
    /// <summary>
    /// Looks up the member whose data spelling equals <paramref name="utf8"/> byte for byte.
    /// </summary>
    /// <param name="utf8">The spelling as UTF-8 bytes (no quotes, no terminator).</param>
    /// <param name="value">The member, or <c>default</c> (0, never a valid loaded value) when not found.</param>
    /// <returns><c>true</c> when <paramref name="utf8"/> is exactly one of the spellings; <c>false</c> otherwise, including the empty span.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out RuleFlag value)
    {
        if (utf8.SequenceEqual("keep_hotbar_on_death"u8))
        {
            value = RuleFlag.KeepHotbarOnDeath;
            return true;
        }

        if (utf8.SequenceEqual("presort_outfeed"u8))
        {
            value = RuleFlag.PresortOutfeed;
            return true;
        }

        if (utf8.SequenceEqual("insured_rebuild"u8))
        {
            value = RuleFlag.InsuredRebuild;
            return true;
        }

        if (utf8.SequenceEqual("suppress_late_complaint"u8))
        {
            value = RuleFlag.SuppressLateComplaint;
            return true;
        }

        if (utf8.SequenceEqual("priority_mail"u8))
        {
            value = RuleFlag.PriorityMail;
            return true;
        }

        if (utf8.SequenceEqual("free_forecast"u8))
        {
            value = RuleFlag.FreeForecast;
            return true;
        }

        if (utf8.SequenceEqual("shared_build"u8))
        {
            value = RuleFlag.SharedBuild;
            return true;
        }

        if (utf8.SequenceEqual("local_knowledge_arrow"u8))
        {
            value = RuleFlag.LocalKnowledgeArrow;
            return true;
        }

        if (utf8.SequenceEqual("belt_momentum"u8))
        {
            value = RuleFlag.BeltMomentum;
            return true;
        }

        if (utf8.SequenceEqual("early_district_unlock"u8))
        {
            value = RuleFlag.EarlyDistrictUnlock;
            return true;
        }

        if (utf8.SequenceEqual("enemies_marked"u8))
        {
            value = RuleFlag.EnemiesMarked;
            return true;
        }

        if (utf8.SequenceEqual("postal_sprint"u8))
        {
            value = RuleFlag.PostalSprint;
            return true;
        }

        if (utf8.SequenceEqual("cursed_mail"u8))
        {
            value = RuleFlag.CursedMail;
            return true;
        }

        if (utf8.SequenceEqual("rush_hour"u8))
        {
            value = RuleFlag.RushHour;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the data spelling of a member.</summary>
    /// <param name="value">The member.</param>
    /// <returns>The UTF-8 spelling, or an empty span when <paramref name="value"/> is not a defined member.</returns>
    public static ReadOnlySpan<byte> ToSpelling(RuleFlag value) => value switch
    {
        RuleFlag.KeepHotbarOnDeath => "keep_hotbar_on_death"u8,
        RuleFlag.PresortOutfeed => "presort_outfeed"u8,
        RuleFlag.InsuredRebuild => "insured_rebuild"u8,
        RuleFlag.SuppressLateComplaint => "suppress_late_complaint"u8,
        RuleFlag.PriorityMail => "priority_mail"u8,
        RuleFlag.FreeForecast => "free_forecast"u8,
        RuleFlag.SharedBuild => "shared_build"u8,
        RuleFlag.LocalKnowledgeArrow => "local_knowledge_arrow"u8,
        RuleFlag.BeltMomentum => "belt_momentum"u8,
        RuleFlag.EarlyDistrictUnlock => "early_district_unlock"u8,
        RuleFlag.EnemiesMarked => "enemies_marked"u8,
        RuleFlag.PostalSprint => "postal_sprint"u8,
        RuleFlag.CursedMail => "cursed_mail"u8,
        RuleFlag.RushHour => "rush_hour"u8,
        _ => default,
    };
}
