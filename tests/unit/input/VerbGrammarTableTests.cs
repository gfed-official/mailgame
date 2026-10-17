using PerformativeMail.Client.Core.Input;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>input-controls story 006 AC H-R4-5: one verb with one grammar per interactable target type (GDD R4.2).</summary>
[Trait("Id", "H-R4-5")]
public class VerbGrammarTableTests
{
    [Fact]
    public void V1Table_MapsEachTargetTypeToTheGddVerbAndGrammar()
    {
        (string Target, string Verb, Grammar Grammar)[] expected =
        [
            (TargetTypes.Container, "open", Grammar.Tap),
            (TargetTypes.VehicleSeat, "enter", Grammar.Tap),
            (TargetTypes.Turret, "mount", Grammar.Tap),
            (TargetTypes.ShopCounter, "shop", Grammar.Tap),
            (TargetTypes.Console, "use", Grammar.Tap),
            (TargetTypes.Construct, "configure", Grammar.Tap),
            (TargetTypes.WorldItem, "pick_up", Grammar.Tap),
            (TargetTypes.Mailbox, "deliver", Grammar.Hold),
            (TargetTypes.Destination, "deliver", Grammar.Hold),
            (TargetTypes.InboundCatchable, "catch", Grammar.HeldState),
        ];

        Assert.Equal(expected.Length, VerbGrammarTable.V1.Rows.Length);
        foreach ((string target, string verb, Grammar grammar) in expected)
        {
            Assert.True(VerbGrammarTable.V1.TryGet(target, out VerbGrammarRow row), target);
            Assert.Equal(verb, row.Verb);
            Assert.Equal(grammar, row.Grammar);
        }
    }

    [Fact]
    public void V1Table_Validates()
    {
        ValidationReport report = VerbGrammarTable.V1.Validate();

        Assert.True(report.IsValid, report.ToString());
        Assert.Empty(report.Entries);
    }

    [Fact]
    public void TwoVerbsForOneTargetType_FailsValidation_NamingTheTargetType()
    {
        VerbGrammarRow[] rows = [.. VerbGrammarTable.V1.Rows, new(TargetTypes.Mailbox, "open", Grammar.Tap)];

        ValidationReport report = VerbGrammarTable.Validate(rows);

        ValidationEntry e = Assert.Single(report.Errors);
        Assert.Equal(ValidationCodes.GrammarTarget, e.Code);
        Assert.Equal(TargetTypes.Mailbox, e.Target);
        Assert.Contains("'mailbox'", e.Message, StringComparison.Ordinal);
        Assert.Equal(["deliver (Hold)", "open (Tap)"], e.Actions);
        Assert.Contains("(target mailbox)", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADisallowedGrammar_OrAnEmptyRow_FailsValidation()
    {
        ValidationReport report = VerbGrammarTable.Validate(
        [
            new(TargetTypes.Turret, "aim", Grammar.AimRelease),
            new(string.Empty, "open", Grammar.Tap),
        ]);

        Assert.Equal(2, report.Errors.Count);
        Assert.All(report.Errors, e => Assert.Equal(ValidationCodes.GrammarTarget, e.Code));
        Assert.Equal(TargetTypes.Turret, report.Errors[0].Target);
    }

    [Fact]
    public void UnknownTargetType_IsNotFound()
    {
        Assert.False(VerbGrammarTable.V1.TryGet("truck", out _));
    }
}
