using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Balance;

public static partial class BalanceKeys
{
    /// <summary>The <c>player.*</c> balance rows: key names, units, hard ranges, M-1a default values and safe ranges (CD9).</summary>
    /// <example><code>ImmutableArray&lt;BalanceRow&gt; rows = BalanceKeys.Player.Rows;</code></example>
    public static partial class Player
    {
        /// <summary>The owner file name and key prefix.</summary>
        public const string Owner = "player";

        /// <summary>The key <c>player.walkMmPerSec</c>.</summary>
        public const string WalkMmPerSecKey = "walkMmPerSec";

        /// <summary>The key <c>player.sprintMmPerSec</c>.</summary>
        public const string SprintMmPerSecKey = "sprintMmPerSec";

        /// <summary>The key <c>player.swimMmPerSec</c>.</summary>
        public const string SwimMmPerSecKey = "swimMmPerSec";

        /// <summary>The key <c>player.ticksAccel</c>.</summary>
        public const string TicksAccelKey = "ticksAccel";

        /// <summary>The key <c>player.ticksDecel</c>.</summary>
        public const string TicksDecelKey = "ticksDecel";

        /// <summary>The key <c>player.airControlPm</c>.</summary>
        public const string AirControlPmKey = "airControlPm";

        /// <summary>The key <c>player.turnDegPerSec</c>.</summary>
        public const string TurnDegPerSecKey = "turnDegPerSec";

        /// <summary>The key <c>player.g</c>.</summary>
        public const string GKey = "g";

        /// <summary>The key <c>player.jumpHeightMm</c>.</summary>
        public const string JumpHeightMmKey = "jumpHeightMm";

        /// <summary>The key <c>player.extraJumpPm</c>.</summary>
        public const string ExtraJumpPmKey = "extraJumpPm";

        /// <summary>The key <c>player.coyoteTicks</c>.</summary>
        public const string CoyoteTicksKey = "coyoteTicks";

        /// <summary>The key <c>player.jumpBufferTicks</c>.</summary>
        public const string JumpBufferTicksKey = "jumpBufferTicks";

        /// <summary>The key <c>player.stepUpMm</c>.</summary>
        public const string StepUpMmKey = "stepUpMm";

        /// <summary>The key <c>player.capsuleRadiusMm</c>.</summary>
        public const string CapsuleRadiusMmKey = "capsuleRadiusMm";

        /// <summary>The key <c>player.capsuleHeightMm</c>.</summary>
        public const string CapsuleHeightMmKey = "capsuleHeightMm";

        /// <summary>The key <c>player.swimEnterMm</c>.</summary>
        public const string SwimEnterMmKey = "swimEnterMm";

        /// <summary>The key <c>player.swimExitMm</c>.</summary>
        public const string SwimExitMmKey = "swimExitMm";

        /// <summary>The key <c>player.maxWadeSlowPm</c>.</summary>
        public const string MaxWadeSlowPmKey = "maxWadeSlowPm";

        /// <summary>The key <c>player.haulOutMm</c>.</summary>
        public const string HaulOutMmKey = "haulOutMm";

        /// <summary>The key <c>player.haulOutTicks</c>.</summary>
        public const string HaulOutTicksKey = "haulOutTicks";

        /// <summary>The key <c>player.rangeMm</c>.</summary>
        public const string RangeMmKey = "rangeMm";

        /// <summary>The key <c>player.incumbentSlackMm</c>.</summary>
        public const string IncumbentSlackMmKey = "incumbentSlackMm";

        /// <summary>The key <c>player.focusConePm</c>.</summary>
        public const string FocusConePmKey = "focusConePm";

        /// <summary>The key <c>player.nearMm</c>.</summary>
        public const string NearMmKey = "nearMm";

        /// <summary>The key <c>player.nearAxisMm</c>.</summary>
        public const string NearAxisMmKey = "nearAxisMm";

        /// <summary>The key <c>player.wDist</c>.</summary>
        public const string WDistKey = "wDist";

        /// <summary>The key <c>player.wAng</c>.</summary>
        public const string WAngKey = "wAng";

        /// <summary>The key <c>player.stickyPm</c>.</summary>
        public const string StickyPmKey = "stickyPm";

        /// <summary>The key <c>player.focusDwellTicks</c>.</summary>
        public const string FocusDwellTicksKey = "focusDwellTicks";

        /// <summary>The key <c>player.minHoldTicks</c>.</summary>
        public const string MinHoldTicksKey = "minHoldTicks";

        /// <summary>The key <c>player.rangeSlackMm</c>.</summary>
        public const string RangeSlackMmKey = "rangeSlackMm";

        /// <summary>The key <c>player.rangeHistoryTicks</c>.</summary>
        public const string RangeHistoryTicksKey = "rangeHistoryTicks";

        /// <summary>The key <c>player.positionStaleTicks</c>.</summary>
        public const string PositionStaleTicksKey = "positionStaleTicks";

        /// <summary>The key <c>player.claimToleranceMm</c>.</summary>
        public const string ClaimToleranceMmKey = "claimToleranceMm";

        /// <summary>The key <c>player.supportSpeedCapMm</c>.</summary>
        public const string SupportSpeedCapMmKey = "supportSpeedCapMm";

        /// <summary>The key <c>player.pushCapMm</c>.</summary>
        public const string PushCapMmKey = "pushCapMm";

        /// <summary>The key <c>player.pushSharePm</c>.</summary>
        public const string PushSharePmKey = "pushSharePm";

        /// <summary>The key <c>player.pushDeadMm</c>.</summary>
        public const string PushDeadMmKey = "pushDeadMm";

        /// <summary>The key <c>player.ghostAfterTicks</c>.</summary>
        public const string GhostAfterTicksKey = "ghostAfterTicks";

        /// <summary>The key <c>player.buffCapPm</c>.</summary>
        public const string BuffCapPmKey = "buffCapPm";

        /// <summary>The key <c>player.maxExtraJumps</c>.</summary>
        public const string MaxExtraJumpsKey = "maxExtraJumps";

        /// <summary>The key <c>player.respawnMinMs</c>.</summary>
        public const string RespawnMinMsKey = "respawnMinMs";

        /// <summary>The key <c>player.respawnMs</c>.</summary>
        public const string RespawnMsKey = "respawnMs";

        /// <summary>The key <c>player.terminalMmS</c>.</summary>
        public const string TerminalMmSKey = "terminalMmS";

        /// <summary>The key <c>player.slopeMaxPm</c>.</summary>
        public const string SlopeMaxPmKey = "slopeMaxPm";

        /// <summary>The key <c>player.subStepMm</c>.</summary>
        public const string SubStepMmKey = "subStepMm";

        /// <summary>The key <c>player.maxSubStepsPerTick</c>.</summary>
        public const string MaxSubStepsPerTickKey = "maxSubStepsPerTick";

        /// <summary>The key <c>player.slideIterations</c>.</summary>
        public const string SlideIterationsKey = "slideIterations";

        /// <summary>The key <c>player.chestMm</c>.</summary>
        public const string ChestMmKey = "chestMm";

        /// <summary>The key <c>player.maxAnchors</c>.</summary>
        public const string MaxAnchorsKey = "maxAnchors";

        /// <summary>The key <c>player.maxCarrierCandidates</c>.</summary>
        public const string MaxCarrierCandidatesKey = "maxCarrierCandidates";

        // One table drives the rows, the default entries and (with the generated handle record) the owner record, so a key's range and value cannot drift apart.
        private static ImmutableArray<BalanceKnob> Knobs => ImmutableArray.Create(
            new BalanceKnob(WalkMmPerSecKey, BalanceUnit.MmPerSec, 5000, 0, 100_000, 4000, 6000),
            new BalanceKnob(SprintMmPerSecKey, BalanceUnit.MmPerSec, 7500, 0, 100_000, 6000, 9000),
            new BalanceKnob(SwimMmPerSecKey, BalanceUnit.MmPerSec, 2000, 0, 100_000, 1600, 2400),
            new BalanceKnob(TicksAccelKey, BalanceUnit.Ticks, 4, 0, 100_000, 3, 8),
            new BalanceKnob(TicksDecelKey, BalanceUnit.Ticks, 3, 0, 100_000, 2, 6),
            new BalanceKnob(AirControlPmKey, BalanceUnit.Permille, 500, 0, 100_000, 300, 700),
            new BalanceKnob(TurnDegPerSecKey, BalanceUnit.DegPerSec, 720, 0, 100_000, 360, 1440),
            new BalanceKnob(GKey, BalanceUnit.MmPerSecSq, 24_000, 1, 1_000_000, 18_000, 30_000),
            new BalanceKnob(JumpHeightMmKey, BalanceUnit.Mm, 1200, 0, 1_000_000),
            new BalanceKnob(ExtraJumpPmKey, BalanceUnit.Permille, 900, 0, 100_000, 800, 1000),
            new BalanceKnob(CoyoteTicksKey, BalanceUnit.Ticks, 4, 0, 100_000, 3, 6),
            new BalanceKnob(JumpBufferTicksKey, BalanceUnit.Ticks, 4, 0, 100_000, 3, 6),
            new BalanceKnob(StepUpMmKey, BalanceUnit.Mm, 400, 0, 1_000_000, 250, 500),
            new BalanceKnob(CapsuleRadiusMmKey, BalanceUnit.Mm, 300, 0, 1_000_000, 250, 400),
            new BalanceKnob(CapsuleHeightMmKey, BalanceUnit.Mm, 1400, 0, 1_000_000, 1300, 1600),
            new BalanceKnob(SwimEnterMmKey, BalanceUnit.Mm, 900, 0, 1_000_000, 700, 1100),
            new BalanceKnob(SwimExitMmKey, BalanceUnit.Mm, 700, 0, 1_000_000, 500, 800),
            new BalanceKnob(MaxWadeSlowPmKey, BalanceUnit.Permille, 200, 0, 100_000, 0, 400),
            new BalanceKnob(HaulOutMmKey, BalanceUnit.Mm, 1200, 0, 1_000_000, 800, 1500),
            new BalanceKnob(HaulOutTicksKey, BalanceUnit.Ticks, 15, 0, 100_000, 10, 30),
            new BalanceKnob(RangeMmKey, BalanceUnit.Mm, 2500, 0, 1_000_000),
            new BalanceKnob(IncumbentSlackMmKey, BalanceUnit.Mm, 400, 0, 1_000_000, 200, 400),
            new BalanceKnob(FocusConePmKey, BalanceUnit.Permille, 900, 0, 100_000, 700, 1000),
            new BalanceKnob(NearMmKey, BalanceUnit.Mm, 800, 0, 1_000_000, 0, 1500),
            new BalanceKnob(NearAxisMmKey, BalanceUnit.Mm, 150, 0, 1_000_000, 100, 300),
            new BalanceKnob(WDistKey, BalanceUnit.Count, 400, 0, 1_048_576, 200, 600),
            new BalanceKnob(WAngKey, BalanceUnit.Count, 600, 0, 1_048_576, 400, 800),
            new BalanceKnob(StickyPmKey, BalanceUnit.Permille, 60, 0, 100_000, 30, 100),
            new BalanceKnob(FocusDwellTicksKey, BalanceUnit.Ticks, 4, 0, 100_000, 2, 8),
            new BalanceKnob(MinHoldTicksKey, BalanceUnit.Ticks, 6, 0, 100_000, 6, 9),
            new BalanceKnob(RangeSlackMmKey, BalanceUnit.Mm, 500, 0, 1_000_000, 500, 500),
            new BalanceKnob(RangeHistoryTicksKey, BalanceUnit.Ticks, 9, 0, 100_000, 6, 12),
            new BalanceKnob(PositionStaleTicksKey, BalanceUnit.Ticks, 30, 0, 100_000, 19, 60),
            new BalanceKnob(ClaimToleranceMmKey, BalanceUnit.Mm, 1500, 0, 1_000_000, 1000, 2500),
            new BalanceKnob(SupportSpeedCapMmKey, BalanceUnit.MmPerSec, 12_000, 0, 100_000),
            new BalanceKnob(PushCapMmKey, BalanceUnit.Mm, 60, 0, 1_000_000, 30, 90),
            new BalanceKnob(PushSharePmKey, BalanceUnit.Permille, 500, 0, 100_000, 400, 500),
            new BalanceKnob(PushDeadMmKey, BalanceUnit.Mm, 10, 0, 1_000_000, 5, 30),
            new BalanceKnob(GhostAfterTicksKey, BalanceUnit.Ticks, 45, 0, 100_000, 30, 90),
            new BalanceKnob(BuffCapPmKey, BalanceUnit.Permille, 400, 0, 100_000, 300, 600),
            new BalanceKnob(MaxExtraJumpsKey, BalanceUnit.Count, 1, 0, 16),
            new BalanceKnob(RespawnMinMsKey, BalanceUnit.Ms, 1000, 0, 3_600_000),
            new BalanceKnob(RespawnMsKey, BalanceUnit.Ms, 10_000, 0, 3_600_000, 6000, 15_000),
            new BalanceKnob(TerminalMmSKey, BalanceUnit.MmPerSec, 20_000, 1, 1_000_000),
            new BalanceKnob(SlopeMaxPmKey, BalanceUnit.Permille, 700, 0, 100_000),
            new BalanceKnob(SubStepMmKey, BalanceUnit.Mm, 300, 1, 1_000_000),
            new BalanceKnob(MaxSubStepsPerTickKey, BalanceUnit.Count, 4, 1, 64),
            new BalanceKnob(SlideIterationsKey, BalanceUnit.Count, 3, 1, 64),
            new BalanceKnob(ChestMmKey, BalanceUnit.Mm, 1000, 0, 1_000_000),
            new BalanceKnob(MaxAnchorsKey, BalanceUnit.Count, 4096, 1, 1_048_576),
            // Hard max 64 = CarrierScratch.SolidCapacity (a layout constant in Players; a test pins the two together). The same limit
            // also bounds the 32 construct-box and 8 vehicle-ref buffers as min(limit, capacity).
            new BalanceKnob(MaxCarrierCandidatesKey, BalanceUnit.Count, 64, 1, 64));

        /// <summary>The registry rows (a new array each call; the class holds no static state).</summary>
        public static ImmutableArray<BalanceRow> Rows => BalanceKnob.ToRows(Owner, Knobs);

        /// <summary>The M-1a default entries with their safe-range annotations (a new array each call).</summary>
        /// <example><code>BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());</code></example>
        public static ImmutableArray<BalanceEntry> DefaultEntries() => BalanceKnob.ToEntries(Owner, Knobs);

        /// <summary>Resolves every <c>player.*</c> handle against <paramref name="registry"/> once, at composition.</summary>
        /// <param name="registry">A registry that holds every player row (for example <see cref="CreateRegistry"/>).</param>
        /// <exception cref="ArgumentException">A row is missing.</exception>
        /// <example><code>PlayerKeys keys = BalanceKeys.Player.Resolve(table.Registry);</code></example>
        public static PlayerKeys Resolve(BalanceRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            return new PlayerKeys(
                BalanceKeys.Handle(registry, Owner, WalkMmPerSecKey),
                BalanceKeys.Handle(registry, Owner, SprintMmPerSecKey),
                BalanceKeys.Handle(registry, Owner, SwimMmPerSecKey),
                BalanceKeys.Handle(registry, Owner, TicksAccelKey),
                BalanceKeys.Handle(registry, Owner, TicksDecelKey),
                BalanceKeys.Handle(registry, Owner, AirControlPmKey),
                BalanceKeys.Handle(registry, Owner, TurnDegPerSecKey),
                BalanceKeys.Handle(registry, Owner, GKey),
                BalanceKeys.Handle(registry, Owner, JumpHeightMmKey),
                BalanceKeys.Handle(registry, Owner, ExtraJumpPmKey),
                BalanceKeys.Handle(registry, Owner, CoyoteTicksKey),
                BalanceKeys.Handle(registry, Owner, JumpBufferTicksKey),
                BalanceKeys.Handle(registry, Owner, StepUpMmKey),
                BalanceKeys.Handle(registry, Owner, CapsuleRadiusMmKey),
                BalanceKeys.Handle(registry, Owner, CapsuleHeightMmKey),
                BalanceKeys.Handle(registry, Owner, SwimEnterMmKey),
                BalanceKeys.Handle(registry, Owner, SwimExitMmKey),
                BalanceKeys.Handle(registry, Owner, MaxWadeSlowPmKey),
                BalanceKeys.Handle(registry, Owner, HaulOutMmKey),
                BalanceKeys.Handle(registry, Owner, HaulOutTicksKey),
                BalanceKeys.Handle(registry, Owner, RangeMmKey),
                BalanceKeys.Handle(registry, Owner, IncumbentSlackMmKey),
                BalanceKeys.Handle(registry, Owner, FocusConePmKey),
                BalanceKeys.Handle(registry, Owner, NearMmKey),
                BalanceKeys.Handle(registry, Owner, NearAxisMmKey),
                BalanceKeys.Handle(registry, Owner, WDistKey),
                BalanceKeys.Handle(registry, Owner, WAngKey),
                BalanceKeys.Handle(registry, Owner, StickyPmKey),
                BalanceKeys.Handle(registry, Owner, FocusDwellTicksKey),
                BalanceKeys.Handle(registry, Owner, MinHoldTicksKey),
                BalanceKeys.Handle(registry, Owner, RangeSlackMmKey),
                BalanceKeys.Handle(registry, Owner, RangeHistoryTicksKey),
                BalanceKeys.Handle(registry, Owner, PositionStaleTicksKey),
                BalanceKeys.Handle(registry, Owner, ClaimToleranceMmKey),
                BalanceKeys.Handle(registry, Owner, SupportSpeedCapMmKey),
                BalanceKeys.Handle(registry, Owner, PushCapMmKey),
                BalanceKeys.Handle(registry, Owner, PushSharePmKey),
                BalanceKeys.Handle(registry, Owner, PushDeadMmKey),
                BalanceKeys.Handle(registry, Owner, GhostAfterTicksKey),
                BalanceKeys.Handle(registry, Owner, BuffCapPmKey),
                BalanceKeys.Handle(registry, Owner, MaxExtraJumpsKey),
                BalanceKeys.Handle(registry, Owner, RespawnMinMsKey),
                BalanceKeys.Handle(registry, Owner, RespawnMsKey),
                BalanceKeys.Handle(registry, Owner, TerminalMmSKey),
                BalanceKeys.Handle(registry, Owner, SlopeMaxPmKey),
                BalanceKeys.Handle(registry, Owner, SubStepMmKey),
                BalanceKeys.Handle(registry, Owner, MaxSubStepsPerTickKey),
                BalanceKeys.Handle(registry, Owner, SlideIterationsKey),
                BalanceKeys.Handle(registry, Owner, ChestMmKey),
                BalanceKeys.Handle(registry, Owner, MaxAnchorsKey),
                BalanceKeys.Handle(registry, Owner, MaxCarrierCandidatesKey));
        }
    }
}
