using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// One live container's header (ADR-0009 section 5): the replicated, slot-indexed record in <see cref="ContainerTable"/>. Dimensions are
/// instance state (initialised from the <c>ContainerDef</c> and <c>IStatSheet.Read(InventoryRows)</c>; perks grow rows later). The
/// outfeed and heap handles ADR-0009 lists are added by the stories that need them. A small blittable struct; a default value is a
/// free slot (<see cref="Id"/> none).
/// </summary>
/// <example>
/// <code>
/// ref ContainerHeader h = ref table[slot];
/// if (h.IsLive) { int cells = h.W * h.H; }
/// </code>
/// </example>
public struct ContainerHeader
{
    /// <summary>The owner slot value meaning "no owner" (a world container such as <c>po_intake</c>).</summary>
    public const byte NoOwner = 0xFF;

    /// <summary>The container's id; none while the table slot is free.</summary>
    public ContainerId Id;

    /// <summary>The container's def (ordinal in the content set's container table).</summary>
    public DefIndex DefIndex;

    /// <summary>Columns of the instance (def width).</summary>
    public ushort W;

    /// <summary>Rows of the instance (def rows after the <c>InventoryRows</c> stat).</summary>
    public ushort H;

    /// <summary>Layout copied from the def.</summary>
    public ContainerLayout Layout;

    /// <summary>The owning player slot value, or <see cref="NoOwner"/>.</summary>
    public byte OwnerSlot;

    /// <summary>Free cells (starts at <c>W * H</c>); entries (story 002) maintain it.</summary>
    public ushort FreeCells;

    /// <summary>Entries held (story 002 maintains it).</summary>
    public ushort EntryCount;

    /// <summary>Bumped by every mutation of the container's contents.</summary>
    public uint Version;

    /// <summary>The <see cref="Version"/> at the last whole-container replace.</summary>
    public uint LastReplaceVersion;

    /// <summary>Bumped when the stat sheet changed in a way that affects this container (R15 growth).</summary>
    public uint StatEpoch;

    /// <summary>First sort stamp (ADR-0009 <c>lastSorted[2]</c>; written by the sort story).</summary>
    public uint LastSorted0;

    /// <summary>Second sort stamp (ADR-0009 <c>lastSorted[2]</c>).</summary>
    public uint LastSorted1;

    /// <summary>True when the header describes a live container.</summary>
    public readonly bool IsLive => !Id.IsNone;
}
