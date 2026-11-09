using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.ManualDelivery;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Grid-inventory story 022 (GDD R7 <c>deliver_matching</c> scan order; TR-delivery-014 Inventory half; ADR-0009 erratum 2026-10-23,
/// ADR-0016 l.117): <see cref="IInventoryApi.HostPanelView"/>, the live read-only host <see cref="IPanelView"/> of a player slot, walked
/// by <see cref="DeliverScan.Enumerate"/> in the same order as the in-memory client view built from the same fixture.
/// </summary>
public class HostPanelViewTests
{
    private const uint Address17 = 17u;
    private static readonly PlayerSlot _a = PlayerSlot.FromValue(0);

    [Fact]
    [Trait("Id", "TR-delivery-014")]
    public void Enumerate_OverTheHostView_YieldsCursorHotbarThenInventoryRowMajor_AsTheClientViewDoes()
    {
        // Arrange: a cursor entry, hotbar 2 and 5, inventory 1x1 entries written at (3,1), (0,0), (5,0)
        var rig = new InvAutoPlaceRig();
        ushort cursor = rig.PlaceMail(rig.CursorA, "letter", 0, 0, Address17, new uint[] { 1 });
        ushort hot2 = rig.PlaceMail(rig.HotbarA, "letter", 2, 0, Address17, new uint[] { 2 });
        ushort hot5 = rig.PlaceMail(rig.HotbarA, "letter", 5, 0, Address17, new uint[] { 3 });
        ushort inv31 = rig.PlaceMail(rig.InventoryA, "letter", 3, 1, Address17, new uint[] { 4 });
        ushort inv00 = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address17, new uint[] { 5 });
        ushort inv50 = rig.PlaceMail(rig.InventoryA, "letter", 5, 0, Address17, new uint[] { 6 });
        EntryId[] expected = new[] { cursor, hot2, hot5, inv00, inv50, inv31 }.Select(e => rig.Entries[e].Id).ToArray();

        // Arrange: the client test double over the same fixture, its inventory filled in row-major anchor order
        var client = new InMemoryPanelView { Cursor = Facts(rig, cursor) };
        client.Hotbar[2] = Facts(rig, hot2);
        client.Hotbar[5] = Facts(rig, hot5);
        client.Inventory.AddRange(new[] { inv00, inv50, inv31 }.Select(e => Facts(rig, e)));

        // Act
        IPanelView host = rig.Inventory.HostPanelView(_a);
        List<(PanelSection, EntryId)> hostOrder = Scan(host);
        List<(PanelSection, EntryId)> clientOrder = Scan(client);

        // Assert
        Assert.Equal(expected, hostOrder.Select(x => x.Item2));
        Assert.Equal(
            new[] { PanelSection.Cursor, PanelSection.Hotbar, PanelSection.Hotbar, PanelSection.Inventory, PanelSection.Inventory, PanelSection.Inventory },
            hostOrder.Select(x => x.Item1));
        Assert.Equal(clientOrder, hostOrder);
    }

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void Entry_CarriesTheAuthoritativeKindKeyCategoryAddressFlagsUnitsAndId_WithNoHintRuns()
    {
        // Arrange: a 3-letter Cursed stack at inventory (0,0) and 7 planks at (1,0)
        var rig = new InvAutoPlaceRig();
        ushort mail = rig.PlaceMail(rig.InventoryA, "postcard", 0, 0, Address17, new uint[] { 10, 11, 12 }, flags: 1);
        ushort planks = rig.PlaceItem(rig.InventoryA, "plank", 1, 0, 7);

        // Act
        IPanelView view = rig.Inventory.HostPanelView(_a);
        PanelEntryFacts m = view.Entry(PanelSection.Inventory, 0);
        PanelEntryFacts p = view.Entry(PanelSection.Inventory, 1);

        // Assert
        Assert.Equal(rig.Entries[mail].Id, m.EntryId);
        Assert.Equal(rig.KindIndex("postcard").Value, m.KindKey);
        Assert.Equal(ItemCategory.Mail, m.Category);
        Assert.Equal(Address17, m.Address.Value);
        Assert.Equal((byte)1, m.Flags);
        Assert.Equal((byte)3, m.Units);
        Assert.Equal(0, m.RunCount);
        Assert.Equal(rig.Entries[planks].Id, p.EntryId);
        Assert.Equal(rig.ItemIndex("plank").Value, p.KindKey);
        Assert.Equal(ItemCategory.Material, p.Category);
        Assert.Equal(AddressId.None, p.Address);
        Assert.Equal((byte)7, p.Units);
        Assert.Equal(0, p.RunCount);
    }

    [Theory]
    [Trait("Id", "TR-inventory-001")]
    [InlineData("hotbar-5", 5)]
    [InlineData("inventory-2-0", -1)]
    [InlineData("empty", -1)]
    public void CursorSlot_IsTheSourceHotbarSlot_ElseMinusOne(string source, int expected)
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        if (source != "empty")
        {
            bool hotbar = source == "hotbar-5";
            ushort from = hotbar ? rig.HotbarA : rig.InventoryA;
            ushort entry = rig.PlaceMail(from, "letter", hotbar ? 5 : 2, 0, Address17, new uint[] { 1 });
            var pick = new Op { Kind = InvOpKinds.Pick, ReqId = 80 };
            pick.SetPayload(new PickPayload { From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value });
            InvOpTestHelpers.AssertOk(rig.Run(rig.PlayerA, in pick));
        }

        // Act
        int slot = rig.Inventory.HostPanelView(_a).CursorSlot;

        // Assert
        Assert.Equal(expected, slot);
    }

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void EntryCount_BackpackIsEmptyAtM1a_AndEverySectionIsEmptyBeforeTheFirstJoin()
    {
        // Arrange: slot 0 joined with a letter in its backpack-less panel; slot 3 never joined
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address17, new uint[] { 1 });
        IPanelView joined = rig.Inventory.HostPanelView(_a);
        IPanelView never = rig.Inventory.HostPanelView(PlayerSlot.FromValue(3));

        // Act + Assert
        Assert.Equal(0, joined.EntryCount(PanelSection.Backpack));
        foreach (PanelSection s in Enum.GetValues<PanelSection>())
        {
            Assert.Equal(0, never.EntryCount(s));
        }
    }

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void Enumerate_ChangesNoVersionLedgerCountEventOrPoolSlot()
    {
        // Arrange: a filled panel
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.CursorA, "letter", 0, 0, Address17, new uint[] { 1 });
        _ = rig.PlaceMail(rig.HotbarA, "letter", 1, 0, Address17, new uint[] { 2, 3 });
        _ = rig.PlaceItem(rig.InventoryA, "plank", 4, 1, 5);
        List<long> before = State(rig);

        // Act
        _ = Scan(rig.Inventory.HostPanelView(_a));

        // Assert
        Assert.Equal(before, State(rig));
    }

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void Enumerate_AfterConsumeUnitTakesTheLastUnit_NoLongerYieldsIt_ThroughTheSameView()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort keep = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address17, new uint[] { 1 });
        ushort gone = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address17, new uint[] { 2 });
        EntryId keepId = rig.Entries[keep].Id;
        EntryId goneId = rig.Entries[gone].Id;
        IPanelView before = rig.Inventory.HostPanelView(_a);
        Assert.Equal(new[] { keepId, goneId }, Scan(before).Select(x => x.Item2));
        ContainerId inventory = rig.IdOf(rig.InventoryA);

        // Act
        Assert.True(IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(inventory, goneId), ConsumeReason.Delivered, rig.PlayerA)));
        IPanelView after = rig.Inventory.HostPanelView(_a);

        // Assert
        Assert.Same(before, after);
        Assert.Equal(new[] { keepId }, Scan(after).Select(x => x.Item2));
    }

    // ---------------------------------------------------------------- helpers

    private static List<(PanelSection, EntryId)> Scan(IPanelView view)
    {
        var order = new List<(PanelSection, EntryId)>();
        for (DeliverScan.Enumerator it = DeliverScan.Enumerate(view); it.MoveNext();)
        {
            order.Add((it.Section, it.Current.EntryId));
        }

        return order;
    }

    private static PanelEntryFacts Facts(InvAutoPlaceRig rig, ushort entry) =>
        new(rig.Entries[entry].Id, rig.Entries[entry].MailKind, ItemCategory.Mail, new AddressId(rig.Entries[entry].AddressId), 0, rig.Entries[entry].Count, ReadOnlySpan<PanelHintRun>.Empty);

    // Every container version, the ledger's per-row counts, the event log length and the entry pool's live count.
    private static List<long> State(InvAutoPlaceRig rig)
    {
        var state = new List<long>();
        for (ushort c = 0; c < rig.Inventory.Containers.Capacity; c++)
        {
            state.Add(rig.Inventory.Containers[c].Version);
        }

        UnitLedger ledger = rig.Inventory.Ledger;
        for (int row = 0; row < ledger.Rows; row++)
        {
            for (int s = 0; s < UnitLedger.LiveStateCount; s++)
            {
                state.Add(ledger.Live(row, (LedgerState)s));
            }

            state.Add(ledger.Spawned(row));
            state.Add(ledger.Granted(row));
            state.Add(ledger.Consumed(row));
            state.Add(ledger.Destroyed(row));
        }

        state.Add(rig.Host.World.Events.Count);
        state.Add(rig.Entries.LiveCount);
        return state;
    }
}
