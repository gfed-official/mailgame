namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// Whether a registered rule still runs. A rule id is permanent (GDD CD23): a withdrawn rule stays listed as
/// <see cref="Retired"/>, is never renumbered and its id is never reused. Values are append-only.
/// </summary>
/// <example>
/// <code>
/// bool listedButInert = rule.Status == RuleStatus.Retired;
/// </code>
/// </example>
public enum RuleStatus : byte
{
    /// <summary>The rule is live.</summary>
    Active = 1,

    /// <summary>The rule was withdrawn (for example CV-THR-14, retired into CV-PLR-06). It never fires.</summary>
    Retired = 2,

    /// <summary>
    /// The id is registered, with its owner, alias, severity and stage, but the rule cannot run yet because its inputs do not
    /// exist at the current milestone (for example CV-PLR-13 needs the interaction catalogue). It never fires and the fixture
    /// meta-test skips it explicitly; <see cref="RuleSpec.DeferredReason"/> says why. It becomes <see cref="Active"/> when its row is
    /// replaced by a real one. Values are append-only.
    /// </summary>
    Deferred = 3,
}
