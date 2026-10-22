using System.Reflection;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Integration.Inventory;

/// <summary>
/// Story grid-inventory/001: the M-1a container catalogue (GDD R1, H-R1-1), the world's <c>po_intake</c> and per-player panel
/// containers created on <c>PlayerJoin</c>, instance dimensions from def + stat sheet, and the I-6 shape of <see cref="ContainerId"/>.
/// </summary>
public class ContainerCatalogueTests
{
    private static Op JoinOp(uint req, byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = req };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        return op;
    }

    private static Op LeaveOp(uint req, uint playerId)
    {
        var op = new Op { Kind = OpKind.PlayerLeave, ReqId = req };
        op.SetPayload(new PlayerLeavePayload { PlayerId = playerId });
        return op;
    }

    private static SimHost Build() => SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, BuiltInContent.CreateM1a(), 42));

    private static InventorySystem InventoryOf(SimHost host) => host.Systems.OfType<InventorySystem>().Single();

    private static void Send(SimHost host, in Op op) => Assert.True(host.World.EnqueueOp(ActorId.System, in op));

    private static uint Join(SimHost host, byte slot, uint req = 1)
    {
        Op join = JoinOp(req, slot);
        Send(host, in join);
        host.World.Tick();
        return host.Services.Slots.Entry(PlayerSlot.FromValue(slot)).PlayerId.Value;
    }

    private static void Leave(SimHost host, uint playerId, uint req = 2)
    {
        Op leave = LeaveOp(req, playerId);
        Send(host, in leave);
        host.World.Tick();
    }

    private static ContainerDef DefOf(SimHost host, string id) => host.Content.Containers.AsSpan().ToArray().Single(d => d.Id == id);

    public static TheoryData<string, int, int, int, ContainerLayout, ContainerView, BeltAccess, string, bool> R1Rows => new()
    {
        { "hotbar", 8, 1, 8, ContainerLayout.Slots, ContainerView.Grid, BeltAccess.None, "any", true },
        { "player_inventory", 8, 2, 16, ContainerLayout.Grid, ContainerView.Grid, BeltAccess.None, "any", true },
        { "po_intake", 20, 16, 320, ContainerLayout.Grid, ContainerView.Manifest, BeltAccess.OutfeedOnly, "mail", false },
        { "cursor", 1, 1, 1, ContainerLayout.Slots, ContainerView.Auto, BeltAccess.None, "any", true },
    };

    [Theory]
    [MemberData(nameof(R1Rows))]
    [Trait("Id", "H-R1-1")]
    public void BuiltSystem_ContainerMatchesItsR1Row(string id, int w, int h, int cells, ContainerLayout layout, ContainerView view, BeltAccess belt, string categories, bool handCarried)
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);
        if (id != "po_intake")
        {
            Join(host, 0);
        }

        // Act
        ContainerDef def = DefOf(host, id);
        ContainerId live = id switch
        {
            "hotbar" => inventory.HotbarOf(PlayerSlot.FromValue(0)),
            "player_inventory" => inventory.PlayerInventoryOf(PlayerSlot.FromValue(0)),
            "cursor" => inventory.CursorOf(PlayerSlot.FromValue(0)),
            _ => inventory.IntakeContainer,
        };
        bool found = inventory.TryGetHeader(live, out ContainerHeader header);

        // Assert
        Assert.True(found);
        Assert.Equal(w, def.GridW);
        Assert.Equal(h, def.GridH);
        Assert.Equal(cells, def.GridW * def.GridH);
        Assert.Equal(w, header.W);
        Assert.Equal(h, header.H);
        Assert.Equal(cells, header.W * header.H);
        Assert.Equal(cells, header.FreeCells);
        Assert.Equal(layout, def.Layout);
        Assert.Equal(layout, header.Layout);
        Assert.Equal(view, def.View);
        Assert.Equal(belt, def.BeltAccess);
        Assert.Equal(handCarried, def.HandCarried);
        if (categories == "any")
        {
            Assert.False(def.HasAllowedCategories);
        }
        else
        {
            Assert.True(def.HasAllowedCategories);
            Assert.Equal(new[] { ItemCategory.Mail }, def.AllowedCategories.ToArray());
        }
    }

    [Fact]
    [Trait("Id", "H-R1-1")]
    public void M1aGreyboxBuild_HasExactlyOneIntakeContainer_WithAStableId()
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);
        ContainerId before = inventory.IntakeContainer;
        int liveAtBuild = inventory.LiveContainerCount;

        // Act
        Join(host, 1);
        host.World.Tick();
        host.World.Tick();

        // Assert
        Assert.False(before.IsNone);
        Assert.Equal(before, inventory.IntakeContainer);
        Assert.True(inventory.TryGetHeader(before, out ContainerHeader h));
        Assert.Equal(DefOf(host, "po_intake").Id, host.Content.Containers[h.DefIndex].Id);
        Assert.Equal(ContainerHeader.NoOwner, h.OwnerSlot);
        Assert.Equal(1, liveAtBuild); // only the intake until someone joins
    }

    [Fact]
    [Trait("Id", "H-R1-1")]
    public void PlayerJoin_CreatesHotbarInventoryAndCursor_WithFreshDistinctIdsAndZeroVersions()
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);
        PlayerSlot slot = PlayerSlot.FromValue(3);

        // Act
        Join(host, 3);

        // Assert
        ContainerId[] ids = { inventory.HotbarOf(slot), inventory.PlayerInventoryOf(slot), inventory.CursorOf(slot) };
        Assert.All(ids, id => Assert.False(id.IsNone));
        Assert.Equal(3, ids.Distinct().Count());
        Assert.DoesNotContain(inventory.IntakeContainer, ids);
        Assert.Equal(4, inventory.LiveContainerCount);
        foreach (ContainerId id in ids)
        {
            Assert.True(inventory.TryGetHeader(id, out ContainerHeader h));
            Assert.Equal(0u, h.Version);
            Assert.Equal(0u, h.LastReplaceVersion);
            Assert.Equal(0u, h.StatEpoch);
            Assert.Equal(3, h.OwnerSlot);
        }
    }

    [Fact]
    [Trait("Id", "H-R1-1")]
    public void PlayerLeave_KeepsTheContainers_AndRejoinKeepsTheSameIds()
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);
        PlayerSlot slot = PlayerSlot.FromValue(2);
        uint player = Join(host, 2);
        ContainerId hotbar = inventory.HotbarOf(slot);
        ContainerId bag = inventory.PlayerInventoryOf(slot);
        ContainerId cursor = inventory.CursorOf(slot);
        int live = inventory.LiveContainerCount;

        // Act
        Leave(host, player);
        bool keptOnLeave = inventory.HotbarOf(slot) == hotbar && inventory.LiveContainerCount == live;
        Join(host, 2, req: 3);

        // Assert
        Assert.True(keptOnLeave);
        Assert.Equal(hotbar, inventory.HotbarOf(slot));
        Assert.Equal(bag, inventory.PlayerInventoryOf(slot));
        Assert.Equal(cursor, inventory.CursorOf(slot));
        Assert.Equal(live, inventory.LiveContainerCount);
    }

    [Fact]
    [Trait("Id", "H-R1-1")]
    public void TwoSlots_GetDisjointContainerIds()
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);

        // Act
        Join(host, 0);
        Join(host, 5, req: 2);

        // Assert
        var all = new List<ContainerId> { inventory.IntakeContainer };
        foreach (byte s in new byte[] { 0, 5 })
        {
            PlayerSlot slot = PlayerSlot.FromValue(s);
            all.Add(inventory.HotbarOf(slot));
            all.Add(inventory.PlayerInventoryOf(slot));
            all.Add(inventory.CursorOf(slot));
        }

        Assert.Equal(7, all.Distinct().Count());
        Assert.Equal(7, inventory.LiveContainerCount);
        Assert.True(inventory.HotbarOf(PlayerSlot.FromValue(1)).IsNone);
    }

    [Fact]
    public void JoinWithAFullContainerTable_RecordsACheckAndLeavesTheSlotWithoutContainers_NeverThrowing()
    {
        // Arrange: capacity 8 holds the intake + two panels (7); a third panel needs 3 with only 1 free
        ContentSet content = BuiltInContent.CreateM1a();
        InventorySettings tight = InventorySettings.FromBalance(content.Inventory) with { ContainerCapacity = 8 };
        var ring = new SimCheckRing();
        var inventory = new InventorySystem(content, StatSheet.Build(content, new StatSources([])), ring, tight);
        var listener = (IPlayerJoinListener)inventory;

        // Act
        listener.OnPlayerJoined(PlayerSlot.FromValue(0), new EntityId(16_777_217u), 5);
        listener.OnPlayerJoined(PlayerSlot.FromValue(1), new EntityId(16_777_218u), 6);
        listener.OnPlayerJoined(PlayerSlot.FromValue(2), new EntityId(16_777_219u), 7);

        // Assert
        Assert.False(inventory.HotbarOf(PlayerSlot.FromValue(0)).IsNone);
        Assert.False(inventory.HotbarOf(PlayerSlot.FromValue(1)).IsNone);
        Assert.True(inventory.HotbarOf(PlayerSlot.FromValue(2)).IsNone);
        Assert.Equal(7, inventory.LiveContainerCount);
        Assert.Equal(1, ring.Count);
        Assert.Equal((ushort)CheckCode.InventoryContainerCapacity, ring[0].Code);
        Assert.Equal(2L, ring[0].A);
        Assert.Equal(8L, ring[0].B);
    }

    [Fact]
    public void JoinThatFillsTheTableExactly_Succeeds_AndTheNextJoinRecordsTheCheck()
    {
        // Arrange: capacity 7 = intake + exactly two panels
        ContentSet content = BuiltInContent.CreateM1a();
        InventorySettings exact = InventorySettings.FromBalance(content.Inventory) with { ContainerCapacity = 7 };
        var ring = new SimCheckRing();
        var inventory = new InventorySystem(content, StatSheet.Build(content, new StatSources([])), ring, exact);
        var listener = (IPlayerJoinListener)inventory;

        // Act
        listener.OnPlayerJoined(PlayerSlot.FromValue(0), new EntityId(16_777_217u), 5);
        listener.OnPlayerJoined(PlayerSlot.FromValue(1), new EntityId(16_777_218u), 6);
        int liveAfterExactFill = inventory.LiveContainerCount;
        int checksAfterExactFill = ring.Count;
        listener.OnPlayerJoined(PlayerSlot.FromValue(2), new EntityId(16_777_219u), 7);

        // Assert: the boundary join fills the table without a check; the next one records exactly one and creates nothing
        Assert.Equal(7, liveAfterExactFill);
        Assert.Equal(0, checksAfterExactFill);
        Assert.False(inventory.CursorOf(PlayerSlot.FromValue(1)).IsNone);
        Assert.True(inventory.HotbarOf(PlayerSlot.FromValue(2)).IsNone);
        Assert.Equal(7, inventory.LiveContainerCount);
        Assert.Equal(1, ring.Count);
        Assert.Equal((ushort)CheckCode.InventoryContainerCapacity, ring[0].Code);
    }

    [Fact]
    public void Storage_IsSizedFromTheSettings_AndEveryCreatedContainerStartsWithAnEmptyGrid()
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);
        Join(host, 0);

        // Assert: pool and occupancy come from InventorySettings / inventory.maxRows / the widest def (po_intake, 20)
        Assert.Equal(inventory.Settings.EntryPoolCapacity, inventory.Entries.Capacity);
        Assert.Equal(0, inventory.Entries.LiveCount);
        Assert.Equal(inventory.Settings.ContainerCapacity, inventory.Occupancy.ContainerCapacity);
        Assert.Equal(host.Content.Inventory.MaxRows, inventory.Occupancy.MaxRows);
        Assert.Equal(20, inventory.Occupancy.MaxWidth);
        Assert.Equal(4, inventory.LiveContainerCount);
        for (ushort slot = 0; slot < inventory.LiveContainerCount; slot++)
        {
            ContainerHeader h = inventory.Containers[slot];
            Assert.Equal(h.W * h.H, h.FreeCells);
            for (int y = 0; y < h.H; y++)
            {
                Assert.Equal(0UL, inventory.Occupancy.RowBits(slot, y));
                for (int x = 0; x < h.W; x++)
                {
                    Assert.Equal(Occupancy.Free, inventory.Occupancy.OwnerAt(slot, h.W, x, y));
                }
            }

            Assert.True(inventory.Occupancy.Fits(slot, h.W, h.H, 0, 0, h.W, h.H));
        }
    }

    [Fact]
    public void Accessors_ForTheNoneSlot_ReturnNone()
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);

        // Act + Assert
        Assert.True(inventory.HotbarOf(PlayerSlot.None).IsNone);
        Assert.True(inventory.PlayerInventoryOf(PlayerSlot.None).IsNone);
        Assert.True(inventory.CursorOf(PlayerSlot.None).IsNone);
        Assert.False(inventory.TryGetHeader(ContainerId.None, out _));
    }

    /// <summary>One container record of the Inventory dump section (ADR-0006 section 4 fixed widths, little-endian): 28 bytes (story 012 added <c>nextArrivalSeq</c> after <c>statEpoch</c>).</summary>
    private const int DumpRecordBytes = 4 + 2 + 2 + 2 + 1 + 4 + 4 + 4 + 4 + 1;

    /// <summary>Story 013's WorldItem trailer with no WorldItem and no tombstone: worldSeq, live count, five FIFO counts, tombstone count.</summary>
    private const int EmptyWorldItemTrailerBytes = 4 + 4 + (5 * 4) + 4;

    /// <summary>The payload of the section with id <paramref name="id"/> in a whole-world dump (header, then id u16 / length u32 / payload).</summary>
    private static byte[] Section(byte[] dump, ushort id)
    {
        int pos = 6;
        while (pos < dump.Length)
        {
            ushort sectionId = BitConverter.ToUInt16(dump, pos);
            int length = (int)BitConverter.ToUInt32(dump, pos + 2);
            if (sectionId == id)
            {
                return dump.AsSpan(pos + 6, length).ToArray();
            }

            pos += 6 + length;
        }

        Assert.Fail($"Section {id} is not in the dump.");
        return Array.Empty<byte>();
    }

    private static SimHost BuildWithJoins(params byte[] slots)
    {
        SimHost host = Build();
        uint req = 1;
        foreach (byte slot in slots)
        {
            Join(host, slot, req++);
        }

        return host;
    }

    [Fact]
    public void Dump_InventorySection_HasTheDocumentedLayout_InAscendingIdOrder_AndIsIdenticalAcrossIdenticalWorlds()
    {
        // Arrange: two independently built worlds with the same join history (slot 5, then slot 1)
        SimHost a = BuildWithJoins(5, 1);
        SimHost b = BuildWithJoins(5, 1);
        InventorySystem inventory = InventoryOf(a);
        byte[] empty = Section(DumpHash.Bytes(Build().World), SectionIds.Inventory);

        // Act
        byte[] section = Section(DumpHash.Bytes(a.World), SectionIds.Inventory);
        byte[] other = Section(DumpHash.Bytes(b.World), SectionIds.Inventory);

        // Assert: identical bytes across worlds; a join changed the section; header = nextId, liveCount
        Assert.Equal(section, other);
        Assert.NotEqual(empty, section);
        Assert.Equal(8 + 1 * DumpRecordBytes + EmptyWorldItemTrailerBytes, empty.Length);
        Assert.Equal(8u, BitConverter.ToUInt32(section, 0));
        Assert.Equal(7u, BitConverter.ToUInt32(section, 4));
        Assert.Equal(8 + 7 * DumpRecordBytes + EmptyWorldItemTrailerBytes, section.Length);

        // Records: ascending ids 1..7, owner 0xFF then slot 5 x3 then slot 1 x3, fields matching the live headers, versions 0,
        // nextArrivalSeq 1 (no outfeed insertion yet)
        byte[] expectedOwners = [ContainerHeader.NoOwner, 5, 5, 5, 1, 1, 1];
        for (int i = 0; i < 7; i++)
        {
            int at = 8 + i * DumpRecordBytes;
            var id = new ContainerId(BitConverter.ToUInt32(section, at));
            Assert.Equal((uint)(i + 1), id.Value);
            Assert.True(inventory.TryGetHeader(id, out ContainerHeader h));
            Assert.Equal(h.DefIndex.Value, BitConverter.ToUInt16(section, at + 4));
            Assert.Equal(h.W, BitConverter.ToUInt16(section, at + 6));
            Assert.Equal(h.H, BitConverter.ToUInt16(section, at + 8));
            Assert.Equal((byte)h.Layout, section[at + 10]);
            Assert.Equal(0u, BitConverter.ToUInt32(section, at + 11));
            Assert.Equal(0u, BitConverter.ToUInt32(section, at + 15));
            Assert.Equal(0u, BitConverter.ToUInt32(section, at + 19));
            Assert.Equal(1u, BitConverter.ToUInt32(section, at + 23));
            Assert.Equal(h.NextArrivalSeq, BitConverter.ToUInt32(section, at + 23));
            Assert.Equal(expectedOwners[i], section[at + 27]);
        }
    }

    [Fact]
    [Trait("Id", "H-R1-1")]
    public void HeaderDimensions_AreDefWidthAndStatSheetRows_WhichEqualDefRowsWithNoSources()
    {
        // Arrange
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);

        // Act
        Join(host, 0);

        // Assert: no sources, so the sheet reads the def rows
        PlayerSlot slot = PlayerSlot.FromValue(0);
        foreach ((ContainerId id, string def) in new[] { (inventory.HotbarOf(slot), "hotbar"), (inventory.PlayerInventoryOf(slot), "player_inventory"), (inventory.IntakeContainer, "po_intake") })
        {
            inventory.TryGetHeader(id, out ContainerHeader h);
            Assert.Equal(DefOf(host, def).GridW, h.W);
            Assert.Equal(DefOf(host, def).GridH, h.H);
            Assert.Equal(DefOf(host, def).GridH, host.StatSheet.Read(Stat.InventoryRows, h.DefIndex.Value));
        }
    }

    [Fact]
    [Trait("Id", "H-R1-1")]
    public void HeaderRows_FollowAnInventoryRowsSource_PushedBeforeTheJoin()
    {
        // Arrange: a personal +1 row source on player_inventory for slot 4 (story 016 owns the live-growth path)
        SimHost host = Build();
        InventorySystem inventory = InventoryOf(host);
        PlayerSlot slot = PlayerSlot.FromValue(4);
        ushort bagDef = (ushort)Array.FindIndex(host.Content.Containers.AsSpan().ToArray(), d => d.Id == "player_inventory");
        var src = new ModifierSource(SourceKind.Perk, DefIndex.FromValue(0), 0, ModifierOwner.ForSlot(slot), 1, ModifierLifetime.Run);
        var mod = new Modifier(src.Kind, src.Id, 0, slot, Stat.InventoryRows, bagDef, ModifierOp.Add, 1, src.Lifetime);
        Assert.True(host.Services.Stats.Push(in src, [mod]));

        // Act
        Join(host, 4);

        // Assert
        inventory.TryGetHeader(inventory.PlayerInventoryOf(slot), out ContainerHeader bag);
        inventory.TryGetHeader(inventory.HotbarOf(slot), out ContainerHeader hotbar);
        Assert.Equal(3, bag.H);
        Assert.Equal(1, hotbar.H);
    }

    [Fact]
    [Trait("Id", "H-R1-1")]
    public void ContainerId_IsAPlainReadonlyStruct_WithExplicitEqualityMembers()
    {
        // Arrange
        Type t = typeof(ContainerId);
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Act
        MethodInfo[] methods = t.GetMethods(Declared);

        // Assert
        Assert.True(t.IsValueType);
        Assert.True(t.GetCustomAttribute<System.Runtime.CompilerServices.IsReadOnlyAttribute>() is not null);
        Assert.DoesNotContain(methods, m => m.Name == "<Clone>$" || m.Name == "PrintMembers");
        Assert.Contains(methods, m => m.Name == nameof(IEquatable<ContainerId>.Equals) && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(ContainerId));
        Assert.Contains(methods, m => m.Name == nameof(GetHashCode) && m.GetParameters().Length == 0);
        Assert.Contains(methods, m => m.Name == "op_Equality");
        Assert.Contains(methods, m => m.Name == "op_Inequality");
        Assert.Equal(typeof(uint), t.GetProperty(nameof(ContainerId.Value))!.PropertyType);
        Assert.True(typeof(IEquatable<ContainerId>).IsAssignableFrom(t));
        var id = new ContainerId(0x8000_0005u);
        Assert.Equal(unchecked((int)0x8000_0005u), id.GetHashCode());
        Assert.True(id == new ContainerId(0x8000_0005u));
        Assert.True(id != ContainerId.None);
        Assert.True(ContainerId.None.IsNone);
    }
}

/// <summary>Story grid-inventory/001: after warm-up, joins and leaves allocate nothing on the tick thread and the container table never grows.</summary>
[Collection(NonParallelCollection.Name)]
public class ContainerAllocationTests
{
    [Fact]
    [Trait("Id", "H-R1-1")]
    public void ThousandTicksOfJoinsAndLeaves_AfterWarmUp_Allocate0Bytes_AndTheTableCapacityIsUnchanged()
    {
        // Arrange: the 0 B guardrail applies to optimised builds only (TD-008 pattern, RecorderTests.SkipWhenUnoptimised)
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(debuggable is { IsJITOptimizerDisabled: true }, "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, BuiltInContent.CreateM1a(), 42));
        InventorySystem inventory = host.Systems.OfType<InventorySystem>().Single();
        int capacity = inventory.Containers.Capacity;
        const int Slots = 4;
        var playerIds = new uint[Slots];
        long tickId = 0;
        void Window()
        {
            for (int tick = 0; tick < 1000; tick++)
            {
                byte slot = (byte)(tick % Slots);
                if (playerIds[slot] == 0)
                {
                    var join = new Op { Kind = OpKind.PlayerJoin, ReqId = (uint)++tickId };
                    join.SetPayload(new PlayerJoinPayload { Slot = slot });
                    host.World.EnqueueOp(ActorId.System, in join);
                }
                else
                {
                    var leave = new Op { Kind = OpKind.PlayerLeave, ReqId = (uint)++tickId };
                    leave.SetPayload(new PlayerLeavePayload { PlayerId = playerIds[slot] });
                    host.World.EnqueueOp(ActorId.System, in leave);
                }

                host.World.Tick();
                playerIds[slot] = playerIds[slot] == 0 ? host.Services.Slots.Entry(PlayerSlot.FromValue(slot)).PlayerId.Value : 0;
            }
        }

        // Act + Assert
        AllocAssert.Zero("1000 ticks of alternating PlayerJoin/PlayerLeave with Grid Inventory bound", Window);
        Assert.Equal(capacity, inventory.Containers.Capacity);
        Assert.Equal(1 + Slots * 3, inventory.LiveContainerCount); // rejoins reuse the kept containers
    }
}
