using PerformativeMail.Client.Core.Input;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 006 AC H-FX-3a (pad-path clause): every registered action has a pad binding, a declared pad path or
/// an exemption, and the exemption list is exactly Chat, Push-to-talk and Read prompt.
/// </summary>
[Trait("Id", "H-FX-3a")]
public class PadPathTests
{
    [Fact]
    public void ExemptionList_IsExactlyChatPushToTalkAndReadPrompt()
    {
        Assert.Equal(["chat", "push_to_talk", "read_prompt"], PadPathTable.M1a.Exemptions);
        Assert.Equal(PadPathTable.V1Exemptions, PadPathTable.M1a.Exemptions);
    }

    [Fact]
    public void NoExemptAction_IsRegisteredAtM1a()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();

        Assert.All(PadPathTable.M1a.Exemptions, id => Assert.Null(map.Resolve(id)));
    }

    [Fact]
    public void BuiltInMap_HasNoPadPathErrors_AndTheHotbarDigitsGoThroughHotbarStep()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();
        ValidationReport report = ActionMapValidator.Validate(map, ReachableSet.Default);

        Assert.Empty(report.ErrorsWith(ValidationCodes.PadPath));
        foreach (ActionDef d in map.All.Where(d => !PadPathTable.HasPadBinding(d)))
        {
            Assert.StartsWith("hotbar_slot_", d.StringId, StringComparison.Ordinal);
            Assert.True(PadPathTable.M1a.TryGetPath(d.StringId, out PadPathEntry path));
            Assert.Equal(PadPathKind.ViaAction, path.Kind);
            Assert.Equal("hotbar_step", path.ViaActionId);
        }
    }

    [Fact]
    public void JumpWithoutAPadBinding_IsReported_EvenWithTheExemptionListPresent()
    {
        ValidationReport report = Validate(Edited(ActionId.Jump, d => d with { PadPrimary = InputBinding.Unbound }));

        ValidationEntry e = Assert.Single(report.ErrorsWith(ValidationCodes.PadPath));
        Assert.Equal("jump", e.Action);
    }

    [Fact]
    public void ARegisteredExemptAction_WithoutAPadBinding_IsNotReported()
    {
        ValidationReport report = Validate(Edited(ActionId.Jump, d => d, ChatLike(InputBinding.Single(KeyF), id: "chat")));

        Assert.Empty(report.ErrorsWith(ValidationCodes.PadPath));
    }

    [Fact]
    public void AnActionMenuPath_CoversAnActionWithoutAPadBinding()
    {
        ActionDef[] extra = [ChatLike(InputBinding.Single(KeyF), id: "y_menu_verb")];
        var table = new PadPathTable([.. PadPathTable.M1a.Paths, new PadPathEntry("y_menu_verb", PadPathKind.ActionMenu)], PadPathTable.V1Exemptions);

        ValidationReport withPath = Validate(Edited(ActionId.Jump, d => d, extra), table);
        ValidationReport without = Validate(Edited(ActionId.Jump, d => d, extra));

        Assert.Empty(withPath.ErrorsWith(ValidationCodes.PadPath));
        Assert.Contains(without.ErrorsWith(ValidationCodes.PadPath), e => e.Action == "y_menu_verb");
    }

    [Fact]
    public void APadPathThroughAnActionWithoutAPadBinding_IsReported()
    {
        ValidationReport report = Validate(Edited(ActionId.HotbarStep, d => d with { PadPrimary = InputBinding.Unbound }));

        List<ValidationEntry> errors = report.ErrorsWith(ValidationCodes.PadPath);
        Assert.Equal(9, errors.Count); // hotbar_step itself and the eight digits whose path goes through it
        Assert.Contains(errors, e => e.Action == "hotbar_step");
        Assert.Contains(errors, e => e.Action == "hotbar_slot_8" && e.Message.Contains("hotbar_step", StringComparison.Ordinal));
    }
}
