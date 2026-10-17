namespace PerformativeMail.Sim.Content;

/// <summary>
/// The snake_case data spelling table for <see cref="Stat"/> (content-data CD12; ADR-0010 section 7 "generated
/// snake_case data spelling"). Hand-written and compile-time, in the same discipline as <see cref="WeightClassSpelling"/>:
/// no reflection, no Enum parsing, no static state, ordinal UTF-8 comparison with no numeric or case-insensitive
/// fallback. The spelling of each member is its PascalCase name in snake_case (<c>ThrowRangePermille</c> to
/// <c>throw_range_permille</c>); <c>StatDefTableTests</c> derives that from the member name and checks the map is
/// bijective. Stateless, so safe to call from any thread.
/// </summary>
/// <example>
/// <code>
/// bool ok = StatSpelling.TryParse("throw_range_permille"u8, out Stat s);   // ok, s == Stat.ThrowRangePermille
/// ReadOnlySpan&lt;byte&gt; back = StatSpelling.ToSpelling(Stat.ExtraJumps);   // "extra_jumps"u8
/// </code>
/// </example>
public static class StatSpelling
{
    /// <summary>
    /// Looks up the member whose data spelling equals <paramref name="utf8"/> byte for byte.
    /// </summary>
    /// <param name="utf8">The spelling as UTF-8 bytes (no quotes, no terminator).</param>
    /// <param name="value">The member, or <c>default</c> (0, never a valid loaded value) when not found.</param>
    /// <returns><c>true</c> when <paramref name="utf8"/> is exactly one of the spellings; <c>false</c> otherwise, including the empty span.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out Stat value)
    {
        if (utf8.SequenceEqual("deliver_insert_pm"u8))
        {
            value = Stat.DeliverInsertPm;
            return true;
        }

        if (utf8.SequenceEqual("move_speed_add_pm"u8))
        {
            value = Stat.MoveSpeedAddPm;
            return true;
        }

        if (utf8.SequenceEqual("extra_jumps"u8))
        {
            value = Stat.ExtraJumps;
            return true;
        }

        if (utf8.SequenceEqual("respawn_ms"u8))
        {
            value = Stat.RespawnMs;
            return true;
        }

        if (utf8.SequenceEqual("inventory_rows"u8))
        {
            value = Stat.InventoryRows;
            return true;
        }

        if (utf8.SequenceEqual("mail_max_stack"u8))
        {
            value = Stat.MailMaxStack;
            return true;
        }

        if (utf8.SequenceEqual("weight_penalty_pm"u8))
        {
            value = Stat.WeightPenaltyPm;
            return true;
        }

        if (utf8.SequenceEqual("mail_value_mult_pm"u8))
        {
            value = Stat.MailValueMultPm;
            return true;
        }

        if (utf8.SequenceEqual("thrown_correct_pay_mult_pm"u8))
        {
            value = Stat.ThrownCorrectPayMultPm;
            return true;
        }

        if (utf8.SequenceEqual("late_value_ratio_pm"u8))
        {
            value = Stat.LateValueRatioPm;
            return true;
        }

        if (utf8.SequenceEqual("misdelivery_penalty_mult_pm"u8))
        {
            value = Stat.MisdeliveryPenaltyMultPm;
            return true;
        }

        if (utf8.SequenceEqual("misdelivery_complaint_mult_pm"u8))
        {
            value = Stat.MisdeliveryComplaintMultPm;
            return true;
        }

        if (utf8.SequenceEqual("throw_range_permille"u8))
        {
            value = Stat.ThrowRangePermille;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the data spelling of a member.</summary>
    /// <param name="value">The member.</param>
    /// <returns>The UTF-8 spelling, or an empty span when <paramref name="value"/> is not a defined member.</returns>
    public static ReadOnlySpan<byte> ToSpelling(Stat value) => value switch
    {
        Stat.DeliverInsertPm => "deliver_insert_pm"u8,
        Stat.MoveSpeedAddPm => "move_speed_add_pm"u8,
        Stat.ExtraJumps => "extra_jumps"u8,
        Stat.RespawnMs => "respawn_ms"u8,
        Stat.InventoryRows => "inventory_rows"u8,
        Stat.MailMaxStack => "mail_max_stack"u8,
        Stat.WeightPenaltyPm => "weight_penalty_pm"u8,
        Stat.MailValueMultPm => "mail_value_mult_pm"u8,
        Stat.ThrownCorrectPayMultPm => "thrown_correct_pay_mult_pm"u8,
        Stat.LateValueRatioPm => "late_value_ratio_pm"u8,
        Stat.MisdeliveryPenaltyMultPm => "misdelivery_penalty_mult_pm"u8,
        Stat.MisdeliveryComplaintMultPm => "misdelivery_complaint_mult_pm"u8,
        Stat.ThrowRangePermille => "throw_range_permille"u8,
        _ => default,
    };
}
