using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The facts of one panel entry that <see cref="IPanelView.Entry"/> returns (ADR-0016 section 3; manual-delivery story 002,
/// grid-inventory story 022): the kind key, category, address, raw flags, unit count, entry id and the value-hint runs Manual Delivery
/// F1 prices. It names no Mail &amp; Destinations type, so Inventory can fill it on the host (where the runs stay empty: money is Mail's).
/// A small immutable value; a default value is an empty slot (<see cref="EntryId"/> none).
/// </summary>
/// <example>
/// <code>
/// var letter = new PanelEntryFacts(id, kindKey: 0, ItemCategory.Mail, address, flags: 0, units: 3, runs);
/// PanelHintRun oldest = letter.Run(0);
/// </code>
/// </example>
public readonly struct PanelEntryFacts
{
    /// <summary>The most value-hint runs an entry carries (Mail R17).</summary>
    public const int MaxRuns = 5;

    private readonly PanelHintRuns _runs;

    /// <summary>Creates the facts; runs past <see cref="MaxRuns"/> are dropped.</summary>
    /// <param name="entryId">The entry id.</param>
    /// <param name="kindKey">The mail kind index for mail, else the item def index (<c>EntryRecord.MailKind</c> / <c>ItemId</c>).</param>
    /// <param name="category">The item category (<see cref="ItemCategory.Mail"/> for mail).</param>
    /// <param name="address">The packed address (none for an item).</param>
    /// <param name="flags">The raw flags byte (<c>EntryRecord.Flags</c>).</param>
    /// <param name="units">The unit count.</param>
    /// <param name="runs">The value-hint runs, oldest first; empty when unknown (always on the host).</param>
    public PanelEntryFacts(EntryId entryId, ushort kindKey, ItemCategory category, AddressId address, byte flags, byte units, ReadOnlySpan<PanelHintRun> runs)
    {
        EntryId = entryId;
        KindKey = kindKey;
        Category = category;
        Address = address;
        Flags = flags;
        Units = units;
        int n = Math.Min(runs.Length, MaxRuns);
        PanelHintRuns buffer = default;
        for (int i = 0; i < n; i++)
        {
            buffer[i] = runs[i];
        }

        _runs = buffer;
        RunCount = (byte)n;
    }

    /// <summary>The entry id; none for an empty hotbar slot.</summary>
    public EntryId EntryId { get; }

    /// <summary>The kind key: the mail kind index when <see cref="Category"/> is mail, else the item def index (TD-172 D4).</summary>
    public ushort KindKey { get; }

    /// <summary>The item category.</summary>
    public ItemCategory Category { get; }

    /// <summary>The packed address written on the mail (none for an item).</summary>
    public AddressId Address { get; }

    /// <summary>The raw flags byte (bit 0 Cursed for mail).</summary>
    public byte Flags { get; }

    /// <summary>The number of units in the entry.</summary>
    public byte Units { get; }

    /// <summary>The number of value-hint runs (0 to <see cref="MaxRuns"/>).</summary>
    public byte RunCount { get; }

    /// <summary>True when the facts describe an entry (not an empty slot).</summary>
    public bool IsEntry => !EntryId.IsNone;

    /// <summary>The value-hint run at <paramref name="i"/>, oldest first.</summary>
    /// <param name="i">The run index, 0 to <see cref="RunCount"/> - 1.</param>
    /// <returns>The run; default for an index outside the runs.</returns>
    public PanelHintRun Run(int i) => (uint)i < RunCount ? _runs[i] : default;
}

/// <summary>
/// One value-hint run of a panel entry (Manual Delivery F1 variables; Mail R17): <see cref="Count"/> consecutive units sharing a
/// deadline shift and a spawn value. The same three values as Mail's <c>HintRun</c>, declared here so the port names no Mail type.
/// </summary>
/// <example><code>var run = new PanelHintRun(deadlineShift: 3, count: 6, unitSpawnValue: 8);</code></example>
public readonly struct PanelHintRun
{
    /// <summary>Creates a run.</summary>
    /// <param name="deadlineShift">The deadline shift of the run's units.</param>
    /// <param name="count">The number of units in the run.</param>
    /// <param name="unitSpawnValue">The spawn value of each unit, in cents.</param>
    public PanelHintRun(byte deadlineShift, byte count, ushort unitSpawnValue)
    {
        DeadlineShift = deadlineShift;
        Count = count;
        UnitSpawnValue = unitSpawnValue;
    }

    /// <summary>The deadline shift shared by the run's units.</summary>
    public byte DeadlineShift { get; }

    /// <summary>How many units the run holds.</summary>
    public byte Count { get; }

    /// <summary>The spawn value, in cents, of each unit of the run.</summary>
    public ushort UnitSpawnValue { get; }
}

/// <summary>Fixed-capacity inline storage for an entry's <see cref="PanelHintRun"/>s (no heap).</summary>
[InlineArray(PanelEntryFacts.MaxRuns)]
internal struct PanelHintRuns
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
#pragma warning disable IDE0051 // Remove unused private members
    private PanelHintRun _element;
#pragma warning restore IDE0051
#pragma warning restore CS0169
}
