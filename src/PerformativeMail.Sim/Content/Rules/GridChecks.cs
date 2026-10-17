using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>
/// The bodies of the Grid (grid-inventory) rules CV-GRD-01 to 04, 06 and 07 (content-data story 010). Rule bodies are
/// stateless instance methods (a method group over an instance is not cached by the compiler in a static field, which
/// ADR-0003 section 4 forbids in rule types) that read <c>input.Candidate</c> and report through the context; the rows in
/// <see cref="Grid"/> choose severity and stage. Load-time only, so they allocate freely, except that CV-GRD-01 passes
/// spans (no list copies) to F5. The only state is the optional synthetic <c>MailMaxStack</c> Mul list of
/// <see cref="GridChecks(ImmutableArray{int})"/>, which is immutable.
/// </summary>
/// <example>
/// <code>
/// RuleSpec row = RuleSpec.Create&lt;ContentValidationInput&gt;("CV-GRD-03", "grid-inventory", "Grid (Dependencies)", RuleSeverity.Error,
///     ValidationStage.References, inputs, new GridChecks(default).CheckAllowedCategories);
/// </code>
/// </example>
internal sealed class GridChecks
{
    /// <summary>The largest <c>MailMaxStack</c> (entry <c>count</c> is a <c>uint8</c>), CV-GRD-01's limit.</summary>
    internal const int MaxEntryCount = 255;

    /// <summary>The shortest world item lifetime that does not make thrown mail vanish before a catch, in ms (30 s), CV-GRD-07's limit.</summary>
    internal const int MinDespawnMs = 30_000;

    private readonly ImmutableArray<int> _mailMaxStackMuls;

    /// <summary>Creates the bodies with the Mul sources that can be live together on <c>MailMaxStack</c> (permille).</summary>
    /// <param name="mailMaxStackMuls">
    /// Every <c>MailMaxStack</c> Mul unit that can be live at once. At M-1a there are no perk sources, so production uses
    /// the default (empty) list; a fixture passes synthetic sources (for example one 2000 for a x2.0 Bulk Mailer).
    /// </param>
    public GridChecks(ImmutableArray<int> mailMaxStackMuls)
    {
        _mailMaxStackMuls = mailMaxStackMuls.IsDefault ? ImmutableArray<int>.Empty : mailMaxStackMuls;
    }

    /// <summary>CV-GRD-01, stage 5: each mail kind's <c>maxStack</c> times its worst-case <c>MailMaxStack</c> Mul (F5) is at most 255.</summary>
    internal void CheckMaxStackWorstCase(ContentValidationInput input, RuleContext ctx)
    {
        StatRounding rounding = StatDefs.Get(Stat.MailMaxStack).Rounding;
        foreach (MailKindDef kind in input.Candidate.MailKinds.AsSpan())
        {
            WorstCaseResult worst = WorstCase.High(rounding, kind.MaxStack, ReadOnlySpan<int>.Empty, _mailMaxStackMuls.AsSpan(), ReadOnlySpan<int>.Empty, 0);
            if (worst.Status != WorstCaseStatus.Ok)
            {
                continue;   // too many live Mul factors is CV-PRK-10's finding; Value is 0 and not a result
            }

            if (worst.Value > MaxEntryCount)
            {
                ctx.Report(
                    new DefRef(DefType.MailKind, kind.Id), ContentValidationInput.LocationOf("mail_kinds", kind.Id),
                    "maxStack " + Num(kind.MaxStack) + " gives worst-case MailMaxStack " + Num(worst.Value),
                    "worst-case MailMaxStack at most " + Num(MaxEntryCount));
            }
        }
    }

    /// <summary>CV-GRD-02, stage 5: a <c>handCarried</c> container whose categories admit a <c>bulk</c>-class item or mail kind.</summary>
    internal void CheckHandCarriedBulk(ContentValidationInput input, RuleContext ctx)
    {
        ContentSet set = input.Candidate;
        foreach (ContainerDef container in set.Containers.AsSpan())
        {
            if (!container.HandCarried)
            {
                continue;
            }

            string? bulk = FirstAdmittedBulk(set, container);
            if (bulk is not null)
            {
                ctx.Report(
                    new DefRef(DefType.Container, container.Id), ContentValidationInput.LocationOf("containers", container.Id),
                    "handCarried container admits bulk " + bulk, "allowedCategories that admit no bulk-class def");
            }
        }
    }

    private static string? FirstAdmittedBulk(ContentSet set, ContainerDef container)
    {
        foreach (ItemDef item in set.Items.AsSpan())
        {
            if (item.WeightClass == WeightClass.Bulk && Admits(container, item.Category))
            {
                return "item '" + item.Id + "'";
            }
        }

        foreach (MailKindDef kind in set.MailKinds.AsSpan())
        {
            if (kind.WeightClass == WeightClass.Bulk && Admits(container, ItemCategory.Mail))
            {
                return "mail kind '" + kind.Id + "'";
            }
        }

        return null;
    }

    private static bool Admits(ContainerDef container, ItemCategory category)
    {
        if (!container.HasAllowedCategories)
        {
            return true;   // omitted means any category
        }

        return !container.AllowedCategories.IsDefaultOrEmpty && container.AllowedCategories.Contains(category);
    }

    /// <summary>CV-GRD-03, stage 2 (runs at stage 4, see <see cref="Grid"/>): <c>allowedCategories</c> present but empty.</summary>
    internal void CheckAllowedCategories(ContentValidationInput input, RuleContext ctx)
    {
        foreach (ContainerDef container in input.Candidate.Containers.AsSpan())
        {
            if (container.HasAllowedCategories && container.AllowedCategories.IsDefaultOrEmpty)
            {
                ctx.Report(
                    new DefRef(DefType.Container, container.Id), ContentValidationInput.LocationOf("containers", container.Id),
                    "allowedCategories = []", "omit allowedCategories for any category, or list at least one");
            }
        }
    }

    /// <summary>CV-GRD-04, stage 2 (runs at stage 4): a grid or slots container with W or H below 1, and any <c>maxStack</c> below 1.</summary>
    internal void CheckDimensions(ContentValidationInput input, RuleContext ctx)
    {
        ContentSet set = input.Candidate;
        foreach (ContainerDef container in set.Containers.AsSpan())
        {
            if (container.Layout != ContainerLayout.Pile && (container.GridW < 1 || container.GridH < 1))
            {
                ctx.Report(
                    new DefRef(DefType.Container, container.Id), ContentValidationInput.LocationOf("containers", container.Id),
                    "grid " + Num(container.GridW) + "x" + Num(container.GridH), "W and H at least 1");
            }
        }

        foreach (ItemDef item in set.Items.AsSpan())
        {
            ReportMaxStack(ctx, new DefRef(DefType.Item, item.Id), ContentValidationInput.LocationOf("items", item.Id), item.MaxStack);
        }

        foreach (MailKindDef kind in set.MailKinds.AsSpan())
        {
            ReportMaxStack(ctx, new DefRef(DefType.MailKind, kind.Id), ContentValidationInput.LocationOf("mail_kinds", kind.Id), kind.MaxStack);
        }
    }

    private static void ReportMaxStack(RuleContext ctx, DefRef def, SourceLocation where, int maxStack)
    {
        if (maxStack < 1)
        {
            ctx.Report(def, where, "maxStack " + Num(maxStack), "maxStack at least 1");
        }
    }

    /// <summary>CV-GRD-06, stage 5 (Warning): <c>capEvictMinAgeSeconds</c> at or above <c>worldItemDespawnSeconds</c> leaves nothing evictable.</summary>
    internal void CheckEvictAgeBelowDespawn(ContentValidationInput input, RuleContext ctx)
    {
        InventoryBalance inventory = input.Candidate.Inventory;
        if (inventory.CapEvictMinAgeSeconds >= inventory.WorldItemDespawnSeconds)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn("builtin/balance/inventory"),
                "inventory.capEvictMinAgeSeconds " + Num(inventory.CapEvictMinAgeSeconds) + " ms, inventory.worldItemDespawnSeconds " + Num(inventory.WorldItemDespawnSeconds) + " ms",
                "inventory.capEvictMinAgeSeconds below inventory.worldItemDespawnSeconds");
        }
    }

    /// <summary>CV-GRD-07, stage 2 (runs at stage 4, Warning): <c>worldItemDespawnSeconds</c> below 30 s.</summary>
    internal void CheckDespawnAtLeast30Seconds(ContentValidationInput input, RuleContext ctx)
    {
        int despawn = input.Candidate.Inventory.WorldItemDespawnSeconds;
        if (despawn < MinDespawnMs)
        {
            ctx.Report(
                DefRef.None, SourceLocation.BuiltIn("builtin/balance/inventory"),
                "inventory.worldItemDespawnSeconds " + Num(despawn) + " ms", "at least " + Num(MinDespawnMs) + " ms (thrown mail must outlive a catch)");
        }
    }

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
}
