using System.Collections.Immutable;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 006 AC H-R4-1 (table-driven over every reachable state x every bound input, after
/// most-specific-chord resolution, Back counted once), the binding-shape rules, H-EC-15 and the report type.
/// </summary>
[Trait("Id", "H-R4-1")]
public class ConflictValidatorTests
{
    private static InputBinding S(PhysicalInput i) => InputBinding.Single(i);

    // ------------------------------------------------------------------ the built-in map

    [Fact]
    public void BuiltInMap_ReportsZeroErrorsAndZeroWarnings_ThroughTheBootEntryPoint()
    {
        ValidationReport report = ActionMapValidator.ValidateForBoot(BuiltInActionMap.Definitions(), out ActionMap? map);

        Assert.True(report.IsValid, report.ToString());
        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
        Assert.NotNull(map);
        Assert.Empty(ActionMapValidator.Validate(map, ReachableSet.Default).Errors);
    }

    [Fact]
    public void BuiltInMap_EveryReachableStateAndInput_ResolvesToAtMostOneLiveAction_IndependentRecount()
    {
        // An independent recount over the raw def bindings (not the resolver's index): plain bindings only, ladder steps as
        // Back. Chorded inputs are covered by the resolver tests.
        ActionMap map = BuiltInActionMap.CreateM1a();
        foreach (ushort state in ReachableSet.Default.Ids.ToArray())
        {
            foreach (PhysicalInputName named in PhysicalInputNames.Table.ToArray())
            {
                var live = new HashSet<ushort>();
                foreach (ActionDef d in map.All)
                {
                    if (!d.IsLive(state))
                    {
                        continue;
                    }

                    foreach (InputBinding b in new[] { d.KbmPrimary, d.KbmSecondary, d.PadPrimary, d.PadSecondary })
                    {
                        bool numpad = named.Input == KeyNumpadEnter && b.AcceptsNumpadEnter && b.Contains(KeyEnter);
                        if (b.Kind != BindingKind.Chord && (b.Contains(named.Input) || numpad))
                        {
                            live.Add(b.IsBackLadderStep ? ActionId.Back : d.Id);
                        }
                    }
                }

                Assert.True(live.Count <= 1, $"{named.Name} in {RouterState.ToDebugString(state)}: {string.Join(", ", live.Select(id => map.Get(id)!.StringId))}");
            }
        }
    }

    [Fact]
    public void BuiltInMap_CapturedStates_AreIncluded_OnlyPauseAndBackLive()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();

        Assert.Equal(ActionId.Back, map.Chords.Resolve(ModifierMask.None, KeyEscape, TextEntryWorldId));
        Assert.Equal(ActionId.Pause, map.Chords.Resolve(ModifierMask.None, KeyP, TextEntryWorldId));
        Assert.Equal(ActionId.None, map.Chords.Resolve(ModifierMask.None, KeyE, TextEntryWorldId));
    }

    [Fact]
    public void BuiltInMap_SharedBetweenDisjointStates_ProducesInfoEntries()
    {
        ValidationReport report = ActionMapValidator.Validate(BuiltInActionMap.CreateM1a(), ReachableSet.Default);

        Assert.Contains(report.Infos, e => e.Code == ValidationCodes.SharedDisjoint && e.Input == PadA && e.Actions.SequenceEqual(["jump", "pick_place"]));
        Assert.Contains(report.Infos, e => e.Input == KeyTab && e.Actions.SequenceEqual(["open_panel", "close_panel"]));
        Assert.All(report.Infos, e => Assert.Equal(ValidationSeverity.Info, e.Severity));
    }

    // ------------------------------------------------------------------ planted conflicts

    [Fact]
    public void SpaceOnJumpAndPickPlace_IsLegal_WithAnInfoLine()
    {
        ValidationReport report = Validate(Edited(ActionId.PickPlace, d => d with { KbmSecondary = S(KeySpace) }));

        Assert.True(report.IsValid, report.ToString());
        ValidationEntry info = Assert.Single(report.Infos, e => e.Input == KeySpace);
        Assert.Equal(ValidationCodes.SharedDisjoint, info.Code);
        Assert.Equal(["jump", "pick_place"], info.Actions);
        Assert.Contains("never overlap", info.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowAndInteractOnOneKey_IsAViolationInWorld_HEC15()
    {
        ValidationReport report = Validate(Edited(ActionId.Throw, d => d with { KbmPrimary = S(KeyE) }));

        Assert.Contains(report.ErrorsWith(ValidationCodes.ConflictColive), e =>
            e.StateId == WorldId && e.Input == KeyE && e.Actions.SequenceEqual(["interact", "throw"]));
    }

    [Fact]
    public void TwoActionsOnTheSameChordInACoLiveState_Conflict()
    {
        ValidationReport report = Validate(Edited(ActionId.HotbarSlot1, d => d with { KbmSecondary = InputBinding.Chord(KeyShift, MouseLeft) }));

        Assert.Contains(report.ErrorsWith(ValidationCodes.ConflictColive), e =>
            e.StateId == PanelId && e.Input == MouseLeft && e.Modifiers == ModifierMask.Shift && e.Actions.SequenceEqual(["hotbar_slot_1", "quick_move"]));
    }

    [Fact]
    public void ChordOnAKeyThatIsAlsoALiveHold_ReportsTapHoldShare_AndShiftAsModifierConflictsWithSprint()
    {
        // Drop on Shift+E in World: Interact holds E (resolved by chord, but a tap and a hold share E), and Shift is Sprint
        // there while also being Drop's modifier (R4.1 "including when used as a modifier").
        ValidationReport report = Validate(Edited(ActionId.Drop, d => d with { KbmPrimary = InputBinding.Chord(KeyShift, KeyE) }));

        Assert.Contains(report.ErrorsWith(ValidationCodes.TapHoldShare), e =>
            e.StateId == WorldId && e.Input == KeyE && e.Actions.SequenceEqual(["interact", "drop"]));
        Assert.Contains(report.ErrorsWith(ValidationCodes.ConflictColive), e =>
            e.StateId == WorldId && e.Input == KeyShift && e.Actions.SequenceEqual(["sprint", "drop"]));
    }

    [Fact]
    public void PauseEscWithoutTheLadderFlag_ConflictsWithBack_InEveryState()
    {
        ValidationReport report = Validate(Edited(ActionId.Pause, d => d with { KbmPrimary = S(KeyEscape) }));

        List<ValidationEntry> esc = report.ErrorsWith(ValidationCodes.ConflictColive).Where(e => e.Input == KeyEscape).ToList();
        Assert.Equal(ReachableSet.Default.Count, esc.Count);
        Assert.All(esc, e => Assert.Equal(["pause", "back"], e.Actions));
        Assert.Contains(esc, e => e.StateId == TextEntryWorldId);
    }

    [Fact]
    public void LadderStepFlag_OnAnInputThatIsNotBacks_IsRejected()
    {
        ValidationReport report = Validate(Edited(ActionId.Drop, d => d with { KbmPrimary = S(KeyQ).WithBackLadderStep() }));

        ValidationEntry e = Assert.Single(report.ErrorsWith(ValidationCodes.LadderStep));
        Assert.Equal("drop", e.Action);
        Assert.Equal(KeyQ, e.Input);
    }

    [Fact]
    public void BuiltInLadderSteps_AreExactlyPauseEsc_ClosePanelB_CancelReturnB()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();
        var steps = map.All
            .SelectMany(d => new[] { d.KbmPrimary, d.KbmSecondary, d.PadPrimary, d.PadSecondary }.Where(b => b.IsBackLadderStep).Select(b => $"{d.StringId}:{b.Input}"))
            .ToArray();

        Assert.Equal(["pause:KeyEscape", "cancel_return:PadB", "close_panel:PadB"], steps);
        Assert.Equal(ActionId.Back, map.Chords.Resolve(ModifierMask.None, PadB, HoldingOpenId));
        Assert.Equal(ActionId.CancelReturn, map.Chords.Resolve(ModifierMask.None, MouseRight, HoldingOpenId));
    }

    // ------------------------------------------------------------------ binding shapes

    [Fact]
    public void PadChord_IsRejected()
    {
        ValidationReport report = Validate(Edited(ActionId.QuickMove, d => d with { PadSecondary = InputBinding.Chord(PadLeftBumper, PadA) }));

        ValidationEntry e = Assert.Single(report.ErrorsWith(ValidationCodes.ChordShape));
        Assert.Equal("quick_move", e.Action);
        Assert.Equal(ValidationFields.PadSecondary, e.Field);
    }

    [Fact]
    public void KbmChordOfTwoNonModifiers_IsRejected()
    {
        ValidationReport report = Validate(Edited(ActionId.QuickMove, d => d with { KbmPrimary = InputBinding.Chord(KeyQ, MouseLeft) }));

        ValidationEntry e = Assert.Single(report.ErrorsWith(ValidationCodes.ChordShape));
        Assert.Equal(ValidationFields.KbmPrimary, e.Field);
        Assert.Contains("non-modifier", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WasdAndArrows_SharingAKey_IsRejected()
    {
        ValidationReport report = Validate(Edited(ActionId.GridCursor, d => d with { KbmPrimary = S(KeyW) }));

        ValidationEntry e = Assert.Single(report.ErrorsWith(ValidationCodes.MoveCursorShare));
        Assert.Equal(KeyW, e.Input);
        Assert.Equal(["move", "grid_cursor"], e.Actions);
    }

    [Fact]
    public void EscOnANonBackNonPauseAction_IsRejected()
    {
        ValidationReport report = Validate(Edited(ActionId.Jump, d => d with { KbmSecondary = S(KeyEscape) }));

        Assert.Contains(report.ErrorsWith(ValidationCodes.ReservedInput), e => e.Action == "jump" && e.Input == KeyEscape);
    }

    [Fact]
    public void MenuOnANonPauseAction_IsRejected()
    {
        ValidationReport report = Validate(Edited(ActionId.Jump, d => d with { PadSecondary = S(PadMenu) }));

        Assert.Contains(report.ErrorsWith(ValidationCodes.ReservedInput), e => e.Action == "jump" && e.Input == PadMenu);
    }

    // ------------------------------------------------------------------ report type

    [Fact]
    public void Report_SerialisesToText_WithCodeStateInputAndActions()
    {
        // Drop bound to E: Validate also asserts that the load fails and yields no map.
        ValidationReport report = Validate(Edited(ActionId.Drop, d => d with { KbmPrimary = S(KeyE) }));
        string text = report.ToString();

        Assert.False(report.IsValid);
        Assert.Contains(report.ErrorsWith(ValidationCodes.ConflictColive), e =>
            e.StateId == WorldId && e.Input == KeyE && e.Modifiers == ModifierMask.None && e.Actions.SequenceEqual(["interact", "drop"]));
        Assert.True(report.HasError(ValidationCodes.ConflictColive, "drop"));

        Assert.Contains("ERROR CONFLICT_COLIVE interact.Bindings:", text, StringComparison.Ordinal);
        Assert.Contains("[interact, drop]", text, StringComparison.Ordinal);
        Assert.Contains("(input Key.E)", text, StringComparison.Ordinal);
        Assert.Contains("(state " + RouterState.ToDebugString(WorldId) + ")", text, StringComparison.Ordinal);
        Assert.Contains("INFO SHARED_DISJOINT", text, StringComparison.Ordinal);
        Assert.Equal(report.Entries.Count, report.Errors.Count + report.Warnings.Count + report.Infos.Count);
    }

    [Fact]
    public void FieldFailures_KeepTheFieldCode_AndBlockBoot()
    {
        ImmutableArray<ActionDef> defs = Edited(ActionId.Jump, d => d with { OwnerGdd = string.Empty });

        ValidationReport report = ActionMapValidator.ValidateForBoot(defs, out ActionMap? map);

        Assert.Null(map);
        ValidationEntry e = Assert.Single(report.Errors);
        Assert.Equal(ValidationCodes.Field, e.Code);
        Assert.Equal(ValidationSeverity.Error, e.Severity);
    }

    [Fact]
    [Trait("Category", "Advisory")]
    public void Validation_AtLoad_ElapsedTime_IsWrittenToOutput_Advisory()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();
        _ = ActionMapValidator.Validate(map, ReachableSet.Default); // warm-up (JIT)

        long best = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _ = ActionMapValidator.Validate(map, ReachableSet.Default);
            best = Math.Min(best, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        // Advisory guardrail (ADR-0012 section 2, target < 5 ms): reported, never asserted, so a loaded machine cannot flake it.
        double ms = best * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        TestContext.Current.TestOutputHelper?.WriteLine($"reachable-state validation of the M-1a map: best of 5 = {ms:F3} ms (advisory target < 5 ms)");
        Assert.True(best >= 0);
    }

    // ------------------------------------------------------------------ gate round: ladder rules, NumpadEnter, chord recount, codes

    [Fact]
    public void PadLadderStepOnPause_IsRejected_BNeverPauses()
    {
        ValidationReport report = Validate(Edited(ActionId.Pause, d => d with { PadSecondary = S(PadB).WithBackLadderStep() }));

        ValidationEntry e = Assert.Single(report.ErrorsWith(ValidationCodes.LadderStep));
        Assert.Equal("pause", e.Action);
        Assert.Equal(ValidationFields.PadSecondary, e.Field);
        Assert.Contains("B never pauses", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LadderStepOwnerLiveWhereBackIsNot_IsRejected()
    {
        // Back live only in World: Close panel's pad-B step is live in Panel, where no Back handler would receive the press.
        LivePredicate worldOnly = NamedStates.World with { CaptureMask = CaptureMask.None };
        ValidationReport report = Validate(Edited(ActionId.Back, d => d with { LiveIn = [worldOnly] }));

        Assert.Contains(report.ErrorsWith(ValidationCodes.LadderStep), e => e.Action == "close_panel" && e.Field == ValidationFields.PadPrimary && e.Message.Contains("not live", StringComparison.Ordinal));
        Assert.Contains(report.ErrorsWith(ValidationCodes.LadderStep), e => e.Action == "cancel_return");
        Assert.Contains(report.ErrorsWith(ValidationCodes.LadderStep), e => e.Action == "pause");
    }

    [Fact]
    public void LadderFlagOnAChord_IsRejected()
    {
        ValidationReport report = Validate(Edited(ActionId.Drop, d => d with { KbmPrimary = InputBinding.Chord(KeyShift, KeyQ).WithBackLadderStep() }));

        ValidationEntry e = Assert.Single(report.ErrorsWith(ValidationCodes.LadderStep));
        Assert.Equal("drop", e.Action);
        Assert.Contains("single input", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatLikeTapOnPlainEnter_DoesNotConflictOnNumpadEnter_BecauseTheAliasBelongsToTheBindingNotTheKey()
    {
        // GDD Edge Cases: NumpadEnter is an alias of the default Enter binding; remapping treats it as a distinct key. A new
        // binding on Enter without the alias flag does not take NumpadEnter, so Pick / place keeps it alone.
        ValidationReport report = Validate(Edited(ActionId.PickPlace, d => d, ChatLike(S(KeyEnter))));

        Assert.Contains(report.ErrorsWith(ValidationCodes.ConflictColive), e =>
            e.StateId == PanelId && e.Input == KeyEnter && e.Actions.SequenceEqual(["pick_place", "chat_like"]));
        Assert.DoesNotContain(report.Errors, e => e.Input == KeyNumpadEnter);
    }

    [Fact]
    public void ChatLikeTapOnEnterWithTheNumpadAlias_ConflictsOnNumpadEnterToo()
    {
        ValidationReport report = Validate(Edited(ActionId.PickPlace, d => d, ChatLike(S(KeyEnter).WithNumpadEnterAlias())));

        Assert.Contains(report.ErrorsWith(ValidationCodes.ConflictColive), e =>
            e.StateId == PanelId && e.Input == KeyNumpadEnter && e.Actions.SequenceEqual(["pick_place", "chat_like"]));
    }

    [Fact]
    public void BuiltInMap_EveryChordInEveryReachableState_IndependentRecount()
    {
        // Independent of the resolver's index: for each KBM chord in the raw defs (Shift+Enter, Shift+NumpadEnter, Shift+LMB),
        // count the live actions bound to exactly that chord; at most one, and Resolve with Shift held must return it, or the
        // single live plain binding on the key when no chord is live.
        ActionMap map = BuiltInActionMap.CreateM1a();
        var chords = new List<(PhysicalInput Mod, PhysicalInput Key)>();
        foreach (ActionDef d in map.All)
        {
            foreach (InputBinding b in new[] { d.KbmPrimary, d.KbmSecondary })
            {
                if (b.Kind == BindingKind.Chord)
                {
                    chords.Add((b.Modifier, b.Key));
                    if (b.AcceptsNumpadEnter && b.Key == KeyEnter)
                    {
                        chords.Add((b.Modifier, KeyNumpadEnter));
                    }
                }
            }
        }

        Assert.Equal([(KeyShift, MouseLeft), (KeyShift, KeyEnter), (KeyShift, KeyNumpadEnter)], chords);
        foreach (ushort state in ReachableSet.Default.Ids.ToArray())
        {
            foreach ((PhysicalInput mod, PhysicalInput key) in chords)
            {
                HashSet<ushort> chorded = LiveOn(map, state, key, chord: true);
                HashSet<ushort> plain = LiveOn(map, state, key, chord: false);
                Assert.True(chorded.Count <= 1 && plain.Count <= 1, $"{mod}+{key} in {RouterState.ToDebugString(state)}");
                ushort expected = chorded.Count == 1 ? chorded.Single() : plain.Count == 1 ? plain.Single() : ActionId.None;
                Assert.Equal(expected, map.Chords.Resolve(ModifierMask.Shift, key, state));
            }
        }
    }

    private static HashSet<ushort> LiveOn(ActionMap map, ushort state, PhysicalInput key, bool chord)
    {
        var live = new HashSet<ushort>();
        foreach (ActionDef d in map.All.Where(d => d.IsLive(state)))
        {
            foreach (InputBinding b in new[] { d.KbmPrimary, d.KbmSecondary, d.PadPrimary, d.PadSecondary })
            {
                bool hitsKey = key == KeyNumpadEnter ? b.AcceptsNumpadEnter && b.Contains(KeyEnter) : b.Contains(key);
                bool isChordOnKey = b.Kind == BindingKind.Chord && b.Modifier == KeyShift && (b.Key == key || (key == KeyNumpadEnter && b.Key == KeyEnter && b.AcceptsNumpadEnter));
                if (chord ? isChordOnKey : b.Kind != BindingKind.Chord && hitsKey)
                {
                    live.Add(b.IsBackLadderStep ? ActionId.Back : d.Id);
                }
            }
        }

        return live;
    }

    [Fact]
    public void ValidationCodes_EveryConstantIsPinnedToItsStableLiteral()
    {
        (string Name, string Literal)[] expected =
        [
            ("ChordShape", "CHORD_SHAPE"),
            ("ConflictColive", "CONFLICT_COLIVE"),
            ("Field", "FIELD"),
            ("GrammarTarget", "GRAMMAR_TARGET"),
            ("LadderStep", "LADDER_STEP"),
            ("MoveCursorShare", "MOVE_CURSOR_SHARE"),
            ("PadPath", "PAD_PATH"),
            ("ReservedInput", "RESERVED_INPUT"),
            ("SharedDisjoint", "SHARED_DISJOINT"),
            ("TapHoldShare", "TAP_HOLD_SHARE"),
        ];

        (string Name, string Literal)[] actual = typeof(ValidationCodes).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }
}
