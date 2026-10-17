using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Balance;

public static partial class BalanceKeys
{
    /// <summary>The <c>mail.*</c> balance rows: key names, units, hard ranges, M-1a default values and safe ranges (CD9).</summary>
    /// <example><code>ImmutableArray&lt;BalanceRow&gt; rows = BalanceKeys.Mail.Rows;</code></example>
    public static partial class Mail
    {
        /// <summary>The owner file name and key prefix.</summary>
        public const string Owner = "mail";

        /// <summary>The key <c>mail.payMultMaxPm</c>.</summary>
        public const string PayMultMaxPmKey = "payMultMaxPm";

        /// <summary>The key <c>mail.penaltyMultMaxPm</c>.</summary>
        public const string PenaltyMultMaxPmKey = "penaltyMultMaxPm";

        /// <summary>The key <c>mail.lateValueRatio</c>.</summary>
        public const string LateValueRatioKey = "lateValueRatio";

        /// <summary>The key <c>mail.misdeliveryPenaltyRatio</c>.</summary>
        public const string MisdeliveryPenaltyRatioKey = "misdeliveryPenaltyRatio";

        /// <summary>The key <c>mail.deadLetterShifts</c>.</summary>
        public const string DeadLetterShiftsKey = "deadLetterShifts";

        /// <summary>The key <c>mail.minPenaltyCents</c>.</summary>
        public const string MinPenaltyCentsKey = "minPenaltyCents";

        /// <summary>The key <c>mail.doorComplaintCap</c>.</summary>
        public const string DoorComplaintCapKey = "doorComplaintCap";

        /// <summary>The key <c>mail.doorComplaintWindowSeconds</c>.</summary>
        public const string DoorComplaintWindowSecondsKey = "doorComplaintWindowSeconds";

        /// <summary>The key <c>mail.lateComplaint</c>.</summary>
        public const string LateComplaintKey = "lateComplaint";

        /// <summary>The key <c>mail.lateComplaintWindowSeconds</c>.</summary>
        public const string LateComplaintWindowSecondsKey = "lateComplaintWindowSeconds";

        /// <summary>The key <c>mail.teamComplaintCap</c>.</summary>
        public const string TeamComplaintCapKey = "teamComplaintCap";

        /// <summary>The key <c>mail.closeGraceTicks</c>.</summary>
        public const string CloseGraceTicksKey = "closeGraceTicks";

        /// <summary>The key <c>mail.distanceStepPm</c>.</summary>
        public const string DistanceStepPmKey = "distanceStepPm";

        /// <summary>The key <c>mail.shiftStepPm</c>.</summary>
        public const string ShiftStepPmKey = "shiftStepPm";

        // One table drives the rows, the default entries and (with the generated handle record) the owner record, so a key's range and value cannot drift apart.
        private static ImmutableArray<BalanceKnob> Knobs => ImmutableArray.Create(
            new BalanceKnob(PayMultMaxPmKey, BalanceUnit.Permille, 5000, 0, 100_000),
            new BalanceKnob(PenaltyMultMaxPmKey, BalanceUnit.Permille, 2000, 0, 100_000),
            new BalanceKnob(LateValueRatioKey, BalanceUnit.Permille, 500, 0, 1000, 250, 750),
            new BalanceKnob(MisdeliveryPenaltyRatioKey, BalanceUnit.Permille, 500, 0, 1000, 300, 750),
            new BalanceKnob(DeadLetterShiftsKey, BalanceUnit.Count, 2, 1, 16, 1, 3),
            new BalanceKnob(MinPenaltyCentsKey, BalanceUnit.Cents, 1, 0, 1_000_000_000, 1, 2),
            new BalanceKnob(DoorComplaintCapKey, BalanceUnit.MilliPoints, 20_000, 0, 10_000_000, 16_000, 30_000),
            new BalanceKnob(DoorComplaintWindowSecondsKey, BalanceUnit.Ms, 30_000, 0, 3_600_000, 15_000, 60_000),
            new BalanceKnob(LateComplaintKey, BalanceUnit.MilliPoints, 2000, 0, 10_000_000, 1000, 3000),
            new BalanceKnob(LateComplaintWindowSecondsKey, BalanceUnit.Ms, 30_000, 0, 3_600_000, 20_000, 60_000),
            new BalanceKnob(TeamComplaintCapKey, BalanceUnit.MilliPoints, 40_000, 0, 10_000_000, 30_000, 60_000),
            new BalanceKnob(CloseGraceTicksKey, BalanceUnit.Ticks, 90, 0, 100_000, 30, 150),
            new BalanceKnob(DistanceStepPmKey, BalanceUnit.Permille, 250, 0, 100_000, 150, 350),
            new BalanceKnob(ShiftStepPmKey, BalanceUnit.Permille, 100, 0, 100_000, 50, 150));

        /// <summary>The registry rows (a new array each call; the class holds no static state).</summary>
        public static ImmutableArray<BalanceRow> Rows => BalanceKnob.ToRows(Owner, Knobs);

        /// <summary>The M-1a default entries with their safe-range annotations (a new array each call).</summary>
        /// <example><code>BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());</code></example>
        public static ImmutableArray<BalanceEntry> DefaultEntries() => BalanceKnob.ToEntries(Owner, Knobs);

        /// <summary>Resolves every <c>mail.*</c> handle against <paramref name="registry"/> once, at composition.</summary>
        /// <param name="registry">A registry that holds every mail row (for example <see cref="CreateRegistry"/>).</param>
        /// <exception cref="ArgumentException">A row is missing.</exception>
        /// <example><code>MailKeys keys = BalanceKeys.Mail.Resolve(table.Registry);</code></example>
        public static MailKeys Resolve(BalanceRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            return new MailKeys(
                BalanceKeys.Handle(registry, Owner, PayMultMaxPmKey),
                BalanceKeys.Handle(registry, Owner, PenaltyMultMaxPmKey),
                BalanceKeys.Handle(registry, Owner, LateValueRatioKey),
                BalanceKeys.Handle(registry, Owner, MisdeliveryPenaltyRatioKey),
                BalanceKeys.Handle(registry, Owner, DeadLetterShiftsKey),
                BalanceKeys.Handle(registry, Owner, MinPenaltyCentsKey),
                BalanceKeys.Handle(registry, Owner, DoorComplaintCapKey),
                BalanceKeys.Handle(registry, Owner, DoorComplaintWindowSecondsKey),
                BalanceKeys.Handle(registry, Owner, LateComplaintKey),
                BalanceKeys.Handle(registry, Owner, LateComplaintWindowSecondsKey),
                BalanceKeys.Handle(registry, Owner, TeamComplaintCapKey),
                BalanceKeys.Handle(registry, Owner, CloseGraceTicksKey),
                BalanceKeys.Handle(registry, Owner, DistanceStepPmKey),
                BalanceKeys.Handle(registry, Owner, ShiftStepPmKey));
        }
    }
}
