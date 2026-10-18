using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>Story grid-inventory/001: the container registry's id assignment, map lookup and capacity behaviour.</summary>
public class ContainerTableTests
{
    private static bool Create(ContainerTable t, out ContainerId id, byte owner = ContainerHeader.NoOwner) =>
        t.TryCreate(DefIndex.FromValue(1), 8, 2, ContainerLayout.Grid, owner, out id);

    [Fact]
    public void TryCreate_AssignsMonotonicIdsFromOne_AndFillsTheHeader()
    {
        // Arrange
        var table = new ContainerTable(8);

        // Act
        bool a = Create(table, out ContainerId first, 3);
        bool b = Create(table, out ContainerId second);

        // Assert
        Assert.True(a && b);
        Assert.Equal(1u, first.Value);
        Assert.Equal(2u, second.Value);
        Assert.Equal(3u, table.NextId);
        Assert.Equal(2, table.LiveCount);
        Assert.True(table.TryGetSlot(first, out ushort slot));
        ref ContainerHeader h = ref table[slot];
        Assert.True(h.IsLive);
        Assert.Equal(first, h.Id);
        Assert.Equal(8, h.W);
        Assert.Equal(2, h.H);
        Assert.Equal(16, h.FreeCells);
        Assert.Equal(3, h.OwnerSlot);
        Assert.Equal(0u, h.Version);
    }

    [Fact]
    public void TryGetSlot_FindsEveryLiveId_AndRejectsNoneAndUnknownIds()
    {
        // Arrange
        var table = new ContainerTable(64);
        var ids = new List<ContainerId>();
        for (int i = 0; i < 64; i++)
        {
            Assert.True(Create(table, out ContainerId id));
            ids.Add(id);
        }

        // Act + Assert
        for (int i = 0; i < ids.Count; i++)
        {
            Assert.True(table.TryGetSlot(ids[i], out ushort slot));
            Assert.Equal(ids[i], table[slot].Id);
        }

        Assert.False(table.TryGetSlot(ContainerId.None, out _));
        Assert.False(table.TryGetSlot(new ContainerId(65), out _));
        Assert.False(table.TryGetSlot(new ContainerId(uint.MaxValue), out _));
    }

    [Fact]
    public void TryCreate_WhenFull_ReturnsFalse_ChangesNothing_AndDoesNotConsumeAnId()
    {
        // Arrange
        var table = new ContainerTable(2);
        Create(table, out _);
        Create(table, out _);

        // Act
        bool ok = Create(table, out ContainerId id);

        // Assert
        Assert.False(ok);
        Assert.True(id.IsNone);
        Assert.Equal(2, table.LiveCount);
        Assert.Equal(2, table.Capacity);
        Assert.Equal(3u, table.NextId);
    }

    [Fact]
    public void Ids_AreNeverReused_AndAreUniqueAcrossTheTable()
    {
        // Arrange
        var table = new ContainerTable(16);

        // Act
        var seen = new HashSet<uint>();
        while (Create(table, out ContainerId id))
        {
            seen.Add(id.Value);
        }

        // Assert
        Assert.Equal(16, seen.Count);
        Assert.Equal(Enumerable.Range(1, 16).Select(i => (uint)i), seen.OrderBy(v => v));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    public void Constructor_RejectsACapacityOutsideTheUshortRange(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerTable(capacity));
    }
}
