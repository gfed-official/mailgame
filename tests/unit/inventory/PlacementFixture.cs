using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// The shared H-R4-1 fixture (GDD grid-inventory H-R4-1; story grid-inventory/006, reused by story 007's op-form test): the M-1a
/// <see cref="BuiltInContent"/>, a live <see cref="StatSheetWriter"/> sheet, and an 8 x 4 <see cref="ContainerLayout.Grid"/>
/// "chest" instance created from the unrestricted <c>player_inventory</c> def (no 8 x 4 def exists at M-1a; dims are instance
/// state) holding a Medium at (6,2) and 4 letters to #3 at (0,0). Other containers (<c>po_intake</c>, <c>hotbar</c>, ...) are
/// created on demand. Entries are written straight into the pool and occupancy (the rules read nothing else). Addresses are the
/// literal opaque u32s 3 and 17; nothing is random or timed.
/// </summary>
internal sealed class PlacementFixture
{
    /// <summary>The address of the fixture's letter stack.</summary>
    public const uint Address3 = 3u;

    /// <summary>The H-R3 address.</summary>
    public const uint Address17 = 17u;

    public PlacementFixture()
    {
        Content = BuiltInContent.CreateM1a();
        Stats = new StatSheetWriter(Content, new SimCounters());
        Containers = new ContainerTable(8);
        Occupancy = new Occupancy(8, Content.Inventory.MaxRows, 20);
        Entries = new EntryPool(256, new SimCheckRing());

        Chest = CreateContainer("player_inventory", 8, 4);
        Medium = AddMail(Chest, "medium", 6, 2, address: 99u);
        Letters = AddMail(Chest, "letter", 0, 0, address: Address3, count: 4);
    }

    public ContentSet Content { get; }

    public StatSheetWriter Stats { get; }

    public IStatSheet Sheet => Stats.Sheet;

    public ContainerTable Containers { get; }

    public Occupancy Occupancy { get; }

    public EntryPool Entries { get; }

    /// <summary>The 8 x 4 grid chest's table slot.</summary>
    public ushort Chest { get; }

    /// <summary>The Medium at chest (6,2).</summary>
    public ushort Medium { get; }

    /// <summary>The 4 letters to #3 at chest (0,0).</summary>
    public ushort Letters { get; }

    public static DefIndex Index<T>(DefTable<T> table, string id)
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

        throw new InvalidOperationException("no def " + id);
    }

    public ContainerDef ContainerDef(string id) => Content.Containers[Index(Content.Containers, id)];

    public ContainerDef DefOf(ushort containerSlot) => Content.Containers[Containers[containerSlot].DefIndex];

    public DefIndex KindIndex(string id) => Index(Content.MailKinds, id);

    public DefIndex ItemIndex(string id) => Index(Content.Items, id);

    /// <summary>Creates a container from def <paramref name="defId"/> (its def dims unless given) and resets its occupancy.</summary>
    public ushort CreateContainer(string defId, int w = 0, int h = 0)
    {
        DefIndex def = Index(Content.Containers, defId);
        ContainerDef d = Content.Containers[def];
        w = w == 0 ? d.GridW : w;
        h = h == 0 ? d.GridH : h;
        if (!Containers.TryCreate(def, w, h, d.Layout, ContainerHeader.NoOwner, out ContainerId id) || !Containers.TryGetSlot(id, out ushort slot))
        {
            throw new InvalidOperationException("container table full");
        }

        Occupancy.Reset(slot, w, h);
        return slot;
    }

    public ushort AddMail(ushort container, string kind, int x, int y, uint address, byte flags = 0, byte count = 1, bool rotated = false)
    {
        DefIndex k = KindIndex(kind);
        MailKindDef def = Content.MailKinds[k];
        ushort slot = Allocate(container, (byte)def.GridW, (byte)def.GridH, x, y, rotated, count);
        ref EntryRecord e = ref Entries[slot];
        e.IsMail = true;
        e.MailKind = k.Value;
        e.AddressId = address;
        e.Flags = flags;
        return slot;
    }

    public ushort AddItem(ushort container, string item, int x, int y, byte count = 1, bool rotated = false)
    {
        DefIndex i = ItemIndex(item);
        ItemDef def = Content.Items[i];
        ushort slot = Allocate(container, (byte)def.GridW, (byte)def.GridH, x, y, rotated, count);
        Entries[slot].ItemId = i.Value;
        return slot;
    }

    /// <summary>A mail subject with <c>effMax</c> read from the live sheet now (op time).</summary>
    public PlacementSubject Mail(string kind, uint address, byte flags = 0)
    {
        DefIndex k = KindIndex(kind);
        return PlacementSubject.ForMail(Content.MailKinds[k], k, address, flags, Sheet);
    }

    public PlacementSubject Item(string item)
    {
        DefIndex i = ItemIndex(item);
        return PlacementSubject.ForItem(Content.Items[i], i);
    }

    /// <summary>Validates against the container's own built-in def.</summary>
    public PlacementVerdict Validate(ushort container, in PlacementSubject subject, int x, int y, bool rotated = false, ushort sourceSlot = Occupancy.Free, bool wholeMove = false) =>
        Validate(container, DefOf(container), in subject, x, y, rotated, sourceSlot, wholeMove);

    /// <summary>Validates against <paramref name="def"/> (a fixture def standing in for the container's).</summary>
    public PlacementVerdict Validate(ushort container, ContainerDef def, in PlacementSubject subject, int x, int y, bool rotated = false, ushort sourceSlot = Occupancy.Free, bool wholeMove = false) =>
        PlacementRules.Validate(Occupancy, Entries, container, in Containers[container], def, in subject, x, y, rotated, sourceSlot, wholeMove);

    private ushort Allocate(ushort container, byte w, byte h, int x, int y, bool rotated, byte count)
    {
        if (!Entries.TryAllocate(0, out ushort slot, out _))
        {
            throw new InvalidOperationException("entry pool full");
        }

        ref ContainerHeader header = ref Containers[container];
        bool slots = header.Layout == ContainerLayout.Slots;
        Footprint.Canonicalise(w, h, ref rotated);
        int fw = 1;
        int fh = 1;
        if (!slots)
        {
            Footprint.Effective(w, h, rotated, out fw, out fh);
        }

        ref EntryRecord e = ref Entries[slot];
        e.ContainerSlot = container;
        e.X = (byte)x;
        e.Y = (byte)y;
        e.W = w;
        e.H = h;
        e.Rotated = rotated && !slots;
        e.Count = count;
        Occupancy.Place(container, header.W, header.H, x, y, fw, fh, slot);
        return slot;
    }
}
