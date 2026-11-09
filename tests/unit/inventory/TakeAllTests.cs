using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// WP-F's <c>take_all</c> (GDD R7 l.94, ADR-0009 sections 1-2): the Intake pickup. Matching mail moves oldest first into the panel
/// (mail: player_inventory, then hotbar), what does not match stays, each touched container is sealed once; the op-level refusals change
/// nothing.
/// </summary>
public class TakeAllTests
{
    private static readonly uint _district1Street1 = AddressId.Pack(1, 1, 4).Value;
    private static readonly uint _district1Street2 = AddressId.Pack(1, 2, 6).Value;
    private static readonly uint _district2 = AddressId.Pack(2, 1, 8).Value;

    [Fact]
    public void TakeAll_District_MovesTheMatchingMailOldestFirstIntoThePanel_LeavesTheRest_SealsEachContainerOnce()
    {
        // Arrange: three Intake letters (mailIds 30, 10, 20; one in district 2), Intake open for player A.
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        foreach ((uint address, uint id) in new[] { (_district1Street1, 30u), (_district2, 40u), (_district1Street2, 10u) })
        {
            Assert.Equal(1, IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, address, 0, new[] { new MailId(id) }, MergePolicy.NoMerge).Placed));
        }

        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue(0), rig.Inventory.IntakeContainer));
        uint[] before = MoveSplitMergeTests.Versions(rig);

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, TakeAll(rig.Inventory.IntakeContainer.Value, TakeAllFilter.District, 1));

        // Assert: both district-1 letters left the Intake for the player_inventory, the lower mailId first (row-major (0,0) then (1,0)).
        Reply reply = InvOpTestHelpers.AssertOk(log);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((2, 2, 0), (extra.GotEntries, extra.GotUnits, extra.SkippedEntries));
        Assert.Equal(1, rig.Header(rig.Intake).EntryCount);
        Assert.Equal(new[] { 10u }, rig.IdsOf(rig.EntryAt(rig.InventoryA, 0, 0)));
        Assert.Equal(new[] { 30u }, rig.IdsOf(rig.EntryAt(rig.InventoryA, 1, 0)));
        uint[] expected = (uint[])before.Clone();
        expected[rig.Intake]++;
        expected[rig.InventoryA]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal(2, log.Count(e => e.Kind == InvEventKinds.Version));
    }

    [Theory]
    [InlineData("not_open", (ushort)InvReason.NotOpen)]
    [InlineData("no_target", (ushort)InvReason.NoTarget)]
    [InlineData("bad_filter", CoreReason.BadRequest)]
    public void TakeAll_OpLevelRefusal_RejectsAndChangesNothing(string row, ushort reason)
    {
        // Arrange: one district-1 letter in the Intake; open unless the row is not_open.
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        Assert.Equal(1, IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, _district1Street1, 0, new[] { new MailId(5) }, MergePolicy.NoMerge).Placed));
        if (row != "not_open")
        {
            Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue(0), rig.Inventory.IntakeContainer));
        }

        uint[] before = MoveSplitMergeTests.Versions(rig);
        Op op = row switch
        {
            "no_target" => TakeAll(rig.Inventory.IntakeContainer.Value, TakeAllFilter.District, 2),
            "bad_filter" => TakeAll(rig.Inventory.IntakeContainer.Value, (TakeAllFilter)9, 1),
            _ => TakeAll(rig.Inventory.IntakeContainer.Value, TakeAllFilter.District, 1),
        };

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, op);

        // Assert
        Reply reply = Assert.Single(log, e => e.Kind == EventKind.OpReply).PayloadAs<Reply>();
        Assert.Equal(reason, reply.Reason);
        Assert.DoesNotContain(log, e => e.Kind == InvEventKinds.Version);
        Assert.Equal(before, MoveSplitMergeTests.Versions(rig));
    }

    private static Op TakeAll(uint container, TakeAllFilter filter, uint value)
    {
        var op = new Op { Kind = InvOpKinds.TakeAll, ReqId = 90 };
        op.SetPayload(new TakeAllPayload { Container = container, FilterKind = (byte)filter, FilterValue = value });
        return op;
    }
}
