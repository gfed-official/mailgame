namespace PerformativeMail.Sim.Content.Balance;

/// <summary>The resolved handles of every <c>throw.*</c> balance key (see <see cref="BalanceKeys.Throw.Resolve"/>).</summary>
public readonly record struct ThrowKeys(
    BalanceKey ThrowSpeedLight,
    BalanceKey ThrowSpeedMedium,
    BalanceKey ThrowSpeedHeavy,
    BalanceKey G,
    BalanceKey LoftDeg,
    BalanceKey MaxFlightTicks,
    BalanceKey ThrowCooldownTicks,
    BalanceKey ThrowBurstTicks,
    BalanceKey HandHeightMm,
    BalanceKey FwdOffMm,
    BalanceKey SideOffMm,
    BalanceKey DamagePerPoint,
    BalanceKey DropPushbackMm,
    BalanceKey EnemyHitSlackMm,
    BalanceKey PreviewSettleMs,
    BalanceKey RangeMinPermille,
    BalanceKey RangeMaxPermille,
    BalanceKey CatchRadiusMm,
    BalanceKey CatchPredictMs,
    BalanceKey CatchTapMs,
    BalanceKey CatchHistoryMaxOffsetMm,
    BalanceKey BonkRadiusMm,
    BalanceKey RelayWindowTicks,
    BalanceKey RankArmedCatcher,
    BalanceKey RankMailbox,
    BalanceKey RankGoon,
    BalanceKey RankTeammate,
    BalanceKey RankBelt,
    BalanceKey RankSolid,
    BalanceKey RankGround,
    BalanceKey RankWater);

/// <summary>
/// The typed <c>throw.*</c> values, built once from the <see cref="BalanceTable"/> when the <see cref="ContentSet"/> is
/// built. A system receives only its own record (ADR-0003 section 4). Every value is an already-scaled integer (CD4);
/// <c>...Seconds</c> keys hold milliseconds. Immutable; safe from any thread.
/// </summary>
/// <example><code>ThrowBalance p = contentSet.Throw; int walk = p.ThrowSpeedLight;</code></example>
public sealed record class ThrowBalance
{
    /// <summary>Light throw speed, mm/s. Key <c>throw.throwSpeed.light</c>, default 14_000.</summary>
    public int ThrowSpeedLight { get; init; }

    /// <summary>Medium throw speed, mm/s. Key <c>throw.throwSpeed.medium</c>, default 10_000.</summary>
    public int ThrowSpeedMedium { get; init; }

    /// <summary>Heavy throw speed, mm/s. Key <c>throw.throwSpeed.heavy</c>, default 6000.</summary>
    public int ThrowSpeedHeavy { get; init; }

    /// <summary>Thrown-item gravity, mm/s^2 (distinct from player.g). Key <c>throw.g</c>, default 9800.</summary>
    public int G { get; init; }

    /// <summary>Aim loft, milli-degrees (the key name keeps the GDD spelling; the value is scaled). Key <c>throw.loftDeg</c>, default 6000.</summary>
    public int LoftDeg { get; init; }

    /// <summary>Flight time cap, ticks. Key <c>throw.maxFlightTicks</c>, default 90.</summary>
    public int MaxFlightTicks { get; init; }

    /// <summary>Throw cooldown, ticks. Key <c>throw.throwCooldownTicks</c>, default 5.</summary>
    public int ThrowCooldownTicks { get; init; }

    /// <summary>Throw burst tolerance, ticks. Key <c>throw.throwBurstTicks</c>, default 5.</summary>
    public int ThrowBurstTicks { get; init; }

    /// <summary>Launch height, mm. Key <c>throw.handHeightMm</c>, default 1200.</summary>
    public int HandHeightMm { get; init; }

    /// <summary>Launch forward offset, mm. Key <c>throw.fwdOffMm</c>, default 300.</summary>
    public int FwdOffMm { get; init; }

    /// <summary>Launch side offset, mm. Key <c>throw.sideOffMm</c>, default 250.</summary>
    public int SideOffMm { get; init; }

    /// <summary>Thrown damage per weight point. Key <c>throw.damagePerPoint</c>, default 5.</summary>
    public int DamagePerPoint { get; init; }

    /// <summary>Bounce pushback, mm. Key <c>throw.dropPushbackMm</c>, default 500.</summary>
    public int DropPushbackMm { get; init; }

    /// <summary>Enemy hit slack, mm. Key <c>throw.enemyHitSlackMm</c>, default 300.</summary>
    public int EnemyHitSlackMm { get; init; }

    /// <summary>Preview settle time, ms. Key <c>throw.previewSettleMs</c>, default 0.</summary>
    public int PreviewSettleMs { get; init; }

    /// <summary>Lower clamp of ThrowRangePermille (CD12 clamp). Key <c>throw.rangeMinPermille</c>, default 1000.</summary>
    public int RangeMinPermille { get; init; }

    /// <summary>Upper clamp of ThrowRangePermille (CD12 clamp). Key <c>throw.rangeMaxPermille</c>, default 1500.</summary>
    public int RangeMaxPermille { get; init; }

    /// <summary>Catch radius, mm. Key <c>throw.catchRadiusMm</c>, default 1500.</summary>
    public int CatchRadiusMm { get; init; }

    /// <summary>Catch prompt lead, ms. Key <c>throw.catchPredictMs</c>, default 500.</summary>
    public int CatchPredictMs { get; init; }

    /// <summary>Catch press tolerance, ms. Key <c>throw.catchTapMs</c>, default 500.</summary>
    public int CatchTapMs { get; init; }

    /// <summary>Catch latency-tolerance reach, mm. Key <c>throw.catchHistoryMaxOffsetMm</c>, default 1500.</summary>
    public int CatchHistoryMaxOffsetMm { get; init; }

    /// <summary>Teammate bonk radius, mm. Key <c>throw.bonkRadiusMm</c>, default 300.</summary>
    public int BonkRadiusMm { get; init; }

    /// <summary>What counts as a relay hop, ticks. Key <c>throw.relayWindowTicks</c>, default 150.</summary>
    public int RelayWindowTicks { get; init; }

    /// <summary>Receiver tie-break rank of armedCatcher (a permutation of 0..7). Key <c>throw.rank.armedCatcher</c>, default 0.</summary>
    public int RankArmedCatcher { get; init; }

    /// <summary>Receiver tie-break rank of mailbox (a permutation of 0..7). Key <c>throw.rank.mailbox</c>, default 1.</summary>
    public int RankMailbox { get; init; }

    /// <summary>Receiver tie-break rank of goon (a permutation of 0..7). Key <c>throw.rank.goon</c>, default 2.</summary>
    public int RankGoon { get; init; }

    /// <summary>Receiver tie-break rank of teammate (a permutation of 0..7). Key <c>throw.rank.teammate</c>, default 3.</summary>
    public int RankTeammate { get; init; }

    /// <summary>Receiver tie-break rank of belt (a permutation of 0..7). Key <c>throw.rank.belt</c>, default 4.</summary>
    public int RankBelt { get; init; }

    /// <summary>Receiver tie-break rank of solid (a permutation of 0..7). Key <c>throw.rank.solid</c>, default 5.</summary>
    public int RankSolid { get; init; }

    /// <summary>Receiver tie-break rank of ground (a permutation of 0..7). Key <c>throw.rank.ground</c>, default 6.</summary>
    public int RankGround { get; init; }

    /// <summary>Receiver tie-break rank of water (a permutation of 0..7). Key <c>throw.rank.water</c>, default 7.</summary>
    public int RankWater { get; init; }

    /// <summary>Reads every throw value out of <paramref name="table"/>.</summary>
    /// <param name="table">A table built from a registry that holds the throw rows.</param>
    /// <exception cref="ArgumentException">The table's registry lacks a throw row.</exception>
    public static ThrowBalance FromTable(BalanceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        ThrowKeys k = BalanceKeys.Throw.Resolve(table.Registry);
        return new ThrowBalance
        {
            ThrowSpeedLight = table.GetInt(k.ThrowSpeedLight),
            ThrowSpeedMedium = table.GetInt(k.ThrowSpeedMedium),
            ThrowSpeedHeavy = table.GetInt(k.ThrowSpeedHeavy),
            G = table.GetInt(k.G),
            LoftDeg = table.GetInt(k.LoftDeg),
            MaxFlightTicks = table.GetInt(k.MaxFlightTicks),
            ThrowCooldownTicks = table.GetInt(k.ThrowCooldownTicks),
            ThrowBurstTicks = table.GetInt(k.ThrowBurstTicks),
            HandHeightMm = table.GetInt(k.HandHeightMm),
            FwdOffMm = table.GetInt(k.FwdOffMm),
            SideOffMm = table.GetInt(k.SideOffMm),
            DamagePerPoint = table.GetInt(k.DamagePerPoint),
            DropPushbackMm = table.GetInt(k.DropPushbackMm),
            EnemyHitSlackMm = table.GetInt(k.EnemyHitSlackMm),
            PreviewSettleMs = table.GetInt(k.PreviewSettleMs),
            RangeMinPermille = table.GetInt(k.RangeMinPermille),
            RangeMaxPermille = table.GetInt(k.RangeMaxPermille),
            CatchRadiusMm = table.GetInt(k.CatchRadiusMm),
            CatchPredictMs = table.GetInt(k.CatchPredictMs),
            CatchTapMs = table.GetInt(k.CatchTapMs),
            CatchHistoryMaxOffsetMm = table.GetInt(k.CatchHistoryMaxOffsetMm),
            BonkRadiusMm = table.GetInt(k.BonkRadiusMm),
            RelayWindowTicks = table.GetInt(k.RelayWindowTicks),
            RankArmedCatcher = table.GetInt(k.RankArmedCatcher),
            RankMailbox = table.GetInt(k.RankMailbox),
            RankGoon = table.GetInt(k.RankGoon),
            RankTeammate = table.GetInt(k.RankTeammate),
            RankBelt = table.GetInt(k.RankBelt),
            RankSolid = table.GetInt(k.RankSolid),
            RankGround = table.GetInt(k.RankGround),
            RankWater = table.GetInt(k.RankWater),
        };
    }
}
