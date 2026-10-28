using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// An editable, id-addressed draft of a content set for the validator fixtures of content-data stories 009 to 012:
/// the unsorted def lists plus the balance entries. Copy-on-write (every edit returns a new draft), so one
/// <see cref="ContentFixtures.MinimalValid"/> base serves many fixtures.
/// </summary>
/// <example>
/// <code>
/// ContentDraft bad = ContentFixtures.MinimalValid()
///     .Replace(ContentFixtures.Letter with { MaxStack = 0 })     // replace the def with id "letter"
///     .WithBalance("throw.rangeMinPermille", 1501);              // replace one balance value by key
/// ContentBuildResult r = bad.Build();
/// </code>
/// </example>
internal sealed record ContentDraft(
    ImmutableArray<BalanceEntry> Balance,
    ImmutableArray<ContainerDef> Containers,
    ImmutableArray<ItemDef> Items,
    ImmutableArray<MailKindDef> MailKinds,
    ImmutableArray<DestinationTypeDef> DestinationTypes,
    ImmutableArray<MailMixDef> MailMixes)
{
    /// <summary>
    /// The interaction verb declarations handed to CV-PLR-13 (PCM-P10-1); empty by default, as production content is at M-1a
    /// (no def type carries a verb field yet). Accumulated by <see cref="WithVerbs"/>.
    /// </summary>
    public ImmutableArray<VerbDeclaration> VerbDeclarations { get; init; } = ImmutableArray<VerbDeclaration>.Empty;

    /// <summary>
    /// Appends a verb declaration attributed to the destination type <paramref name="defId"/> (located at
    /// <c>builtin/destination_types/&lt;id&gt;</c>), carrying the raw spellings <paramref name="verbs"/> (none means the def
    /// declares the field but lists no verb).
    /// </summary>
    public ContentDraft WithVerbs(string defId, params string[] verbs) => this with
    {
        VerbDeclarations = VerbDeclarations.Add(new VerbDeclaration(
            new DefRef(DefType.DestinationType, defId),
            ContentValidationInput.LocationOf("destination_types", defId),
            ImmutableArray.Create(verbs))),
    };

    /// <summary>Replaces the one balance entry named <paramref name="key"/> (<c>owner.key</c>) by a value without a safe annotation.</summary>
    public ContentDraft WithBalance(string key, long value)
    {
        int i = Balance.FindIndex(e => e.Name == key);
        if (i < 0)
        {
            throw new ArgumentException("No balance entry " + key, nameof(key));
        }

        return this with { Balance = Balance.SetItem(i, Balance[i] with { Value = value, Safe = null }) };
    }

    /// <summary>Replaces the def whose type and <see cref="IDef.Id"/> match <paramref name="def"/>; throws when there is none.</summary>
    public ContentDraft Replace(ContainerDef def) => this with { Containers = Swap(Containers, def) };

    /// <inheritdoc cref="Replace(ContainerDef)"/>
    public ContentDraft Replace(ItemDef def) => this with { Items = Swap(Items, def) };

    /// <inheritdoc cref="Replace(ContainerDef)"/>
    public ContentDraft Replace(MailKindDef def) => this with { MailKinds = Swap(MailKinds, def) };

    /// <inheritdoc cref="Replace(ContainerDef)"/>
    public ContentDraft Replace(DestinationTypeDef def) => this with { DestinationTypes = Swap(DestinationTypes, def) };

    /// <inheritdoc cref="Replace(ContainerDef)"/>
    public ContentDraft Replace(MailMixDef def) => this with { MailMixes = Swap(MailMixes, def) };

    /// <summary>Adds a def (a second def with the same id is reported by the build as CV-GEN-01).</summary>
    public ContentDraft Add(MailKindDef def) => this with { MailKinds = MailKinds.Add(def) };

    /// <inheritdoc cref="Add(MailKindDef)"/>
    public ContentDraft Add(ItemDef def) => this with { Items = Items.Add(def) };

    /// <inheritdoc cref="Add(MailKindDef)"/>
    public ContentDraft Add(ContainerDef def) => this with { Containers = Containers.Add(def) };

    /// <summary>Empties the container table.</summary>
    public ContentDraft WithoutContainers() => this with { Containers = ImmutableArray<ContainerDef>.Empty };

    /// <summary>Empties the item table.</summary>
    public ContentDraft WithoutItems() => this with { Items = ImmutableArray<ItemDef>.Empty };

    /// <summary>Empties the mail kind table.</summary>
    public ContentDraft WithoutMailKinds() => this with { MailKinds = ImmutableArray<MailKindDef>.Empty };

    /// <summary>Empties the destination type table.</summary>
    public ContentDraft WithoutDestinationTypes() => this with { DestinationTypes = ImmutableArray<DestinationTypeDef>.Empty };

    /// <summary>Empties the mail mix table.</summary>
    public ContentDraft WithoutMailMixes() => this with { MailMixes = ImmutableArray<MailMixDef>.Empty };

    /// <summary>Removes the mail kind with id <paramref name="id"/>; throws when there is none.</summary>
    public ContentDraft RemoveMailKind(string id) => this with { MailKinds = Remove(MailKinds, id) };

    /// <summary>Builds the balance table from the entries; throws when it does not validate.</summary>
    public BalanceTable BuildBalance(RuleRegistry? rules = null)
    {
        BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), Balance, rules);
        return r.Table ?? throw new InvalidOperationException(
            "The draft balance does not validate: " + string.Join("; ", r.Validation.Diagnostics.Select(d => d.RuleId + " " + d.InputKey + " " + d.Value)));
    }

    /// <summary>Builds the candidate through the production pipeline and runs the rules (default: every registered rule), passing the draft's <see cref="VerbDeclarations"/>.</summary>
    public ContentBuildResult Build(RuleRegistry? rules = null, ImmutableArray<PendingReference> references = default) =>
        ContentPipeline.Build(
            BuildBalance(rules), Containers, Items, MailKinds, DestinationTypes, MailMixes, rules ?? RuleRegistry.CreateDefault(), references, VerbDeclarations);

    /// <summary>Builds the adapter over the candidate (even a candidate that fails its rules), for tests that call a rule body directly; carries the draft's <see cref="VerbDeclarations"/>.</summary>
    public ContentValidationInput Input(ImmutableArray<FailedInput> priorFailures = default)
    {
        BalanceTable balance = BuildBalance();
        var set = new ContentSet(
            balance,
            ImmutableArray<PerformativeMail.Sim.Content.Stats.StatBounds>.Empty,
            ContentPipeline.Sorted(Containers),
            ContentPipeline.Sorted(Items),
            ContentPipeline.Sorted(MailKinds),
            ContentPipeline.Sorted(DestinationTypes),
            ContentPipeline.Sorted(MailMixes));
        return new ContentValidationInput(set, priorFailures, default, default, VerbDeclarations);
    }

    private static ImmutableArray<T> Swap<T>(ImmutableArray<T> list, T def)
        where T : IDef
    {
        int i = list.FindIndex(d => d.Id == def.Id);
        return i < 0 ? throw new ArgumentException("No " + typeof(T).Name + " with id " + def.Id, nameof(def)) : list.SetItem(i, def);
    }

    private static ImmutableArray<MailKindDef> Remove(ImmutableArray<MailKindDef> list, string id)
    {
        int i = list.FindIndex(d => d.Id == id);
        return i < 0 ? throw new ArgumentException("No mail kind " + id, nameof(id)) : list.RemoveAt(i);
    }
}

/// <summary>Index helpers over immutable arrays (the BCL has none).</summary>
internal static class ImmutableArrayFind
{
    public static int FindIndex<T>(this ImmutableArray<T> list, Func<T, bool> match)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (match(list[i]))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Minimal valid content: the smallest set that passes every registered rule, plus the defs the overlay edits.</summary>
internal static class ContentFixtures
{
    /// <summary>The one mail kind of <see cref="MinimalValid"/> (id <c>letter</c>).</summary>
    public static MailKindDef Letter => new()
    {
        Id = "letter", Name = "mail.letter", GridW = 1, GridH = 1, MaxStack = 20, BaseValueCents = 8, WeightClass = WeightClass.Light,
        CarryByHand = true, BeltLanes = 1, BeltLengthMm = 500, AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(0)),
        ComplaintOnMisdeliveryMilliPoints = 5_000, UnlockShift = 1,
    };

    /// <summary>The one destination type of <see cref="MinimalValid"/> (id <c>house</c>, accepts <c>letter</c>).</summary>
    public static DestinationTypeDef House => new()
    {
        Id = "house", Accepts = ImmutableArray.Create(DefIndex.FromValue(0)), InsertRateMilliPerSec = 2_000,
        ManualInsertHoldMs = 400, MaxAutomatedFeeders = 1, CatchVolume = new CatchVolume(1_000, 800, 1_000, 1_000, 0),
    };

    /// <summary>The one container of <see cref="MinimalValid"/> (id <c>cursor</c>).</summary>
    public static ContainerDef Cursor => new()
    {
        Id = "cursor", Layout = ContainerLayout.Slots, HasGrid = true, GridW = 1, GridH = 1, View = ContainerView.Auto, HandCarried = true,
        AcceptsInsert = true,
    };

    /// <summary>The one item of <see cref="MinimalValid"/> (id <c>plank</c>).</summary>
    public static ItemDef Plank => new()
    {
        Id = "plank", Name = "item.plank", Category = ItemCategory.Material, GridW = 1, GridH = 1, MaxStack = 20, WeightClass = WeightClass.Light,
        CarryByHand = true,
    };

    /// <summary>The one mix of <see cref="MinimalValid"/>: shift 1 is all letters (600 + 400 permille).</summary>
    public static MailMixDef Mix => new()
    {
        Id = "default",
        // Shift 1 is all letters, split over two entries: CV-MAIL-02 caps one share at 600 and the minimal set has one kind.
        Shifts = ImmutableArray.Create(new ShiftMix(1, ImmutableArray.Create(
            new MailShare(DefIndex.FromValue(0), 600), new MailShare(DefIndex.FromValue(0), 400)))),
        BatchIntervalMs = 15_000, BatchJitterMs = 3_000, TailGuardMs = 9_000, SpawnOverheadPm = 1_600,
        StreetStreakRatioPm = 300, RaidSpawnRateRatioPm = 500, DistanceStepRatioPm = 250, ShiftStepRatioPm = 100,
    };

    /// <summary>One def of each type and the full M-1a default balance: valid against every registered rule.</summary>
    /// <example><code>ContentBuildResult r = ContentFixtures.MinimalValid().Build();   // r.Set is not null</code></example>
    public static ContentDraft MinimalValid() => new(
        BalanceKeys.CreateDefaultEntries(),
        ImmutableArray.Create(Cursor),
        ImmutableArray.Create(Plank),
        ImmutableArray.Create(Letter),
        ImmutableArray.Create(House),
        ImmutableArray.Create(Mix));
}
