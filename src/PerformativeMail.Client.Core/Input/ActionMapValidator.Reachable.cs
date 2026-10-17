using System.Collections.Immutable;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The reachable-state pass of the action-map validator (input-controls story 006, ADR-0012 section 2, GDD R4 / R10, H-R4-1).
/// </summary>
public static partial class ActionMapValidator
{
    /// <summary>
    /// The field checks plus the reachable-state pass over <paramref name="map"/>, with the M-1a pad-path table
    /// (<see cref="PadPathTable.M1a"/>). See <see cref="Validate(ActionMap, ReachableSet, PadPathTable)"/>.
    /// </summary>
    /// <param name="map">A map that passed the field checks.</param>
    /// <param name="reachable">The reachable router states (normally <see cref="ReachableSet.Default"/>).</param>
    /// <returns>The report.</returns>
    /// <example><code>bool ok = ActionMapValidator.Validate(map, ReachableSet.Default).IsValid;</code></example>
    public static ValidationReport Validate(ActionMap map, ReachableSet reachable) => Validate(map, reachable, PadPathTable.M1a);

    /// <summary>
    /// The field checks plus the reachable-state pass (run at load and on every remap; O(reachable states x bindings), outside
    /// the frame budget; allocates, so never on the runtime path):
    /// <list type="bullet">
    /// <item>For every reachable state x every bound input x every subset of the modifiers bound on it, the most specific live
    /// bindings (<see cref="ChordResolver"/>, the same code the router runs) must name one action, Back-ladder steps counted
    /// as Back (<see cref="ValidationCodes.ConflictColive"/>); an input that is a live action and also the modifier of a live
    /// chord is a conflict too.</item>
    /// <item>No co-live Tap action and different Hold action on one input (<see cref="ValidationCodes.TapHoldShare"/>).</item>
    /// <item>Binding shapes: chords are one keyboard modifier plus one non-modifier input, never on the pad
    /// (<see cref="ValidationCodes.ChordShape"/>); Esc only on Back and Pause, Menu only on Pause
    /// (<see cref="ValidationCodes.ReservedInput"/>); Move and Grid cursor share no input
    /// (<see cref="ValidationCodes.MoveCursorShare"/>); Back-ladder steps use Back's own input on that device
    /// (<see cref="ValidationCodes.LadderStep"/>).</item>
    /// <item>Pad coverage (H-FX-3a) through <paramref name="padPaths"/> (<see cref="ValidationCodes.PadPath"/>).</item>
    /// <item>Info: an input shared by actions that are never live together (<see cref="ValidationCodes.SharedDisjoint"/>).</item>
    /// </list>
    /// Each conflict names the state, the input (with modifiers) and the actions, ascending by id.
    /// </summary>
    /// <param name="map">A map that passed the field checks.</param>
    /// <param name="reachable">The reachable router states.</param>
    /// <param name="padPaths">The pad-path and exemption data.</param>
    /// <returns>The report.</returns>
    /// <example><code>ValidationReport r = ActionMapValidator.Validate(map, ReachableSet.Default, PadPathTable.M1a);</code></example>
    public static ValidationReport Validate(ActionMap map, ReachableSet reachable, PadPathTable padPaths)
    {
        ArgumentNullException.ThrowIfNull(map);
        ValidationReport report = Validate(map.All);
        AppendReachableChecks(report, map, reachable, padPaths);
        return report;
    }

    /// <summary>
    /// The boot entry point (story 028): <see cref="ActionMap.TryLoad(IReadOnlyList{ActionDef}, out ActionMap, out ValidationReport)"/>
    /// (field checks, then the reachable-state pass over <see cref="ReachableSet.Default"/> and <see cref="PadPathTable.M1a"/>),
    /// returning the full report. Boot refuses to start unless <paramref name="map"/> is non-null.
    /// </summary>
    /// <param name="defs">The definitions.</param>
    /// <param name="map">The map when the report has no errors; null otherwise.</param>
    /// <returns>The full report (errors, warnings, infos).</returns>
    /// <example>
    /// <code>
    /// ValidationReport report = ActionMapValidator.ValidateForBoot(BuiltInActionMap.Definitions(), out ActionMap? map);
    /// if (map is null) { log.Error(report.ToString()); return; }
    /// </code>
    /// </example>
    public static ValidationReport ValidateForBoot(IReadOnlyList<ActionDef?> defs, out ActionMap? map) =>
        ValidateForBoot(defs, ReachableSet.Default, PadPathTable.M1a, out map);

    /// <summary>The boot entry point with explicit reachable set and pad-path data. See <see cref="ValidateForBoot(IReadOnlyList{ActionDef}, out ActionMap)"/>.</summary>
    /// <param name="defs">The definitions.</param>
    /// <param name="reachable">The reachable router states.</param>
    /// <param name="padPaths">The pad-path and exemption data.</param>
    /// <param name="map">The map when the report has no errors; null otherwise.</param>
    /// <returns>The full report.</returns>
    /// <example><code>var r = ActionMapValidator.ValidateForBoot(defs, ReachableSet.Default, PadPathTable.M1a, out ActionMap? map);</code></example>
    public static ValidationReport ValidateForBoot(IReadOnlyList<ActionDef?> defs, ReachableSet reachable, PadPathTable padPaths, out ActionMap? map)
    {
        _ = ActionMap.TryLoad(defs, reachable, padPaths, out map, out ValidationReport report);
        return report;
    }

    /// <summary>The reachable-state pass, appended to a report whose field checks passed. Used by <see cref="ActionMap.TryLoad(IReadOnlyList{ActionDef}, ReachableSet, PadPathTable, out ActionMap, out ValidationReport)"/>.</summary>
    internal static void AppendReachableChecks(ValidationReport report, ActionMap map, ReachableSet reachable, PadPathTable padPaths)
    {
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(padPaths);
        CheckShapes(report, map, reachable);
        CheckLiveConflicts(report, map, reachable);
        CheckSharedDisjoint(report, map, reachable);
        padPaths.Check(map, report);
    }

    // ------------------------------------------------------------------ binding shapes

    private static void CheckShapes(ValidationReport r, ActionMap map, ReachableSet reachable)
    {
        ActionDef? back = map.Get(ActionId.Back);
        foreach (ActionDef d in map.All)
        {
            CheckShape(r, back, reachable, d, ValidationFields.KbmPrimary, d.KbmPrimary, pad: false);
            CheckShape(r, back, reachable, d, ValidationFields.KbmSecondary, d.KbmSecondary, pad: false);
            CheckShape(r, back, reachable, d, ValidationFields.PadPrimary, d.PadPrimary, pad: true);
            CheckShape(r, back, reachable, d, ValidationFields.PadSecondary, d.PadSecondary, pad: true);
        }

        CheckMoveCursor(r, map);
    }

    private static void CheckShape(ValidationReport r, ActionDef? back, ReachableSet reachable, ActionDef d, string field, InputBinding b, bool pad)
    {
        if (b.IsMissing || b.IsUnbound)
        {
            return;
        }

        CheckChordShape(r, d, field, b, pad);
        CheckReservedInputs(r, d, field, b);
        if (b.IsBackLadderStep)
        {
            CheckLadderStep(r, back, reachable, d, field, b, pad);
        }
    }

    private static void CheckChordShape(ValidationReport r, ActionDef d, string field, InputBinding b, bool pad)
    {
        if (b.Kind != BindingKind.Chord)
        {
            return;
        }

        string? why = pad
            ? "a gamepad binding is never a chord"
            : !ModifierMasks.IsModifier(b.Modifier)
                ? $"{b} is two non-modifier inputs; a chord is a modifier (Key.Shift) plus one input"
                : ModifierMasks.IsModifier(b.Key) ? $"{b} presses a modifier; a chord is a modifier plus one non-modifier input" : null;
        if (why is not null)
        {
            r.Add(new ValidationEntry(DisplayName(d), field, why) { Code = ValidationCodes.ChordShape, Input = b.Key });
        }
    }

    private static void CheckReservedInputs(ValidationReport r, ActionDef d, string field, InputBinding b)
    {
        if (b.Contains(PhysicalInput.KeyEscape) && d.Id != ActionId.Back && d.Id != ActionId.Pause)
        {
            r.Add(new ValidationEntry(DisplayName(d), field, "Key.Escape is reserved for Back (and Pause as the end of its ladder)")
            {
                Code = ValidationCodes.ReservedInput,
                Input = PhysicalInput.KeyEscape,
            });
        }

        if (b.Contains(PhysicalInput.PadMenu) && d.Id != ActionId.Pause)
        {
            r.Add(new ValidationEntry(DisplayName(d), field, "Pad.Menu is reserved for Pause")
            {
                Code = ValidationCodes.ReservedInput,
                Input = PhysicalInput.PadMenu,
            });
        }
    }

    /// <summary>
    /// R5: a ladder step is a single input that is one of Back's own bindings on that device; Pause's step is keyboard-only
    /// (R5.6 "B never pauses"); and Back is live wherever the step's owner is live, so the press always reaches the ladder.
    /// </summary>
    private static void CheckLadderStep(ValidationReport r, ActionDef? back, ReachableSet reachable, ActionDef d, string field, InputBinding b, bool pad)
    {
        string? why = d.Id == ActionId.Back
            ? "Back's own binding cannot be a step of the Back ladder"
            : b.Kind != BindingKind.Single
                ? $"a Back-ladder step is a single input, not {b}"
                : back is null
                    ? "a Back-ladder step needs a Back action in the map"
                    : pad && d.Id == ActionId.Pause
                        ? "B never pauses (GDD R5.6): Pause's Back-ladder step is keyboard-only (Esc); gamepad pause is Menu"
                        : !(pad ? back.PadPrimary.Contains(b.Input) || back.PadSecondary.Contains(b.Input) : back.KbmPrimary.Contains(b.Input) || back.KbmSecondary.Contains(b.Input))
                            ? $"{NameOf(b.Input)} is not one of Back's {(pad ? "gamepad" : "keyboard-and-mouse")} bindings, so it cannot be a Back-ladder step"
                            : FirstStateLiveWithoutBack(d, back, reachable) is { } state
                                ? $"Back is not live in {RouterState.ToDebugString(state)}, where this ladder step's action is live"
                                : null;
        if (why is not null)
        {
            r.Add(new ValidationEntry(DisplayName(d), field, why) { Code = ValidationCodes.LadderStep, Input = b.Kind == BindingKind.Single ? b.Input : PhysicalInput.None });
        }
    }

    private static ushort? FirstStateLiveWithoutBack(ActionDef owner, ActionDef back, ReachableSet reachable)
    {
        ReadOnlySpan<ushort> ids = reachable.Ids;
        for (int s = 0; s < ids.Length; s++)
        {
            if (owner.IsLive(ids[s]) && !back.IsLive(ids[s]))
            {
                return ids[s];
            }
        }

        return null;
    }

    private static void CheckMoveCursor(ValidationReport r, ActionMap map)
    {
        ActionDef? move = map.Get(ActionId.Move);
        ActionDef? cursor = map.Get(ActionId.GridCursor);
        if (move is null || cursor is null)
        {
            return;
        }

        var reported = new HashSet<PhysicalInput>();
        foreach (InputBinding mb in Slots(move))
        {
            for (int i = 0; i < mb.InputCount; i++)
            {
                PhysicalInput p = mb.InputAt(i);
                if (BindsInput(cursor, p) && reported.Add(p))
                {
                    r.Add(new ValidationEntry(move.StringId, ValidationFields.Bindings, $"{NameOf(p)} is bound to both movement and the grid cursor; they never share a key", cursor.StringId)
                    {
                        Code = ValidationCodes.MoveCursorShare,
                        Input = p,
                        Actions = [move.StringId, cursor.StringId],
                    });
                }
            }
        }
    }

    // ------------------------------------------------------------------ the reachable-state pass

    /// <summary>Reused buffers of one conflict pass (load time only).</summary>
    private sealed class ConflictScratch
    {
        public readonly List<ushort> Tied = new(4);
        public readonly List<ushort> Pair = new(2);
        public readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    }

    private static void CheckLiveConflicts(ValidationReport r, ActionMap map, ReachableSet reachable)
    {
        var scratch = new ConflictScratch();
        ReadOnlySpan<ushort> ids = reachable.Ids;
        ReadOnlySpan<PhysicalInput> keys = map.Chords.Keys;
        for (int s = 0; s < ids.Length; s++)
        {
            scratch.Reported.Clear();
            for (int k = 0; k < keys.Length; k++)
            {
                CheckResolvedSubsets(r, map, scratch, ids[s], keys[k]);
                CheckEntryPairs(r, map, scratch, ids[s], keys[k]);
            }
        }
    }

    /// <summary>Every subset of the modifiers bound on <paramref name="key"/> (None included), through the runtime resolver: the top specificity must name one action.</summary>
    private static void CheckResolvedSubsets(ValidationReport r, ActionMap map, ConflictScratch scratch, ushort state, PhysicalInput key)
    {
        int union = (int)map.Chords.ModifiersOn(key);
        for (int held = union; ; held = (held - 1) & union)
        {
            ushort winner = map.Chords.ResolveCore((ModifierMask)held, key, state, scratch.Tied, out ModifierMask winning);
            if (winner != ActionId.None && scratch.Tied.Count > 1)
            {
                AddConflict(r, map, scratch.Reported, ValidationCodes.ConflictColive, state, key, winning, scratch.Tied, $"{ModifierMasks.ToText(winning)}{NameOf(key)} reaches {scratch.Tied.Count} live actions after most-specific-chord resolution");
            }

            if (held == 0)
            {
                return;
            }
        }
    }

    /// <summary>Per live binding on <paramref name="key"/>: its modifiers used as another live action, and Tap + Hold pairs across modifier buckets.</summary>
    private static void CheckEntryPairs(ValidationReport r, ActionMap map, ConflictScratch scratch, ushort state, PhysicalInput key)
    {
        ReadOnlySpan<ChordEntry> entries = map.Chords.EntriesOf(key);
        for (int i = 0; i < entries.Length; i++)
        {
            ChordEntry e = entries[i];
            if (!e.Owner.IsLive(state))
            {
                continue;
            }

            CheckModifierUse(r, map, scratch, state, key, e);
            for (int j = i + 1; j < entries.Length; j++)
            {
                CheckTapHoldPair(r, map, scratch, state, key, e, entries[j]);
            }
        }
    }

    /// <summary>
    /// GDD R10, literally: "a tap-grammar action and a hold-grammar action may never share an input in overlapping states (the
    /// Interact resolver is the only exception, R4.2)". Checked between different actions in different modifier buckets
    /// (Shift+E Tap against E Hold); the same bucket is the co-live check's case. Interact is one action, hence exempt.
    /// </summary>
    private static void CheckTapHoldPair(ValidationReport r, ActionMap map, ConflictScratch scratch, ushort state, PhysicalInput key, ChordEntry e, ChordEntry o)
    {
        if (o.Action == e.Action || o.Modifiers == e.Modifiers || !o.Owner.IsLive(state))
        {
            return;
        }

        Grammar ge = GrammarOf(map, e);
        Grammar go = GrammarOf(map, o);
        if ((ge == Grammar.Tap && go == Grammar.Hold) || (ge == Grammar.Hold && go == Grammar.Tap))
        {
            scratch.Pair.Clear();
            scratch.Pair.Add(e.Action);
            scratch.Pair.Add(o.Action);
            AddConflict(r, map, scratch.Reported, ValidationCodes.TapHoldShare, state, key, ModifierMask.None, scratch.Pair, $"{NameOf(key)} carries a Tap action and a Hold action in one state; a hold can't share a tap's input");
        }
    }

    /// <summary>R4.1 "including when used as a modifier": a chord's modifier must not also be a different live action.</summary>
    private static void CheckModifierUse(ValidationReport r, ActionMap map, ConflictScratch scratch, ushort state, PhysicalInput key, ChordEntry chord)
    {
        int mods = (int)chord.Modifiers;
        while (mods != 0)
        {
            int bit = mods & -mods;
            mods &= mods - 1;
            PhysicalInput modKey = ModifierMasks.KeyOf((ModifierMask)bit);
            ushort plain = map.Chords.ResolveCore(ModifierMask.None, modKey, state, null, out _);
            if (plain != ActionId.None && plain != chord.Action)
            {
                scratch.Pair.Clear();
                scratch.Pair.Add(plain);
                scratch.Pair.Add(chord.Action);
                AddConflict(r, map, scratch.Reported, ValidationCodes.ConflictColive, state, modKey, ModifierMask.None, scratch.Pair, $"{NameOf(modKey)} is a live action and also the modifier of {ModifierMasks.ToText(chord.Modifiers)}{NameOf(key)} in one state");
            }
        }
    }

    private static Grammar GrammarOf(ActionMap map, ChordEntry e) => (map.Get(e.Action) ?? e.Owner).Grammar;

    private static void AddConflict(ValidationReport r, ActionMap map, HashSet<string> reported, string code, ushort state, PhysicalInput key, ModifierMask mods, List<ushort> actions, string message)
    {
        ImmutableArray<string> names = NamesAscending(map, actions);
        if (!reported.Add($"{code}|{(ushort)key}|{(byte)mods}|{string.Join(",", names)}"))
        {
            return;
        }

        r.Add(new ValidationEntry(names[0], ValidationFields.Bindings, message, names[1], state)
        {
            Code = code,
            Input = key,
            Modifiers = mods,
            Actions = names,
        });
    }

    // ------------------------------------------------------------------ info: shared between states that never overlap

    private static void CheckSharedDisjoint(ValidationReport r, ActionMap map, ReachableSet reachable)
    {
        ReadOnlySpan<PhysicalInput> keys = map.Chords.Keys;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var pair = new List<ushort>(2);
        for (int k = 0; k < keys.Length; k++)
        {
            ReadOnlySpan<ChordEntry> entries = map.Chords.EntriesOf(keys[k]);
            for (int i = 0; i < entries.Length; i++)
            {
                for (int j = i + 1; j < entries.Length; j++)
                {
                    ChordEntry a = entries[i];
                    ChordEntry b = entries[j];
                    if (a.Action != b.Action && a.Modifiers == b.Modifiers && !EverCoLive(entries, a.Action, b.Action, a.Modifiers, reachable))
                    {
                        pair.Clear();
                        pair.Add(a.Action);
                        pair.Add(b.Action);
                        AddDisjointInfo(r, map, reported, keys[k], a.Modifiers, pair);
                    }
                }
            }
        }
    }

    private static void AddDisjointInfo(ValidationReport r, ActionMap map, HashSet<string> reported, PhysicalInput key, ModifierMask mods, List<ushort> pair)
    {
        ImmutableArray<string> names = NamesAscending(map, pair);
        if (!reported.Add($"{(ushort)key}|{(byte)mods}|{names[0]}|{names[1]}"))
        {
            return;
        }

        r.Add(new ValidationEntry(names[0], ValidationFields.Bindings, $"{ModifierMasks.ToText(mods)}{NameOf(key)} is also {names[1]}; they never overlap", names[1])
        {
            Severity = ValidationSeverity.Info,
            Code = ValidationCodes.SharedDisjoint,
            Input = key,
            Modifiers = mods,
            Actions = names,
        });
    }

    private static bool EverCoLive(ReadOnlySpan<ChordEntry> entries, ushort a, ushort b, ModifierMask mods, ReachableSet reachable)
    {
        ReadOnlySpan<ushort> ids = reachable.Ids;
        for (int s = 0; s < ids.Length; s++)
        {
            if (AnyLive(entries, a, mods, ids[s]) && AnyLive(entries, b, mods, ids[s]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyLive(ReadOnlySpan<ChordEntry> entries, ushort action, ModifierMask mods, ushort state)
    {
        foreach (ChordEntry e in entries)
        {
            if (e.Action == action && e.Modifiers == mods && e.Owner.IsLive(state))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ helpers

    private static ImmutableArray<string> NamesAscending(ActionMap map, List<ushort> actions)
    {
        // Ids are small; a selection by ascending id avoids List.Sort and keeps the order canonical.
        var builder = ImmutableArray.CreateBuilder<string>(actions.Count);
        int last = -1;
        for (int n = 0; n < actions.Count; n++)
        {
            int min = int.MaxValue;
            foreach (ushort id in actions)
            {
                if (id > last && id < min)
                {
                    min = id;
                }
            }

            if (min == int.MaxValue)
            {
                break;
            }

            ActionDef? def = map.Get((ushort)min);
            builder.Add(def is null ? $"#{min}" : DisplayName(def));
            last = min;
        }

        return builder.ToImmutable();
    }

    private static InputBinding[] Slots(ActionDef d) => [d.KbmPrimary, d.KbmSecondary, d.PadPrimary, d.PadSecondary];

    private static bool BindsInput(ActionDef d, PhysicalInput p) =>
        d.KbmPrimary.Contains(p) || d.KbmSecondary.Contains(p) || d.PadPrimary.Contains(p) || d.PadSecondary.Contains(p);

    private static string NameOf(PhysicalInput p) => PhysicalInputNames.TryGetName(p, out string n) ? n : $"0x{(ushort)p:X4}";
}
