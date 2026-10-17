using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The per-world owner of the stat sources (ADR-0010 section 7): implements <see cref="IStatWriter"/>, owns the fixed-capacity
/// modifier pools (one for the team, sized <c>stats.modifierPoolPerTeam</c>, and one per <see cref="PlayerSlot"/>, sized
/// <c>stats.modifierPoolPerPlayer</c>) and the live sheet. Every successful <see cref="Push"/> or <see cref="Remove"/>
/// rebuilds the whole sheet through <see cref="StatSheet"/> before it returns and raises the version by 1. The pools never
/// grow after construction; a push that does not fit is refused and counted (<see cref="CounterId.StatPoolFull"/>). Each
/// rebuild allocates the new immutable cache (a source add or remove happens at a phase boundary or in the op that applies a
/// pick, never per tick, and it resolves within the 0.2 ms advisory budget). Created once per world by
/// <see cref="Composition.SimComposition"/>; no statics. Not thread-safe: Sim tick thread only. Systems never see this type:
/// they get <see cref="Sheet"/> as <see cref="IStatSheet"/>, and only Perks and Stamps get it as <see cref="IStatWriter"/>.
/// </summary>
/// <example>
/// <code>
/// var writer = new StatSheetWriter(content, counters);
/// IStatSheet sheet = writer.Sheet;                                  // handed to readers
/// var src = new ModifierSource(SourceKind.Perk, perk, 0, ModifierOwner.ForSlot(slot), 3, ModifierLifetime.Run);
/// writer.Push(in src, [legs]);                                      // sheet.Version is now 1
/// </code>
/// </example>
public sealed class StatSheetWriter : IStatWriter
{
    private readonly ContentSet _content;
    private readonly StatLayout[] _layouts;
    private readonly SimCounters _counters;
    private readonly LiveStatSheet _live;
    private readonly OwnerList[] _lists;
    private readonly Modifier[] _modScratch;
    private readonly RuleFlagGrant[] _grantScratch;
    private readonly uint _definedFlagBits;
    private uint _version;

    /// <summary>Creates the writer with the pools sized from <c>content.Settings.Stats</c>.</summary>
    /// <param name="content">The accepted content set.</param>
    /// <param name="counters">The world's counters (refusals are counted here).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public StatSheetWriter(ContentSet content, SimCounters counters)
        : this(content, (content ?? throw new ArgumentNullException(nameof(content))).Settings.Stats, counters)
    {
    }

    /// <summary>Creates the writer with explicit pool sizes (tests use small ones).</summary>
    /// <param name="content">The accepted content set.</param>
    /// <param name="settings">The pool sizes; each must be 0 or more (0 refuses every push for that owner).</param>
    /// <param name="counters">The world's counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A pool size is negative.</exception>
    public StatSheetWriter(ContentSet content, StatsSettings settings, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.ModifierPoolPerTeam);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.ModifierPoolPerPlayer);
        _content = content;
        _counters = counters;
        _layouts = StatSheet.CreateLayouts(content, out _);
        _lists = new OwnerList[PlayerSlot.Count + 1];
        _lists[0] = new OwnerList(settings.ModifierPoolPerTeam);
        for (int i = 1; i < _lists.Length; i++)
        {
            _lists[i] = new OwnerList(settings.ModifierPoolPerPlayer);
        }

        int flagWidth = RuleFlagSet.DefinedWidth;
        _definedFlagBits = flagWidth >= 32 ? uint.MaxValue : (1u << flagWidth) - 1u;
        int totalPool = settings.ModifierPoolPerTeam + (PlayerSlot.Count * settings.ModifierPoolPerPlayer);
        _modScratch = new Modifier[totalPool];
        _grantScratch = new RuleFlagGrant[totalPool * flagWidth];
        _live = new LiveStatSheet(StatSheet.BuildCache(content, new StatSources([])));
    }

    /// <summary>The world's sheet: the stable instance every reader holds. Its reads follow each rebuild.</summary>
    public IStatSheet Sheet => _live;

    /// <summary>The modifier capacity of one owner's pool (fixed at construction); 0 for an unowned value.</summary>
    /// <param name="owner">The owner.</param>
    /// <returns>The capacity.</returns>
    public int ModifierCapacity(ModifierOwner owner) => owner.IsValid ? _lists[owner.ListIndex].Mods.Length : 0;

    /// <summary>The number of modifiers currently stored for one owner; 0 for an unowned value.</summary>
    /// <param name="owner">The owner.</param>
    /// <returns>The count.</returns>
    public int ModifierCount(ModifierOwner owner) => owner.IsValid ? _lists[owner.ListIndex].ModCount : 0;

    /// <summary>The number of live sources of one owner; 0 for an unowned value.</summary>
    /// <param name="owner">The owner.</param>
    /// <returns>The count.</returns>
    public int SourceCount(ModifierOwner owner) => owner.IsValid ? _lists[owner.ListIndex].SourceCount : 0;

    /// <inheritdoc />
    public bool Push(in ModifierSource src, ReadOnlySpan<Modifier> mods)
    {
        if (!IsWellFormed(in src) || !AllApply(in src, mods))
        {
            return Refuse(CounterId.StatWriteRefused);
        }

        OwnerList list = _lists[src.Owner.ListIndex];
        if (list.IndexOf(in src) >= 0)
        {
            return Refuse(CounterId.StatWriteRefused);
        }

        if (!list.HasRoomFor(mods.Length))
        {
            return Refuse(CounterId.StatPoolFull);
        }

        list.Add(in src, mods);
        Commit();
        return true;
    }

    /// <inheritdoc />
    public bool Remove(in ModifierSource src)
    {
        if (!src.Owner.IsValid)
        {
            return Refuse(CounterId.StatWriteRefused);
        }

        OwnerList list = _lists[src.Owner.ListIndex];
        int index = list.IndexOf(in src);
        if (index < 0)
        {
            return Refuse(CounterId.StatWriteRefused);
        }

        list.RemoveAt(index);
        Commit();
        return true;
    }

    private bool Refuse(CounterId counter)
    {
        _counters.Add(counter);
        return false;
    }

    private bool IsWellFormed(in ModifierSource src)
    {
        bool shape = src.Owner.IsValid && src.StackIndex < src.MaxStacks;
        bool kind = src.Kind >= SourceKind.Perk && src.Kind <= SourceKind.RunEvent;
        bool life = src.Lifetime >= ModifierLifetime.Run && src.Lifetime <= ModifierLifetime.Shift;
        return shape && kind && life && (src.Flags.Bits & ~_definedFlagBits) == 0;
    }

    private bool AllApply(in ModifierSource src, ReadOnlySpan<Modifier> mods)
    {
        bool isTeam = src.Owner.IsTeam;
        for (int i = 0; i < mods.Length; i++)
        {
            if (!StatSheet.Applicable(in mods[i], isTeam, _layouts))
            {
                return false;
            }
        }

        return true;
    }

    // Rebuilds the sheet from the pools and publishes it; runs before Push/Remove return.
    private void Commit()
    {
        _version++;
        int mods = 0;
        int grants = 0;
        for (int i = 0; i < _lists.Length; i++)
        {
            ModifierOwner owner = i == 0 ? ModifierOwner.Team : ModifierOwner.ForSlot(PlayerSlot.FromValue((byte)(i - 1)));
            mods = _lists[i].CopyModifiers(_modScratch, mods);
            grants = _lists[i].CopyGrants(_grantScratch, grants, owner);
        }

        var sources = new StatSources(_modScratch.AsSpan(0, mods), _grantScratch.AsSpan(0, grants));
        _live.Publish(StatSheet.BuildCache(_content, sources), _version);
    }

    private struct SourceEntry
    {
        public SourceKind Kind;
        public DefIndex Id;
        public byte StackIndex;
        public RuleFlagSet Flags;
        public int ModStart;
        public int ModCount;
    }

    // One owner's fixed-capacity pool: a dense modifier array and a dense source table (both never reallocated).
    private sealed class OwnerList
    {
        public readonly Modifier[] Mods;
        private readonly SourceEntry[] _sources;

        public OwnerList(int capacity)
        {
            Mods = new Modifier[capacity];
            _sources = new SourceEntry[capacity];
        }

        public int ModCount { get; private set; }

        public int SourceCount { get; private set; }

        public bool HasRoomFor(int modifiers) => SourceCount < _sources.Length && modifiers <= Mods.Length - ModCount;

        public int IndexOf(in ModifierSource src)
        {
            for (int i = 0; i < SourceCount; i++)
            {
                ref SourceEntry e = ref _sources[i];
                if (e.Kind == src.Kind && e.Id == src.Id && e.StackIndex == src.StackIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        public void Add(in ModifierSource src, ReadOnlySpan<Modifier> mods)
        {
            _sources[SourceCount++] = new SourceEntry
            {
                Kind = src.Kind,
                Id = src.Id,
                StackIndex = src.StackIndex,
                Flags = src.Flags,
                ModStart = ModCount,
                ModCount = mods.Length,
            };
            for (int i = 0; i < mods.Length; i++)
            {
                Mods[ModCount++] = mods[i] with
                {
                    SourceKind = src.Kind,
                    SourceId = src.Id,
                    StackIndex = src.StackIndex,
                    OwnerSlot = src.Owner.Slot,
                    Lifetime = src.Lifetime,
                };
            }
        }

        public void RemoveAt(int index)
        {
            SourceEntry removed = _sources[index];
            int tail = ModCount - (removed.ModStart + removed.ModCount);
            Array.Copy(Mods, removed.ModStart + removed.ModCount, Mods, removed.ModStart, tail);
            Array.Clear(Mods, ModCount - removed.ModCount, removed.ModCount);
            ModCount -= removed.ModCount;
            Array.Copy(_sources, index + 1, _sources, index, SourceCount - index - 1);
            SourceCount--;
            _sources[SourceCount] = default;
            for (int i = index; i < SourceCount; i++)
            {
                _sources[i].ModStart -= removed.ModCount;
            }
        }

        public int CopyModifiers(Modifier[] target, int at)
        {
            Array.Copy(Mods, 0, target, at, ModCount);
            return at + ModCount;
        }

        public int CopyGrants(RuleFlagGrant[] target, int at, ModifierOwner owner)
        {
            for (int i = 0; i < SourceCount; i++)
            {
                uint bits = _sources[i].Flags.Bits;
                for (int f = 0; bits != 0; f++, bits >>= 1)
                {
                    if ((bits & 1u) != 0)
                    {
                        target[at++] = new RuleFlagGrant(owner, (RuleFlag)(f + 1));
                    }
                }
            }

            return at;
        }
    }
}
