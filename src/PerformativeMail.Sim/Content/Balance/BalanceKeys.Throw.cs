using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Balance;

public static partial class BalanceKeys
{
    /// <summary>The <c>throw.*</c> balance rows: key names, units, hard ranges, M-1a default values and safe ranges (CD9).</summary>
    /// <example><code>ImmutableArray&lt;BalanceRow&gt; rows = BalanceKeys.Throw.Rows;</code></example>
    public static partial class Throw
    {
        /// <summary>The owner file name and key prefix.</summary>
        public const string Owner = "throw";

        /// <summary>The key <c>throw.throwSpeed.light</c>.</summary>
        public const string ThrowSpeedLightKey = "throwSpeed.light";

        /// <summary>The key <c>throw.throwSpeed.medium</c>.</summary>
        public const string ThrowSpeedMediumKey = "throwSpeed.medium";

        /// <summary>The key <c>throw.throwSpeed.heavy</c>.</summary>
        public const string ThrowSpeedHeavyKey = "throwSpeed.heavy";

        /// <summary>The key <c>throw.g</c>.</summary>
        public const string GKey = "g";

        /// <summary>The key <c>throw.loftDeg</c>.</summary>
        public const string LoftDegKey = "loftDeg";

        /// <summary>The key <c>throw.maxFlightTicks</c>.</summary>
        public const string MaxFlightTicksKey = "maxFlightTicks";

        /// <summary>The key <c>throw.throwCooldownTicks</c>.</summary>
        public const string ThrowCooldownTicksKey = "throwCooldownTicks";

        /// <summary>The key <c>throw.throwBurstTicks</c>.</summary>
        public const string ThrowBurstTicksKey = "throwBurstTicks";

        /// <summary>The key <c>throw.handHeightMm</c>.</summary>
        public const string HandHeightMmKey = "handHeightMm";

        /// <summary>The key <c>throw.fwdOffMm</c>.</summary>
        public const string FwdOffMmKey = "fwdOffMm";

        /// <summary>The key <c>throw.sideOffMm</c>.</summary>
        public const string SideOffMmKey = "sideOffMm";

        /// <summary>The key <c>throw.damagePerPoint</c>.</summary>
        public const string DamagePerPointKey = "damagePerPoint";

        /// <summary>The key <c>throw.dropPushbackMm</c>.</summary>
        public const string DropPushbackMmKey = "dropPushbackMm";

        /// <summary>The key <c>throw.enemyHitSlackMm</c>.</summary>
        public const string EnemyHitSlackMmKey = "enemyHitSlackMm";

        /// <summary>The key <c>throw.previewSettleMs</c>.</summary>
        public const string PreviewSettleMsKey = "previewSettleMs";

        /// <summary>The key <c>throw.rangeMinPermille</c>.</summary>
        public const string RangeMinPermilleKey = "rangeMinPermille";

        /// <summary>The key <c>throw.rangeMaxPermille</c>.</summary>
        public const string RangeMaxPermilleKey = "rangeMaxPermille";

        /// <summary>The key <c>throw.catchRadiusMm</c>.</summary>
        public const string CatchRadiusMmKey = "catchRadiusMm";

        /// <summary>The key <c>throw.catchPredictMs</c>.</summary>
        public const string CatchPredictMsKey = "catchPredictMs";

        /// <summary>The key <c>throw.catchTapMs</c>.</summary>
        public const string CatchTapMsKey = "catchTapMs";

        /// <summary>The key <c>throw.catchHistoryMaxOffsetMm</c>.</summary>
        public const string CatchHistoryMaxOffsetMmKey = "catchHistoryMaxOffsetMm";

        /// <summary>The key <c>throw.bonkRadiusMm</c>.</summary>
        public const string BonkRadiusMmKey = "bonkRadiusMm";

        /// <summary>The key <c>throw.relayWindowTicks</c>.</summary>
        public const string RelayWindowTicksKey = "relayWindowTicks";

        /// <summary>The key <c>throw.rank.armedCatcher</c>.</summary>
        public const string RankArmedCatcherKey = "rank.armedCatcher";

        /// <summary>The key <c>throw.rank.mailbox</c>.</summary>
        public const string RankMailboxKey = "rank.mailbox";

        /// <summary>The key <c>throw.rank.goon</c>.</summary>
        public const string RankGoonKey = "rank.goon";

        /// <summary>The key <c>throw.rank.teammate</c>.</summary>
        public const string RankTeammateKey = "rank.teammate";

        /// <summary>The key <c>throw.rank.belt</c>.</summary>
        public const string RankBeltKey = "rank.belt";

        /// <summary>The key <c>throw.rank.solid</c>.</summary>
        public const string RankSolidKey = "rank.solid";

        /// <summary>The key <c>throw.rank.ground</c>.</summary>
        public const string RankGroundKey = "rank.ground";

        /// <summary>The key <c>throw.rank.water</c>.</summary>
        public const string RankWaterKey = "rank.water";

        // One table drives the rows, the default entries and (with the generated handle record) the owner record, so a key's range and value cannot drift apart.
        private static ImmutableArray<BalanceKnob> Knobs => ImmutableArray.Create(
            new BalanceKnob(ThrowSpeedLightKey, BalanceUnit.MmPerSec, 14_000, 3000, 30_000, 8000, 20_000),
            new BalanceKnob(ThrowSpeedMediumKey, BalanceUnit.MmPerSec, 10_000, 3000, 30_000, 6000, 14_000),
            new BalanceKnob(ThrowSpeedHeavyKey, BalanceUnit.MmPerSec, 6000, 3000, 30_000, 4000, 9000),
            new BalanceKnob(GKey, BalanceUnit.MmPerSecSq, 9800, 1, 1_000_000, 8000, 14_000),
            new BalanceKnob(LoftDegKey, BalanceUnit.MilliDeg, 6000, 0, 360_000, 0, 8000),
            new BalanceKnob(MaxFlightTicksKey, BalanceUnit.Ticks, 90, 30, 150, 60, 150),
            new BalanceKnob(ThrowCooldownTicksKey, BalanceUnit.Ticks, 5, 0, 100_000, 3, 15),
            new BalanceKnob(ThrowBurstTicksKey, BalanceUnit.Ticks, 5, 0, 100_000, 1, 10),
            new BalanceKnob(HandHeightMmKey, BalanceUnit.Mm, 1200, 0, 1_000_000, 1000, 1400),
            new BalanceKnob(FwdOffMmKey, BalanceUnit.Mm, 300, 0, 1_000_000, 300, 500),
            new BalanceKnob(SideOffMmKey, BalanceUnit.Mm, 250, 0, 1_000_000, 0, 400),
            new BalanceKnob(DamagePerPointKey, BalanceUnit.Count, 5, 0, 1_048_576, 3, 8),
            new BalanceKnob(DropPushbackMmKey, BalanceUnit.Mm, 500, 0, 1_000_000, 300, 800),
            new BalanceKnob(EnemyHitSlackMmKey, BalanceUnit.Mm, 300, 0, 1_000_000, 0, 500),
            new BalanceKnob(PreviewSettleMsKey, BalanceUnit.Ms, 0, 0, 3_600_000, 0, 250),
            new BalanceKnob(RangeMinPermilleKey, BalanceUnit.Permille, 1000, 0, 100_000),
            new BalanceKnob(RangeMaxPermilleKey, BalanceUnit.Permille, 1500, 0, 100_000, 1000, 1500),
            new BalanceKnob(CatchRadiusMmKey, BalanceUnit.Mm, 1500, 0, 1_000_000, 1000, 2200),
            new BalanceKnob(CatchPredictMsKey, BalanceUnit.Ms, 500, 0, 3_600_000, 300, 800),
            new BalanceKnob(CatchTapMsKey, BalanceUnit.Ms, 500, 0, 3_600_000, 300, 700),
            new BalanceKnob(CatchHistoryMaxOffsetMmKey, BalanceUnit.Mm, 1500, 0, 1_000_000, 1000, 2000),
            new BalanceKnob(BonkRadiusMmKey, BalanceUnit.Mm, 300, 0, 1_000_000, 250, 500),
            new BalanceKnob(RelayWindowTicksKey, BalanceUnit.Ticks, 150, 0, 100_000, 60, 300),
            new BalanceKnob(RankArmedCatcherKey, BalanceUnit.Count, 0, 0, 7),
            new BalanceKnob(RankMailboxKey, BalanceUnit.Count, 1, 0, 7),
            new BalanceKnob(RankGoonKey, BalanceUnit.Count, 2, 0, 7),
            new BalanceKnob(RankTeammateKey, BalanceUnit.Count, 3, 0, 7),
            new BalanceKnob(RankBeltKey, BalanceUnit.Count, 4, 0, 7),
            new BalanceKnob(RankSolidKey, BalanceUnit.Count, 5, 0, 7),
            new BalanceKnob(RankGroundKey, BalanceUnit.Count, 6, 0, 7),
            new BalanceKnob(RankWaterKey, BalanceUnit.Count, 7, 0, 7));

        /// <summary>The registry rows (a new array each call; the class holds no static state).</summary>
        public static ImmutableArray<BalanceRow> Rows => BalanceKnob.ToRows(Owner, Knobs);

        /// <summary>The M-1a default entries with their safe-range annotations (a new array each call).</summary>
        /// <example><code>BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());</code></example>
        public static ImmutableArray<BalanceEntry> DefaultEntries() => BalanceKnob.ToEntries(Owner, Knobs);

        /// <summary>Resolves every <c>throw.*</c> handle against <paramref name="registry"/> once, at composition.</summary>
        /// <param name="registry">A registry that holds every throw row (for example <see cref="CreateRegistry"/>).</param>
        /// <exception cref="ArgumentException">A row is missing.</exception>
        /// <example><code>ThrowKeys keys = BalanceKeys.Throw.Resolve(table.Registry);</code></example>
        public static ThrowKeys Resolve(BalanceRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            return new ThrowKeys(
                BalanceKeys.Handle(registry, Owner, ThrowSpeedLightKey),
                BalanceKeys.Handle(registry, Owner, ThrowSpeedMediumKey),
                BalanceKeys.Handle(registry, Owner, ThrowSpeedHeavyKey),
                BalanceKeys.Handle(registry, Owner, GKey),
                BalanceKeys.Handle(registry, Owner, LoftDegKey),
                BalanceKeys.Handle(registry, Owner, MaxFlightTicksKey),
                BalanceKeys.Handle(registry, Owner, ThrowCooldownTicksKey),
                BalanceKeys.Handle(registry, Owner, ThrowBurstTicksKey),
                BalanceKeys.Handle(registry, Owner, HandHeightMmKey),
                BalanceKeys.Handle(registry, Owner, FwdOffMmKey),
                BalanceKeys.Handle(registry, Owner, SideOffMmKey),
                BalanceKeys.Handle(registry, Owner, DamagePerPointKey),
                BalanceKeys.Handle(registry, Owner, DropPushbackMmKey),
                BalanceKeys.Handle(registry, Owner, EnemyHitSlackMmKey),
                BalanceKeys.Handle(registry, Owner, PreviewSettleMsKey),
                BalanceKeys.Handle(registry, Owner, RangeMinPermilleKey),
                BalanceKeys.Handle(registry, Owner, RangeMaxPermilleKey),
                BalanceKeys.Handle(registry, Owner, CatchRadiusMmKey),
                BalanceKeys.Handle(registry, Owner, CatchPredictMsKey),
                BalanceKeys.Handle(registry, Owner, CatchTapMsKey),
                BalanceKeys.Handle(registry, Owner, CatchHistoryMaxOffsetMmKey),
                BalanceKeys.Handle(registry, Owner, BonkRadiusMmKey),
                BalanceKeys.Handle(registry, Owner, RelayWindowTicksKey),
                BalanceKeys.Handle(registry, Owner, RankArmedCatcherKey),
                BalanceKeys.Handle(registry, Owner, RankMailboxKey),
                BalanceKeys.Handle(registry, Owner, RankGoonKey),
                BalanceKeys.Handle(registry, Owner, RankTeammateKey),
                BalanceKeys.Handle(registry, Owner, RankBeltKey),
                BalanceKeys.Handle(registry, Owner, RankSolidKey),
                BalanceKeys.Handle(registry, Owner, RankGroundKey),
                BalanceKeys.Handle(registry, Owner, RankWaterKey));
        }
    }
}
