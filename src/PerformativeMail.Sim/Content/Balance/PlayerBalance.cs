namespace PerformativeMail.Sim.Content.Balance;

/// <summary>The resolved handles of every <c>player.*</c> balance key (see <see cref="BalanceKeys.Player.Resolve"/>).</summary>
public readonly record struct PlayerKeys(
    BalanceKey WalkMmPerSec,
    BalanceKey SprintMmPerSec,
    BalanceKey SwimMmPerSec,
    BalanceKey TicksAccel,
    BalanceKey TicksDecel,
    BalanceKey AirControlPm,
    BalanceKey TurnDegPerSec,
    BalanceKey G,
    BalanceKey JumpHeightMm,
    BalanceKey ExtraJumpPm,
    BalanceKey CoyoteTicks,
    BalanceKey JumpBufferTicks,
    BalanceKey StepUpMm,
    BalanceKey CapsuleRadiusMm,
    BalanceKey CapsuleHeightMm,
    BalanceKey SwimEnterMm,
    BalanceKey SwimExitMm,
    BalanceKey MaxWadeSlowPm,
    BalanceKey HaulOutMm,
    BalanceKey HaulOutTicks,
    BalanceKey RangeMm,
    BalanceKey IncumbentSlackMm,
    BalanceKey FocusConePm,
    BalanceKey NearMm,
    BalanceKey NearAxisMm,
    BalanceKey WDist,
    BalanceKey WAng,
    BalanceKey StickyPm,
    BalanceKey FocusDwellTicks,
    BalanceKey MinHoldTicks,
    BalanceKey RangeSlackMm,
    BalanceKey RangeHistoryTicks,
    BalanceKey PositionStaleTicks,
    BalanceKey ClaimToleranceMm,
    BalanceKey SupportSpeedCapMm,
    BalanceKey PushCapMm,
    BalanceKey PushSharePm,
    BalanceKey PushDeadMm,
    BalanceKey GhostAfterTicks,
    BalanceKey BuffCapPm,
    BalanceKey MaxExtraJumps,
    BalanceKey RespawnMinMs,
    BalanceKey RespawnMs,
    BalanceKey TerminalMmS,
    BalanceKey SlopeMaxPm,
    BalanceKey SubStepMm,
    BalanceKey MaxSubStepsPerTick,
    BalanceKey SlideIterations,
    BalanceKey ChestMm,
    BalanceKey MaxAnchors,
    BalanceKey MaxCarrierCandidates);

/// <summary>
/// The typed <c>player.*</c> values, built once from the <see cref="BalanceTable"/> when the <see cref="ContentSet"/> is
/// built. A system receives only its own record (ADR-0003 section 4). Every value is an already-scaled integer (CD4);
/// <c>...Seconds</c> keys hold milliseconds. Immutable; safe from any thread.
/// </summary>
/// <example><code>PlayerBalance p = contentSet.Player; int walk = p.WalkMmPerSec;</code></example>
public sealed record class PlayerBalance
{
    /// <summary>Base walk speed, mm/s. Key <c>player.walkMmPerSec</c>, default 5000.</summary>
    public int WalkMmPerSec { get; init; }

    /// <summary>Base sprint speed, mm/s. Key <c>player.sprintMmPerSec</c>, default 7500.</summary>
    public int SprintMmPerSec { get; init; }

    /// <summary>Base swim speed, mm/s. Key <c>player.swimMmPerSec</c>, default 2000.</summary>
    public int SwimMmPerSec { get; init; }

    /// <summary>Ticks to reach full speed. Key <c>player.ticksAccel</c>, default 4.</summary>
    public int TicksAccel { get; init; }

    /// <summary>Ticks to stop. Key <c>player.ticksDecel</c>, default 3.</summary>
    public int TicksDecel { get; init; }

    /// <summary>Air steering share. Key <c>player.airControlPm</c>, default 500.</summary>
    public int AirControlPm { get; init; }

    /// <summary>Body turn rate, degrees per second. Key <c>player.turnDegPerSec</c>, default 720.</summary>
    public int TurnDegPerSec { get; init; }

    /// <summary>Player gravity, mm/s^2 (distinct from throw.g). Key <c>player.g</c>, default 24_000.</summary>
    public int G { get; init; }

    /// <summary>Jump apex height, mm (locked). Key <c>player.jumpHeightMm</c>, default 1200.</summary>
    public int JumpHeightMm { get; init; }

    /// <summary>Mid-air jump strength share. Key <c>player.extraJumpPm</c>, default 900.</summary>
    public int ExtraJumpPm { get; init; }

    /// <summary>Coyote-time ticks. Key <c>player.coyoteTicks</c>, default 4.</summary>
    public int CoyoteTicks { get; init; }

    /// <summary>Jump-buffer ticks. Key <c>player.jumpBufferTicks</c>, default 4.</summary>
    public int JumpBufferTicks { get; init; }

    /// <summary>Auto step-up height, mm. Key <c>player.stepUpMm</c>, default 400.</summary>
    public int StepUpMm { get; init; }

    /// <summary>Body capsule radius, mm. Key <c>player.capsuleRadiusMm</c>, default 300.</summary>
    public int CapsuleRadiusMm { get; init; }

    /// <summary>Body capsule height, mm. Key <c>player.capsuleHeightMm</c>, default 1400.</summary>
    public int CapsuleHeightMm { get; init; }

    /// <summary>Water depth that starts swimming, mm. Key <c>player.swimEnterMm</c>, default 900.</summary>
    public int SwimEnterMm { get; init; }

    /// <summary>Water depth that ends swimming, mm. Key <c>player.swimExitMm</c>, default 700.</summary>
    public int SwimExitMm { get; init; }

    /// <summary>Shallow-water drag share. Key <c>player.maxWadeSlowPm</c>, default 200.</summary>
    public int MaxWadeSlowPm { get; init; }

    /// <summary>Haul-out reach, mm. Key <c>player.haulOutMm</c>, default 1200.</summary>
    public int HaulOutMm { get; init; }

    /// <summary>Haul-out duration, ticks. Key <c>player.haulOutTicks</c>, default 15.</summary>
    public int HaulOutTicks { get; init; }

    /// <summary>Interaction reach, mm (locked). Key <c>player.rangeMm</c>, default 2500.</summary>
    public int RangeMm { get; init; }

    /// <summary>Focus keep / hold-cancel slack, mm. Key <c>player.incumbentSlackMm</c>, default 400.</summary>
    public int IncumbentSlackMm { get; init; }

    /// <summary>Focus cone share of the half-FOV. Key <c>player.focusConePm</c>, default 900.</summary>
    public int FocusConePm { get; init; }

    /// <summary>Near-focus radius, mm. Key <c>player.nearMm</c>, default 800.</summary>
    public int NearMm { get; init; }

    /// <summary>Near-focus axis tolerance, mm. Key <c>player.nearAxisMm</c>, default 150.</summary>
    public int NearAxisMm { get; init; }

    /// <summary>Focus distance weight. Key <c>player.wDist</c>, default 400.</summary>
    public int WDist { get; init; }

    /// <summary>Focus angle weight. Key <c>player.wAng</c>, default 600.</summary>
    public int WAng { get; init; }

    /// <summary>Focus stickiness share. Key <c>player.stickyPm</c>, default 60.</summary>
    public int StickyPm { get; init; }

    /// <summary>Focus dwell ticks. Key <c>player.focusDwellTicks</c>, default 4.</summary>
    public int FocusDwellTicks { get; init; }

    /// <summary>Floor on any hold, ticks. Key <c>player.minHoldTicks</c>, default 6.</summary>
    public int MinHoldTicks { get; init; }

    /// <summary>Host-side range slack, mm. Key <c>player.rangeSlackMm</c>, default 500.</summary>
    public int RangeSlackMm { get; init; }

    /// <summary>Host-side position history, ticks. Key <c>player.rangeHistoryTicks</c>, default 9.</summary>
    public int RangeHistoryTicks { get; init; }

    /// <summary>Ticks without a transform before a position is stale. Key <c>player.positionStaleTicks</c>, default 30.</summary>
    public int PositionStaleTicks { get; init; }

    /// <summary>Claimed-origin tolerance, mm. Key <c>player.claimToleranceMm</c>, default 1500.</summary>
    public int ClaimToleranceMm { get; init; }

    /// <summary>Belt fast-travel speed ceiling, mm/s. Key <c>player.supportSpeedCapMm</c>, default 12_000.</summary>
    public int SupportSpeedCapMm { get; init; }

    /// <summary>Per-tick push cap, mm. Key <c>player.pushCapMm</c>, default 60.</summary>
    public int PushCapMm { get; init; }

    /// <summary>Push share between carriers. Key <c>player.pushSharePm</c>, default 500.</summary>
    public int PushSharePm { get; init; }

    /// <summary>Push dead zone, mm. Key <c>player.pushDeadMm</c>, default 10.</summary>
    public int PushDeadMm { get; init; }

    /// <summary>Ticks before a stuck pair passes through. Key <c>player.ghostAfterTicks</c>, default 45.</summary>
    public int GhostAfterTicks { get; init; }

    /// <summary>Cap on the sum of speed perks (CD12 clamp). Key <c>player.buffCapPm</c>, default 400.</summary>
    public int BuffCapPm { get; init; }

    /// <summary>Cap on extra jumps (CD12 clamp). Key <c>player.maxExtraJumps</c>, default 1.</summary>
    public int MaxExtraJumps { get; init; }

    /// <summary>Respawn floor, ms (CD12 clamp). Key <c>player.respawnMinMs</c>, default 1000.</summary>
    public int RespawnMinMs { get; init; }

    /// <summary>Base respawn time, ms. Key <c>player.respawnMs</c>, default 10_000.</summary>
    public int RespawnMs { get; init; }

    /// <summary>Terminal fall speed, mm/s (PC F3). Key <c>player.terminalMmS</c>, default 20_000.</summary>
    public int TerminalMmS { get; init; }

    /// <summary>Steepest walkable terrain slope, permille (PC P4). Key <c>player.slopeMaxPm</c>, default 700.</summary>
    public int SlopeMaxPm { get; init; }

    /// <summary>Longest sub-step of the carrier sweep, mm (ADR-0013 section 4). Key <c>player.subStepMm</c>, default 300.</summary>
    public int SubStepMm { get; init; }

    /// <summary>Most sub-steps per tick (ADR-0013 section 4). Key <c>player.maxSubStepsPerTick</c>, default 4.</summary>
    public int MaxSubStepsPerTick { get; init; }

    /// <summary>Most slide iterations per sub-step (ADR-0013 section 4). Key <c>player.slideIterations</c>, default 3.</summary>
    public int SlideIterations { get; init; }

    /// <summary>Height of the focus and range point above the feet, mm (PC F4). Key <c>player.chestMm</c>, default 1000.</summary>
    public int ChestMm { get; init; }

    /// <summary>Anchor registry capacity (ADR-0014 section 1). Key <c>player.maxAnchors</c>, default 4096.</summary>
    public int MaxAnchors { get; init; }

    /// <summary>Static-candidate buffer capacity of the carrier scratch (ADR-0013 section 4). Key <c>player.maxCarrierCandidates</c>, default 64.</summary>
    public int MaxCarrierCandidates { get; init; }

    /// <summary>Reads every player value out of <paramref name="table"/>.</summary>
    /// <param name="table">A table built from a registry that holds the player rows.</param>
    /// <exception cref="ArgumentException">The table's registry lacks a player row.</exception>
    public static PlayerBalance FromTable(BalanceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        PlayerKeys k = BalanceKeys.Player.Resolve(table.Registry);
        return new PlayerBalance
        {
            WalkMmPerSec = table.GetInt(k.WalkMmPerSec),
            SprintMmPerSec = table.GetInt(k.SprintMmPerSec),
            SwimMmPerSec = table.GetInt(k.SwimMmPerSec),
            TicksAccel = table.GetInt(k.TicksAccel),
            TicksDecel = table.GetInt(k.TicksDecel),
            AirControlPm = table.GetInt(k.AirControlPm),
            TurnDegPerSec = table.GetInt(k.TurnDegPerSec),
            G = table.GetInt(k.G),
            JumpHeightMm = table.GetInt(k.JumpHeightMm),
            ExtraJumpPm = table.GetInt(k.ExtraJumpPm),
            CoyoteTicks = table.GetInt(k.CoyoteTicks),
            JumpBufferTicks = table.GetInt(k.JumpBufferTicks),
            StepUpMm = table.GetInt(k.StepUpMm),
            CapsuleRadiusMm = table.GetInt(k.CapsuleRadiusMm),
            CapsuleHeightMm = table.GetInt(k.CapsuleHeightMm),
            SwimEnterMm = table.GetInt(k.SwimEnterMm),
            SwimExitMm = table.GetInt(k.SwimExitMm),
            MaxWadeSlowPm = table.GetInt(k.MaxWadeSlowPm),
            HaulOutMm = table.GetInt(k.HaulOutMm),
            HaulOutTicks = table.GetInt(k.HaulOutTicks),
            RangeMm = table.GetInt(k.RangeMm),
            IncumbentSlackMm = table.GetInt(k.IncumbentSlackMm),
            FocusConePm = table.GetInt(k.FocusConePm),
            NearMm = table.GetInt(k.NearMm),
            NearAxisMm = table.GetInt(k.NearAxisMm),
            WDist = table.GetInt(k.WDist),
            WAng = table.GetInt(k.WAng),
            StickyPm = table.GetInt(k.StickyPm),
            FocusDwellTicks = table.GetInt(k.FocusDwellTicks),
            MinHoldTicks = table.GetInt(k.MinHoldTicks),
            RangeSlackMm = table.GetInt(k.RangeSlackMm),
            RangeHistoryTicks = table.GetInt(k.RangeHistoryTicks),
            PositionStaleTicks = table.GetInt(k.PositionStaleTicks),
            ClaimToleranceMm = table.GetInt(k.ClaimToleranceMm),
            SupportSpeedCapMm = table.GetInt(k.SupportSpeedCapMm),
            PushCapMm = table.GetInt(k.PushCapMm),
            PushSharePm = table.GetInt(k.PushSharePm),
            PushDeadMm = table.GetInt(k.PushDeadMm),
            GhostAfterTicks = table.GetInt(k.GhostAfterTicks),
            BuffCapPm = table.GetInt(k.BuffCapPm),
            MaxExtraJumps = table.GetInt(k.MaxExtraJumps),
            RespawnMinMs = table.GetInt(k.RespawnMinMs),
            RespawnMs = table.GetInt(k.RespawnMs),
            TerminalMmS = table.GetInt(k.TerminalMmS),
            SlopeMaxPm = table.GetInt(k.SlopeMaxPm),
            SubStepMm = table.GetInt(k.SubStepMm),
            MaxSubStepsPerTick = table.GetInt(k.MaxSubStepsPerTick),
            SlideIterations = table.GetInt(k.SlideIterations),
            ChestMm = table.GetInt(k.ChestMm),
            MaxAnchors = table.GetInt(k.MaxAnchors),
            MaxCarrierCandidates = table.GetInt(k.MaxCarrierCandidates),
        };
    }
}
