using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The code factory for the M-1a greybox content set (ADR-0010 section 4). It constructs the same types the M0 JSONC
/// loader will produce (def records, a built <see cref="BalanceTable"/>) and runs the stage 4 to 5 rule runner over them
/// through <see cref="ContentPipeline"/>, so the first gameplay epics read real content before the loader exists. Values
/// are the owner GDDs' (Grid Inventory R1 and AC H-R1-1, Mail and Destinations R1/R3/F3, the Tuning Knobs tables) stored as
/// already-scaled integers (CD4); nothing is held in a static, so every call builds a fresh, equal set. Thread-safe.
/// </summary>
/// <example>
/// <code>
/// ContentSet content = BuiltInContent.CreateM1a();   // call once per process, pass the reference on
/// DefTable&lt;MailKindDef&gt; kinds = content.MailKinds;
/// </code>
/// </example>
public static class BuiltInContent
{
    /// <summary>Builds, validates and returns the M-1a set.</summary>
    /// <returns>The accepted set.</returns>
    /// <exception cref="InvalidOperationException">The built-in data fails its own validation (a programming error; the message lists the findings).</exception>
    public static ContentSet CreateM1a()
    {
        RuleRegistry rules = RuleRegistry.CreateDefault();
        ContentBuildResult r = BuildM1a(rules);
        return r.Set ?? throw new InvalidOperationException(
            "The M-1a built-in content does not validate: " + Describe(r.Validation, rules));
    }

    /// <summary>
    /// Builds the M-1a set and returns the pipeline result without throwing, for tests that run the factory against a
    /// chosen rule registry.
    /// </summary>
    /// <param name="rules">The rule registry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The built-in balance does not build (the message lists the findings).</exception>
    /// <exception cref="ArgumentException">See <see cref="ContentPipeline.Build"/> (ids, missing rule or balance rows).</exception>
    /// <exception cref="OverflowException">See <see cref="ContentPipeline.Build"/>.</exception>
    public static ContentBuildResult BuildM1a(RuleRegistry rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        BalanceBuildResult balance = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries(), rules);
        if (balance.Table is null)
        {
            throw new InvalidOperationException("The M-1a built-in balance does not validate: " + Describe(balance.Validation, rules));
        }

        return Build(balance.Table, M1aContainers(), M1aItems(), M1aMailKinds(), M1aDestinationTypes(), rules);
    }

    /// <summary>
    /// Builds a set from a given balance table and lists, with the single M-1a mail mix referencing the kinds by id.
    /// </summary>
    internal static ContentBuildResult Build(
        BalanceTable balance,
        ImmutableArray<ContainerDef> containers,
        ImmutableArray<ItemDef> items,
        ImmutableArray<MailKindDef> kinds,
        ImmutableArray<DestinationTypeDef> destinations,
        RuleRegistry rules) =>
        ContentPipeline.Build(balance, containers, items, kinds, destinations, ImmutableArray.Create(M1aMailMix(kinds)), rules);

    /// <summary>Formats the findings of a failed build, one per line.</summary>
    internal static string Describe(ValidationResult result, RuleRegistry rules)
    {
        var sb = new System.Text.StringBuilder();
        foreach (ContentDiagnostic d in result.Diagnostics)
        {
            sb.Append('\n').Append(d.Format(rules));
        }

        return sb.ToString();
    }

    /// <summary>The M-1a containers (Grid Inventory R1, AC H-R1-1). Unsorted; the pipeline orders them by id.</summary>
    internal static ImmutableArray<ContainerDef> M1aContainers() => ImmutableArray.Create(
        new ContainerDef
        {
            Id = "hotbar", Layout = ContainerLayout.Slots, HasGrid = true, GridW = 8, GridH = 1, View = ContainerView.Grid,
            BeltAccess = BeltAccess.None, HandCarried = true, AcceptsInsert = true, ReservedSlots = ImmutableArray.Create(0),
        },
        new ContainerDef
        {
            Id = "player_inventory", Layout = ContainerLayout.Grid, HasGrid = true, GridW = 8, GridH = 2, View = ContainerView.Grid,
            BeltAccess = BeltAccess.None, HandCarried = true, AcceptsInsert = true,
        },
        new ContainerDef
        {
            Id = "po_intake", Layout = ContainerLayout.Grid, HasGrid = true, GridW = 20, GridH = 16, View = ContainerView.Manifest,
            BeltAccess = BeltAccess.OutfeedOnly, AcceptsInsert = true, HasAllowedCategories = true,
            AllowedCategories = ImmutableArray.Create(ItemCategory.Mail),
        },
        new ContainerDef
        {
            // GDD R1: the cursor accepts "any hand-carryable", so HandCarried is true (the story's table showed "-").
            Id = "cursor", Layout = ContainerLayout.Slots, HasGrid = true, GridW = 1, GridH = 1, View = ContainerView.Auto,
            BeltAccess = BeltAccess.None, HandCarried = true, AcceptsInsert = true,
        });

    /// <summary>The M-1a items (grid-inventory fixture table). No buy price; sell price 0 (Economy owns prices).</summary>
    internal static ImmutableArray<ItemDef> M1aItems() => ImmutableArray.Create(
        Item("axe", ItemCategory.Tool, 1, 2, 1),
        Item("plank", ItemCategory.Material, 1, 1, 20),
        Item("fiber", ItemCategory.Material, 1, 1, 20),
        Item("log", ItemCategory.Material, 1, 2, 10));

    private static ItemDef Item(string id, ItemCategory category, int w, int h, int maxStack) => new()
    {
        Id = id, Name = "item." + id, Category = category, GridW = w, GridH = h, MaxStack = maxStack,
        WeightClass = WeightClass.Light, CarryByHand = true,
    };

    /// <summary>The M-1a mail kinds (Mail R1). Every kind is accepted by <c>house</c>, which is the only destination type, DefIndex 0.</summary>
    internal static ImmutableArray<MailKindDef> M1aMailKinds() => ImmutableArray.Create(
        Kind("postcard", 1, 1, 40, 4, WeightClass.Light, 3_000, 2),
        Kind("letter", 1, 1, 20, 8, WeightClass.Light, 5_000, 1),
        Kind("small", 1, 2, 1, 30, WeightClass.Light, 8_000, 1),
        Kind("medium", 2, 2, 1, 70, WeightClass.Medium, 12_000, 1),
        Kind("large", 2, 4, 1, 160, WeightClass.Heavy, 16_000, 2));

    // Every mail item occupies one 0.5 m slot of one lane (spec 04 belts: only cargo, deferred, takes two lanes).
    private static MailKindDef Kind(string id, int w, int h, int maxStack, long cents, WeightClass weight, int complaint, int unlockShift) => new()
    {
        Id = id, Name = "mail." + id, GridW = w, GridH = h, MaxStack = maxStack, BaseValueCents = cents, WeightClass = weight,
        CarryByHand = true, BeltLanes = 1, BeltLengthMm = 500, DeadlineOffsetShifts = 0,
        AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(0)),
        ComplaintOnMisdeliveryMilliPoints = complaint, UnlockShift = unlockShift,
    };

    /// <summary>The M-1a destination types: <c>house</c> accepts all five kinds (indices resolved from the kind ids).</summary>
    internal static ImmutableArray<DestinationTypeDef> M1aDestinationTypes()
    {
        ImmutableArray<DefIndex> all = KindIndices("large", "letter", "medium", "postcard", "small");
        return ImmutableArray.Create(new DestinationTypeDef
        {
            Id = "house", Accepts = all, InsertRateMilliPerSec = 2_000, ManualInsertHoldMs = 400, MaxAutomatedFeeders = 1,
            CatchVolume = new CatchVolume(1_000, 800, 1_000, 1_000, 0),
        });
    }

    /// <summary>The single M-1a mail mix (Mail F3, spec 08 section 2.3). Kind references resolve through the kinds' id order.</summary>
    internal static MailMixDef M1aMailMix(ImmutableArray<MailKindDef> kinds)
    {
        ImmutableArray<string> sorted = AssignKinds(kinds).SortedIds;
        DefIndex Idx(string id) => KindIndex(sorted, id);
        return new MailMixDef
        {
            Id = "default",
            Shifts = ImmutableArray.Create(
                new ShiftMix(1, ImmutableArray.Create(
                    new MailShare(Idx("letter"), 600), new MailShare(Idx("small"), 300), new MailShare(Idx("medium"), 100))),
                new ShiftMix(2, ImmutableArray.Create(
                    new MailShare(Idx("postcard"), 100), new MailShare(Idx("letter"), 400), new MailShare(Idx("small"), 300),
                    new MailShare(Idx("medium"), 150), new MailShare(Idx("large"), 50)))),
            BatchIntervalMs = 15_000, BatchJitterMs = 3_000, TailGuardMs = 9_000, OpeningRampBatches = 0,
            SpawnOverheadPm = 1_600, StreetStreakRatioPm = 300, RaidSpawnRateRatioPm = 500,
            DistanceStepRatioPm = 250, ShiftStepRatioPm = 100,
        };
    }

    private static DefAssignment AssignKinds(ImmutableArray<MailKindDef> kinds)
    {
        string[] ids = new string[kinds.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = kinds[i].Id;
        }

        return DefIdAssigner.Assign(ids);
    }

    private static ImmutableArray<DefIndex> KindIndices(params string[] orderedIds)
    {
        ImmutableArray<string> sorted = AssignKinds(M1aMailKinds()).SortedIds;
        ImmutableArray<DefIndex>.Builder b = ImmutableArray.CreateBuilder<DefIndex>(orderedIds.Length);
        foreach (string id in orderedIds)
        {
            b.Add(KindIndex(sorted, id));
        }

        return b.MoveToImmutable();
    }

    private static DefIndex KindIndex(ImmutableArray<string> sortedIds, string id)
    {
        int position = sortedIds.IndexOf(id, StringComparer.Ordinal);
        return position < 0
            ? throw new InvalidOperationException("The M-1a built-in content references the unknown mail kind '" + id + "'.")
            : DefIndex.FromValue((ushort)position);
    }
}
