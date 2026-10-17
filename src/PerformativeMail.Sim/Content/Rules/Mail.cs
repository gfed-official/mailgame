using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>The ids of the Mail (mail-destinations) rules, so rows, fixtures and tests name them without string literals.</summary>
/// <example>
/// <code>
/// bool isShares = d.RuleId == MailRuleIds.Shares;
/// </code>
/// </example>
public static class MailRuleIds
{
    /// <summary>CV-MAIL-01 (Error, stage 4): <c>acceptedBy</c> names destination types and mirrors <c>DestinationTypeDef.accepts</c>.</summary>
    public const string Acceptance = "CV-MAIL-01";

    /// <summary>CV-MAIL-02 (Error, stage 5): each shift's <c>sharesPm</c> sum to exactly 1 000 and each is 0 to 600.</summary>
    public const string Shares = "CV-MAIL-02";

    /// <summary>CV-MAIL-03 (Error, stage 2): <c>deadLetterShifts</c> at least 1.</summary>
    public const string DeadLetterShifts = "CV-MAIL-03";

    /// <summary>CV-MAIL-04 (Error, stage 5): every kind with a share above 0 has an accepting destination or a resolving <c>fallbackKind</c> chain.</summary>
    public const string Acceptors = "CV-MAIL-04";

    /// <summary>CV-MAIL-05 (Error, stage 5): <c>batchJitter</c> below <c>batchInterval</c> / 2.</summary>
    public const string Jitter = "CV-MAIL-05";

    /// <summary>CV-MAIL-06 (Error, stage 5): <c>tailGuard</c> at most <c>batchInterval</c> minus <c>batchJitter</c>.</summary>
    public const string TailGuard = "CV-MAIL-06";
}

/// <summary>
/// The Mail (mail-destinations) rule table (CV-MAIL), owner <c>mail-destinations</c>: CV-MAIL-01 to 06 (content-data
/// story 010). Rows are explicit entries in this list; there is no reflection discovery. Never renumber or reuse an id.
/// <para>
/// Stage and aliases follow the same two conventions as <see cref="Grid"/>: CV-MAIL-03 (catalogue stage 2) runs at stage 4
/// because the runner executes only stages 4 and 5; the catalogue's shared alias <c>Mail (M-R1-1c)</c> is made unique per
/// row with a key suffix, because the registry rejects an alias used by two rules. The batch timings are mix fields in
/// milliseconds (<c>MailMixDef.BatchIntervalMs</c> and so on), so CV-MAIL-05 and 06 compare those.
/// </para>
/// </summary>
/// <example>
/// <code>
/// RuleSpec mail02 = RuleRegistry.CreateDefault().Find("CV-MAIL-02")!;   // Error, stage 5, "Mail (M-R1-1c) shares"
/// </code>
/// </example>
public static class Mail
{
    private const string Owner = "mail-destinations";

    /// <summary>The rows, in registration order. A property that builds the array on each call, so the class holds no static state (ADR-0003 section 4).</summary>
    public static ImmutableArray<RuleSpec> Rules => Table(new MailChecks());

    private static ImmutableArray<RuleSpec> Table(MailChecks checks) => ImmutableArray.Create(
        RuleSpec.Create<ContentValidationInput>(
            MailRuleIds.Acceptance, Owner, "Mail (M-R1-1c) acceptedBy", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create(ContentInputKeys.MailKinds, ContentInputKeys.DestinationTypes),
            checks.CheckAcceptance,
            ImmutableArray.Create(new RuleClause("accepted_by_subset"), new RuleClause("accepted_by_mirror"), new RuleClause("accepts_mirror"))),
        RuleSpec.Create<ContentValidationInput>(
            MailRuleIds.Shares, Owner, "Mail (M-R1-1c) shares", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.MailMixes),
            checks.CheckShares,
            ImmutableArray.Create(new RuleClause("share_sum", HasLimit: true), new RuleClause("share_range", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            MailRuleIds.DeadLetterShifts, Owner, "Mail (M-R1-1c) deadLetterShifts", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create(ContentInputKeys.MailBalance),
            checks.CheckDeadLetterShifts,
            ImmutableArray.Create(new RuleClause("dead_letter_shifts", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            MailRuleIds.Acceptors, Owner, "Mail (M-R1-1c) acceptor", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.MailKinds, ContentInputKeys.DestinationTypes, ContentInputKeys.MailMixes),
            checks.CheckAcceptors,
            ImmutableArray.Create(new RuleClause("no_acceptor", HasLimit: true), new RuleClause("fallback_unresolved"), new RuleClause("fallback_cycle"))),
        RuleSpec.Create<ContentValidationInput>(
            MailRuleIds.Jitter, Owner, "Mail (Dependencies) jitter", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.MailMixes),
            checks.CheckJitter,
            ImmutableArray.Create(new RuleClause("jitter_below_half_interval", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            MailRuleIds.TailGuard, Owner, "Mail (Dependencies) tailGuard", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.MailMixes),
            checks.CheckTailGuard,
            ImmutableArray.Create(new RuleClause("tail_guard_within_interval", HasLimit: true))));
}
