using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The immutable <c>players</c> settings record (ADR-0010 section 4, ADR-0013 and ADR-0014: every constant comes from here or the
/// StatSheet, never a code literal; TR-movement-020). Built once from content-data's <see cref="PlayerBalance"/> when Players is
/// constructed, then read by the Sim without touching the <see cref="BalanceTable"/> (nothing is looked up in the tick path).
/// It holds only <see cref="int"/> values, one per <c>player.*</c> key, with the same names and units as <see cref="PlayerBalance"/>.
/// A key missing from the balance table is a build error (<see cref="PlayerBalance.FromTable"/> throws before a content set
/// exists); a value that cannot work is a <see cref="SimBuildException"/> here. Thread-safe (immutable).
/// </summary>
/// <example>
/// <code>
/// PlayerSettings s = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
/// int radius = s.CapsuleRadiusMm;   // 300
/// </code>
/// </example>
public sealed record class PlayerSettings
{
    /// <summary>Base walk speed, mm/s. Key <c>player.walkMmPerSec</c>, default 5000.</summary>
    public required int WalkMmPerSec { get; init; }

    /// <summary>Base sprint speed, mm/s. Key <c>player.sprintMmPerSec</c>, default 7500.</summary>
    public required int SprintMmPerSec { get; init; }

    /// <summary>Base swim speed, mm/s. Key <c>player.swimMmPerSec</c>, default 2000.</summary>
    public required int SwimMmPerSec { get; init; }

    /// <summary>Ticks to reach full speed. Key <c>player.ticksAccel</c>, default 4.</summary>
    public required int TicksAccel { get; init; }

    /// <summary>Ticks to stop. Key <c>player.ticksDecel</c>, default 3.</summary>
    public required int TicksDecel { get; init; }

    /// <summary>Air steering share. Key <c>player.airControlPm</c>, default 500.</summary>
    public required int AirControlPm { get; init; }

    /// <summary>Body turn rate, degrees per second. Key <c>player.turnDegPerSec</c>, default 720.</summary>
    public required int TurnDegPerSec { get; init; }

    /// <summary>Player gravity, mm/s^2 (distinct from throw.g). Key <c>player.g</c>, default 24_000.</summary>
    public required int G { get; init; }

    /// <summary>Jump apex height, mm (locked). Key <c>player.jumpHeightMm</c>, default 1200.</summary>
    public required int JumpHeightMm { get; init; }

    /// <summary>Mid-air jump strength share. Key <c>player.extraJumpPm</c>, default 900.</summary>
    public required int ExtraJumpPm { get; init; }

    /// <summary>Coyote-time ticks. Key <c>player.coyoteTicks</c>, default 4.</summary>
    public required int CoyoteTicks { get; init; }

    /// <summary>Jump-buffer ticks. Key <c>player.jumpBufferTicks</c>, default 4.</summary>
    public required int JumpBufferTicks { get; init; }

    /// <summary>Auto step-up height, mm. Key <c>player.stepUpMm</c>, default 400.</summary>
    public required int StepUpMm { get; init; }

    /// <summary>Body capsule radius, mm. Key <c>player.capsuleRadiusMm</c>, default 300.</summary>
    public required int CapsuleRadiusMm { get; init; }

    /// <summary>Body capsule height, mm. Key <c>player.capsuleHeightMm</c>, default 1400.</summary>
    public required int CapsuleHeightMm { get; init; }

    /// <summary>Water depth that starts swimming, mm. Key <c>player.swimEnterMm</c>, default 900.</summary>
    public required int SwimEnterMm { get; init; }

    /// <summary>Water depth that ends swimming, mm. Key <c>player.swimExitMm</c>, default 700.</summary>
    public required int SwimExitMm { get; init; }

    /// <summary>Shallow-water drag share. Key <c>player.maxWadeSlowPm</c>, default 200.</summary>
    public required int MaxWadeSlowPm { get; init; }

    /// <summary>Haul-out reach, mm. Key <c>player.haulOutMm</c>, default 1200.</summary>
    public required int HaulOutMm { get; init; }

    /// <summary>Haul-out duration, ticks. Key <c>player.haulOutTicks</c>, default 15.</summary>
    public required int HaulOutTicks { get; init; }

    /// <summary>Interaction reach, mm (locked). Key <c>player.rangeMm</c>, default 2500.</summary>
    public required int RangeMm { get; init; }

    /// <summary>Focus keep / hold-cancel slack, mm. Key <c>player.incumbentSlackMm</c>, default 400.</summary>
    public required int IncumbentSlackMm { get; init; }

    /// <summary>Focus cone share of the half-FOV. Key <c>player.focusConePm</c>, default 900.</summary>
    public required int FocusConePm { get; init; }

    /// <summary>Near-focus radius, mm. Key <c>player.nearMm</c>, default 800.</summary>
    public required int NearMm { get; init; }

    /// <summary>Near-focus axis tolerance, mm. Key <c>player.nearAxisMm</c>, default 150.</summary>
    public required int NearAxisMm { get; init; }

    /// <summary>Focus distance weight. Key <c>player.wDist</c>, default 400.</summary>
    public required int WDist { get; init; }

    /// <summary>Focus angle weight. Key <c>player.wAng</c>, default 600.</summary>
    public required int WAng { get; init; }

    /// <summary>Focus stickiness share. Key <c>player.stickyPm</c>, default 60.</summary>
    public required int StickyPm { get; init; }

    /// <summary>Focus dwell ticks. Key <c>player.focusDwellTicks</c>, default 4.</summary>
    public required int FocusDwellTicks { get; init; }

    /// <summary>Floor on any hold, ticks. Key <c>player.minHoldTicks</c>, default 6.</summary>
    public required int MinHoldTicks { get; init; }

    /// <summary>Host-side range slack, mm. Key <c>player.rangeSlackMm</c>, default 500.</summary>
    public required int RangeSlackMm { get; init; }

    /// <summary>Host-side position history, ticks. Key <c>player.rangeHistoryTicks</c>, default 9.</summary>
    public required int RangeHistoryTicks { get; init; }

    /// <summary>Ticks without a transform before a position is stale. Key <c>player.positionStaleTicks</c>, default 30.</summary>
    public required int PositionStaleTicks { get; init; }

    /// <summary>Claimed-origin tolerance, mm. Key <c>player.claimToleranceMm</c>, default 1500.</summary>
    public required int ClaimToleranceMm { get; init; }

    /// <summary>Belt fast-travel speed ceiling, mm/s. Key <c>player.supportSpeedCapMm</c>, default 12_000.</summary>
    public required int SupportSpeedCapMm { get; init; }

    /// <summary>Per-tick push cap, mm. Key <c>player.pushCapMm</c>, default 60.</summary>
    public required int PushCapMm { get; init; }

    /// <summary>Push share between carriers. Key <c>player.pushSharePm</c>, default 500.</summary>
    public required int PushSharePm { get; init; }

    /// <summary>Push dead zone, mm. Key <c>player.pushDeadMm</c>, default 10.</summary>
    public required int PushDeadMm { get; init; }

    /// <summary>Ticks before a stuck pair passes through. Key <c>player.ghostAfterTicks</c>, default 45.</summary>
    public required int GhostAfterTicks { get; init; }

    /// <summary>Cap on the sum of speed perks (CD12 clamp). Key <c>player.buffCapPm</c>, default 400.</summary>
    public required int BuffCapPm { get; init; }

    /// <summary>Cap on extra jumps (CD12 clamp). Key <c>player.maxExtraJumps</c>, default 1.</summary>
    public required int MaxExtraJumps { get; init; }

    /// <summary>Respawn floor, ms (CD12 clamp). Key <c>player.respawnMinMs</c>, default 1000.</summary>
    public required int RespawnMinMs { get; init; }

    /// <summary>Base respawn time, ms. Key <c>player.respawnMs</c>, default 10_000.</summary>
    public required int RespawnMs { get; init; }

    /// <summary>Terminal fall speed, mm/s (PC F3). Key <c>player.terminalMmS</c>, default 20_000.</summary>
    public required int TerminalMmS { get; init; }

    /// <summary>Steepest walkable terrain slope, permille (PC P4). Key <c>player.slopeMaxPm</c>, default 700.</summary>
    public required int SlopeMaxPm { get; init; }

    /// <summary>Longest sub-step of the carrier sweep, mm (ADR-0013 section 4). Key <c>player.subStepMm</c>, default 300.</summary>
    public required int SubStepMm { get; init; }

    /// <summary>Most sub-steps per tick (ADR-0013 section 4). Key <c>player.maxSubStepsPerTick</c>, default 4.</summary>
    public required int MaxSubStepsPerTick { get; init; }

    /// <summary>Most slide iterations per sub-step (ADR-0013 section 4). Key <c>player.slideIterations</c>, default 3.</summary>
    public required int SlideIterations { get; init; }

    /// <summary>Height of the focus and range point above the feet, mm (PC F4). Key <c>player.chestMm</c>, default 1000.</summary>
    public required int ChestMm { get; init; }

    /// <summary>Anchor registry capacity (ADR-0014 section 1). Key <c>player.maxAnchors</c>, default 4096.</summary>
    public required int MaxAnchors { get; init; }

    /// <summary>Static-candidate buffer limit of the carrier scratch (ADR-0013 section 4), 1 to 64 (<see cref="CarrierScratch.SolidCapacity"/>); the 32 construct-box and 8 vehicle-ref buffers use min(limit, capacity). Key <c>player.maxCarrierCandidates</c>, default 64.</summary>
    public required int MaxCarrierCandidates { get; init; }

    /// <summary>Copies every value out of <paramref name="balance"/> and checks the values the Sim divides by or sizes buffers with.</summary>
    /// <param name="balance">The content set's <c>player.*</c> record.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="balance"/> is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayerSettingsInvalid</c>: a capacity, divisor or capsule size is below 1.</exception>
    /// <example><code>PlayerSettings s = PlayerSettings.FromBalance(content.Player);</code></example>
    public static PlayerSettings FromBalance(PlayerBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        var s = new PlayerSettings
        {
            WalkMmPerSec = balance.WalkMmPerSec,
            SprintMmPerSec = balance.SprintMmPerSec,
            SwimMmPerSec = balance.SwimMmPerSec,
            TicksAccel = balance.TicksAccel,
            TicksDecel = balance.TicksDecel,
            AirControlPm = balance.AirControlPm,
            TurnDegPerSec = balance.TurnDegPerSec,
            G = balance.G,
            JumpHeightMm = balance.JumpHeightMm,
            ExtraJumpPm = balance.ExtraJumpPm,
            CoyoteTicks = balance.CoyoteTicks,
            JumpBufferTicks = balance.JumpBufferTicks,
            StepUpMm = balance.StepUpMm,
            CapsuleRadiusMm = balance.CapsuleRadiusMm,
            CapsuleHeightMm = balance.CapsuleHeightMm,
            SwimEnterMm = balance.SwimEnterMm,
            SwimExitMm = balance.SwimExitMm,
            MaxWadeSlowPm = balance.MaxWadeSlowPm,
            HaulOutMm = balance.HaulOutMm,
            HaulOutTicks = balance.HaulOutTicks,
            RangeMm = balance.RangeMm,
            IncumbentSlackMm = balance.IncumbentSlackMm,
            FocusConePm = balance.FocusConePm,
            NearMm = balance.NearMm,
            NearAxisMm = balance.NearAxisMm,
            WDist = balance.WDist,
            WAng = balance.WAng,
            StickyPm = balance.StickyPm,
            FocusDwellTicks = balance.FocusDwellTicks,
            MinHoldTicks = balance.MinHoldTicks,
            RangeSlackMm = balance.RangeSlackMm,
            RangeHistoryTicks = balance.RangeHistoryTicks,
            PositionStaleTicks = balance.PositionStaleTicks,
            ClaimToleranceMm = balance.ClaimToleranceMm,
            SupportSpeedCapMm = balance.SupportSpeedCapMm,
            PushCapMm = balance.PushCapMm,
            PushSharePm = balance.PushSharePm,
            PushDeadMm = balance.PushDeadMm,
            GhostAfterTicks = balance.GhostAfterTicks,
            BuffCapPm = balance.BuffCapPm,
            MaxExtraJumps = balance.MaxExtraJumps,
            RespawnMinMs = balance.RespawnMinMs,
            RespawnMs = balance.RespawnMs,
            TerminalMmS = balance.TerminalMmS,
            SlopeMaxPm = balance.SlopeMaxPm,
            SubStepMm = balance.SubStepMm,
            MaxSubStepsPerTick = balance.MaxSubStepsPerTick,
            SlideIterations = balance.SlideIterations,
            ChestMm = balance.ChestMm,
            MaxAnchors = balance.MaxAnchors,
            MaxCarrierCandidates = balance.MaxCarrierCandidates,
        };

        Require(s.CapsuleRadiusMm, nameof(CapsuleRadiusMm));
        Require(s.TerminalMmS, nameof(TerminalMmS));
        Require(s.SubStepMm, nameof(SubStepMm));
        Require(s.MaxSubStepsPerTick, nameof(MaxSubStepsPerTick));
        Require(s.SlideIterations, nameof(SlideIterations));
        Require(s.MaxAnchors, nameof(MaxAnchors));
        Require(s.MaxCarrierCandidates, nameof(MaxCarrierCandidates));
        Require(s.CapsuleHeightMm, nameof(CapsuleHeightMm));
        Require(s.TicksAccel, nameof(TicksAccel));
        Require(s.TicksDecel, nameof(TicksDecel));
        Require(s.G, nameof(G));
        return s;
    }

    private static void Require(int value, string name)
    {
        if (value < 1)
        {
            throw new SimBuildException(SimBuildErrorCode.PlayerSettingsInvalid, $"player.{char.ToLowerInvariant(name[0])}{name[1..]} must be at least 1 (was {value}).");
        }
    }
}
