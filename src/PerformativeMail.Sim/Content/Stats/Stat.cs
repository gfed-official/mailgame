using PerformativeMail.Sim.Content.Stats;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// A closed stat key of the stat catalogue (content-data CD12, TR-content-014, ADR-0010 section 7). This is the M-1a
/// subset: the first 13 CD12 rows, which are exactly the stats the Grid, Player Character, Manual Delivery, Mail and
/// Throwing systems read. Each value is the member's 1-based CD12 row position; later rows (<c>BikeSpeedMultPm</c> and
/// on, provisional per OQ-CDD-16) are appended with their CD12 positions by their reading systems' epics. Members are
/// PascalCase with a unit suffix; a value is never renumbered, reordered or reused (it is part of the content hash and
/// frozen by <c>StatDefTableTests</c>). 0 is never a valid loaded value. Each member has one code-side
/// <see cref="StatDef"/> row (<see cref="StatDefs.Get"/>); data spellings are in <see cref="StatSpelling"/>.
/// </summary>
/// <example>
/// <code>
/// StatDef def = StatDefs.Get(Stat.ThrowRangePermille);   // Add, base 1000, clamp throw.rangeMinPermille..throw.rangeMaxPermille
/// </code>
/// </example>
public enum Stat : byte
{
    /// <summary>DeliverInsertPm (Quick Hands +500). Data spelling <c>deliver_insert_pm</c>.</summary>
    DeliverInsertPm = 1,

    /// <summary>MoveSpeedAddPm (Long Legs, Postal Sprint transient). Data spelling <c>move_speed_add_pm</c>.</summary>
    MoveSpeedAddPm = 2,

    /// <summary>ExtraJumps (Bunny Hop). Data spelling <c>extra_jumps</c>.</summary>
    ExtraJumps = 3,

    /// <summary>RespawnMs (Second Wind Set). Data spelling <c>respawn_ms</c>.</summary>
    RespawnMs = 4,

    /// <summary>InventoryRows (Big Pockets, Bike Courier, Bulk Depot). Data spelling <c>inventory_rows</c>.</summary>
    InventoryRows = 5,

    /// <summary>MailMaxStack (Bulk Mailer). Data spelling <c>mail_max_stack</c>.</summary>
    MailMaxStack = 6,

    /// <summary>WeightPenaltyPm (Strong Back). Data spelling <c>weight_penalty_pm</c>.</summary>
    WeightPenaltyPm = 7,

    /// <summary>MailValueMultPm (Stamp Collector). Data spelling <c>mail_value_mult_pm</c>.</summary>
    MailValueMultPm = 8,

    /// <summary>ThrownCorrectPayMultPm (Cannon Arm). Data spelling <c>thrown_correct_pay_mult_pm</c>.</summary>
    ThrownCorrectPayMultPm = 9,

    /// <summary>LateValueRatioPm (Grace Period). Data spelling <c>late_value_ratio_pm</c>.</summary>
    LateValueRatioPm = 10,

    /// <summary>MisdeliveryPenaltyMultPm (Fragile Handling). Data spelling <c>misdelivery_penalty_mult_pm</c>.</summary>
    MisdeliveryPenaltyMultPm = 11,

    /// <summary>MisdeliveryComplaintMultPm (Fragile Handling). Data spelling <c>misdelivery_complaint_mult_pm</c>.</summary>
    MisdeliveryComplaintMultPm = 12,

    /// <summary>ThrowRangePermille (Cannon Arm). Data spelling <c>throw_range_permille</c>.</summary>
    ThrowRangePermille = 13,
}
