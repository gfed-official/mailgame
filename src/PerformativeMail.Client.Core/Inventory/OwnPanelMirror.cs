using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Client.Core.Inventory;

/// <summary>
/// The client's mirror of its own panel (WP-F; ADR-0009 section 4 rules 2-3; the minimal stand-in for H-MIRROR's <c>ContainerMirror</c>):
/// the hotbar, player_inventory and cursor, fed only by the targeted container records MirrorDrain hands it (<c>Inserted</c>,
/// <c>Removed</c>, <c>Merged</c>, <c>Moved</c>; <c>SplitFrom</c> needs nothing, its <c>Removed</c> and <c>Inserted</c> carry the change).
/// A container's records are staged and applied when its <c>Version</c> seal arrives (the client application unit); a seal whose
/// <c>version_from</c> is not the mirrored version is applied anyway and counted in <see cref="Gaps"/> (resync is M0). Records of any
/// other container (the Intake while it is open) and mailId continuation chunks are ignored. Mail hint runs do not travel, so the
/// entries carry none. It is the client's <see cref="IPanelView"/> (deliver preview, hotbar strip) and, with the local hotbar selection
/// (<see cref="Select"/>), its <see cref="ISelectedEntryView"/> (aim preview). Never reads <c>SimWorld</c>. Allocation-free after
/// construction; client thread only.
/// </summary>
/// <example>
/// <code>
/// var mirror = new OwnPanelMirror(content, ContainerIdPlan.Hotbar(0), ContainerIdPlan.PlayerInventory(0), ContainerIdPlan.Cursor(0));
/// builder.BindMirrorPart(OwnPanelMirror.Kinds, mirror);
/// mirror.Select(1);
/// if (mirror.TryGet(out SelectedEntryFacts f)) { /* throwable */ }
/// </code>
/// </example>
public sealed class OwnPanelMirror : IMirrorPart, IPanelView, ISelectedEntryView
{
    /// <summary>The hotbar's slot count (slot 0 is the hands).</summary>
    public const int HotbarSlots = 8;

    private const int Hotbar = 0;
    private const int Bag = 1;
    private const int Held = 2;
    private const int MaxEntries = 32;
    private const int MaxStaged = 128;

    private readonly ContentSet _content;
    private readonly uint[] _ids = new uint[3];
    private readonly uint[] _versions = new uint[3];
    private readonly Row[][] _entries = [new Row[MaxEntries], new Row[MaxEntries], new Row[MaxEntries]];
    private readonly int[] _counts = new int[3];
    private readonly EventEntry[] _staged = new EventEntry[MaxStaged];
    private readonly int[] _order = new int[MaxEntries];
    private int _stagedCount;
    private bool _orderValid;

    /// <summary>Creates the mirror over the local player's three panel containers.</summary>
    /// <param name="content">The content set (item categories, weight classes).</param>
    /// <param name="hotbar">The hotbar container.</param>
    /// <param name="inventory">The player_inventory container.</param>
    /// <param name="cursor">The cursor container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public OwnPanelMirror(ContentSet content, ContainerId hotbar, ContainerId inventory, ContainerId cursor)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _ids[Hotbar] = hotbar.Value;
        _ids[Bag] = inventory.Value;
        _ids[Held] = cursor.Value;
    }

    /// <summary>The container record kinds this part applies.</summary>
    public static EventKindSet Kinds => EventKindSet.Of(
        InvEventKinds.Inserted, InvEventKinds.Removed, InvEventKinds.Merged, InvEventKinds.Moved, InvEventKinds.SplitFrom, InvEventKinds.Version);

    /// <summary>The hotbar container id.</summary>
    public ContainerId HotbarId => new(_ids[Hotbar]);

    /// <summary>The player_inventory container id.</summary>
    public ContainerId InventoryId => new(_ids[Bag]);

    /// <summary>Seals whose <c>version_from</c> did not match the mirror (applied anyway; resync is M0).</summary>
    public int Gaps { get; private set; }

    /// <summary>Applied seals, all containers (changes whenever the displayed panel does).</summary>
    public uint Version { get; private set; }

    /// <inheritdoc />
    public byte SelectedIndex { get; private set; }

    /// <inheritdoc />
    public int CursorSlot => -1;

    /// <summary>Selects hotbar slot <paramref name="index"/> locally (0 the hands to 7); the caller sends <c>HotbarSelect</c>.</summary>
    /// <param name="index">The slot.</param>
    /// <returns>False for an index outside 0 to 7.</returns>
    public bool Select(int index)
    {
        if ((uint)index >= HotbarSlots)
        {
            return false;
        }

        SelectedIndex = (byte)index;
        return true;
    }

    /// <summary>The hotbar slot holding <paramref name="entry"/>, or -1.</summary>
    /// <param name="entry">The entry id.</param>
    /// <returns>The slot.</returns>
    public int HotbarSlotOf(EntryId entry)
    {
        int i = Find(Hotbar, entry.Value);
        return i < 0 ? -1 : _entries[Hotbar][i].X;
    }

    /// <summary>The oldest-placed mail entry of the player_inventory in row-major order, or <see cref="EntryId.None"/>.</summary>
    /// <returns>The entry id.</returns>
    public EntryId FirstInventoryMail()
    {
        int n = EntryCount(PanelSection.Inventory);
        for (int i = 0; i < n; i++)
        {
            PanelEntryFacts f = Entry(PanelSection.Inventory, i);
            if (f.Category == ItemCategory.Mail)
            {
                return f.EntryId;
            }
        }

        return EntryId.None;
    }

    /// <summary>The address of own entry <paramref name="entry"/> (any section), or <see cref="AddressId.None"/>.</summary>
    /// <param name="entry">The entry id.</param>
    /// <returns>The address.</returns>
    public AddressId AddressOf(EntryId entry)
    {
        for (int c = 0; c < _ids.Length; c++)
        {
            int i = Find(c, entry.Value);
            if (i >= 0)
            {
                return new AddressId(_entries[c][i].Address);
            }
        }

        return AddressId.None;
    }

    /// <summary>True when any hotbar slot from 1 holds mail.</summary>
    /// <returns>Whether the hotbar carries mail.</returns>
    public bool HotbarHasMail()
    {
        for (int i = 0; i < _counts[Hotbar]; i++)
        {
            if (_entries[Hotbar][i].IsMail)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
        if ((e.Flags & EventFlags.Continuation) != 0)
        {
            return;
        }

        uint container = e.PayloadAs<uint>();
        int c = Array.IndexOf(_ids, container);
        if (c < 0)
        {
            return;
        }

        if (e.Kind != InvEventKinds.Version)
        {
            if (_stagedCount < MaxStaged)
            {
                _staged[_stagedCount++] = e;
            }

            return;
        }

        InvVersionPayload seal = e.PayloadAs<InvVersionPayload>();
        if (seal.VersionFrom != _versions[c])
        {
            Gaps++;
        }

        int keep = 0;
        for (int i = 0; i < _stagedCount; i++)
        {
            ref readonly EventEntry r = ref _staged[i];
            if (r.PayloadAs<uint>() == container)
            {
                ApplyRecord(c, in r);
            }
            else
            {
                _staged[keep++] = r;
            }
        }

        _stagedCount = keep;
        _versions[c] = seal.VersionTo;
        _orderValid = false;
        Version = unchecked(Version + 1);
    }

    /// <inheritdoc />
    public void ApplyReply(in Reply r)
    {
    }

    /// <inheritdoc />
    public int EntryCount(PanelSection s) => s switch
    {
        PanelSection.Cursor => _counts[Held] > 0 ? 1 : 0,
        PanelSection.Hotbar => HotbarSlots,
        PanelSection.Inventory => _counts[Bag],
        _ => 0,
    };

    /// <inheritdoc />
    public PanelEntryFacts Entry(PanelSection s, int i)
    {
        switch (s)
        {
            case PanelSection.Cursor:
                return i == 0 && _counts[Held] > 0 ? Facts(in _entries[Held][0]) : default;
            case PanelSection.Hotbar:
                for (int k = 0; k < _counts[Hotbar]; k++)
                {
                    if (_entries[Hotbar][k].X == i)
                    {
                        return Facts(in _entries[Hotbar][k]);
                    }
                }

                return default;
            case PanelSection.Inventory:
                RefreshOrder();
                return (uint)i < (uint)_counts[Bag] ? Facts(in _entries[Bag][_order[i]]) : default;
            default:
                return default;
        }
    }

    /// <inheritdoc />
    public bool TryGet(out SelectedEntryFacts facts)
    {
        facts = default;
        for (int k = 0; k < _counts[Hotbar]; k++)
        {
            ref readonly Row e = ref _entries[Hotbar][k];
            if (e.X != SelectedIndex)
            {
                continue;
            }

            ItemCategory category = CategoryOf(in e);
            var def = DefIndex.FromValue(e.IsMail ? e.MailKind : e.ItemId);
            WeightClass weight = e.IsMail ? _content.MailKinds[def].WeightClass : _content.Items[def].WeightClass;
            var unit = new UnitFacts(def, category, new AddressId(e.Address), 0, 0, (MailFlags)e.Flags);
            facts = new SelectedEntryFacts(SelectedIndex, EntryId.FromValue(e.Id), e.Count, e.IsMail, unit, weight, def);
            return true;
        }

        return false;
    }

    private void ApplyRecord(int c, in EventEntry r)
    {
        Row[] list = _entries[c];
        switch (r.Kind)
        {
            case InvEventKinds.Inserted:
            {
                InvInsertedPayload p = r.PayloadAs<InvInsertedPayload>();
                if (_counts[c] < MaxEntries)
                {
                    list[_counts[c]++] = new Row
                    {
                        Id = p.EntryId, X = p.X, Y = p.Y, Count = p.Count, IsMail = p.IsMail != 0, MailKind = p.MailKind, ItemId = p.ItemId,
                        Address = p.AddressId, Flags = p.Flags,
                    };
                }

                break;
            }

            case InvEventKinds.Removed:
            {
                InvRemovedPayload p = r.PayloadAs<InvRemovedPayload>();
                int i = Find(c, p.EntryId);
                if (i >= 0 && list[i].Count <= p.Count)
                {
                    list[i] = list[--_counts[c]];
                }
                else if (i >= 0)
                {
                    list[i].Count = (byte)(list[i].Count - p.Count);
                }

                break;
            }

            case InvEventKinds.Merged:
            {
                InvMergedPayload p = r.PayloadAs<InvMergedPayload>();
                int i = Find(c, p.TargetEntryId);
                if (i >= 0)
                {
                    list[i].Count = (byte)Math.Min(byte.MaxValue, list[i].Count + p.AddedCount);
                }

                break;
            }

            case InvEventKinds.Moved:
            {
                InvMovedPayload p = r.PayloadAs<InvMovedPayload>();
                int i = Find(c, p.EntryId);
                if (i >= 0)
                {
                    list[i].X = p.X;
                    list[i].Y = p.Y;
                }

                break;
            }

            default:
                break; // SplitFrom: its Removed and Inserted already carry the change
        }
    }

    private int Find(int c, uint id)
    {
        Row[] list = _entries[c];
        for (int i = 0; i < _counts[c]; i++)
        {
            if (list[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    // The player_inventory in row-major anchor order (the host scan's order).
    private void RefreshOrder()
    {
        if (_orderValid)
        {
            return;
        }

        Row[] list = _entries[Bag];
        int n = _counts[Bag];
        for (int i = 0; i < n; i++)
        {
            _order[i] = i;
        }

        for (int i = 1; i < n; i++)
        {
            int v = _order[i];
            int key = (list[v].Y * 256) + list[v].X;
            int j = i - 1;
            while (j >= 0 && (list[_order[j]].Y * 256) + list[_order[j]].X > key)
            {
                _order[j + 1] = _order[j];
                j--;
            }

            _order[j + 1] = v;
        }

        _orderValid = true;
    }

    private ItemCategory CategoryOf(in Row e) =>
        e.IsMail ? ItemCategory.Mail
        : e.ItemId < _content.Items.Count ? _content.Items[DefIndex.FromValue(e.ItemId)].Category
        : default;

    private PanelEntryFacts Facts(in Row e) =>
        new(EntryId.FromValue(e.Id), e.IsMail ? e.MailKind : e.ItemId, CategoryOf(in e), new AddressId(e.Address), e.Flags, e.Count, default);

    private struct Row
    {
        public uint Id;
        public byte X;
        public byte Y;
        public byte Count;
        public bool IsMail;
        public ushort MailKind;
        public ushort ItemId;
        public uint Address;
        public byte Flags;
    }
}
