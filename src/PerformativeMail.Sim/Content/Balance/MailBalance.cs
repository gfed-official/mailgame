namespace PerformativeMail.Sim.Content.Balance;

/// <summary>The resolved handles of every <c>mail.*</c> balance key (see <see cref="BalanceKeys.Mail.Resolve"/>).</summary>
public readonly record struct MailKeys(
    BalanceKey PayMultMaxPm,
    BalanceKey PenaltyMultMaxPm,
    BalanceKey LateValueRatio,
    BalanceKey MisdeliveryPenaltyRatio,
    BalanceKey DeadLetterShifts,
    BalanceKey MinPenaltyCents,
    BalanceKey DoorComplaintCap,
    BalanceKey DoorComplaintWindowSeconds,
    BalanceKey LateComplaint,
    BalanceKey LateComplaintWindowSeconds,
    BalanceKey TeamComplaintCap,
    BalanceKey CloseGraceTicks,
    BalanceKey DistanceStepPm,
    BalanceKey ShiftStepPm);

/// <summary>
/// The typed <c>mail.*</c> values, built once from the <see cref="BalanceTable"/> when the <see cref="ContentSet"/> is
/// built. A system receives only its own record (ADR-0003 section 4). Every value is an already-scaled integer (CD4);
/// <c>...Seconds</c> keys hold milliseconds. Immutable; safe from any thread.
/// </summary>
/// <example><code>MailBalance p = contentSet.Mail; int walk = p.PayMultMaxPm;</code></example>
public sealed record class MailBalance
{
    /// <summary>Cap on the pay multiplier (CD12 clamp). Key <c>mail.payMultMaxPm</c>, default 5000.</summary>
    public int PayMultMaxPm { get; init; }

    /// <summary>Cap on the penalty multiplier (CD12 clamp). Key <c>mail.penaltyMultMaxPm</c>, default 2000.</summary>
    public int PenaltyMultMaxPm { get; init; }

    /// <summary>Late pay share. Key <c>mail.lateValueRatio</c>, default 500.</summary>
    public int LateValueRatio { get; init; }

    /// <summary>Misdelivery penalty share of value. Key <c>mail.misdeliveryPenaltyRatio</c>, default 500.</summary>
    public int MisdeliveryPenaltyRatio { get; init; }

    /// <summary>Shifts before mail stops paying. Key <c>mail.deadLetterShifts</c>, default 2.</summary>
    public int DeadLetterShifts { get; init; }

    /// <summary>Smallest penalty, cents. Key <c>mail.minPenaltyCents</c>, default 1.</summary>
    public int MinPenaltyCents { get; init; }

    /// <summary>Complaint cap per op and door burst, milli-points. Key <c>mail.doorComplaintCap</c>, default 20_000.</summary>
    public int DoorComplaintCap { get; init; }

    /// <summary>Door complaint bucket window, ms. Key <c>mail.doorComplaintWindowSeconds</c>, default 30_000.</summary>
    public int DoorComplaintWindowSeconds { get; init; }

    /// <summary>Late complaint, milli-points. Key <c>mail.lateComplaint</c>, default 2000.</summary>
    public int LateComplaint { get; init; }

    /// <summary>Late complaint window, ms. Key <c>mail.lateComplaintWindowSeconds</c>, default 30_000.</summary>
    public int LateComplaintWindowSeconds { get; init; }

    /// <summary>Team complaint burst cap before playerScale, milli-points. Key <c>mail.teamComplaintCap</c>, default 40_000.</summary>
    public int TeamComplaintCap { get; init; }

    /// <summary>Closing grace for holds and throws started while open, ticks. Key <c>mail.closeGraceTicks</c>, default 90.</summary>
    public int CloseGraceTicks { get; init; }

    /// <summary>Far-district value step. Key <c>mail.distanceStepPm</c>, default 250.</summary>
    public int DistanceStepPm { get; init; }

    /// <summary>Late-shift value step. Key <c>mail.shiftStepPm</c>, default 100.</summary>
    public int ShiftStepPm { get; init; }

    /// <summary>Reads every mail value out of <paramref name="table"/>.</summary>
    /// <param name="table">A table built from a registry that holds the mail rows.</param>
    /// <exception cref="ArgumentException">The table's registry lacks a mail row.</exception>
    public static MailBalance FromTable(BalanceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        MailKeys k = BalanceKeys.Mail.Resolve(table.Registry);
        return new MailBalance
        {
            PayMultMaxPm = table.GetInt(k.PayMultMaxPm),
            PenaltyMultMaxPm = table.GetInt(k.PenaltyMultMaxPm),
            LateValueRatio = table.GetInt(k.LateValueRatio),
            MisdeliveryPenaltyRatio = table.GetInt(k.MisdeliveryPenaltyRatio),
            DeadLetterShifts = table.GetInt(k.DeadLetterShifts),
            MinPenaltyCents = table.GetInt(k.MinPenaltyCents),
            DoorComplaintCap = table.GetInt(k.DoorComplaintCap),
            DoorComplaintWindowSeconds = table.GetInt(k.DoorComplaintWindowSeconds),
            LateComplaint = table.GetInt(k.LateComplaint),
            LateComplaintWindowSeconds = table.GetInt(k.LateComplaintWindowSeconds),
            TeamComplaintCap = table.GetInt(k.TeamComplaintCap),
            CloseGraceTicks = table.GetInt(k.CloseGraceTicks),
            DistanceStepPm = table.GetInt(k.DistanceStepPm),
            ShiftStepPm = table.GetInt(k.ShiftStepPm),
        };
    }
}
