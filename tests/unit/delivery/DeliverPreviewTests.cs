using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 002 (GDD MD3, F1; TR-delivery-003, TR-delivery-013; ADR-0016 section 3): <see cref="DeliverPreview.Preview"/>
/// over an in-memory panel. FX-MB is house #17 at 17 Elm St, OPEN, shift 3, wallet 0; FX-L is a letter worth 8 ¢ (penalty 4 ¢); FX-W
/// is a floor of −500. Sheets are real (<see cref="StatSheet.Build"/> over the M-1a content). AC-5's order and open-chest halves are
/// in <see cref="DeliverScanTests"/>.
/// </summary>
public class DeliverPreviewTests
{
    private const int Shift = 3;
    private const int HandsSlot = 0;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();
    private static readonly AddressId _elm17 = AddressId.Pack(1, 1, 17);
    private static readonly AddressId _larch14 = AddressId.Pack(1, 2, 14);
    private static readonly ushort _letter = KindIndex("letter");

    [Theory]
    [Trait("Id", "M-MD3-1")]
    [InlineData("closed", DeliverBranchKind.Reject)]
    [InlineData("backpack-only", DeliverBranchKind.Match)]
    [InlineData("selected-only", DeliverBranchKind.Match)]
    [InlineData("cursor-only", DeliverBranchKind.Match)]
    [InlineData("hands-selected", DeliverBranchKind.Match)]
    [InlineData("selected-larch", DeliverBranchKind.MisdeliverOne)]
    [InlineData("tool-selected", DeliverBranchKind.Nothing)]
    public void Preview_FxPRows_GiveTheMd3Branch_AndClosedScansNothing(string row, DeliverBranchKind expected)
    {
        // Arrange
        var panel = new InMemoryPanelView();
        DestState state = DestState.Open;
        int selected = HandsSlot;
        switch (row)
        {
            case "closed":
                state = DestState.Closed;
                panel.Hotbar[1] = Letter(panel, _elm17);
                panel.Inventory.Add(Letter(panel, _elm17));
                break;
            case "backpack-only":
                panel.Backpack.Add(Letter(panel, _elm17));
                break;
            case "selected-only":
                selected = 3;
                panel.Hotbar[3] = Letter(panel, _elm17);
                panel.Inventory.Add(Letter(panel, _larch14));
                break;
            case "cursor-only":
                panel.Cursor = Letter(panel, _elm17);
                break;
            case "hands-selected":
                panel.Inventory.Add(Letter(panel, _elm17));
                break;
            case "selected-larch":
                selected = 2;
                panel.Hotbar[2] = Letter(panel, _larch14);
                break;
            case "tool-selected":
                selected = 1;
                panel.Hotbar[1] = panel.Tool(ItemIndex("axe"));
                panel.Inventory.Add(Letter(panel, _larch14));
                break;
        }

        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = Preview(House(state), panel, selected, wallet: 0, ref scratch);

        // Assert
        Assert.Equal(expected, b.Kind);
        if (row == "closed")
        {
            Assert.Equal(MailRejectReason.Closed, b.RejectReason);
            Assert.Equal(0, panel.Reads);
            Assert.Equal(0, scratch.ScannedEntries);
        }
        else if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(1, scratch.Evaluations);
            Assert.True(scratch.ScannedEntries > 0);
        }

        if (row == "selected-only")
        {
            Assert.Equal(panel.Hotbar[3].EntryId, b.Entry(0));
        }
    }

    [Fact]
    [Trait("Id", "M-MD3-2")]
    public void Preview_TwoDeadLetters_IsMatchWithDeadOnly_AndPaysNothing()
    {
        // Arrange: 2 matching letters, value 9, deadline 1, at shift 3
        var panel = new InMemoryPanelView();
        panel.Inventory.Add(panel.Mail(_letter, _elm17, 0, (1, 2, 9)));
        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = Preview(House(DestState.Open), panel, HandsSlot, wallet: 0, ref scratch);

        // Assert
        Assert.Equal(DeliverBranchKind.Match, b.Kind);
        Assert.True(b.DeadOnly);
        Assert.Equal(0, b.PredictedPay.Value);
    }

    [Theory]
    [Trait("Id", "M-MD3-2")]
    [InlineData((byte)MailFlags.Cursed, true)]
    [InlineData((byte)MailFlags.None, false)]
    public void Preview_SelectedNonMatch_ReportsCursedFromItsFlags(byte flags, bool cursed)
    {
        // Arrange
        var panel = new InMemoryPanelView();
        panel.Hotbar[4] = panel.Mail(_letter, _larch14, flags, (Shift, 1, 8));
        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = Preview(House(DestState.Open), panel, 4, wallet: 0, ref scratch);

        // Assert
        Assert.Equal(DeliverBranchKind.MisdeliverOne, b.Kind);
        Assert.Equal(cursed, b.Cursed);
    }

    [Fact]
    [Trait("Id", "M-F1-1")]
    public void Preview_F1Example_ThreeEntries_Give16Units_105Cents_3Late_2Dead()
    {
        // Arrange: A 6 x (8, deadline 3); B runs 3 x (8, deadline 2) + 2 x (9, deadline 1); C 5 x (9, deadline 3)
        var panel = new InMemoryPanelView();
        panel.Hotbar[1] = panel.Mail(_letter, _elm17, 0, (3, 6, 8));
        panel.Inventory.Add(panel.Mail(_letter, _elm17, 0, (2, 3, 8), (1, 2, 9)));
        panel.Backpack.Add(panel.Mail(_letter, _elm17, 0, (3, 5, 9)));
        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = Preview(House(DestState.Open), panel, HandsSlot, wallet: 0, ref scratch);

        // Assert
        Assert.Equal(DeliverBranchKind.Match, b.Kind);
        Assert.Equal(3, b.K);
        Assert.Equal(16, b.N);
        Assert.Equal(105, b.PredictedPay.Value);
        Assert.Equal(3, b.LateUnits);
        Assert.Equal(2, b.DeadUnits);
        Assert.False(b.DeadOnly);
    }

    [Theory]
    [Trait("Id", "M-F1-1")]
    [InlineData("none", 8)]
    [InlineData("stamp-collector", 10)]
    [InlineData("cannon-arm", 8)]
    public void Preview_OnTimeLetterWorth8_PaysPerTheSheet_AndCannonArmNeverAppliesToAHand(string sheet, long expected)
    {
        // Arrange: one on-time letter worth 8 at #17
        IStatSheet stats = sheet switch
        {
            "stamp-collector" => Sheet(Team(Stat.MailValueMultPm, 1250, _letter)),
            "cannon-arm" => Sheet(Mine(Stat.ThrownCorrectPayMultPm, 1100)),
            _ => Sheet(),
        };
        var panel = new InMemoryPanelView();
        panel.Inventory.Add(panel.Mail(_letter, _elm17, 0, (Shift, 1, 8)));
        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = Preview(House(DestState.Open), panel, HandsSlot, wallet: 0, ref scratch, stats);

        // Assert
        Assert.Equal(expected, b.PredictedPay.Value);
    }

    [Theory]
    [Trait("Id", "M-F1-2")]
    [InlineData(new[] { 8 }, 0, 4, false)]
    [InlineData(new[] { 8, 12 }, 0, 4, false)]
    [InlineData(new[] { 1 }, 0, 1, false)]
    [InlineData(new[] { 8 }, -496, 4, false)]
    [InlineData(new[] { 8 }, -497, 4, true)]
    public void Preview_SelectedMisdelivery_PricesRun0_AndRefusesBelowTheFloor(int[] runValues, long wallet, long penalty, bool refused)
    {
        // Arrange: the selected letter is for 14 Larch Lane; its runs are oldest first
        var runs = new (int, int, int)[runValues.Length];
        for (int i = 0; i < runValues.Length; i++)
        {
            runs[i] = (Shift, 1, runValues[i]);
        }

        var panel = new InMemoryPanelView();
        panel.Hotbar[5] = panel.Mail(_letter, _larch14, 0, runs);
        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = Preview(House(DestState.Open), panel, 5, wallet, ref scratch);

        // Assert
        Assert.Equal(DeliverBranchKind.MisdeliverOne, b.Kind);
        Assert.Equal(panel.Hotbar[5].EntryId, b.SelectedEntry);
        Assert.Equal(_larch14, b.SelectedAddress);
        Assert.Equal(penalty, b.PredictedPenalty.Value);
        Assert.Equal(refused, b.FloorRefusal);
    }

    [Fact]
    [Trait("Id", "M-MD8-1a")]
    public void Preview_HandsSelected_FiveMatchingLettersInTheBackpack_IsMatchWithN5()
    {
        // Arrange
        var panel = new InMemoryPanelView();
        panel.Backpack.Add(panel.Mail(_letter, _elm17, 0, (Shift, 5, 8)));
        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = Preview(House(DestState.Open), panel, HandsSlot, wallet: 0, ref scratch);

        // Assert
        Assert.Equal(DeliverBranchKind.Match, b.Kind);
        Assert.Equal(5, b.N);
    }

    // ---------------------------------------------------------------- helpers

    internal static DeliverBranch Preview(
        DestinationFacts d, IPanelView panel, int selected, long wallet, ref DeliverPreviewScratch scratch, IStatSheet? stats = null)
    {
        var sink = default(SimCounterSink);
        return DeliverPreview.Preview(
            in d,
            panel,
            selected,
            Shift,
            _content.Mail,
            stats ?? Sheet(),
            Cents.FromCents(wallet, ref sink),
            Cents.FromCents(-500, ref sink),
            ref scratch);
    }

    /// <summary>FX-MB: house #17 at 17 Elm St (the M-1a house type accepts every kind).</summary>
    internal static DestinationFacts House(DestState state) =>
        new(EntityId.Compose(EntityClass.Destination, 17), _elm17, DefIndex.FromValue(0), state, ulong.MaxValue >> (64 - _content.MailKinds.Count), false);

    internal static PanelEntryFacts Letter(InMemoryPanelView panel, AddressId address) => panel.Mail(_letter, address, 0, (Shift, 1, 8));

    internal static ushort KindIndex(string id)
    {
        for (int i = 0; i < _content.MailKinds.Count; i++)
        {
            if (_content.MailKinds[DefIndex.FromValue((ushort)i)].Id == id)
            {
                return (ushort)i;
            }
        }

        throw new InvalidOperationException("no mail kind " + id);
    }

    private static ushort ItemIndex(string id)
    {
        for (int i = 0; i < _content.Items.Count; i++)
        {
            if (_content.Items[DefIndex.FromValue((ushort)i)].Id == id)
            {
                return (ushort)i;
            }
        }

        throw new InvalidOperationException("no item " + id);
    }

    private static Modifier Team(Stat stat, int value, ushort qualifier) =>
        new(SourceKind.Perk, DefIndex.FromValue(0), 0, PlayerSlot.None, stat, qualifier, ModifierOp.Mul, value, ModifierLifetime.Run);

    private static Modifier Mine(Stat stat, int value) =>
        new(SourceKind.Perk, DefIndex.FromValue(0), 0, PlayerSlot.FromValue(0), stat, StatSheet.NoQualifier, ModifierOp.Mul, value, ModifierLifetime.Run);

    private static IStatSheet Sheet(params Modifier[] modifiers) => StatSheet.Build(_content, new StatSources(modifiers));
}
