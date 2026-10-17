using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>
/// The bodies of the Mail (mail-destinations) rules CV-MAIL-01 to 06 (content-data story 010). Stateless instance methods
/// that read <c>input.Candidate</c> and report through the context (see <see cref="GridChecks"/> for why they are not
/// static); the rows in <see cref="Mail"/> choose severity and stage. Load-time only, so they allocate freely. Stateless; no statics.
/// </summary>
/// <example>
/// <code>
/// RuleSpec row = RuleSpec.Create&lt;ContentValidationInput&gt;("CV-MAIL-03", "mail-destinations", "", RuleSeverity.Error,
///     ValidationStage.References, inputs, new MailChecks().CheckDeadLetterShifts);
/// </code>
/// </example>
internal sealed class MailChecks
{
    /// <summary>The exact total of one shift's spawn shares, in permille (CV-MAIL-02).</summary>
    internal const int ShareTotalPm = 1_000;

    /// <summary>The largest single share, in permille (CV-MAIL-02).</summary>
    internal const int MaxSharePm = 600;

    /// <summary>CV-MAIL-01, stage 4: <c>acceptedBy</c> names destination types, and the kind and destination lists mirror each other.</summary>
    internal void CheckAcceptance(ContentValidationInput input, RuleContext ctx)
    {
        ContentSet set = input.Candidate;
        ReadOnlySpan<MailKindDef> kinds = set.MailKinds.AsSpan();
        ReadOnlySpan<DestinationTypeDef> destinations = set.DestinationTypes.AsSpan();
        for (int k = 0; k < kinds.Length; k++)
        {
            MailKindDef kind = kinds[k];
            DefRef from = new(DefType.MailKind, kind.Id);
            SourceLocation where = ContentValidationInput.LocationOf("mail_kinds", kind.Id);
            foreach (DefIndex d in Listed(kind.AcceptedBy))
            {
                if (d.IsNone || d.Value >= destinations.Length)
                {
                    ctx.Report(from, where, "acceptedBy contains " + IndexText(d), "an index below " + Num(destinations.Length) + " into the DestinationType table");
                }
                else if (!Contains(destinations[d.Value].Accepts, k))
                {
                    ctx.Report(from, where, "acceptedBy lists '" + destinations[d.Value].Id + "', which does not accept '" + kind.Id + "'", "acceptedBy mirrors DestinationTypeDef.accepts");
                }
            }
        }

        for (int d = 0; d < destinations.Length; d++)
        {
            ReportUnlisted(ctx, destinations[d], d, kinds);
        }
    }

    private static void ReportUnlisted(RuleContext ctx, DestinationTypeDef destination, int destinationIndex, ReadOnlySpan<MailKindDef> kinds)
    {
        foreach (DefIndex k in Listed(destination.Accepts))
        {
            if (!k.IsNone && k.Value < kinds.Length && !Contains(kinds[k.Value].AcceptedBy, destinationIndex))
            {
                ctx.Report(
                    new DefRef(DefType.DestinationType, destination.Id), ContentValidationInput.LocationOf("destination_types", destination.Id),
                    "accepts '" + kinds[k.Value].Id + "', whose acceptedBy omits '" + destination.Id + "'", "accepts mirrors MailKindDef.acceptedBy");
            }
        }
    }

    /// <summary>CV-MAIL-02, stage 5: each shift's shares sum to exactly 1 000 and each share is 0 to 600.</summary>
    internal void CheckShares(ContentValidationInput input, RuleContext ctx)
    {
        foreach (MailMixDef mix in input.Candidate.MailMixes.AsSpan())
        {
            if (mix.Shifts.IsDefaultOrEmpty)
            {
                continue;   // an empty mix passes here; CV-GEN-18 fails an empty table
            }

            DefRef from = new(DefType.MailMix, mix.Id);
            SourceLocation where = ContentValidationInput.LocationOf("mail_mixes", mix.Id);
            foreach (ShiftMix shift in mix.Shifts)
            {
                CheckShift(ctx, from, where, shift);
            }
        }
    }

    private static void CheckShift(RuleContext ctx, DefRef from, SourceLocation where, ShiftMix shift)
    {
        long sum = 0;
        string label = "shift " + Num(shift.Shift);
        foreach (MailShare share in shift.Shares.IsDefault ? ImmutableArray<MailShare>.Empty : shift.Shares)
        {
            sum += share.SharePm;
            if (share.SharePm < 0 || share.SharePm > MaxSharePm)
            {
                ctx.Report(from, where, label + " share " + Num(share.SharePm), "each share 0 to " + Num(MaxSharePm));
            }
        }

        if (sum != ShareTotalPm)
        {
            ctx.Report(from, where, label + " shares sum to " + Num(sum), "exactly " + Num(ShareTotalPm));
        }
    }

    /// <summary>CV-MAIL-03, stage 2 (runs at stage 4): <c>deadLetterShifts</c> at least 1.</summary>
    internal void CheckDeadLetterShifts(ContentValidationInput input, RuleContext ctx)
    {
        int shifts = input.Candidate.Mail.DeadLetterShifts;
        if (shifts < 1)
        {
            ctx.Report(DefRef.None, SourceLocation.BuiltIn("builtin/balance/mail"), "mail.deadLetterShifts " + Num(shifts), "at least 1 (0 would make every unit dead on arrival)");
        }
    }

    /// <summary>
    /// CV-MAIL-04, stage 5: every kind with a share above 0 in some shift has an accepting destination type, or a
    /// <c>fallbackKind</c> chain that reaches a kind that has one, without a cycle.
    /// </summary>
    internal void CheckAcceptors(ContentValidationInput input, RuleContext ctx)
    {
        ContentSet set = input.Candidate;
        ReadOnlySpan<MailKindDef> kinds = set.MailKinds.AsSpan();
        bool[] spawned = SpawnedKinds(set, kinds.Length);
        bool[] accepted = AcceptedKinds(set, kinds.Length);
        for (int k = 0; k < kinds.Length; k++)
        {
            if (spawned[k] && !accepted[k])
            {
                CheckChain(ctx, kinds, accepted, k);
            }
        }
    }

    private static void CheckChain(RuleContext ctx, ReadOnlySpan<MailKindDef> kinds, bool[] accepted, int start)
    {
        MailKindDef kind = kinds[start];
        DefRef from = new(DefType.MailKind, kind.Id);
        SourceLocation where = ContentValidationInput.LocationOf("mail_kinds", kind.Id);
        DefIndex next = kind.FallbackKind;
        if (next.IsNone)
        {
            ctx.Report(from, where, "'" + kind.Id + "' has share above 0, no accepting destination type and no fallbackKind", "an accepting destination type or a fallbackKind");
            return;
        }

        for (int steps = 0; steps <= kinds.Length; steps++)
        {
            if (next.IsNone)
            {
                ctx.Report(from, where, "fallbackKind chain of '" + kind.Id + "' ends at a kind with no accepting destination type", "a chain that reaches an accepting destination type");
                return;
            }

            if (next.Value >= kinds.Length || accepted[next.Value])
            {
                return;   // an unknown index is CV-GEN-01's finding; an accepted kind ends the chain successfully
            }

            next = kinds[next.Value].FallbackKind;
        }

        ctx.Report(from, where, "fallbackKind chain of '" + kind.Id + "' cycles", "a fallbackKind chain without a cycle");
    }

    private static bool[] SpawnedKinds(ContentSet set, int kindCount)
    {
        bool[] spawned = new bool[kindCount];
        foreach (MailMixDef mix in set.MailMixes.AsSpan())
        {
            foreach (ShiftMix shift in mix.Shifts.IsDefault ? ImmutableArray<ShiftMix>.Empty : mix.Shifts)
            {
                foreach (MailShare share in shift.Shares.IsDefault ? ImmutableArray<MailShare>.Empty : shift.Shares)
                {
                    if (share.SharePm > 0 && !share.Kind.IsNone && share.Kind.Value < kindCount)
                    {
                        spawned[share.Kind.Value] = true;
                    }
                }
            }
        }

        return spawned;
    }

    private static bool[] AcceptedKinds(ContentSet set, int kindCount)
    {
        bool[] accepted = new bool[kindCount];
        foreach (DestinationTypeDef destination in set.DestinationTypes.AsSpan())
        {
            foreach (DefIndex k in Listed(destination.Accepts))
            {
                if (!k.IsNone && k.Value < kindCount)
                {
                    accepted[k.Value] = true;
                }
            }
        }

        return accepted;
    }

    /// <summary>CV-MAIL-05, stage 5: <c>batchJitter</c> below half of <c>batchInterval</c>.</summary>
    internal void CheckJitter(ContentValidationInput input, RuleContext ctx)
    {
        foreach (MailMixDef mix in input.Candidate.MailMixes.AsSpan())
        {
            if ((long)mix.BatchJitterMs * 2 >= mix.BatchIntervalMs)
            {
                ctx.Report(
                    new DefRef(DefType.MailMix, mix.Id), ContentValidationInput.LocationOf("mail_mixes", mix.Id),
                    "batchJitter " + Num(mix.BatchJitterMs) + " ms, batchInterval " + Num(mix.BatchIntervalMs) + " ms",
                    "batchJitter below batchInterval / 2");
            }
        }
    }

    /// <summary>CV-MAIL-06, stage 5: <c>tailGuard</c> at most <c>batchInterval</c> minus <c>batchJitter</c>.</summary>
    internal void CheckTailGuard(ContentValidationInput input, RuleContext ctx)
    {
        foreach (MailMixDef mix in input.Candidate.MailMixes.AsSpan())
        {
            long room = (long)mix.BatchIntervalMs - mix.BatchJitterMs;
            if (mix.TailGuardMs > room)
            {
                ctx.Report(
                    new DefRef(DefType.MailMix, mix.Id), ContentValidationInput.LocationOf("mail_mixes", mix.Id),
                    "tailGuard " + Num(mix.TailGuardMs) + " ms, batchInterval " + Num(mix.BatchIntervalMs) + " ms, batchJitter " + Num(mix.BatchJitterMs) + " ms",
                    "tailGuard at most " + Num(room) + " ms (batchInterval - batchJitter)");
            }
        }
    }

    private static ImmutableArray<DefIndex> Listed(ImmutableArray<DefIndex> list) => list.IsDefault ? ImmutableArray<DefIndex>.Empty : list;

    private static bool Contains(ImmutableArray<DefIndex> list, int ordinal)
    {
        foreach (DefIndex i in Listed(list))
        {
            if (!i.IsNone && i.Value == ordinal)
            {
                return true;
            }
        }

        return false;
    }

    private static string IndexText(DefIndex i) => i.IsNone ? "none" : Num(i.Value);

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
}
