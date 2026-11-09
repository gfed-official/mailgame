using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Inventory;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 002 AC-5 (GDD M-MD10-1a, M-INT-1 order half; TR-delivery-014; ADR-0016 section 3): <see cref="DeliverScan.Enumerate"/>
/// walks cursor → hotbar → inventory → backpack, and the preview's MATCH list keeps that order; mail in an open external container is
/// never scanned (over the real host view, grid-inventory story 022).
/// </summary>
public class DeliverScanTests
{
    private static readonly AddressId _elm17 = AddressId.Pack(1, 1, 17);

    [Fact]
    [Trait("Id", "M-INT-1")]
    public void Enumerate_MatchesInEverySection_YieldsCursorHotbarInventoryBackpack_AndTheMatchListKeepsIt()
    {
        // Arrange: matches at the cursor, hotbar slot 2, inventory (r0, c1) after a non-match at (r0, c0), and the backpack
        var panel = new InMemoryPanelView();
        panel.Backpack.Add(DeliverPreviewTests.Letter(panel, _elm17));
        panel.Inventory.Add(DeliverPreviewTests.Letter(panel, AddressId.Pack(1, 2, 14)));
        panel.Inventory.Add(DeliverPreviewTests.Letter(panel, _elm17));
        panel.Hotbar[2] = DeliverPreviewTests.Letter(panel, _elm17);
        panel.Cursor = DeliverPreviewTests.Letter(panel, _elm17);
        EntryId[] expected = { panel.Cursor.EntryId, panel.Hotbar[2].EntryId, panel.Inventory[1].EntryId, panel.Backpack[0].EntryId };
        var scratch = new DeliverPreviewScratch();

        // Act
        var scanned = new List<(PanelSection, int, EntryId)>();
        for (DeliverScan.Enumerator it = DeliverScan.Enumerate(panel); it.MoveNext();)
        {
            scanned.Add((it.Section, it.Index, it.Current.EntryId));
        }

        DeliverBranch b = DeliverPreviewTests.Preview(DeliverPreviewTests.House(DestState.Open), panel, 0, 0, ref scratch);

        // Assert
        Assert.Equal(
            new[]
            {
                (PanelSection.Cursor, 0, expected[0]),
                (PanelSection.Hotbar, 2, expected[1]),
                (PanelSection.Inventory, 0, panel.Inventory[0].EntryId),
                (PanelSection.Inventory, 1, expected[2]),
                (PanelSection.Backpack, 0, expected[3]),
            },
            scanned);
        Assert.Equal(DeliverBranchKind.Match, b.Kind);
        Assert.Equal(4, b.K);
        Assert.Equal(expected, Enumerable.Range(0, b.K).Select(b.Entry));
    }

    [Fact]
    [Trait("Id", "M-MD10-1a")]
    public void Preview_OverTheHostView_MatchingMailOnlyInAnOpenChest_IsNothing()
    {
        // Arrange: player A has a chest open that holds a 17 Elm St letter; A's own panel is empty
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(4, 2);
        _ = rig.PlaceMail(chest, "letter", 0, 0, _elm17.Value, new uint[] { 7 });
        PlayerSlot a = PlayerSlot.FromValue(0);
        Assert.True(rig.Inventory.TryOpenExternal(a, rig.IdOf(chest)));
        var scratch = new DeliverPreviewScratch();

        // Act
        DeliverBranch b = DeliverPreviewTests.Preview(
            DeliverPreviewTests.House(DestState.Open), rig.Inventory.HostPanelView(a), 0, 0, ref scratch);

        // Assert
        Assert.Equal(DeliverBranchKind.Nothing, b.Kind);
    }
}
