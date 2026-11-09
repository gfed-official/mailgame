using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// The in-memory client <see cref="IPanelView"/> of manual-delivery story 002 (ADR-0016 Risks: tests use an in-memory view): a
/// cursor entry, an 8-slot hotbar indexed by slot, and inventory and backpack lists the caller fills in row-major order. Counts every
/// <see cref="IPanelView.EntryCount"/> and <see cref="IPanelView.Entry"/> call in <see cref="Reads"/>.
/// </summary>
internal sealed class InMemoryPanelView : IPanelView
{
    public const int HotbarSlots = 8;

    private uint _nextId = 1;

    public PanelEntryFacts Cursor { get; set; }

    public PanelEntryFacts[] Hotbar { get; } = new PanelEntryFacts[HotbarSlots];

    public List<PanelEntryFacts> Inventory { get; } = new();

    public List<PanelEntryFacts> Backpack { get; } = new();

    public int CursorSlot { get; set; } = -1;

    public int Reads { get; private set; }

    /// <summary>A mail entry with a fresh id; <paramref name="runs"/> are (deadline, count, value) triples, oldest first.</summary>
    public PanelEntryFacts Mail(ushort kind, AddressId address, byte flags = 0, params (int Deadline, int Count, int Value)[] runs)
    {
        var hint = new PanelHintRun[runs.Length];
        int units = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            hint[i] = new PanelHintRun((byte)runs[i].Deadline, (byte)runs[i].Count, (ushort)runs[i].Value);
            units += runs[i].Count;
        }

        return new PanelEntryFacts(EntryId.Real(_nextId++), kind, ItemCategory.Mail, address, flags, (byte)units, hint);
    }

    /// <summary>A one-unit tool entry with a fresh id.</summary>
    public PanelEntryFacts Tool(ushort itemId) =>
        new(EntryId.Real(_nextId++), itemId, ItemCategory.Tool, AddressId.None, 0, 1, ReadOnlySpan<PanelHintRun>.Empty);

    public int EntryCount(PanelSection s)
    {
        Reads++;
        return s switch
        {
            PanelSection.Cursor => Cursor.IsEntry ? 1 : 0,
            PanelSection.Hotbar => HotbarSlots,
            PanelSection.Inventory => Inventory.Count,
            _ => Backpack.Count,
        };
    }

    public PanelEntryFacts Entry(PanelSection s, int i)
    {
        Reads++;
        return s switch
        {
            PanelSection.Cursor => i == 0 ? Cursor : default,
            PanelSection.Hotbar => (uint)i < HotbarSlots ? Hotbar[i] : default,
            PanelSection.Inventory => (uint)i < (uint)Inventory.Count ? Inventory[i] : default,
            _ => (uint)i < (uint)Backpack.Count ? Backpack[i] : default,
        };
    }
}
