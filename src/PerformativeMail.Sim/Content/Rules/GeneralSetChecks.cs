using System.Globalization;
using System.Text;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>
/// The bodies of the General set-level rules CV-GEN-01 (reference clauses), CV-GEN-17 and CV-GEN-18 (content-data story
/// 009). Rule bodies are stateless instance methods (a method group over an instance is not cached by the compiler in a
/// static field, which ADR-0003 section 4 forbids in rule types) that read <c>input.Candidate</c> and report through the context; the registry rows
/// in <see cref="General"/> choose severity and stage. Load-time only, so they allocate freely. Stateless; no statics.
/// </summary>
/// <example>
/// <code>
/// RuleSpec row = RuleSpec.Create&lt;ContentValidationInput&gt;("CV-GEN-18", "content-data-definitions", "", RuleSeverity.Error,
///     ValidationStage.WholeSet, inputs, new GeneralSetChecks().CheckRequiredTables);
/// </code>
/// </example>
internal sealed class GeneralSetChecks
{
    /// <summary>CV-GEN-01, stage 4: every index reference and every pending by-id reference resolves to a def of its declared type.</summary>
    internal void CheckReferences(ContentValidationInput input, RuleContext ctx)
    {
        ContentSet set = input.Candidate;
        CheckKindRefs(set, ctx);
        CheckDestinationRefs(set, ctx);
        CheckMixRefs(set, ctx);
        CheckPending(input, ctx);
    }

    private static void CheckKindRefs(ContentSet set, RuleContext ctx)
    {
        foreach (MailKindDef kind in set.MailKinds.AsSpan())
        {
            DefRef from = new(DefType.MailKind, kind.Id);
            SourceLocation where = ContentValidationInput.LocationOf("mail_kinds", kind.Id);
            for (int i = 0; !kind.AcceptedBy.IsDefaultOrEmpty && i < kind.AcceptedBy.Length; i++)
            {
                CheckIndex(ctx, from, where, "acceptedBy", i, kind.AcceptedBy[i], DefType.DestinationType, set.DestinationTypes.Count, allowNone: false);
            }

            CheckIndex(ctx, from, where, "fallbackKind", -1, kind.FallbackKind, DefType.MailKind, set.MailKinds.Count, allowNone: true);
        }
    }

    private static void CheckDestinationRefs(ContentSet set, RuleContext ctx)
    {
        foreach (DestinationTypeDef destination in set.DestinationTypes.AsSpan())
        {
            DefRef from = new(DefType.DestinationType, destination.Id);
            SourceLocation where = ContentValidationInput.LocationOf("destination_types", destination.Id);
            for (int i = 0; !destination.Accepts.IsDefaultOrEmpty && i < destination.Accepts.Length; i++)
            {
                CheckIndex(ctx, from, where, "accepts", i, destination.Accepts[i], DefType.MailKind, set.MailKinds.Count, allowNone: false);
            }
        }
    }

    private static void CheckMixRefs(ContentSet set, RuleContext ctx)
    {
        foreach (MailMixDef mix in set.MailMixes.AsSpan())
        {
            if (mix.Shifts.IsDefaultOrEmpty)
            {
                continue;
            }

            DefRef from = new(DefType.MailMix, mix.Id);
            SourceLocation where = ContentValidationInput.LocationOf("mail_mixes", mix.Id);
            foreach (ShiftMix shift in mix.Shifts)
            {
                string field = "shifts[" + shift.Shift.ToString(CultureInfo.InvariantCulture) + "].shares";
                for (int i = 0; !shift.Shares.IsDefaultOrEmpty && i < shift.Shares.Length; i++)
                {
                    CheckIndex(ctx, from, where, field, i, shift.Shares[i].Kind, DefType.MailKind, set.MailKinds.Count, allowNone: false);
                }
            }
        }
    }

    private static void CheckIndex(
        RuleContext ctx, DefRef from, SourceLocation where, string field, int position, DefIndex index, DefType target, int count, bool allowNone)
    {
        if (allowNone && index.IsNone)
        {
            return;
        }

        if (!index.IsNone && index.Value < count)
        {
            return;
        }

        string at = position < 0 ? field : field + "[" + position.ToString(CultureInfo.InvariantCulture) + "]";
        string value = at + " = " + (index.IsNone ? "none" : index.Value.ToString(CultureInfo.InvariantCulture));
        ctx.Report(from, where, value, "an index below " + count.ToString(CultureInfo.InvariantCulture) + " into the " + TypeName(target) + " table");
    }

    /// <summary>
    /// Resolves the by-id references. The skip semantics are deliberately mixed, so do not make one match the other:
    /// <list type="bullet">
    /// <item>A pending reference whose expected def type is Container or Item and whose table failed stage 3 (duplicates or
    /// too many defs) is skipped here, individually and silently (uncounted): its table is incomplete, so "missing" would
    /// be a false finding, and CV-GEN-01 does not declare those keys, because declaring them would skip the whole rule.</item>
    /// <item>When a MailKind, DestinationType or MailMix table failed, the runner skips the whole rule and counts it
    /// (the rule declares those three keys), so this method never runs; the same per-reference check is only a guard.</item>
    /// </list>
    /// </summary>
    private static void CheckPending(ContentValidationInput input, RuleContext ctx)
    {
        if (input.References.IsEmpty)
        {
            return;
        }

        ContentSet set = input.Candidate;
        var cache = new Dictionary<DefType, HashSet<string>?>();   // one id set per def type per call
        foreach (PendingReference r in input.References)
        {
            if (TableFailed(input, r.Expected))
            {
                continue;
            }

            HashSet<string>? expected = CachedIds(set, cache, r.Expected);
            if (expected is null || !expected.Contains(r.TargetId))
            {
                ctx.Report(r.From, r.Where, r.Field + " = '" + r.TargetId + "'", AllowedText(set, cache, r));
            }
        }
    }

    private static string AllowedText(ContentSet set, Dictionary<DefType, HashSet<string>?> cache, PendingReference r)
    {
        string allowed = "the id of an existing " + TypeName(r.Expected);
        foreach (DefType other in OtherTypes(r.Expected))
        {
            HashSet<string>? ids = CachedIds(set, cache, other);
            if (ids is not null && ids.Contains(r.TargetId))
            {
                return allowed + "; '" + r.TargetId + "' exists only as " + TypeName(other);
            }
        }

        return allowed;
    }

    private static HashSet<string>? CachedIds(ContentSet set, Dictionary<DefType, HashSet<string>?> cache, DefType type)
    {
        if (!cache.TryGetValue(type, out HashSet<string>? ids))
        {
            ids = IdsOf(set, type);
            cache[type] = ids;
        }

        return ids;
    }

    private static bool TableFailed(ContentValidationInput input, DefType type)
    {
        string key = KeyOf(type);
        foreach (FailedInput f in input.PriorFailures)
        {
            if (string.Equals(f.Input, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static DefType[] OtherTypes(DefType except)
    {
        DefType[] all = { DefType.Container, DefType.Item, DefType.MailKind, DefType.DestinationType, DefType.MailMix };
        var others = new List<DefType>(4);
        foreach (DefType t in all)
        {
            if (t != except)
            {
                others.Add(t);
            }
        }

        return others.ToArray();
    }

    private static HashSet<string>? IdsOf(ContentSet set, DefType type) => type switch
    {
        DefType.Container => IdsOf(set.Containers),
        DefType.Item => IdsOf(set.Items),
        DefType.MailKind => IdsOf(set.MailKinds),
        DefType.DestinationType => IdsOf(set.DestinationTypes),
        DefType.MailMix => IdsOf(set.MailMixes),
        _ => null,
    };

    private static HashSet<string> IdsOf<T>(DefTable<T> table)
        where T : IDef
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (T def in table.AsSpan())
        {
            ids.Add(def.Id);
        }

        return ids;
    }

    private static string KeyOf(DefType type) => type switch
    {
        DefType.Container => ContentInputKeys.Containers,
        DefType.Item => ContentInputKeys.Items,
        DefType.MailKind => ContentInputKeys.MailKinds,
        DefType.DestinationType => ContentInputKeys.DestinationTypes,
        DefType.MailMix => ContentInputKeys.MailMixes,
        _ => string.Empty,
    };

    /// <summary>CV-GEN-17, stage 5: each stat's base, per qualifier, lies inside the stat's own resolved <c>[min, max]</c>.</summary>
    internal void CheckStatBases(ContentValidationInput input, RuleContext ctx)
    {
        ContentSet set = input.Candidate;
        foreach (StatDef def in StatDefs.All())
        {
            int slot = (int)def.Stat - 1;
            StatBounds bounds = slot >= 0 && slot < set.ResolvedStatBounds.Length ? set.ResolvedStatBounds[slot] : def.ResolveBounds(set.Balance);
            CheckStat(def, bounds, set, ctx);
        }
    }

    /// <summary>CV-GEN-17 for one stat row against the given resolved bounds (separate so a test can pass a synthetic row).</summary>
    internal void CheckStat(StatDef def, StatBounds bounds, ContentSet set, RuleContext ctx)
    {
        if (!bounds.IsValid)
        {
            return;   // inverted bounds are CV-GEN-15's finding
        }

        string spelling = Encoding.UTF8.GetString(StatSpelling.ToSpelling(def.Stat));
        string allowed = Num(bounds.Min) + ".." + Num(bounds.Max);
        SourceLocation statLocation = SourceLocation.BuiltIn("builtin/statdef/" + spelling);
        switch (def.Base.Kind)
        {
            case BaseSourceKind.ContainerRows:
                CheckContainerRows(set, bounds, ctx, spelling, allowed);
                break;
            case BaseSourceKind.MailKindMaxStack:
                CheckKindMaxStack(set, bounds, ctx, spelling, allowed);
                break;
            case BaseSourceKind.Balance:
                Check(ctx, true, def.ResolveBase(set.Balance), bounds, DefRef.None, statLocation, "base (" + def.Base.Key + ")", spelling, allowed);
                break;
            case BaseSourceKind.Constant:
                Check(ctx, true, def.Base.Value, bounds, DefRef.None, statLocation, "base (constant)", spelling, allowed);
                break;
            default:
                break;   // an unknown base kind has no value to compare
        }
    }

    private static void CheckContainerRows(ContentSet set, StatBounds bounds, RuleContext ctx, string spelling, string allowed)
    {
        foreach (ContainerDef c in set.Containers.AsSpan())
        {
            Check(ctx, c.HasGrid, c.GridH, bounds, new DefRef(DefType.Container, c.Id), ContentValidationInput.LocationOf("containers", c.Id), "rows", spelling, allowed);
        }
    }

    private static void CheckKindMaxStack(ContentSet set, StatBounds bounds, RuleContext ctx, string spelling, string allowed)
    {
        foreach (MailKindDef k in set.MailKinds.AsSpan())
        {
            Check(ctx, true, k.MaxStack, bounds, new DefRef(DefType.MailKind, k.Id), ContentValidationInput.LocationOf("mail_kinds", k.Id), "maxStack", spelling, allowed);
        }
    }

    private static void Check(
        RuleContext ctx, bool applies, long value, StatBounds bounds, DefRef def, SourceLocation where, string what, string spelling, string allowed)
    {
        if (applies && (value < bounds.Min || value > bounds.Max))
        {
            ctx.Report(def, where, what + " " + Num(value) + " (stat " + spelling + ")", allowed);
        }
    }

    /// <summary>CV-GEN-18, stage 5: one finding per empty table among MailKindDef, MailMixDef, DestinationTypeDef and ContainerDef.</summary>
    internal void CheckRequiredTables(ContentValidationInput input, RuleContext ctx)
    {
        ContentSet set = input.Candidate;
        Empty(ctx, set.MailKinds.Count, "mail_kinds", "MailKindDef");
        Empty(ctx, set.MailMixes.Count, "mail_mixes", "MailMixDef");
        Empty(ctx, set.DestinationTypes.Count, "destination_types", "DestinationTypeDef");
        Empty(ctx, set.Containers.Count, "containers", "ContainerDef");
    }

    private static void Empty(RuleContext ctx, int count, string folder, string typeName)
    {
        if (count == 0)
        {
            ctx.Report(DefRef.None, SourceLocation.BuiltIn("builtin/" + folder), "0 " + typeName + " defs", "at least one " + typeName);
        }
    }

    private static string TypeName(DefType type) => type switch
    {
        DefType.Container => "Container",
        DefType.Item => "Item",
        DefType.MailKind => "MailKind",
        DefType.DestinationType => "DestinationType",
        DefType.MailMix => "MailMix",
        _ => "def",
    };

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
}
