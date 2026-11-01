using System.Collections.Immutable;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// One binding of the per-input index: the pressed input, the modifiers it needs, the action it counts as (Back for a
/// Back-ladder step) and the def whose live predicate gates it.
/// </summary>
internal readonly struct ChordEntry
{
    public readonly PhysicalInput Key;
    public readonly ModifierMask Modifiers;
    public readonly ushort Action;
    public readonly ActionDef Owner;

    public ChordEntry(PhysicalInput key, ModifierMask modifiers, ushort action, ActionDef owner)
    {
        Key = key;
        Modifiers = modifiers;
        Action = action;
        Owner = owner;
    }
}

/// <summary>
/// Most-specific-chord-wins resolution (GDD R10, ADR-0012 sections 2 and 3): given the modifiers physically held, the input
/// just pressed and the router state, the live action whose binding needs the most of the held modifiers wins, and the
/// plainer bindings on that input do not also fire (Shift+Enter is Quick-move, not also Pick / place). A binding needing a
/// modifier that is not held never matches; extra held modifiers never block a plainer binding (Shift held for Sprint does
/// not block E). Back-ladder step bindings (<see cref="InputBinding.IsBackLadderStep"/>) resolve to Back.
/// <para>
/// Built once per <see cref="ActionMap"/> (<see cref="ActionMap.Chords"/>): every binding of every action is indexed by its
/// pressed input (keyboard, mouse and pad blocks of <see cref="PhysicalInput"/>, a flat array slice per input), so
/// resolution scans only the bindings on that input: 0 B, no dictionary, no Godot <c>InputMap</c> modifier matching. The
/// conflict validator calls the same routine per (state, input, modifier subset), so runtime and validation cannot disagree.
/// Inside a slice bindings are in canonical order: owner action id ascending, then slot (KBM primary, KBM secondary, pad
/// primary, pad secondary). On a tie at the top specificity (a map the validator rejects, so never a loaded map) the first
/// binding in that order wins. Modifier state passed in must be read from raw held input, never from mute-filtered state
/// (story 007). Immutable after construction; thread-safe.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ActionMap map = BuiltInActionMap.CreateM1a();
/// ushort a = ChordResolver.Resolve(ModifierMask.Shift, PhysicalInput.KeyEnter, panelStateId, map);   // ActionId.QuickMove
/// ushort b = map.Chords.Resolve(ModifierMask.None, PhysicalInput.KeyEnter, panelStateId);            // ActionId.PickPlace
/// bool hit = map.Chords.TryResolve(ModifierMask.Shift, PhysicalInput.KeyE, worldStateId, out ChordMatch m);
/// // hit, m.Action == ActionId.Interact, m.Modifiers == ModifierMask.None (Shift is not consumed)
/// </code>
/// </example>
public sealed class ChordResolver
{
    private const int BlockSize = 256;
    private const int SlotCount = 3 * BlockSize;

    private readonly int[] _start;
    private readonly ChordEntry[] _entries;
    private readonly ModifierMask[] _modifiersOn;
    private readonly PhysicalInput[] _keys;

    private ChordResolver(int[] start, ChordEntry[] entries, ModifierMask[] modifiersOn, PhysicalInput[] keys)
    {
        _start = start;
        _entries = entries;
        _modifiersOn = modifiersOn;
        _keys = keys;
    }

    /// <summary>Every physical input some binding presses, ascending. Allocation-free view.</summary>
    public ReadOnlySpan<PhysicalInput> Keys => _keys;

    /// <summary>
    /// Builds the index over <paramref name="defs"/> (ascending id). Malformed bindings and chords that break the shape rules
    /// (pad chords, a non-modifier as the modifier, a modifier as the pressed input) are not indexed; the validator reports
    /// them. Allocates; called once by <see cref="ActionMap"/>.
    /// </summary>
    internal static ChordResolver Build(ImmutableArray<ActionDef> defs)
    {
        List<ChordEntry> raw = CollectEntries(defs);
        int[] start = SliceStarts(raw);
        var next = new int[SlotCount];
        Array.Copy(start, next, SlotCount);
        var entries = new ChordEntry[raw.Count];
        var modifiersOn = new ModifierMask[SlotCount];

        // Counting sort by slot: stable, so each slice keeps canonical order (no List.Sort).
        foreach (ChordEntry e in raw)
        {
            int slot = SlotOf(e.Key);
            entries[next[slot]++] = e;
            modifiersOn[slot] |= e.Modifiers;
        }

        return new ChordResolver(start, entries, modifiersOn, KeysOf(start));
    }

    /// <summary>
    /// The most specific live action for a press: <paramref name="pressed"/> with <paramref name="held"/> modifiers in state
    /// <paramref name="stateId"/>, through <paramref name="map"/>'s index. 0 B.
    /// </summary>
    /// <param name="held">The modifiers physically held (raw input, not mute-filtered).</param>
    /// <param name="pressed">The input that went down.</param>
    /// <param name="stateId">The packed router state id.</param>
    /// <param name="map">The action map.</param>
    /// <returns>The winning <see cref="ActionId"/>, or <see cref="ActionId.None"/> when nothing live is bound.</returns>
    /// <example><code>ushort id = ChordResolver.Resolve(ModifierMask.Shift, PhysicalInput.MouseLeft, panelId, map);   // QuickMove</code></example>
    public static ushort Resolve(ModifierMask held, PhysicalInput pressed, ushort stateId, ActionMap map) =>
        map.Chords.Resolve(held, pressed, stateId);

    /// <summary>Instance form of <see cref="Resolve(ModifierMask, PhysicalInput, ushort, ActionMap)"/>. 0 B.</summary>
    /// <param name="held">The modifiers physically held.</param>
    /// <param name="pressed">The input that went down.</param>
    /// <param name="stateId">The packed router state id.</param>
    /// <returns>The winning <see cref="ActionId"/>, or <see cref="ActionId.None"/>.</returns>
    /// <example><code>ushort id = map.Chords.Resolve(ModifierMask.None, PhysicalInput.KeyEscape, stateId);   // Back</code></example>
    public ushort Resolve(ModifierMask held, PhysicalInput pressed, ushort stateId)
    {
        int w = FindWinner(held, pressed, stateId, out _);
        return w < 0 ? ActionId.None : _entries[w].Action;
    }

    /// <summary>
    /// Resolves a press and reports the matched modifiers and the Back-ladder step (see <see cref="ChordMatch"/>). 0 B.
    /// </summary>
    /// <param name="held">The modifiers physically held (raw input, not mute-filtered).</param>
    /// <param name="pressed">The input that went down.</param>
    /// <param name="stateId">The packed router state id.</param>
    /// <param name="match">The match; <c>default</c> when nothing live is bound.</param>
    /// <returns>True when a live action matched.</returns>
    /// <example><code>if (map.Chords.TryResolve(held, key, stateId, out ChordMatch m)) { consumed |= m.Modifiers; }</code></example>
    public bool TryResolve(ModifierMask held, PhysicalInput pressed, ushort stateId, out ChordMatch match)
    {
        int w = FindWinner(held, pressed, stateId, out _);
        if (w < 0)
        {
            match = default;
            return false;
        }

        ChordEntry e = _entries[w];
        ushort step = e.Action == ActionId.Back ? LowestLadderOwner(pressed, e.Modifiers, stateId) : ActionId.None;
        match = new ChordMatch(e.Action, e.Modifiers, step);
        return true;
    }

    /// <summary>The union of the modifiers any binding on <paramref name="key"/> needs. 0 B.</summary>
    /// <param name="key">The pressed input.</param>
    /// <returns>The union; None for a plain-only or unbound input.</returns>
    /// <example><code>ModifierMask m = map.Chords.ModifiersOn(PhysicalInput.KeyEnter);   // Shift</code></example>
    public ModifierMask ModifiersOn(PhysicalInput key)
    {
        int slot = SlotOf(key);
        return slot < 0 ? ModifierMask.None : _modifiersOn[slot];
    }

    /// <summary>The indexed bindings pressing <paramref name="key"/>, in canonical order.</summary>
    internal ReadOnlySpan<ChordEntry> EntriesOf(PhysicalInput key)
    {
        int slot = SlotOf(key);
        return slot < 0 ? ReadOnlySpan<ChordEntry>.Empty : _entries.AsSpan(_start[slot], _start[slot + 1] - _start[slot]);
    }

    /// <summary>
    /// The validator's form of the shared routine: the winning action, the modifiers it matched and, when
    /// <paramref name="tied"/> is non-null, every distinct action at the winning specificity (the conflict set).
    /// </summary>
    internal ushort ResolveCore(ModifierMask held, PhysicalInput pressed, ushort stateId, List<ushort>? tied, out ModifierMask winningMask)
    {
        tied?.Clear();
        int w = FindWinner(held, pressed, stateId, out int bestSpec);
        if (w < 0)
        {
            winningMask = ModifierMask.None;
            return ActionId.None;
        }

        winningMask = _entries[w].Modifiers;
        if (tied is not null)
        {
            CollectTied(held, pressed, stateId, bestSpec, tied);
        }

        return _entries[w].Action;
    }

    /// <summary>
    /// The single resolution routine: a binding is a candidate when its modifiers are a subset of <paramref name="held"/> and
    /// its owner is live; the candidate needing the most modifiers wins, the first in canonical order on a tie. Returns the
    /// absolute entry index, or -1. 0 B.
    /// </summary>
    private int FindWinner(ModifierMask held, PhysicalInput pressed, ushort stateId, out int bestSpec)
    {
        bestSpec = -1;
        int slot = SlotOf(pressed);
        if (slot < 0)
        {
            return -1;
        }

        int best = -1;
        for (int i = _start[slot]; i < _start[slot + 1]; i++)
        {
            ref readonly ChordEntry e = ref _entries[i];
            if ((e.Modifiers & ~held) != 0)
            {
                continue;
            }

            int spec = ModifierMasks.Specificity(e.Modifiers);
            if (spec > bestSpec && e.Owner.IsLive(stateId))
            {
                bestSpec = spec;
                best = i;
            }
        }

        return best;
    }

    private void CollectTied(ModifierMask held, PhysicalInput pressed, ushort stateId, int bestSpec, List<ushort> tied)
    {
        ReadOnlySpan<ChordEntry> slice = EntriesOf(pressed);
        for (int i = 0; i < slice.Length; i++)
        {
            ref readonly ChordEntry e = ref slice[i];
            if ((e.Modifiers & ~held) == 0
                && ModifierMasks.Specificity(e.Modifiers) == bestSpec
                && !tied.Contains(e.Action)
                && e.Owner.IsLive(stateId))
            {
                tied.Add(e.Action);
            }
        }
    }

    private ushort LowestLadderOwner(PhysicalInput pressed, ModifierMask modifiers, ushort stateId)
    {
        ReadOnlySpan<ChordEntry> slice = EntriesOf(pressed);
        ushort lowest = ActionId.None;
        for (int i = 0; i < slice.Length; i++)
        {
            ref readonly ChordEntry e = ref slice[i];
            if (e.Action == ActionId.Back && e.Owner.Id != ActionId.Back && e.Modifiers == modifiers
                && (lowest == ActionId.None || e.Owner.Id < lowest) && e.Owner.IsLive(stateId))
            {
                lowest = e.Owner.Id;
            }
        }

        return lowest;
    }

    /// <summary>The index slot of an input: block (keyboard 0, mouse 1, pad 2) times 256 plus the low byte; -1 outside.</summary>
    private static int SlotOf(PhysicalInput input)
    {
        int block = ((ushort)input >> 8) - 1;
        return block >= 0 && block < 3 ? (block * BlockSize) + ((ushort)input & 0xFF) : -1;
    }

    private static List<ChordEntry> CollectEntries(ImmutableArray<ActionDef> defs)
    {
        var raw = new List<ChordEntry>();
        foreach (ActionDef d in defs)
        {
            AddBinding(raw, d, d.KbmPrimary, pad: false);
            AddBinding(raw, d, d.KbmSecondary, pad: false);
            AddBinding(raw, d, d.PadPrimary, pad: true);
            AddBinding(raw, d, d.PadSecondary, pad: true);
        }

        return raw;
    }

    /// <summary>Prefix sums of the per-slot counts: slot <c>s</c> occupies <c>[start[s], start[s + 1])</c>.</summary>
    private static int[] SliceStarts(List<ChordEntry> raw)
    {
        var start = new int[SlotCount + 1];
        foreach (ChordEntry e in raw)
        {
            start[SlotOf(e.Key) + 1]++;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            start[i + 1] += start[i];
        }

        return start;
    }

    private static PhysicalInput[] KeysOf(int[] start)
    {
        var keys = new List<PhysicalInput>();
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (start[slot + 1] > start[slot])
            {
                keys.Add((PhysicalInput)((((slot / BlockSize) + 1) << 8) | (slot % BlockSize)));
            }
        }

        return keys.ToArray();
    }

    private static void AddBinding(List<ChordEntry> raw, ActionDef def, InputBinding b, bool pad)
    {
        if (b.IsMissing || b.IsUnbound || !b.IsWellFormed())
        {
            return;
        }

        ushort action = b.IsBackLadderStep ? ActionId.Back : def.Id;
        if (b.Kind == BindingKind.Chord)
        {
            ModifierMask m = ModifierMasks.FromInput(b.Modifier);
            if (pad || m == ModifierMask.None || ModifierMasks.IsModifier(b.Key) || !PhysicalInputs.IsKeyboardMouse(b.Key))
            {
                return;
            }

            Add(raw, def, b, b.Key, m, action);
            return;
        }

        int n = b.InputCount;
        for (int i = 0; i < n; i++)
        {
            Add(raw, def, b, b.InputAt(i), ModifierMask.None, action);
        }
    }

    private static void Add(List<ChordEntry> raw, ActionDef def, InputBinding b, PhysicalInput key, ModifierMask m, ushort action)
    {
        if (SlotOf(key) < 0)
        {
            return;
        }

        raw.Add(new ChordEntry(key, m, action, def));
        if (b.AcceptsNumpadEnter && key == PhysicalInput.KeyEnter)
        {
            raw.Add(new ChordEntry(PhysicalInput.KeyNumpadEnter, m, action, def));
        }
    }
}
