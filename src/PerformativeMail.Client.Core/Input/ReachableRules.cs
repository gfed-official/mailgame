using System.Collections.Immutable;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// One reachable-state clause as data (GDD R2 "Reachable states"): if a state satisfies <see cref="Antecedent"/> it must also
/// satisfy <see cref="Consequent"/>. A state is reachable iff no rule is violated. The consequent is a conjunction (all six
/// masks), so "<c>aiming ⇒ OnFoot ∧ no overlay ∧ ¬holding ∧ no capture</c>" is one rule.
/// </summary>
/// <example>
/// <code>
/// var rule = new ReachabilityRule("holding => OnFoot",
///     LivePredicate.Any with { HoldingMask = HoldingMask.Yes },
///     LivePredicate.Any with { BodyMask = BodyMask.OnFoot });
/// bool violated = rule.IsViolatedBy(stateId);
/// </code>
/// </example>
public readonly struct ReachabilityRule
{
    /// <summary>Creates a rule.</summary>
    /// <param name="name">Short human-readable clause text for diagnostics (a literal, never built at runtime).</param>
    /// <param name="antecedent">The "if" predicate.</param>
    /// <param name="consequent">The "then" predicate.</param>
    public ReachabilityRule(string name, LivePredicate antecedent, LivePredicate consequent)
    {
        Name = name;
        Antecedent = antecedent;
        Consequent = consequent;
    }

    /// <summary>Short clause text for diagnostics.</summary>
    public string Name { get; }

    /// <summary>The "if" predicate.</summary>
    public LivePredicate Antecedent { get; }

    /// <summary>The "then" predicate: must hold whenever the antecedent does.</summary>
    public LivePredicate Consequent { get; }

    /// <summary>True when the state satisfies the antecedent but not the consequent. An id of 1008 or more violates nothing here (it is rejected as invalid elsewhere).</summary>
    /// <param name="stateId">The packed router state id.</param>
    /// <returns>Whether this rule forbids the state.</returns>
    /// <example><code>bool bad = rule.IsViolatedBy(id);</code></example>
    public bool IsViolatedBy(ushort stateId) => Antecedent.IsLive(stateId) && !Consequent.IsLive(stateId);
}

/// <summary>
/// The seven GDD R2 "Reachable states" clauses, as an immutable array of <see cref="ReachabilityRule"/> data. The conflict
/// validator (story 006) and the H-R2-8 golden test read this same instance through <see cref="ReachableSet.Default"/>. Do not
/// add branches elsewhere that restate a clause; add or edit a rule here and regenerate the golden file deliberately.
/// Immutable, thread-safe.
/// </summary>
/// <example>
/// <code>
/// foreach (ReachabilityRule rule in ReachableRules.All) { /* inspect rule.Name */ }
/// int first = ReachableRules.FirstViolation(ReachableRules.All, id);   // -1 when reachable
/// </code>
/// </example>
public static class ReachableRules
{
    /// <summary>Index of <c>aiming ⇒ OnFoot ∧ no overlay ∧ ¬holding ∧ no capture</c>.</summary>
    public const int AimingIndex = 0;

    /// <summary>Index of <c>submenu ≠ none ⇒ overlay Panel</c>.</summary>
    public const int SubmenuIndex = 1;

    /// <summary>Index of <c>holding ⇒ body OnFoot</c>.</summary>
    public const int HoldingIndex = 2;

    /// <summary>Index of <c>overlay Panel ⇒ body OnFoot</c>.</summary>
    public const int PanelIndex = 3;

    /// <summary>Index of <c>overlay History ⇒ body ∈ {OnFoot, Dead}</c>.</summary>
    public const int HistoryIndex = 4;

    /// <summary>Index of <c>body Dead ⇒ overlay ∈ {none, Draft, Results, History} ∧ ¬holding ∧ ¬aiming</c>.</summary>
    public const int DeadIndex = 5;

    /// <summary>Index of <c>overlay ∈ {Map, Station, Draft, Results, History} ⇒ ¬aiming</c>.</summary>
    public const int NoAimUnderOverlayIndex = 6;

    /// <summary>The clauses, in the GDD's order (the <c>...Index</c> constants).</summary>
    public static readonly ImmutableArray<ReachabilityRule> All = ImmutableArray.Create(
        new ReachabilityRule(
            "aiming => OnFoot and no overlay and not holding and no capture",
            LivePredicate.Any with { AimingMask = AimingMask.Yes },
            LivePredicate.Any with
            {
                BodyMask = BodyMask.OnFoot,
                OverlayMask = OverlayMask.None,
                HoldingMask = HoldingMask.No,
                CaptureMask = CaptureMask.None,
            }),
        new ReachabilityRule(
            "submenu /= none => overlay Panel",
            LivePredicate.Any with { SubmenuMask = SubmenuMask.ActionMenu | SubmenuMask.SplitStepper },
            LivePredicate.Any with { OverlayMask = OverlayMask.Panel }),
        new ReachabilityRule(
            "holding => body OnFoot",
            LivePredicate.Any with { HoldingMask = HoldingMask.Yes },
            LivePredicate.Any with { BodyMask = BodyMask.OnFoot }),
        new ReachabilityRule(
            "overlay Panel => body OnFoot",
            LivePredicate.Any with { OverlayMask = OverlayMask.Panel },
            LivePredicate.Any with { BodyMask = BodyMask.OnFoot }),
        new ReachabilityRule(
            "overlay History => body in {OnFoot, Dead}",
            LivePredicate.Any with { OverlayMask = OverlayMask.History },
            LivePredicate.Any with { BodyMask = BodyMask.OnFoot | BodyMask.Dead }),
        new ReachabilityRule(
            "body Dead => overlay in {none, Draft, Results, History} and not holding and not aiming",
            LivePredicate.Any with { BodyMask = BodyMask.Dead },
            LivePredicate.Any with
            {
                OverlayMask = OverlayMask.None | OverlayMask.Draft | OverlayMask.Results | OverlayMask.History,
                HoldingMask = HoldingMask.No,
                AimingMask = AimingMask.No,
            }),
        new ReachabilityRule(
            "overlay in {Map, Station, Draft, Results, History} => not aiming",
            LivePredicate.Any with
            {
                OverlayMask = OverlayMask.Map | OverlayMask.Station | OverlayMask.Draft | OverlayMask.Results | OverlayMask.History,
            },
            LivePredicate.Any with { AimingMask = AimingMask.No }));

    /// <summary>The index of the first rule in <paramref name="rules"/> that forbids the state, or -1 when none does.</summary>
    /// <param name="rules">The rule list (normally <see cref="All"/>; a mutated copy in tests).</param>
    /// <param name="stateId">The packed router state id (below 1008).</param>
    /// <returns>The index of the first violated rule, or -1.</returns>
    /// <exception cref="ArgumentException"><paramref name="rules"/> is a default (uninitialised) array.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stateId"/> is 1008 or more; such an id is never reachable (use <see cref="ReachableSet.Contains"/> for a non-throwing check).</exception>
    /// <example><code>bool reachable = ReachableRules.FirstViolation(ReachableRules.All, id) &lt; 0;</code></example>
    public static int FirstViolation(ImmutableArray<ReachabilityRule> rules, ushort stateId)
    {
        if (rules.IsDefault)
        {
            throw new ArgumentException("The rule list is a default ImmutableArray.", nameof(rules));
        }

        if (!PerformativeMail.Sim.ClientLayer.RouterStateInfo.IsValid(stateId))
        {
            throw new ArgumentOutOfRangeException(nameof(stateId), stateId, "Router state ids are below 1008.");
        }

        for (int i = 0; i < rules.Length; i++)
        {
            if (rules[i].IsViolatedBy(stateId))
            {
                return i;
            }
        }

        return -1;
    }
}
