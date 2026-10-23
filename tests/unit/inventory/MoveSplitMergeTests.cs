using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/008: the <c>Move</c> handler's split-and-place and hotbar swap arms (<see cref="MoveHandler"/>,
/// <see cref="InventoryMutator.SplitAndPlace"/>) and the <c>Merge</c> op (<see cref="MergeHandler"/>) through the real phase-0 drain of
/// a composed world with players A (slot 0) and B (slot 1); GDD R4 (l.79), R6, R7 (l.90, l.103), l.379 and H-X-4 (l.817); H-R4-2 c/d,
/// H-R4-3 c, H-R6-1 b, H-R7-3; ADR-0009 sections 1-4; ADR-0004 (ids never reused). Every case runs from slot 0 and from slot 1. State is
/// arranged only through real calls: the rig's mutator-backed <c>PlaceMail</c> / <c>PlaceItem</c>, <c>TryOpenExternal</c>, Intake
/// <c>Spawn</c> and <c>DetachForThrow</c> in a tick phase (the arena exhaustion, an unplaced entry), the rig's documented settings
/// overrides (<c>entryPoolCapacity</c>, and <see cref="InventorySettings.EntryInitialLastId"/> through <c>tune</c>), story 011's
/// issued-but-dead container id seam (<see cref="SkipOneContainerId"/>) and the real ops. Every
/// reject asserts the reply (reason and <c>ResultVersionA/B</c>), that the tick log holds the <c>OpReply</c> alone (no record, no
/// seal), and <see cref="SideEffects"/> unchanged (container headers with versions, occupancy row bitmaps, entries with their mail
/// ids and StackKey-index membership, the pool's high-water and live count, the arena, the negative-fit cache, weight P,
/// <c>CountItem</c>, subscriber masks, externals, check ring writes and the id allocators). The entry-pool refusal, the swap's
/// per-container bump, the merge chain and a refused split leaving the entry where it is (arena or pool) are Mail Lead planning
/// defaults pending Evan (TD-152); Evan's 2026-10-22 5:11 AM PT ruling covers only a leashed letter staying on the cursor when the
/// mail-id arena is full (the code, <c>capacity_exhausted</c>, is ADR-0009's).
/// </summary>
public partial class MoveSplitMergeTests
{
    private const uint Address3 = 3u;
    private const uint Address5 = 5u;

    // ------------------------------------------------------------------ helpers

    /// <summary>The fixture container def <see cref="MergeContent"/> adds: an 8 x 4 grid with <c>acceptsInsert: false</c>.</summary>
    private const string OutputOnlyDef = "output_only";

    /// <summary>The M-1a content plus <see cref="OutputOnlyDef"/> and a <c>bigstack</c> item (1 x 1, <c>maxStack</c> 300, above the u8 count).</summary>
    internal static ContentSet MergeContent()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ContainerDef inventoryDef = m1a.Containers[PlacementFixture.Index(m1a.Containers, "player_inventory")];
        ImmutableArray<ContainerDef> containers = m1a.Containers.AsSpan().ToArray()
            .Append(inventoryDef with { Id = OutputOnlyDef, GridW = 8, GridH = 4, AcceptsInsert = false })
            .ToImmutableArray();
        ItemDef plank = m1a.Items[PlacementFixture.Index(m1a.Items, "plank")];
        ImmutableArray<ItemDef> items = m1a.Items.AsSpan().ToArray().Append(plank with { Id = "bigstack", MaxStack = 300 }).ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, new DefTable<ContainerDef>(containers), new DefTable<ItemDef>(items), m1a.MailKinds, m1a.DestinationTypes, m1a.MailMixes);
    }

    /// <summary>The M-1a content with <c>letter</c>'s <c>maxStack</c> raised from 20 to 255 (mail's cap: the u8 count and the <c>MailMaxStack</c> bound).</summary>
    internal static ContentSet MailStack255Content()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ImmutableArray<MailKindDef> kinds = m1a.MailKinds.AsSpan().ToArray()
            .Select(k => k.Id == "letter" ? k with { MaxStack = 255 } : k)
            .ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, m1a.Containers, m1a.Items, new DefTable<MailKindDef>(kinds), m1a.DestinationTypes, m1a.MailMixes);
    }

    /// <summary>
    /// The story 011 seam for an issued-but-dead container id (no container is destroyed before M-1b): advances the container table's
    /// private id counter by one through reflection, which must find the field and a non-zero high-water, so the skipped id is below
    /// the high-water but not live. The map and every live container are untouched.
    /// </summary>
    internal static uint SkipOneContainerId(InvAutoPlaceRig rig)
    {
        ContainerTable table = rig.Inventory.Containers;
        uint skipped = table.NextId;
        Assert.True(skipped > 0u);
        FieldInfo? field = typeof(ContainerTable).GetField("_nextId", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(skipped, Convert.ToUInt32(field.GetValue(table)));
        field.SetValue(table, skipped + 1);
        Assert.Equal(skipped + 1, table.NextId);
        Assert.False(table.TryGetSlot(new ContainerId(skipped), out _));
        return skipped;
    }

    /// <summary>Places <paramref name="units"/> of <paramref name="kind"/> (an item def, else letters of that mail kind to #3 with ids from <paramref name="firstId"/>).</summary>
    private static ushort PlaceUnits(InvAutoPlaceRig rig, ushort container, string kind, int x, int y, byte units, uint firstId) =>
        kind is "plank" or "fiber" or "log" or "axe"
            ? rig.PlaceItem(container, kind, x, y, units)
            : rig.PlaceMail(container, kind, x, y, Address3, InvAutoPlaceRig.Ids(firstId, units));

    /// <summary>Every live mail entry's id list is strictly ascending, and no id is held by two entries (R6).</summary>
    internal static void AssertEveryIdListAscendingNoDuplicate(InvAutoPlaceRig rig)
    {
        var seen = new HashSet<uint>();
        int lists = 0;
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord e = rig.Entries[(ushort)s];
            if (!e.IsLive || !e.IsMail)
            {
                continue;
            }

            uint[] ids = rig.IdsOf((ushort)s);
            Assert.Equal(e.Count, ids.Length);
            for (int i = 1; i < ids.Length; i++)
            {
                Assert.True(ids[i - 1] < ids[i], $"entry {s}: ids not ascending at {i}");
            }

            Assert.All(ids, id => Assert.True(seen.Add(id), $"id {id} held twice"));
            lists++;
        }

        Assert.True(lists > 0);
    }

    private static Op MergeOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, ushort target) =>
        MergeOpRaw(rig.IdOf(from).Value, rig.Entries[entry].Id.Value, rig.IdOf(to).Value, rig.Entries[target].Id.Value);

    private static Op MergeOpRaw(uint from, uint entry, uint to, uint target)
    {
        var op = new Op { Kind = InvOpKinds.Merge, ReqId = 83 };
        op.SetPayload(new MergePayload { From = from, EntryId = entry, To = to, TargetEntryId = target });
        return op;
    }

    private static InvAutoPlaceRig NewRig(ContentSet? content = null) => new(content: content, joinPlayerB: true);

    /// <summary>A fresh 8 x 4 <c>player_inventory</c>-def grid with no owner, opened as <paramref name="slot"/>'s external.</summary>
    internal static ushort OpenChest(InvAutoPlaceRig rig, int slot)
    {
        ushort chest = rig.CreateGrid(8, 4);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.IdOf(chest)));
        return chest;
    }

    /// <summary>The container named <paramref name="where"/>: <c>chest</c>, or the slot's <c>inventory</c>, <c>hotbar</c> or <c>cursor</c>.</summary>
    private static ushort Where(InvAutoPlaceRig rig, int slot, string where, ushort chest) => where switch
    {
        "chest" => chest,
        "inventory" => rig.InventoryOf(slot),
        "hotbar" => rig.HotbarOf(slot),
        "cursor" => rig.CursorOf(slot),
        _ => throw new ArgumentOutOfRangeException(nameof(where), where, null),
    };

    /// <summary>The swap fixtures: <c>axe</c>, <c>planks</c> (3), <c>letters3</c> (3 letters to #3) or <c>letters5</c> (4 letters to #5), ids from <paramref name="firstId"/>.</summary>
    private static ushort PlaceNamed(InvAutoPlaceRig rig, ushort container, string name, int x, int y, uint firstId) => name switch
    {
        "axe" => rig.PlaceItem(container, "axe", x, y, 1),
        "planks" => rig.PlaceItem(container, "plank", x, y, 3),
        "letters3" => rig.PlaceMail(container, "letter", x, y, Address3, InvAutoPlaceRig.Ids(firstId, 3)),
        "letters5" => rig.PlaceMail(container, "letter", x, y, Address5, InvAutoPlaceRig.Ids(firstId, 4)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>Places <paramref name="units"/> of <paramref name="kind"/> (an item def, else a mail kind to #3 with ids from 1).</summary>
    private static ushort PlaceKind(InvAutoPlaceRig rig, ushort container, string kind, int x, int y, byte units)
    {
        bool isItem = false;
        for (int i = 0; i < rig.Content.Items.Count; i++)
        {
            isItem |= rig.Content.Items[DefIndex.FromValue((ushort)i)].Id == kind;
        }

        return isItem
            ? rig.PlaceItem(container, kind, x, y, units)
            : rig.PlaceMail(container, kind, x, y, Address3, InvAutoPlaceRig.Ids(1, units));
    }

    /// <summary>The M-1a content with the cursor def admitting mail only (a whitelist the displaced Axe fails).</summary>
    private static ContentSet MailOnlyCursorContent()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ImmutableArray<ContainerDef> containers = m1a.Containers.AsSpan().ToArray()
            .Select(c => c.Id == "cursor" ? c with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Mail) } : c)
            .ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, new DefTable<ContainerDef>(containers), m1a.Items, m1a.MailKinds, m1a.DestinationTypes, m1a.MailMixes);
    }

    /// <summary>Spawns single letters into the Intake (one tick) until <see cref="IInventoryApi.Spawn"/> places none.</summary>
    internal static void ExhaustArenaThroughIntakeSpawns(InvAutoPlaceRig rig)
    {
        DefIndex letter = rig.KindIndex("letter");
        ContainerId intake = rig.Inventory.IntakeContainer;
        var one = new MailId[1];
        uint next = 1_000_000;
        int placed = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            int n = 0;
            while (true)
            {
                one[0] = new MailId(next++);
                if (rig.Inventory.Spawn(intake, letter, 9, 0, one, MergePolicy.NoMerge).Placed != 1)
                {
                    return n;
                }

                n++;
            }
        });
        Assert.True(placed > 0);
        Assert.True(rig.Header(rig.Intake).FreeCells > 0); // refused at the arena, not for room
        Assert.False(rig.Inventory.MailIds.CanReserve(1));
    }

    /// <summary>
    /// Consumes <paramref name="units"/> of the entry through <see cref="IInventoryApi.ConsumeUnit"/> (story 015's real API), one per
    /// tick; the last unit's removal frees its arena block.
    /// </summary>
    internal static void ConsumeUnits(InvAutoPlaceRig rig, ushort container, ushort entry, int units)
    {
        var at = new EntryRef(rig.IdOf(container), rig.Entries[entry].Id);
        for (int i = 0; i < units; i++)
        {
            Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ConsumeUnit(at, ConsumeReason.Delivered, ActorId.System)));
        }

        Assert.False(rig.Entries.TryGetSlot(at.Entry, out _));
    }

    /// <summary>D1's <c>classPts</c> of the entry's def, from the balance knobs (not from the module under test).</summary>
    internal static int ClassPts(InvAutoPlaceRig rig, ushort entry)
    {
        EntryRecord e = rig.Entries[entry];
        WeightClass c = e.IsMail
            ? rig.Content.MailKinds[DefIndex.FromValue(e.MailKind)].WeightClass
            : rig.Content.Items[DefIndex.FromValue(e.ItemId)].WeightClass;
        InventoryBalance k = rig.Content.Inventory;
        return c switch
        {
            WeightClass.Light => k.WeightPointsLight,
            WeightClass.Medium => k.WeightPointsMedium,
            WeightClass.Heavy => k.WeightPointsHeavy,
            _ => throw new InvalidOperationException($"entry {entry}: class {c}"),
        };
    }

    /// <summary><see cref="IInventoryApi.WeightPoints"/> of <paramref name="slot"/> and of the other joined slot.</summary>
    private static (int Actor, int Other) Points(InvAutoPlaceRig rig, int slot) =>
        (rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)slot)), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)(1 - slot))));

    /// <summary>The version of every live container, by table slot.</summary>
    internal static uint[] Versions(InvAutoPlaceRig rig)
    {
        ContainerTable containers = rig.Inventory.Containers;
        var versions = new uint[containers.LiveCount];
        for (int s = 0; s < versions.Length; s++)
        {
            versions[s] = containers[(ushort)s].Version;
        }

        return versions;
    }

    /// <summary><c>CountItem</c> of every item def in every live container, in table order.</summary>
    private static int[] AllItemCounts(InvAutoPlaceRig rig)
    {
        ContainerTable containers = rig.Inventory.Containers;
        var counts = new List<int>();
        for (int s = 0; s < containers.LiveCount; s++)
        {
            for (int i = 0; i < rig.Content.Items.Count; i++)
            {
                counts.Add(rig.Inventory.CountItem(new[] { containers[(ushort)s].Id }, DefIndex.FromValue((ushort)i)));
            }
        }

        return counts.ToArray();
    }

    /// <summary>
    /// <see cref="IInventoryApi.WeightPoints"/> of every slot equals the brute-force recount (<see cref="WeightRecountPropertyTests.Recount"/>),
    /// and <c>CountItem</c> of every item def equals the sum of the live entries' units in every container.
    /// </summary>
    internal static void AssertWeightAndCountsMatchRecount(InvAutoPlaceRig rig)
    {
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            Assert.Equal(WeightRecountPropertyTests.Recount(rig.Content, rig.Inventory, s), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)s)));
        }

        ContainerTable containers = rig.Inventory.Containers;
        for (int i = 0; i < rig.Content.Items.Count; i++)
        {
            DefIndex item = DefIndex.FromValue((ushort)i);
            for (int c = 0; c < containers.LiveCount; c++)
            {
                int units = 0;
                for (int s = 0; s < rig.Entries.Capacity; s++)
                {
                    EntryRecord e = rig.Entries[(ushort)s];
                    if (e.IsLive && !e.IsMail && e.ContainerSlot == c && e.ItemId == item.Value)
                    {
                        units += e.Count;
                    }
                }

                Assert.Equal(units, rig.Inventory.CountItem(new[] { containers[(ushort)c].Id }, item));
            }
        }
    }

    /// <summary>The StackKey index equals a recount: a live entry is linked iff it is placed and its count is below its <c>effMax</c> read now.</summary>
    internal static void AssertIndexesMatchARecount(InvAutoPlaceRig rig)
    {
        int linked = 0;
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord e = rig.Entries[(ushort)s];
            if (!e.IsLive)
            {
                continue;
            }

            bool expected = e.ContainerSlot != EntryRecord.NoContainer && e.Count < rig.SubjectOf((ushort)s).EffMax;
            Assert.Equal(expected, rig.Index.Contains((ushort)s));
            linked += expected ? 1 : 0;
        }

        Assert.True(linked > 0);
    }

    private static Op MoveOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, int x, int y, byte count = 0)
    {
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 82 };
        op.SetPayload(new MovePayload
        {
            From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value, To = rig.IdOf(to).Value, BaseVersion = rig.Header(to).Version,
            X = (short)x, Y = (short)y, Count = count,
        });
        return op;
    }

    internal static Reply AssertOk(EventEntry[] log)
    {
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(CoreReason.Ok, reply.Reason);
        return reply;
    }

    /// <summary>
    /// The reject contract (TD-105 item 5; ADR-0007 l.128; ADR-0009 section 3): the reason; the reply's <c>ResultVersionA/B</c> are the
    /// source's and the destination's current versions; the <c>OpReply</c> is the only entry in the tick log (no record, no seal) and
    /// is sent to the actor's slot alone; and every recorded side effect is as before.
    /// </summary>
    private static void AssertRejected(InvAutoPlaceRig rig, EventEntry[] log, InvReason reason, List<long> before, int actorSlot, ushort from, ushort to) =>
        AssertRejectedWithVersions(rig, log, reason, before, actorSlot, (rig.Header(from).Version, rig.Header(to).Version));

    /// <summary>
    /// <see cref="AssertRejected"/> with the expected <c>(ResultVersionA, ResultVersionB)</c> given: (0, 0) for a reject before the
    /// containers are resolved (<c>bad_request</c>, <c>container_gone</c>, <c>not_open</c>).
    /// </summary>
    private static void AssertRejectedWithVersions(
        InvAutoPlaceRig rig, EventEntry[] log, InvReason reason, List<long> before, int actorSlot, (uint A, uint B) versions)
    {
        EventEntry entry = Assert.Single(log);
        Assert.Equal(EventKind.OpReply, entry.Kind);
        Assert.Equal(1 << actorSlot, entry.Audience);
        Reply reply = entry.PayloadAs<Reply>();
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal((ushort)reason, reply.Reason);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal(versions, (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(before, SideEffects(rig));
    }

    internal static (uint Container, uint Entry) Removed(EventEntry e)
    {
        InvRemovedPayload p = e.PayloadAs<InvRemovedPayload>();
        return (p.Container, p.EntryId);
    }

    internal static (uint Container, uint Entry) Inserted(EventEntry e)
    {
        InvInsertedPayload p = e.PayloadAs<InvInsertedPayload>();
        return (p.Container, p.EntryId);
    }

    internal static EventEntry[] InventoryRecords(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.ItemsLost).ToArray();

    /// <summary>The inventory records and seals that start a record (mail-id run continuations dropped), in log order.</summary>
    internal static EventEntry[] RecordHeads(EventEntry[] log) =>
        InventoryRecords(log).Where(e => (e.Flags & EventFlags.Continuation) == 0).ToArray();

    internal static InvVersionPayload[] Seals(EventEntry[] log) =>
        log.Where(e => e.Kind == InvEventKinds.Version).Select(e => e.PayloadAs<InvVersionPayload>()).ToArray();

    /// <summary>
    /// Everything a split or swap could change or record: every container header (version and lastReplaceVersion included), its
    /// occupancy <b>row bitmaps</b>, subscriber mask and <c>CountItem</c> of every item def; every slot's external, weight P and cursor
    /// origin (<see cref="InventorySystem.TryGetCursorOrigin"/>: present or not, and its entry, container, cells and rotation; story
    /// 010); every live entry with its position, count, arrival seq, mail ids and StackKey-index membership; the pool's high-water and
    /// live count; the arena's units, blocks and free and peak blocks per class; the negative-fit cache's private arrays (read by
    /// reflection, which must find at least one non-empty array); the check ring's writes; the entity and mail id allocators; and the
    /// live WorldItem count (story 010). Shared with <see cref="QuickMoveReturnTests"/>.
    /// </summary>
    internal static List<long> SideEffects(InvAutoPlaceRig rig)
    {
        var values = new List<long>();
        ContainerTable containers = rig.Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            ContainerHeader h = containers[(ushort)s];
            values.AddRange(new long[] { h.Id.Value, h.Version, h.LastReplaceVersion, h.StatEpoch, h.EntryCount, h.FreeCells, h.NextArrivalSeq });
            values.Add(rig.Inventory.Subscribers.Mask((ushort)s));
            for (int y = 0; y < h.H; y++)
            {
                values.Add(unchecked((long)rig.Inventory.Occupancy.RowBits((ushort)s, y)));
            }

            for (int i = 0; i < rig.Content.Items.Count; i++)
            {
                values.Add(rig.Inventory.CountItem(new[] { h.Id }, DefIndex.FromValue((ushort)i)));
            }
        }

        for (byte slot = 0; slot < PlayerSlot.Count; slot++)
        {
            values.Add(rig.Inventory.OpenSet.ExternalOf(PlayerSlot.FromValue(slot)).Value);
            values.Add(rig.Inventory.WeightPoints(PlayerSlot.FromValue(slot)));
            if (rig.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue(slot), out InventorySystem.CursorOrigin o))
            {
                values.AddRange(new long[] { 1, o.Entry.Value, o.Container.Value, o.X, o.Y, o.Rotated ? 1 : 0 });
            }
            else
            {
                values.Add(0);
            }
        }

        values.Add(rig.Entries.LastId);
        values.Add(rig.Entries.LiveCount);
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord r = rig.Entries[(ushort)s];
            if (r.IsLive)
            {
                values.AddRange(new long[] { s, r.Id.Value, r.Count, r.ContainerSlot, r.X, r.Y, r.Rotated ? 1 : 0, r.ArrivalSeq, r.MailIds.Raw });
                values.Add(rig.Index.Contains((ushort)s) ? 1 : 0);
                values.AddRange(rig.IdsOf((ushort)s).Select(id => (long)id));
            }
        }

        MailIdArena arena = rig.Inventory.MailIds;
        values.Add(arena.LiveUnits);
        values.Add(arena.LiveBlocks);
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            values.Add(arena.FreeBlocks(c));
            values.Add(arena.PeakLiveBlocks(c));
        }

        object cache = rig.Inventory.AutoPlace.NoFitCache;
        FieldInfo[] arrays = cache.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Where(f => f.FieldType.IsArray).ToArray();
        Assert.NotEmpty(arrays);
        int cells = 0;
        foreach (FieldInfo f in arrays)
        {
            var array = (Array?)f.GetValue(cache);
            Assert.NotNull(array);
            foreach (object v in array)
            {
                values.Add(Convert.ToInt64(v));
                cells++;
            }
        }

        Assert.True(cells > 0);
        SimCheckRing ring = rig.Host.World.CheckRing;
        values.Add(ring.Count + (long)ring.Overwritten + ring.Suppressed(CheckCode.InventoryArenaExhausted));
        for (int c = 0; c < 256; c++)
        {
            values.Add(rig.Host.Services.EntityIds.Peek((EntityClass)c));
        }

        values.Add(rig.Host.Services.MailIds.Peek());
        values.Add(rig.Inventory.WorldItems.LiveCount); // story 010: a reject (and return) never creates a WorldItem
        return values;
    }
}
