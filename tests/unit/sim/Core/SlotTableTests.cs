using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-026 for story 018 AC-3 (ADR-0004 section 4): a slot freed in tick T is reassignable only from T + 1,
/// <c>IsPresent</c> and <c>SlotOf</c> reflect occupied slots only, <c>ConnectedMask</c> has one bit per occupied slot, and the
/// table is readable through a view that exposes no writes.
/// </summary>
public class SlotTableTests
{
    private static readonly EntityId _playerA = EntityId.Compose(EntityClass.Player, 1);
    private static readonly EntityId _playerB = EntityId.Compose(EntityClass.Player, 2);

    private static PlayerSlot Slot(byte v) => PlayerSlot.FromValue(v);

    [Fact]
    public void Ac3_FreshTable_EverySlotIsAssignable_AtTickZero_AndNobodyIsPresent()
    {
        var table = new SlotTable();

        for (byte i = 0; i < 8; i++)
        {
            Assert.True(table.CanAssign(Slot(i), 0));
            Assert.False(table.Entry(Slot(i)).Occupied);
            Assert.Equal(PlayerSlotEntry.NeverFreed, table.Entry(Slot(i)).FreedTick);
        }

        Assert.Equal((byte)0, table.ConnectedMask);
        Assert.False(table.IsPresent(ActorId.FromEntity(_playerA)));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(41u)]
    [InlineData(0x7FFF_FFFEu)]
    public void Ac3_SlotFreedAtTickT_CannotBeAssignedAtT_ButCanFromTPlusOne(uint t)
    {
        var table = new SlotTable();
        PlayerSlot slot = Slot(3);
        Assert.True(table.Assign(slot, _playerA, t));

        Assert.True(table.Free(slot, t));

        Assert.False(table.CanAssign(slot, t));
        Assert.False(table.Assign(slot, _playerB, t));
        Assert.False(table.Entry(slot).Occupied);
        Assert.Equal(t, table.Entry(slot).FreedTick);
        Assert.True(table.CanAssign(slot, t + 1));
        Assert.True(table.Assign(slot, _playerB, t + 1));
        Assert.Equal(_playerB, table.Entry(slot).PlayerId);
    }

    [Fact]
    public void Ac3_SlotFreedAtTickT_CannotBeAssignedBeforeT()
    {
        // A tick that went backwards (a restored world) must not reopen a slot early.
        var table = new SlotTable();
        PlayerSlot slot = Slot(0);
        Assert.True(table.Assign(slot, _playerA, 5));
        Assert.True(table.Free(slot, 9));

        Assert.False(table.CanAssign(slot, 8));
        Assert.False(table.CanAssign(slot, 9));
        Assert.True(table.CanAssign(slot, 10));
    }

    [Fact]
    public void Ac3_OccupiedSlot_CannotBeAssignedAgain_AndFreeingAFreeSlotIsRefused()
    {
        var table = new SlotTable();
        PlayerSlot slot = Slot(7);

        Assert.False(table.Free(slot, 1));
        Assert.True(table.Assign(slot, _playerA, 1));
        Assert.False(table.CanAssign(slot, 100));
        Assert.False(table.Assign(slot, _playerB, 100));
        Assert.Equal(_playerA, table.Entry(slot).PlayerId);
    }

    [Fact]
    public void Ac3_NoneSlotAndNonePlayer_AreRefused()
    {
        var table = new SlotTable();

        Assert.False(table.CanAssign(PlayerSlot.None, 0));
        Assert.False(table.Assign(PlayerSlot.None, _playerA, 0));
        Assert.False(table.Free(PlayerSlot.None, 0));
        Assert.False(table.Assign(Slot(0), EntityId.None, 0));
        Assert.Equal((byte)0, table.ConnectedMask);
        Assert.False(table.Entry(PlayerSlot.None).Occupied);
    }

    [Fact]
    public void Ac3_IsPresent_IsTrueOnlyForOccupiedSlots()
    {
        var table = new SlotTable();
        Assert.True(table.Assign(Slot(2), _playerA, 0));

        Assert.True(table.IsPresent(ActorId.FromEntity(_playerA)));
        Assert.False(table.IsPresent(ActorId.FromEntity(_playerB)));
        Assert.False(table.IsPresent(ActorId.None));
        Assert.False(table.IsPresent(ActorId.System));

        Assert.True(table.Free(Slot(2), 4));

        Assert.False(table.IsPresent(ActorId.FromEntity(_playerA)));
    }

    [Fact]
    public void Ac3_SlotOf_FindsTheOccupiedSlot()
    {
        var table = new SlotTable();
        Assert.True(table.Assign(Slot(5), _playerA, 0));
        Assert.True(table.Assign(Slot(1), _playerB, 0));

        Assert.Equal(Slot(5), table.SlotOf(_playerA));
        Assert.Equal(Slot(1), table.SlotOf(_playerB));
        Assert.True(table.SlotOf(EntityId.None).IsNone);
        Assert.True(table.SlotOf(EntityId.Compose(EntityClass.Player, 3)).IsNone);
    }

    [Fact]
    public void Ac3_ConnectedMask_HasOneBitPerOccupiedSlot()
    {
        var table = new SlotTable();
        Assert.True(table.Assign(Slot(0), _playerA, 0));
        Assert.True(table.Assign(Slot(7), _playerB, 0));
        Assert.Equal((byte)0b1000_0001, table.ConnectedMask);

        Assert.True(table.Assign(Slot(3), EntityId.Compose(EntityClass.Player, 3), 0));
        Assert.Equal((byte)0b1000_1001, table.ConnectedMask);

        Assert.True(table.Free(Slot(0), 2));
        Assert.Equal((byte)0b1000_1000, table.ConnectedMask);
    }

    [Fact]
    public void Ac3_ReadOnlyView_ExposesNoWrites_AndWritersAreNotPublic()
    {
        foreach (MethodInfo m in typeof(ISlotView).GetMethods())
        {
            Assert.NotEqual("Assign", m.Name);
            Assert.NotEqual("Free", m.Name);
        }

        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
        Assert.Null(typeof(SlotTable).GetMethod("Assign", Public));
        Assert.Null(typeof(SlotTable).GetMethod("Free", Public));

        var table = new SlotTable();
        Assert.True(table.Assign(Slot(4), _playerA, 0));
        ISlotView view = table;
        Assert.True(view.IsPresent(ActorId.FromEntity(_playerA)));
        Assert.Equal((byte)0b0001_0000, view.ConnectedMask);
        Assert.False(view.CanAssign(Slot(4), 9));
    }

    [Fact]
    public void Ac3_ReuseCycle_AssignFreeAssign_KeepsTableConsistent()
    {
        var table = new SlotTable();
        PlayerSlot slot = Slot(6);

        for (uint t = 0; t < 5; t++)
        {
            EntityId who = EntityId.Compose(EntityClass.Player, t + 1);
            Assert.True(table.Assign(slot, who, t));
            Assert.Equal(slot, table.SlotOf(who));
            Assert.True(table.Free(slot, t));
            Assert.True(table.SlotOf(who).IsNone);
        }

        Assert.Equal((byte)0, table.ConnectedMask);
    }
}
