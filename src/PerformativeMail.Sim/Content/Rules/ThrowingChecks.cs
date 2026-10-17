using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>
/// The bodies of the Throwing (throwing-catching) rules CV-THR-01 to 06, 08 to 13, 15, 16 and 18 (content-data story 012; the
/// text is the owner's rule table in <c>throwing-catching.md</c>, TC-CV-1). Rule bodies are stateless instance methods (a method
/// group over an instance is not cached by the compiler in a static field, which ADR-0003 section 4 forbids in rule types) that
/// read <c>input.Candidate</c> and report through the context; the rows in <see cref="Throwing"/> choose severity and stage. The
/// only state is the optional synthetic <c>ThrowRangePermille</c> Add list of <see cref="ThrowingChecks(ImmutableArray{int})"/>,
/// which is immutable. Balance-only findings belong to no def, so they are addressed by <see cref="DefRef.None"/> at the
/// <c>throw</c> balance file. Load-time only, so they allocate freely.
/// </summary>
/// <example>
/// <code>
/// RuleSpec row = RuleSpec.Create&lt;ContentValidationInput&gt;("CV-THR-03", "throwing-catching", "THROW-CV-03", RuleSeverity.Error,
///     ValidationStage.References, inputs, new ThrowingChecks(default).CheckGravity);
/// </code>
/// </example>
internal sealed class ThrowingChecks
{
    /// <summary>The slowest legal throw speed, mm/s (CV-THR-01).</summary>
    internal const int SpeedMinMmS = 3000;

    /// <summary>The fastest legal throw speed, mm/s (CV-THR-01).</summary>
    internal const int SpeedMaxMmS = 30_000;

    /// <summary>The fewest flight ticks (CV-THR-04).</summary>
    internal const int FlightTicksMin = 30;

    /// <summary>The most flight ticks (CV-THR-04).</summary>
    internal const int FlightTicksMax = 150;

    /// <summary>The number of receiver kinds the rank table orders (CV-THR-05).</summary>
    internal const int ReceiverKinds = 8;

    /// <summary>A catch volume's yaw is a multiple of this, milli-degrees (90 degrees, CV-THR-06).</summary>
    internal const int YawStepMilliDeg = 90_000;

    /// <summary>The steepest legal aim before loft, milli-degrees (70 degrees, CV-THR-11).</summary>
    internal const int MaxPitchMilliDeg = 70_000;

    /// <summary>The host's elevation bound, milli-degrees (78 degrees, CV-THR-11).</summary>
    internal const int HostPitchBoundMilliDeg = 78_000;

    /// <summary>The longest effective catch reach, mm (CV-THR-18).</summary>
    internal const int MaxCatchReachMm = 3700;

    private const string ThrowFile = "builtin/balance/throw";
    private const long TicksPerSecond = SimSettings.TickRateHz;

    private readonly ImmutableArray<int> _throwRangeAdds;

    /// <summary>Creates the bodies with the single Add sources of <c>ThrowRangePermille</c> (permille).</summary>
    /// <param name="throwRangeAdds">
    /// Each Add unit that can be live. At M-1a there are no perk sources, so production uses the default (empty) list; a fixture
    /// passes synthetic sources (for example +500 for Cannon Arm).
    /// </param>
    public ThrowingChecks(ImmutableArray<int> throwRangeAdds)
    {
        _throwRangeAdds = throwRangeAdds.IsDefault ? ImmutableArray<int>.Empty : throwRangeAdds;
    }

    /// <summary>
    /// CV-THR-01 (THROW-CV-01), stage 2 (runs at stage 4): clause <c>speed_order</c> (<c>0 &lt; heavy &lt; medium &lt; light</c>, one
    /// finding) and clause <c>speed_range</c> (each speed within 3000 to 30000 mm/s, one finding per speed). The balance hard range is
    /// the same 3000 to 30000 (CV-GEN-13), so the range clause is the defence if that range is ever relaxed.
    /// </summary>
    internal void CheckSpeeds(ContentValidationInput input, RuleContext ctx)
    {
        ThrowBalance t = input.Candidate.Throw;
        if (!(0 < t.ThrowSpeedHeavy && t.ThrowSpeedHeavy < t.ThrowSpeedMedium && t.ThrowSpeedMedium < t.ThrowSpeedLight))
        {
            ReportBalance(
                ctx, "throw.throwSpeed heavy " + Num(t.ThrowSpeedHeavy) + ", medium " + Num(t.ThrowSpeedMedium) + ", light " + Num(t.ThrowSpeedLight) + " mm/s",
                "0 < heavy < medium < light");
        }

        ReportSpeedRange(ctx, "throw.throwSpeed.heavy", t.ThrowSpeedHeavy);
        ReportSpeedRange(ctx, "throw.throwSpeed.medium", t.ThrowSpeedMedium);
        ReportSpeedRange(ctx, "throw.throwSpeed.light", t.ThrowSpeedLight);
    }

    private static void ReportSpeedRange(RuleContext ctx, string key, int speed)
    {
        if (speed < SpeedMinMmS || speed > SpeedMaxMmS)
        {
            ReportBalance(ctx, key + " " + Num(speed) + " mm/s", Num(SpeedMinMmS) + ".." + Num(SpeedMaxMmS) + " mm/s");
        }
    }

    /// <summary>
    /// CV-THR-02 (THROW-CV-02), stage 5, F5 single-source mode: each Add source of <c>ThrowRangePermille</c> is evaluated alone on
    /// the base (1000) through <see cref="WorstCase"/>; a raising source whose result exceeds <c>throw.rangeMaxPermille</c> is
    /// clause <c>range_above_max</c>, a lowering one below <c>throw.rangeMinPermille</c> is clause <c>range_below_min</c>. The
    /// runtime clamp is not a rejection; this bounds dead content. With no sources (M-1a) nothing is reported.
    /// </summary>
    internal void CheckThrowRangeSources(ContentValidationInput input, RuleContext ctx)
    {
        ThrowBalance t = input.Candidate.Throw;
        StatDef def = StatDefs.Get(Stat.ThrowRangePermille);
        for (int i = 0; i < _throwRangeAdds.Length; i++)
        {
            int add = _throwRangeAdds[i];
            ReadOnlySpan<int> one = new(in add);
            if (add > 0)
            {
                WorstCaseResult worst = WorstCase.High(def.Rounding, def.Base.Value, one, ReadOnlySpan<int>.Empty, ReadOnlySpan<int>.Empty, 0);
                if (worst.Status == WorstCaseStatus.Ok && worst.Value > t.RangeMaxPermille)
                {
                    ReportBalance(
                        ctx, "ThrowRangePermille " + Num(def.Base.Value) + " + " + Num(add) + " = " + Num(worst.Value),
                        "at most throw.rangeMaxPermille " + Num(t.RangeMaxPermille));
                }
            }
            else if (add < 0)
            {
                WorstCaseResult worst = WorstCase.Low(def.Rounding, def.Base.Value, one, ReadOnlySpan<int>.Empty, ReadOnlySpan<int>.Empty);
                if (worst.Status == WorstCaseStatus.Ok && worst.Value < t.RangeMinPermille)
                {
                    ReportBalance(
                        ctx, "ThrowRangePermille " + Num(def.Base.Value) + " + " + Num(add) + " = " + Num(worst.Value),
                        "at least throw.rangeMinPermille " + Num(t.RangeMinPermille));
                }
            }
        }
    }

    /// <summary>CV-THR-03 (THROW-CV-03), stage 2 (runs at stage 4): <c>throw.g</c> above 0 (the balance hard range rejects 0 first, CV-GEN-13).</summary>
    internal void CheckGravity(ContentValidationInput input, RuleContext ctx)
    {
        int g = input.Candidate.Throw.G;
        if (g <= 0)
        {
            ReportBalance(ctx, "throw.g " + Num(g), "above 0");
        }
    }

    /// <summary>CV-THR-04 (THROW-CV-04), stage 2 (runs at stage 4): <c>maxFlightTicks</c> within 30 to 150 (the hard range is the same, CV-GEN-13).</summary>
    internal void CheckMaxFlightTicks(ContentValidationInput input, RuleContext ctx)
    {
        int ticks = input.Candidate.Throw.MaxFlightTicks;
        if (ticks < FlightTicksMin || ticks > FlightTicksMax)
        {
            ReportBalance(ctx, "throw.maxFlightTicks " + Num(ticks), Num(FlightTicksMin) + ".." + Num(FlightTicksMax) + " ticks");
        }
    }

    /// <summary>
    /// CV-THR-05 (THROW-CV-05), stage 2 (runs at stage 4): the eight <c>throw.rank.*</c> values are a permutation of 0 to 7, so
    /// every receiver kind has a distinct tie-break rank. One finding.
    /// </summary>
    internal void CheckRankPermutation(ContentValidationInput input, RuleContext ctx)
    {
        ThrowBalance t = input.Candidate.Throw;
        int[] ranks =
        {
            t.RankArmedCatcher, t.RankMailbox, t.RankGoon, t.RankTeammate, t.RankBelt, t.RankSolid, t.RankGround, t.RankWater,
        };
        int seen = 0;
        string? problem = null;
        for (int i = 0; i < ranks.Length && problem is null; i++)
        {
            int r = ranks[i];
            if (r < 0 || r >= ReceiverKinds)
            {
                problem = "rank " + Num(r) + " is outside 0.." + Num(ReceiverKinds - 1);
            }
            else if ((seen & (1 << r)) != 0)
            {
                problem = "rank " + Num(r) + " is used twice";
            }
            else
            {
                seen |= 1 << r;
            }
        }

        if (problem is not null)
        {
            ReportBalance(ctx, "throw.rank.*: " + problem, "a permutation of 0.." + Num(ReceiverKinds - 1));
        }
    }

    /// <summary>
    /// CV-THR-06 (THROW-CV-06), stage 4: each destination type's catch volume has a yaw that is a multiple of 90 degrees (clause
    /// <c>volume_axis_aligned</c>), every extent above 0 (<c>volume_extents_positive</c>) and its bottom at or above the mailbox base
    /// (<c>volume_bottom_at_base</c>: <c>centreHeight - height / 2 &gt;= 0</c>, tested as <c>2 x centreHeight - height &gt;= 0</c> so
    /// an odd height is exact).
    /// </summary>
    internal void CheckCatchVolumes(ContentValidationInput input, RuleContext ctx)
    {
        foreach (DestinationTypeDef type in input.Candidate.DestinationTypes.AsSpan())
        {
            CatchVolume v = type.CatchVolume;
            DefRef def = new(DefType.DestinationType, type.Id);
            SourceLocation where = ContentValidationInput.LocationOf("destination_types", type.Id);
            if (v.YawMilliDeg % YawStepMilliDeg != 0)
            {
                ctx.Report(def, where, "catchVolume yaw " + Num(v.YawMilliDeg) + " milli-deg", "a multiple of " + Num(YawStepMilliDeg) + " milli-deg (axis-aligned)");
            }

            if (v.WidthMm <= 0 || v.HeightMm <= 0 || v.DepthMm <= 0)
            {
                ctx.Report(
                    def, where, "catchVolume extents " + Num(v.WidthMm) + " x " + Num(v.HeightMm) + " x " + Num(v.DepthMm) + " mm", "every extent above 0");
            }

            if (2L * v.CentreHeightMm - v.HeightMm < 0)
            {
                ctx.Report(
                    def, where, "catchVolume centre " + Num(v.CentreHeightMm) + " mm, height " + Num(v.HeightMm) + " mm (bottom " + Num(v.CentreHeightMm - (v.HeightMm / 2)) + " mm)",
                    "bottom at or above the mailbox base (2 x centre at least height)");
            }
        }
    }

    /// <summary>
    /// CV-THR-08 (THROW-CV-08), stage 4: every ItemDef and MailKindDef has a defined weight class (clause <c>weight_class_present</c>;
    /// an absent field is CV-GEN-05 at load, so only a value outside the closed enum reaches here) and a <c>bulk</c> one is not
    /// hand-carried (clause <c>bulk_not_hand_carried</c>: <c>carryByHand</c> true would put it on the hotbar).
    /// </summary>
    internal void CheckWeightClasses(ContentValidationInput input, RuleContext ctx)
    {
        foreach (ItemDef item in input.Candidate.Items.AsSpan())
        {
            CheckWeightClass(ctx, new DefRef(DefType.Item, item.Id), ContentValidationInput.LocationOf("items", item.Id), item.WeightClass, item.CarryByHand);
        }

        foreach (MailKindDef kind in input.Candidate.MailKinds.AsSpan())
        {
            CheckWeightClass(ctx, new DefRef(DefType.MailKind, kind.Id), ContentValidationInput.LocationOf("mail_kinds", kind.Id), kind.WeightClass, kind.CarryByHand);
        }
    }

    private static void CheckWeightClass(RuleContext ctx, DefRef def, SourceLocation where, WeightClass weight, bool carryByHand)
    {
        if (weight < WeightClass.Light || weight > WeightClass.Bulk)
        {
            ctx.Report(def, where, "weightClass " + Num((byte)weight), "light, medium, heavy or bulk");
        }
        else if (weight == WeightClass.Bulk && carryByHand)
        {
            ctx.Report(def, where, "weightClass bulk with carryByHand true", "carryByHand false (bulk is never on the hotbar)");
        }
    }

    /// <summary>CV-THR-09 (THROW-CV-09), stage 5, cross-owner: <c>throw.fwdOffMm</c> at least <c>player.capsuleRadiusMm</c>, so the origin clears the thrower's body.</summary>
    internal void CheckFwdOffClearsCapsule(ContentValidationInput input, RuleContext ctx)
    {
        int fwd = input.Candidate.Throw.FwdOffMm;
        int radius = input.Candidate.Player.CapsuleRadiusMm;
        if (fwd < radius)
        {
            ReportBalance(ctx, "throw.fwdOffMm " + Num(fwd) + " mm, player.capsuleRadiusMm " + Num(radius) + " mm", "throw.fwdOffMm at least player.capsuleRadiusMm");
        }
    }

    /// <summary>CV-THR-10 (THROW-CV-10), stage 5, cross-owner: <c>0 &lt; throw.handHeightMm</c> at most <c>player.capsuleHeightMm</c>, so the hand is inside the body's height.</summary>
    internal void CheckHandHeightInCapsule(ContentValidationInput input, RuleContext ctx)
    {
        int hand = input.Candidate.Throw.HandHeightMm;
        int height = input.Candidate.Player.CapsuleHeightMm;
        if (hand <= 0 || hand > height)
        {
            ReportBalance(ctx, "throw.handHeightMm " + Num(hand) + " mm, player.capsuleHeightMm " + Num(height) + " mm", "above 0 and at most player.capsuleHeightMm");
        }
    }

    /// <summary>
    /// CV-THR-11 (THROW-CV-11), stage 2 (runs at stage 4): 70 degrees plus <c>loftDeg</c> at most 78 degrees, so a legal
    /// maximum-pitch throw never fails the host's 78.5 degree bound. <c>loftDeg</c> holds milli-degrees despite its name.
    /// </summary>
    internal void CheckLoft(ContentValidationInput input, RuleContext ctx)
    {
        long loft = input.Candidate.Throw.LoftDeg;
        if (MaxPitchMilliDeg + loft > HostPitchBoundMilliDeg)
        {
            ReportBalance(
                ctx, "throw.loftDeg " + Num(loft) + " milli-deg (70 degrees + loft = " + Num(MaxPitchMilliDeg + loft) + ")",
                "70 degrees + loft at most " + Num(HostPitchBoundMilliDeg) + " milli-deg");
        }
    }

    /// <summary>CV-THR-12 (THROW-CV-12), stage 2 (runs at stage 4): <c>catchTapMs</c> and <c>catchPredictMs</c> are whole ticks (F3 <see cref="ExactTickCheck"/>, multiples of 100 ms).</summary>
    internal void CheckCatchWholeTicks(ContentValidationInput input, RuleContext ctx)
    {
        ThrowBalance t = input.Candidate.Throw;
        ReportNotWholeTicks(ctx, "throw.catchTapMs", t.CatchTapMs);
        ReportNotWholeTicks(ctx, "throw.catchPredictMs", t.CatchPredictMs);
    }

    private static void ReportNotWholeTicks(RuleContext ctx, string key, int ms)
    {
        if (!ExactTickCheck.IsWholeTicks(ms))
        {
            ReportBalance(ctx, key + " " + Num(ms) + " ms", "a multiple of 100 ms (whole ticks at 30 Hz)");
        }
    }

    /// <summary>CV-THR-13 (THROW-CV-13), stage 2 (runs at stage 4): <c>catchPredictMs</c> at most <c>maxFlightTicks x 1000 / 30</c> ms, tested without division as <c>predict x 30 &lt;= maxFlightTicks x 1000</c>.</summary>
    internal void CheckCatchPredictWithinFlight(ContentValidationInput input, RuleContext ctx)
    {
        ThrowBalance t = input.Candidate.Throw;
        if (t.CatchPredictMs * TicksPerSecond > t.MaxFlightTicks * 1000L)
        {
            ReportBalance(
                ctx, "throw.catchPredictMs " + Num(t.CatchPredictMs) + " ms, throw.maxFlightTicks " + Num(t.MaxFlightTicks),
                "catchPredictMs at most maxFlightTicks x 1000 / 30 ms");
        }
    }

    /// <summary>CV-THR-15 (THROW-CV-15), stage 2 (runs at stage 4): <c>damagePerPoint</c> at least 1.</summary>
    internal void CheckDamagePerPoint(ContentValidationInput input, RuleContext ctx)
    {
        int damage = input.Candidate.Throw.DamagePerPoint;
        if (damage < 1)
        {
            ReportBalance(ctx, "throw.damagePerPoint " + Num(damage), "at least 1");
        }
    }

    /// <summary>CV-THR-16 (THROW-CV-16), stage 2 (runs at stage 4): <c>throwCooldownTicks</c> and <c>throwBurstTicks</c> at least 1 (replaces the stale <c>cooldownSlackTicks</c>).</summary>
    internal void CheckCooldownAndBurst(ContentValidationInput input, RuleContext ctx)
    {
        ThrowBalance t = input.Candidate.Throw;
        if (t.ThrowCooldownTicks < 1)
        {
            ReportBalance(ctx, "throw.throwCooldownTicks " + Num(t.ThrowCooldownTicks), "at least 1 tick");
        }

        if (t.ThrowBurstTicks < 1)
        {
            ReportBalance(ctx, "throw.throwBurstTicks " + Num(t.ThrowBurstTicks), "at least 1 tick");
        }
    }

    /// <summary>CV-THR-18 (THROW-CV-18), stage 2 (runs at stage 4): <c>catchRadiusMm + catchHistoryMaxOffsetMm</c> at most 3700, bounding the effective catch reach.</summary>
    internal void CheckCatchReach(ContentValidationInput input, RuleContext ctx)
    {
        long radius = input.Candidate.Throw.CatchRadiusMm;
        long offset = input.Candidate.Throw.CatchHistoryMaxOffsetMm;
        if (radius + offset > MaxCatchReachMm)
        {
            ReportBalance(
                ctx, "throw.catchRadiusMm " + Num(radius) + " + throw.catchHistoryMaxOffsetMm " + Num(offset) + " = " + Num(radius + offset) + " mm",
                "at most " + Num(MaxCatchReachMm) + " mm");
        }
    }

    private static void ReportBalance(RuleContext ctx, string value, string allowed) =>
        ctx.Report(DefRef.None, SourceLocation.BuiltIn(ThrowFile), value, allowed);

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
}
