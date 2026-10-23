using System.Collections.Immutable;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

// Inside the namespace so it wins over the PerformativeMail.Sim.Delivery namespace (manual-delivery 001), which shadows the bare
// name from an outer scope.
using Delivery = PerformativeMail.Client.Core.Input.Delivery;

/// <summary>
/// input-controls story 005 ACs "H-R1-1", "Aliases" and "Frame bits and delivery": the action map loads only valid defs, one
/// fixture per missing field fails with a report entry that names the action id, aliases resolve and collide as specified, and
/// the Frame / Immediate / frame-bit rules hold.
/// </summary>
[Trait("Id", "H-R1-1")]
public class ActionMapTests
{
    private static ActionDef Valid(ushort id = 1, string name = "jump") => new()
    {
        Id = id,
        StringId = name,
        Grammar = Grammar.Tap,
        LiveIn = [NamedStates.World with { CaptureMask = CaptureMask.None }],
        Delivery = Delivery.Immediate,
        KbmPrimary = InputBinding.Single(PhysicalInput.KeySpace),
        KbmSecondary = InputBinding.Unbound,
        PadPrimary = InputBinding.Single(PhysicalInput.PadA),
        PadSecondary = InputBinding.Unbound,
        Reserved = [],
        Required = false,
        OwnerGdd = "design/gdd/player-character-movement.md",
        Aliases = [],
    };

    private static ValidationReport Run(params ActionDef[] defs) => ActionMapValidator.Validate(defs);

    [Fact]
    public void ValidDefs_Load_AndLookupWorks()
    {
        ActionDef a = Valid(1, "jump");
        ActionDef b = Valid(5, "sprint") with { KbmPrimary = InputBinding.Single(PhysicalInput.KeyShift), PadPrimary = InputBinding.Single(PhysicalInput.PadLeftStickClick) };

        bool ok = ActionMap.TryLoad([b, a], out ActionMap? map, out ValidationReport report);

        Assert.True(ok, report.ToString());
        Assert.NotNull(map);
        Assert.Equal(2, map.Count);
        Assert.Same(a, map.Get(1));
        Assert.Same(b, map.Get(5));
        Assert.Null(map.Get(2));
        Assert.Null(map.Get(6));
        Assert.Null(map.Get(ushort.MaxValue));
        Assert.Equal(new ushort[] { 1, 5 }, map.All.Select(d => d.Id).ToArray());
    }

    public static IEnumerable<object[]> MissingFieldFixtures()
    {
        // (fixture name, expected failing field, mutation). The fixture is "jump" unless the mutation renames it.
        yield return Row("ActionId zero", ValidationFields.Id, d => d with { Id = 0 });
        yield return Row("StringId empty", ValidationFields.StringId, d => d with { StringId = string.Empty });
        yield return Row("Grammar none", ValidationFields.Grammar, d => d with { Grammar = Grammar.None });
        yield return Row("Grammar undefined value", ValidationFields.Grammar, d => d with { Grammar = (Grammar)99 });
        yield return Row("Grammar undefined (old flag combination)", ValidationFields.Grammar, d => d with { Grammar = (Grammar)3 | (Grammar)64 });
        yield return Row("LiveIn default", ValidationFields.LiveIn, d => d with { LiveIn = default });
        yield return Row("LiveIn empty", ValidationFields.LiveIn, d => d with { LiveIn = [] });
        yield return Row("LiveIn matches nothing (default predicate)", ValidationFields.LiveIn, d => d with { LiveIn = [default(LivePredicate)] });
        yield return Row("LiveIn only unreachable states", ValidationFields.LiveIn, d => d with { LiveIn = [LivePredicate.Any with { AimingMask = AimingMask.Yes, OverlayMask = OverlayMask.Panel }] });
        yield return Row("Delivery unspecified", ValidationFields.Delivery, d => d with { Delivery = Delivery.Unspecified });
        yield return Row("Delivery undefined value", ValidationFields.Delivery, d => d with { Delivery = (Delivery)99 });
        yield return Row("KBM secondary repeats primary", ValidationFields.KbmSecondary, d => d with { KbmSecondary = d.KbmPrimary });
        yield return Row("Pad secondary repeats primary", ValidationFields.PadSecondary, d => d with { PadSecondary = d.PadPrimary });
        yield return Row("Malformed step pair", ValidationFields.KbmPrimary, d => d with { KbmPrimary = InputBinding.StepPair(PhysicalInput.WheelUp, PhysicalInput.WheelUp) });
        yield return Row("KbmPrimary missing", ValidationFields.KbmPrimary, d => d with { KbmPrimary = default });
        yield return Row("KbmSecondary missing", ValidationFields.KbmSecondary, d => d with { KbmSecondary = default });
        yield return Row("PadPrimary missing", ValidationFields.PadPrimary, d => d with { PadPrimary = default });
        yield return Row("PadSecondary missing", ValidationFields.PadSecondary, d => d with { PadSecondary = default });
        yield return Row("Reserved input not bound", ValidationFields.Reserved, d => d with { Reserved = [PhysicalInput.KeyEscape] });
        yield return Row("Required but unbound (kbm)", ValidationFields.Required, d => d with { Required = true, KbmPrimary = InputBinding.Unbound });
        yield return Row("Required but unbound (pad)", ValidationFields.Required, d => d with { Required = true, PadPrimary = InputBinding.Unbound });
        yield return Row("OwnerGdd null", ValidationFields.OwnerGdd, d => d with { OwnerGdd = null! });
        yield return Row("OwnerGdd empty", ValidationFields.OwnerGdd, d => d with { OwnerGdd = string.Empty });
        yield return Row("Pad input in a KBM slot", ValidationFields.KbmPrimary, d => d with { KbmPrimary = InputBinding.Single(PhysicalInput.PadA) });
        yield return Row("KBM input in a pad slot", ValidationFields.PadPrimary, d => d with { PadPrimary = InputBinding.Single(PhysicalInput.KeyE) });
        yield return Row("Malformed chord", ValidationFields.KbmPrimary, d => d with { KbmPrimary = InputBinding.Chord(PhysicalInput.KeyShift, PhysicalInput.KeyShift) });
        yield return Row("NumpadEnter alias without Enter", ValidationFields.KbmPrimary, d => d with { KbmPrimary = InputBinding.Single(PhysicalInput.KeyE).WithNumpadEnterAlias() });
        yield return Row("TargetDriven without Hold", ValidationFields.TargetDriven, d => d with { TargetDriven = true });
        yield return Row("CommitOnRelease without AimRelease", ValidationFields.CommitOnRelease, d => d with { CommitOnRelease = true });

        static object[] Row(string name, string field, Func<ActionDef, ActionDef> mutate)
        {
            ActionDef def = mutate(Valid(1, "jump"));
            string reportedName = string.IsNullOrEmpty(def.StringId) ? "#" + def.Id : def.StringId;
            return [name, field, reportedName, def];
        }
    }

    [Theory]
    [MemberData(nameof(MissingFieldFixtures))]
    public void Fixture_MissingOrWrongField_FailsAndNamesTheAction(string fixture, string field, string reportedName, ActionDef def)
    {
        ValidationReport report = Run(def);

        Assert.False(report.IsValid, fixture);
        Assert.True(report.Has(reportedName, field), $"{fixture}: expected an entry for {reportedName}.{field}, got:\n{report}");
        Assert.False(ActionMap.TryLoad([def], out ActionMap? map, out _), fixture);
        Assert.Null(map);
    }

    [Fact]
    public void DuplicateActionId_FailsAndNamesBothActions()
    {
        ValidationReport report = Run(Valid(3, "jump"), Valid(3, "sprint"));

        Assert.True(report.Has("jump", ValidationFields.Id), report.ToString());
        Assert.True(report.Has("sprint", ValidationFields.Id), report.ToString());
    }

    [Fact]
    public void DuplicateStringId_FailsAndNamesBothActions()
    {
        ValidationReport report = Run(Valid(1, "jump"), Valid(2, "jump"));

        Assert.Equal(2, report.Entries.Count(e => e.Action == "jump" && e.Field == ValidationFields.StringId));
    }

    [Fact]
    public void TryLoad_WithANullEntry_ReturnsFalse()
    {
        bool ok = ActionMap.TryLoad([Valid(), null], out ActionMap? map, out ValidationReport report);

        Assert.False(ok);
        Assert.Null(map);
        Assert.True(report.Has("(null)", ValidationFields.Def));
    }

    [Fact]
    public void PhysicalInputs_ClassifyByDeviceBlock()
    {
        Assert.True(PhysicalInputs.IsKeyboardMouse(PhysicalInput.KeyW));
        Assert.True(PhysicalInputs.IsKeyboardMouse(PhysicalInput.MouseMotion));
        Assert.True(PhysicalInputs.IsGamepad(PhysicalInput.PadRightTrigger));
        Assert.False(PhysicalInputs.IsGamepad(PhysicalInput.WheelUp));
        Assert.False(PhysicalInputs.IsKeyboardMouse(PhysicalInput.PadA));
        Assert.False(PhysicalInputs.IsGamepad(PhysicalInput.None));
        Assert.False(PhysicalInputs.IsKeyboardMouse(PhysicalInput.None));
        foreach (PhysicalInput input in Enum.GetValues<PhysicalInput>().Where(i => i != PhysicalInput.None))
        {
            Assert.NotEqual(PhysicalInputs.IsGamepad(input), PhysicalInputs.IsKeyboardMouse(input));
        }
    }

    [Fact]
    public void ReportEntry_CanCarryTheOtherActionAndState()
    {
        var report = new ValidationReport();
        report.Add("jump", ValidationFields.KbmPrimary, "conflict", otherAction: "sprint", stateId: 7);

        ValidationEntry e = Assert.Single(report.Entries);
        Assert.Equal("jump", e.Action);
        Assert.Equal("sprint", e.OtherAction);
        Assert.Equal((ushort)7, e.StateId);
        Assert.Contains("sprint", report.ToString());
        Assert.Null(new ValidationEntry("a", "b", "c").OtherAction);
    }

    [Fact]
    public void ValidReport_IsEmpty_AndToStringIsEmpty()
    {
        ValidationReport report = Run(Valid());

        Assert.True(report.IsValid);
        Assert.Empty(report.Entries);
        Assert.Equal(string.Empty, report.ToString());
    }

    [Fact]
    public void DefaultBinding_IsMissing_AndUnboundIsNot()
    {
        Assert.True(default(InputBinding).IsMissing);
        Assert.False(InputBinding.Unbound.IsMissing);
        Assert.True(InputBinding.Unbound.IsUnbound);
        Assert.True(InputBinding.Unbound.IsWellFormed());
        Assert.False(default(InputBinding).IsWellFormed());
    }

    // ------------------------------------------------------------------ aliases

    [Fact]
    public void Resolve_ReturnsTheActionForItsIdOrAnyAlias()
    {
        ActionDef a = Valid(1, "jump") with { Aliases = ["hop", "leap"] };
        ActionDef b = Valid(2, "sprint") with { KbmPrimary = InputBinding.Single(PhysicalInput.KeyShift), PadPrimary = InputBinding.Single(PhysicalInput.PadLeftStickClick) };
        Assert.True(ActionMap.TryLoad([a, b], out ActionMap? map, out ValidationReport report), report.ToString());

        Assert.Same(a, map.Resolve("jump"));
        Assert.Same(a, map.Resolve("hop"));
        Assert.Same(a, map.Resolve("leap"));
        Assert.Same(b, map.Resolve("sprint"));
        Assert.Null(map.Resolve("Jump"));
        Assert.Null(map.Resolve("nope"));
        Assert.Null(map.Resolve(null));
        Assert.Null(map.Resolve(string.Empty));
    }

    [Fact]
    public void Aliases_TwoActionsClaimingTheSameAlias_FailBothNamed()
    {
        ValidationReport report = Run(Valid(1, "jump") with { Aliases = ["hop"] }, Valid(2, "sprint") with { Aliases = ["hop"] });

        Assert.True(report.Has("jump", ValidationFields.Aliases), report.ToString());
        Assert.True(report.Has("sprint", ValidationFields.Aliases), report.ToString());
    }

    [Fact]
    public void Aliases_EqualToAnotherActionsId_Fail()
    {
        ValidationReport report = Run(Valid(1, "jump") with { Aliases = ["sprint"] }, Valid(2, "sprint"));

        Assert.True(report.Has("jump", ValidationFields.Aliases), report.ToString());
        Assert.False(report.Has("sprint"), report.ToString());
    }

    [Fact]
    public void Aliases_EqualToOwnId_RepeatedOrEmpty_Fail()
    {
        Assert.True(Run(Valid(1, "jump") with { Aliases = ["jump"] }).Has("jump", ValidationFields.Aliases));
        Assert.True(Run(Valid(1, "jump") with { Aliases = ["hop", "hop"] }).Has("jump", ValidationFields.Aliases));
        Assert.True(Run(Valid(1, "jump") with { Aliases = [""] }).Has("jump", ValidationFields.Aliases));
    }

    // ------------------------------------------------------------------ frame bits and delivery

    [Fact]
    public void FrameAction_WithoutABit_Fails()
    {
        ValidationReport report = Run(Valid() with { Delivery = Delivery.Frame, FrameBit = null });

        Assert.True(report.Has("jump", ValidationFields.FrameBit), report.ToString());
    }

    [Fact]
    public void ImmediateAction_WithABit_Fails()
    {
        ValidationReport report = Run(Valid() with { Delivery = Delivery.Immediate, FrameBit = FrameAction.Jump });

        Assert.True(report.Has("jump", ValidationFields.FrameBit), report.ToString());
    }

    [Fact]
    public void AxisAction_WithABit_Fails_AndWithoutOneIsValid()
    {
        ActionDef axis = Valid() with { Grammar = Grammar.Axis, Delivery = Delivery.Frame, FrameBit = null };

        Assert.True(Run(axis).IsValid, Run(axis).ToString());
        Assert.True(Run(axis with { FrameBit = FrameAction.Jump }).Has("jump", ValidationFields.FrameBit));
    }

    [Fact]
    public void TwoFrameActions_SharingABit_FailBothNamed()
    {
        ActionDef a = Valid(1, "jump") with { Delivery = Delivery.Frame, FrameBit = FrameAction.Jump };
        ActionDef b = Valid(2, "sprint") with { Delivery = Delivery.Frame, FrameBit = FrameAction.Jump };

        ValidationReport report = Run(a, b);

        Assert.True(report.Has("jump", ValidationFields.FrameBit), report.ToString());
        Assert.True(report.Has("sprint", ValidationFields.FrameBit), report.ToString());
    }

    [Fact]
    public void UndefinedFrameBit_Fails()
    {
        ValidationReport report = Run(Valid() with { Delivery = Delivery.Frame, FrameBit = (FrameAction)200 });

        Assert.True(report.Has("jump", ValidationFields.FrameBit), report.ToString());
    }

    [Fact]
    public void IsLive_IsTheUnionOfThePredicates_AndFalseWhenEmptyOrInvalid()
    {
        LivePredicate world = NamedStates.World;
        LivePredicate panel = NamedStates.Panel;
        ActionDef def = Valid() with { LiveIn = [world, panel] };
        ushort inWorld = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);
        ushort inPanel = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, false, false, SubmenuLayer.None);
        ushort holdingOpen = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.None);

        Assert.True(def.IsLive(inWorld));
        Assert.True(def.IsLive(inPanel));
        Assert.False(def.IsLive(holdingOpen));
        Assert.False(def.IsLive(RouterStateInfo.InvalidId));
        Assert.False((def with { LiveIn = default }).IsLive(inWorld));
        Assert.False((def with { LiveIn = [] }).IsLive(inWorld));
    }

    [Fact]
    public void Binding_ShapesAndAccessors()
    {
        InputBinding wasd = InputBinding.Composite4(PhysicalInput.KeyW, PhysicalInput.KeyS, PhysicalInput.KeyA, PhysicalInput.KeyD);
        InputBinding chord = InputBinding.Chord(PhysicalInput.KeyShift, PhysicalInput.MouseLeft);
        InputBinding stick = InputBinding.AxisPair(PhysicalInput.PadLeftStickX, PhysicalInput.PadLeftStickY);
        InputBinding enter = InputBinding.Single(PhysicalInput.KeyEnter).WithNumpadEnterAlias();
        InputBinding step = InputBinding.StepPair(PhysicalInput.WheelUp, PhysicalInput.WheelDown);

        Assert.Equal(BindingKind.StepPair, step.Kind);
        Assert.Equal(PhysicalInput.WheelUp, step.Previous);
        Assert.Equal(PhysicalInput.WheelDown, step.Next);
        Assert.Equal(2, step.InputCount);
        Assert.True(step.IsWellFormed());
        Assert.Equal(4, wasd.InputCount);
        Assert.Equal(PhysicalInput.KeyW, wasd.Up);
        Assert.Equal(PhysicalInput.KeyD, wasd.Right);
        Assert.Equal(PhysicalInput.KeyShift, chord.Modifier);
        Assert.Equal(PhysicalInput.MouseLeft, chord.Key);
        Assert.Equal(PhysicalInput.PadLeftStickY, stick.AxisY);
        Assert.True(enter.AcceptsNumpadEnter);
        Assert.False(InputBinding.Single(PhysicalInput.KeyEnter).AcceptsNumpadEnter);
        Assert.NotEqual(InputBinding.Single(PhysicalInput.KeyEnter), enter);
        Assert.True(wasd.Contains(PhysicalInput.KeyS));
        Assert.False(wasd.Contains(PhysicalInput.KeyE));
        Assert.Equal("Key.Shift+Mouse.Left", chord.ToString());
        Assert.Equal("Key.Enter (+NumpadEnter)", enter.ToString());
        Assert.Equal(wasd.GetHashCode(), InputBinding.Composite4(PhysicalInput.KeyW, PhysicalInput.KeyS, PhysicalInput.KeyA, PhysicalInput.KeyD).GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => chord.InputAt(2));
        Assert.False(InputBinding.Single(PhysicalInput.None).IsWellFormed());
        Assert.False(InputBinding.Single((PhysicalInput)0xFFFF).IsWellFormed());
    }
}
