using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using PerformativeMail.Sim.Content.Rules;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// The rule registry (GDD CD23, ADR-0010 section 5): every rule's permanent id, owner, alias, severity, stage,
/// declared inputs and clauses, from explicit lists in <c>Content/Rules/&lt;Owner&gt;.cs</c>. There is no reflection
/// discovery. An instance is immutable and thread-safe. Construction validates the whole table and throws
/// <see cref="ArgumentException"/> on a defect (a malformed or duplicate id, a duplicate or colliding alias, a
/// retired rule with a check or without a target, a deferred rule with a check, without a reason or naming a retirement target, a deferral reason on a non-deferred rule, a duplicate clause or input), because such a defect is a bug in the code that builds the
/// table, found by the first test run. Ids are compared ordinally and case-sensitively.
/// </summary>
/// <example>
/// <code>
/// RuleRegistry registry = RuleRegistry.CreateDefault();
/// registry.TryResolve("THROW-CV-12", out RuleSpec? rule);   // rule.Id == "CV-THR-12"
/// string label = registry.FormatId("CV-THR-12");            // "CV-THR-12 (THROW-CV-12)"
/// </code>
/// </example>
public sealed class RuleRegistry
{
    private readonly ImmutableArray<RuleSpec> _rules;
    private readonly Dictionary<string, RuleSpec> _byId;
    private readonly Dictionary<string, RuleSpec> _byAlias;

    /// <summary>Builds a registry from <paramref name="rules"/> (registration order is kept).</summary>
    /// <param name="rules">The rows; a default array reads as empty.</param>
    /// <exception cref="ArgumentException">The table is malformed (see the type remarks).</exception>
    public RuleRegistry(ImmutableArray<RuleSpec> rules)
    {
        _rules = rules.IsDefault ? ImmutableArray<RuleSpec>.Empty : rules;
        _byId = new Dictionary<string, RuleSpec>(StringComparer.Ordinal);
        _byAlias = new Dictionary<string, RuleSpec>(StringComparer.Ordinal);

        foreach (RuleSpec rule in _rules)
        {
            ValidateRow(rule);
            if (!_byId.TryAdd(rule.Id, rule))
            {
                throw new ArgumentException("Rule id registered twice: " + rule.Id);
            }
        }

        // Aliases are checked after all ids are known so an alias cannot shadow a later id either.
        foreach (RuleSpec rule in _rules)
        {
            if (rule.Alias.Length > 0)
            {
                AddAlias(rule.Alias, rule);
            }

            foreach (string aka in rule.AlsoKnownAs)
            {
                AddAlias(aka, rule);
            }
        }
    }

    /// <summary>All rows in registration order, including retired ones.</summary>
    public ImmutableArray<RuleSpec> Rules => _rules;

    /// <summary>
    /// The production registry: <see cref="General.Rules"/>, <see cref="Grid.Rules"/>, <see cref="Mail.Rules"/>,
    /// <see cref="Player.Rules"/> and <see cref="Throwing.Rules"/>, in that order. Builds a new instance on each call
    /// (no static state).
    /// </summary>
    public static RuleRegistry CreateDefault() =>
        Compose(General.Rules, Grid.Rules, Mail.Rules, Player.Rules, Throwing.Rules);

    /// <summary>Concatenates owner tables, in order, into one registry (tests add a test-only table this way).</summary>
    /// <param name="tables">The owner tables.</param>
    public static RuleRegistry Compose(params ImmutableArray<RuleSpec>[] tables)
    {
        ImmutableArray<RuleSpec>.Builder all = ImmutableArray.CreateBuilder<RuleSpec>();
        foreach (ImmutableArray<RuleSpec> table in tables)
        {
            if (!table.IsDefault)
            {
                all.AddRange(table);
            }
        }

        return new RuleRegistry(all.ToImmutable());
    }

    /// <summary>Looks a row up by its canonical id only.</summary>
    /// <param name="id">For example <c>CV-THR-12</c>.</param>
    /// <returns>The row, or null when unknown.</returns>
    public RuleSpec? Find(string id) => id is not null && _byId.TryGetValue(id, out RuleSpec? rule) ? rule : null;

    /// <summary>
    /// Resolves a canonical id or any alias spelling (<c>THROW-CV-12</c>, <c>PC rule 7</c>, <c>Throwing rule 14</c>)
    /// to its row. The result's <see cref="RuleSpec.Id"/> is the canonical id.
    /// </summary>
    /// <param name="idOrAlias">The text to resolve (exact, case-sensitive).</param>
    /// <param name="rule">The row when found.</param>
    public bool TryResolve(string idOrAlias, [NotNullWhen(true)] out RuleSpec? rule)
    {
        if (idOrAlias is not null)
        {
            if (_byId.TryGetValue(idOrAlias, out RuleSpec? byId))
            {
                rule = byId;
                return true;
            }

            if (_byAlias.TryGetValue(idOrAlias, out RuleSpec? byAlias))
            {
                rule = byAlias;
                return true;
            }
        }

        rule = null;
        return false;
    }

    /// <summary>
    /// The text a message prints for a rule: <c>CV-THR-12 (THROW-CV-12)</c>, or just the id when the rule has no alias
    /// or is not registered.
    /// </summary>
    /// <param name="id">The canonical rule id.</param>
    public string FormatId(string id)
    {
        RuleSpec? rule = Find(id);
        return rule is null || rule.Alias.Length == 0 ? id : id + " (" + rule.Alias + ")";
    }

    /// <summary>
    /// The ids of retired rules whose <see cref="RuleSpec.RetiredInto"/> target is not a registered active rule. The
    /// registry does not reject these at construction (a retired row may be listed before the rule that absorbed it is
    /// written; CV-THR-14 precedes CV-PLR-06), so a test checks the list once the target's owner has registered.
    /// </summary>
    public ImmutableArray<string> UnresolvedRetirements()
    {
        ImmutableArray<string>.Builder open = ImmutableArray.CreateBuilder<string>();
        foreach (RuleSpec rule in _rules)
        {
            if (rule.Status == RuleStatus.Retired)
            {
                RuleSpec? target = Find(rule.RetiredInto);
                if (target is null || target.Status != RuleStatus.Active)
                {
                    open.Add(rule.Id);
                }
            }
        }

        return open.ToImmutable();
    }

    private void AddAlias(string alias, RuleSpec owner)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Rule " + owner.Id + " has a blank alias.");
        }

        if (_byId.ContainsKey(alias))
        {
            throw new ArgumentException("Alias '" + alias + "' of " + owner.Id + " collides with a rule id.");
        }

        if (!_byAlias.TryAdd(alias, owner))
        {
            throw new ArgumentException("Alias '" + alias + "' is used by " + _byAlias[alias].Id + " and " + owner.Id + ".");
        }
    }

    private static void ValidateRow(RuleSpec rule)
    {
        if (!IsWellFormedId(rule.Id))
        {
            throw new ArgumentException("Malformed rule id '" + rule.Id + "'; expected CV-<AREA>-NN (AREA is 2 to 4 capital letters, NN two digits).");
        }

        if (string.IsNullOrWhiteSpace(rule.Owner))
        {
            throw new ArgumentException("Rule " + rule.Id + " has no owner.");
        }

        ValidateStatus(rule);
        ValidateInputs(rule);
        ValidateClauses(rule);
    }

    private static void ValidateStatus(RuleSpec rule)
    {
        if (rule.Status == RuleStatus.Retired)
        {
            if (rule.HasCheck || rule.RetiredInto.Length == 0)
            {
                throw new ArgumentException("Retired rule " + rule.Id + " must have no check and name the rule it was retired into.");
            }
        }
        else if (rule.RetiredInto.Length != 0)
        {
            throw new ArgumentException("Active or deferred rule " + rule.Id + " must not name a retirement target.");
        }

        if (rule.Status == RuleStatus.Deferred)
        {
            if (rule.HasCheck || string.IsNullOrWhiteSpace(rule.DeferredReason))
            {
                throw new ArgumentException("Deferred rule " + rule.Id + " must have no check and say why it is deferred.");
            }
        }
        else if (rule.DeferredReason.Length != 0)
        {
            throw new ArgumentException("Rule " + rule.Id + " names a deferral reason but is not deferred.");
        }
    }

    private static void ValidateInputs(RuleSpec rule)
    {
        for (int i = 0; i < rule.Inputs.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(rule.Inputs[i]))
            {
                throw new ArgumentException("Rule " + rule.Id + " declares a blank input.");
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(rule.Inputs[i], rule.Inputs[j], StringComparison.Ordinal))
                {
                    throw new ArgumentException("Rule " + rule.Id + " declares input '" + rule.Inputs[i] + "' twice.");
                }
            }
        }
    }

    private static void ValidateClauses(RuleSpec rule)
    {
        for (int i = 0; i < rule.Clauses.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(rule.Clauses[i].Name))
            {
                throw new ArgumentException("Rule " + rule.Id + " has a clause with no name.");
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(rule.Clauses[i].Name, rule.Clauses[j].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Rule " + rule.Id + " has clause '" + rule.Clauses[i].Name + "' twice.");
                }
            }
        }
    }

    /// <summary>True for <c>CV-</c> + 2 to 4 capital letters + <c>-</c> + exactly two digits.</summary>
    private static bool IsWellFormedId(string id)
    {
        if (id is null || !id.StartsWith("CV-", StringComparison.Ordinal))
        {
            return false;
        }

        int dash = id.IndexOf('-', 3);
        if (dash < 0)
        {
            return false;
        }

        int areaLen = dash - 3;
        if (areaLen < 2 || areaLen > 4 || id.Length != dash + 3)
        {
            return false;
        }

        for (int i = 3; i < dash; i++)
        {
            if (id[i] < 'A' || id[i] > 'Z')
            {
                return false;
            }
        }

        return id[dash + 1] >= '0' && id[dash + 1] <= '9' && id[dash + 2] >= '0' && id[dash + 2] <= '9';
    }
}
