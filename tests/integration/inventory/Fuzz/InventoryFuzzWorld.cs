using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Integration.Inventory.Fuzz;

/// <summary>
/// One fuzz world (story grid-inventory/018; GDD H-R19-1 M-1a subset, H-DET-1): the M-1a built-in content, the real
/// <see cref="InventorySystem"/> and the <see cref="FuzzAutomationSystem"/> stand-in, plus the two fixture external containers
/// (<see cref="CreateFixtureExternals"/>) and the 200 arranged item units (<see cref="ArrangeItemUnits"/>), all made before the first
/// tick so every world built from the same seed starts identical. Players join, and the 400 mail units arrive, through ops the
/// generator writes (<see cref="InventoryFuzzGenerator.JoinOps"/>, <see cref="InventoryFuzzGenerator.PrepopulationOps"/>).
/// </summary>
internal sealed class InventoryFuzzWorld
{
    /// <summary>Columns and rows of each fixture external container (a GDD chest is 8 x 6).</summary>
    public const int ExternalW = 8;

    /// <inheritdoc cref="ExternalW"/>
    public const int ExternalH = 6;

    /// <summary>The arranged item units: entries per M-1a item def and units per entry (200 units in 31 entries).</summary>
    private static readonly (string Item, int Entries, byte Count)[] _itemArrangement =
    [
        ("plank", 4, 20), ("fiber", 3, 20), ("log", 4, 10), ("axe", 20, 1),
    ];

    private InventoryFuzzWorld(SimHost host)
    {
        Host = host;
        Inventory = host.Systems.OfType<InventorySystem>().Single();
        Automation = host.Systems.OfType<FuzzAutomationSystem>().Single();
        (Shared, Other) = CreateFixtureExternals(Inventory, host.Content);
        (ArrangedItemEntries, InitialItemUnits) = ArrangeItemUnits(Inventory, host.Content);
    }

    public SimHost Host { get; }

    public SimWorld World => Host.World;

    public InventorySystem Inventory { get; }

    public FuzzAutomationSystem Automation { get; }

    /// <summary>The shared fixture chest (at least half of the fuzz's ops target it).</summary>
    public ContainerId Shared { get; }

    /// <summary>The second fixture external container.</summary>
    public ContainerId Other { get; }

    /// <summary>The arranged item entries' ids (detached, in Flight until the pre-population catches them).</summary>
    public uint[] ArrangedItemEntries { get; }

    /// <summary>The arranged units per item def (indexed by def ordinal).</summary>
    public long[] InitialItemUnits { get; }

    /// <summary>
    /// Builds a world with run seed <paramref name="runSeed"/> over <paramref name="content"/> (default: the M-1a built-in set);
    /// <paramref name="recorder"/> and <paramref name="sink"/> are bound when given.
    /// </summary>
    public static InventoryFuzzWorld Build(ulong runSeed, ContentSet? content = null, IOpRecorder? recorder = null, IEventSink? sink = null)
    {
        var profile = SimProfile.ForTest(
            "inventory-fuzz",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx => new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
            new SimSystemEntry(SystemId.MailDestinations, ctx => new FuzzAutomationSystem((InventorySystem)ctx.Api<IInventoryApi>(SystemId.GridInventory), ctx.Rng, ctx.Tick)));
        SimHost host = SimComposition.Build(new SimBuildArgs(profile, content ?? BuiltInContent.CreateM1a(), runSeed) { Recorder = recorder, EventSink = sink });
        return new InventoryFuzzWorld(host);
    }

    /// <summary>The actor of the player in <paramref name="slot"/> (after its join).</summary>
    public ActorId ActorOf(int slot) => ActorId.FromEntity(Host.Services.Slots.Entry(PlayerSlot.FromValue((byte)slot)).PlayerId);

    /// <summary>
    /// The two fixture external containers: Mail Lead default, approved by Evan 2026-11-01 (TD-204 item 8). M-1a ships no chest def, so both are
    /// created with the internal <see cref="InventorySystem.TryCreateContainer"/> from an existing M-1a def that accepts both mail and
    /// items: <c>player_inventory</c> (no allowed-category list), with explicit chest dimensions and no owner. Change the fixture's
    /// containers here only.
    /// </summary>
    private static (ContainerId Shared, ContainerId Other) CreateFixtureExternals(InventorySystem inventory, ContentSet content)
    {
        DefIndex def = DefOf(content.Containers, "player_inventory");
        ContainerDef d = content.Containers[def];
        Assert.False(d.HasAllowedCategories); // accepts mail and items
        Assert.True(inventory.TryCreateContainer(def, ExternalW, ExternalH, ContainerLayout.Grid, ContainerHeader.NoOwner, out ContainerId shared));
        Assert.True(inventory.TryCreateContainer(def, ExternalW, ExternalH, ContainerLayout.Grid, ContainerHeader.NoOwner, out ContainerId other));
        return (shared, other);
    }

    /// <summary>
    /// The 200 item units. M-1a has no item faucet (no Grant: the ledger's <c>granted</c> is set only by the story-017 Recount seam), so,
    /// as the inventory rigs do, the units are arranged as detached entries before the first tick and the ledger is recounted once
    /// (they count as granted, in Flight); the pre-population then catches them into the panels with <c>TryCatch</c>.
    /// </summary>
    private static (uint[] Entries, long[] Units) ArrangeItemUnits(InventorySystem inventory, ContentSet content)
    {
        var ids = new List<uint>();
        var units = new long[content.Items.Count];
        foreach ((string item, int entries, byte count) in _itemArrangement)
        {
            DefIndex i = DefOf(content.Items, item);
            ItemDef def = content.Items[i];
            for (int n = 0; n < entries; n++)
            {
                Assert.True(inventory.Entries.TryAllocate(0, out ushort slot, out EntryId id));
                ref EntryRecord e = ref inventory.Entries[slot];
                e.ItemId = i.Value;
                e.W = (byte)def.GridW;
                e.H = (byte)def.GridH;
                inventory.Entries.SetCount(slot, count);
                ids.Add(id.Value);
                units[i.Value] += count;
            }
        }

        inventory.Ledger.Recount(inventory);
        return (ids.ToArray(), units);
    }

    /// <summary>The ordinal of def <paramref name="id"/>.</summary>
    internal static DefIndex DefOf<T>(DefTable<T> table, string id)
        where T : IDef
    {
        ReadOnlySpan<T> span = table.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].Id == id)
            {
                return DefIndex.FromValue((ushort)i);
            }
        }

        throw new InvalidOperationException($"No def '{id}'.");
    }
}
