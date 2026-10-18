using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>
/// The bodies of the Player Character rules CV-PLR-01, 02, 04, 06, 08, 09, 10 and 11 (content-data story 011; the text is
/// the owner's rule table in <c>player-character-movement.md</c>, PCM-F8-3) and CV-PLR-13 (player-character story 012,
/// PCM-P10-1 / input-controls H-R4-5). Rule bodies are stateless instance methods
/// (a method group over an instance is not cached by the compiler in a static field, which ADR-0003 section 4 forbids in
/// rule types) that read <c>input.Candidate</c> and report through the context; the rows in <see cref="Player"/> choose
/// severity and stage. Each finding belongs to no def, so it is addressed by <see cref="DefRef.None"/> at the
/// <c>player</c> balance file. Load-time only, so they allocate freely.
/// </summary>
/// <example>
/// <code>
/// RuleSpec row = RuleSpec.Create&lt;ContentValidationInput&gt;("CV-PLR-04", "player-character-movement", "PC rule 4", RuleSeverity.Error,
///     ValidationStage.References, inputs, new PlayerChecks().CheckMinHold);
/// </code>
/// </example>
internal sealed class PlayerChecks
{
    /// <summary>The shortest hold the hold timer can honour, in ticks (CV-PLR-04's limit).</summary>
    internal const int MinHoldTicksFloor = 6;

    /// <summary>The shortest stale-position window, in ticks: 2.5 keepalive intervals at 4 Hz, rounded up (CV-PLR-11's limit).</summary>
    internal const int PositionStaleTicksFloor = 19;

    /// <summary>The margin between the swim exit and enter depths, in mm (CV-PLR-02), and between the incumbent slack and the range slack (CV-PLR-09).</summary>
    internal const int HysteresisMm = 100;

    /// <summary><c>g / 30</c> must not truncate, so <c>player.g</c> is a multiple of this (CV-PLR-10).</summary>
    internal const int GravityStep = 30;

    private const string PlayerFile = "builtin/balance/player";

    /// <summary>
    /// CV-PLR-01 (PC rule 1), stage 5: <c>rangeMm + rangeSlackMm</c> must not exceed the external container leash, so a container
    /// the host accepts is never auto-closed by the client. Reads Grid's registered <c>inventory.externalLeashMeters</c> (a
    /// millimetre value despite the name; PC calls it <c>leashMm</c> and <c>externalContainerLeashMeters</c>).
    /// </summary>
    internal void CheckRangePlusSlackWithinLeash(ContentValidationInput input, RuleContext ctx)
    {
        long range = input.Candidate.Player.RangeMm;
        long slack = input.Candidate.Player.RangeSlackMm;
        long leash = input.Candidate.Inventory.ExternalLeashMeters;
        if (range + slack > leash)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "player.rangeMm " + Num(range) + " + player.rangeSlackMm " + Num(slack) + " = " + Num(range + slack) + " mm",
                "at most inventory.externalLeashMeters " + Num(leash) + " mm");
        }
    }

    /// <summary>CV-PLR-02 (PC rule 2), stage 2 (runs at stage 4): <c>swimExitMm</c> at most <c>swimEnterMm - 100</c>, so swimming has hysteresis.</summary>
    internal void CheckSwimHysteresis(ContentValidationInput input, RuleContext ctx)
    {
        int enter = input.Candidate.Player.SwimEnterMm;
        int exit = input.Candidate.Player.SwimExitMm;
        if (exit > enter - HysteresisMm)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "player.swimExitMm " + Num(exit) + " mm, player.swimEnterMm " + Num(enter) + " mm",
                "player.swimExitMm at most player.swimEnterMm - " + Num(HysteresisMm) + " = " + Num(enter - HysteresisMm) + " mm");
        }
    }

    /// <summary>CV-PLR-04 (PC rule 4), stage 2 (runs at stage 4): <c>minHoldTicks</c> at least 6.</summary>
    internal void CheckMinHold(ContentValidationInput input, RuleContext ctx)
    {
        int hold = input.Candidate.Player.MinHoldTicks;
        if (hold < MinHoldTicksFloor)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "player.minHoldTicks " + Num(hold), "at least " + Num(MinHoldTicksFloor) + " ticks");
        }
    }

    /// <summary>
    /// CV-PLR-06 (PC rule 6, Throwing rule 14; formerly CV-THR-14), stage 5: the catch range <c>throw.catchRadiusMm</c> must be
    /// below <c>player.rangeMm</c>, so a catch is never farther than a hold can reach.
    /// </summary>
    internal void CheckCatchRadiusBelowRange(ContentValidationInput input, RuleContext ctx)
    {
        int catchRadius = input.Candidate.Throw.CatchRadiusMm;
        int range = input.Candidate.Player.RangeMm;
        if (catchRadius >= range)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "throw.catchRadiusMm " + Num(catchRadius) + " mm, player.rangeMm " + Num(range) + " mm",
                "throw.catchRadiusMm below player.rangeMm");
        }
    }

    /// <summary>CV-PLR-08 (PC rule 8), stage 2 (runs at stage 4): <c>haulOutMm</c> at least <c>stepUpMm</c>, so a swimmer can climb what a walker steps over.</summary>
    internal void CheckHaulOutAtLeastStepUp(ContentValidationInput input, RuleContext ctx)
    {
        int haulOut = input.Candidate.Player.HaulOutMm;
        int stepUp = input.Candidate.Player.StepUpMm;
        if (haulOut < stepUp)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "player.haulOutMm " + Num(haulOut) + " mm, player.stepUpMm " + Num(stepUp) + " mm",
                "player.haulOutMm at least player.stepUpMm");
        }
    }

    /// <summary>
    /// CV-PLR-09 (PC rule 9), stage 2 (runs at stage 4): <c>incumbentSlackMm + 100</c> at most <c>rangeSlackMm</c>, so a hold the
    /// client completes is never rejected for range.
    /// </summary>
    internal void CheckIncumbentSlackWithinRangeSlack(ContentValidationInput input, RuleContext ctx)
    {
        long incumbent = input.Candidate.Player.IncumbentSlackMm;
        long rangeSlack = input.Candidate.Player.RangeSlackMm;
        if (incumbent + HysteresisMm > rangeSlack)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "player.incumbentSlackMm " + Num(incumbent) + " mm, player.rangeSlackMm " + Num(rangeSlack) + " mm",
                "player.incumbentSlackMm + " + Num(HysteresisMm) + " at most player.rangeSlackMm");
        }
    }

    /// <summary>
    /// CV-PLR-10 (PC rule 10), stage 2 (runs at stage 4), no division by zero and no truncation: clause <c>positive_fields</c>
    /// (<c>ticksAccel</c>, <c>ticksDecel</c>, <c>airControlPm</c> and <c>rangeMm</c> above 0, one finding per field) and clause
    /// <c>g_multiple_of_30</c> (<c>player.g</c> a multiple of 30, so <c>g / 30</c> is exact). The balance hard range rejects
    /// <c>g</c> below 1 first (CV-GEN-13), so the rule never sees 0 or a negative <c>g</c>.
    /// </summary>
    internal void CheckDivisionSafety(ContentValidationInput input, RuleContext ctx)
    {
        PlayerBalance p = input.Candidate.Player;
        ReportNotPositive(ctx, "player.ticksAccel", p.TicksAccel);
        ReportNotPositive(ctx, "player.ticksDecel", p.TicksDecel);
        ReportNotPositive(ctx, "player.airControlPm", p.AirControlPm);
        ReportNotPositive(ctx, "player.rangeMm", p.RangeMm);
        if (p.G % GravityStep != 0)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "player.g " + Num(p.G), "a multiple of " + Num(GravityStep) + " mm/s^2");
        }
    }

    /// <summary>CV-PLR-11 (PC rule 11), stage 2 (runs at stage 4): <c>positionStaleTicks</c> at least 19 (2.5 keepalive intervals at 4 Hz).</summary>
    internal void CheckPositionStale(ContentValidationInput input, RuleContext ctx)
    {
        int stale = input.Candidate.Player.PositionStaleTicks;
        if (stale < PositionStaleTicksFloor)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn(PlayerFile),
                "player.positionStaleTicks " + Num(stale), "at least " + Num(PositionStaleTicksFloor) + " ticks");
        }
    }

    /// <summary>
    /// CV-PLR-13 (PCM-P10-1, input-controls H-R4-5), stage 2 (runs at stage 4): every verb a content definition names is in the
    /// closed <see cref="Verb"/> catalogue, and one interactable declares exactly one verb. Reads
    /// <see cref="ContentValidationInput.VerbDeclarations"/>, not a def field: at M-1a no def type carries a verb, so built-in
    /// content passes no declarations and the rule is exercised by fixtures; when a def type later carries verbs the loader fills
    /// the list and this body is unchanged. Clause <c>one_verb_per_interactable</c>: a declaration with zero or two or more
    /// spellings, one finding naming them all. Clause <c>known_verb</c>: one finding per spelling that
    /// <see cref="VerbSpelling.TryParse"/> rejects (so <c>revive</c> is reported, and <c>Deliver</c> too: spellings are
    /// snake_case, compared byte for byte). A declaration with two unknown verbs reports both clauses.
    /// </summary>
    internal void CheckVerbs(ContentValidationInput input, RuleContext ctx)
    {
        foreach (VerbDeclaration decl in input.VerbDeclarations)
        {
            // A default (never-initialised) array reads as "no verb" rather than throwing; a null spelling is reported as unknown.
            ImmutableArray<string> verbs = decl.Verbs.IsDefault ? ImmutableArray<string>.Empty : decl.Verbs;
            if (verbs.Length != 1)
            {
                ctx.Report(
                    decl.Def, decl.Location,
                    verbs.IsEmpty ? "no verb" : Num(verbs.Length) + " verbs: " + string.Join(", ", verbs),
                    "exactly one verb per interactable", ContentInputKeys.Verbs);
            }

            foreach (string? spelling in verbs)
            {
                if (spelling is null || !VerbSpelling.TryParse(Encoding.UTF8.GetBytes(spelling), out _))
                {
                    ctx.Report(decl.Def, decl.Location, spelling ?? "(null)", "one of the Verb catalogue: " + CatalogueSpellings(), ContentInputKeys.Verbs);
                }
            }
        }
    }

    /// <summary>The 13 catalogue spellings in enum order, comma separated (built from <see cref="VerbSpelling.ToSpelling"/>, so it never drifts).</summary>
    private static string CatalogueSpellings()
    {
        ReadOnlySpan<Verb> members = new[]
        {
            Verb.Deliver, Verb.Open, Verb.PickUp, Verb.Mount, Verb.Operate, Verb.Configure, Verb.Shop,
            Verb.Announce, Verb.Dismount, Verb.Catch, Verb.Push, Verb.Rebuild, Verb.Deconstruct,
        };
        var sb = new StringBuilder();
        foreach (Verb v in members)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(Encoding.UTF8.GetString(VerbSpelling.ToSpelling(v)));
        }

        return sb.ToString();
    }

    private static void ReportNotPositive(RuleContext ctx, string key, int value)
    {
        if (value <= 0)
        {
            ctx.Report(DefRef.None, SourceLocation.BuiltIn(PlayerFile), key + " " + Num(value), key + " above 0");
        }
    }

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
}
