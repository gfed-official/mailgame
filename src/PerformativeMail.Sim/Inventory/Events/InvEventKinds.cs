using System.Collections.Immutable;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Inventory.Events;

/// <summary>
/// Grid Inventory's <see cref="EventKind"/> values (ADR-0008 section 3: kinds mirror the <see cref="OpKindBlock"/> ranges, inventory
/// <c>0x0080-0x00FF</c>; ADR-0009 section 4; story grid-inventory/005). Append-only, never renumbered or reused. Kept in the owning
/// module (as <see cref="Ops.InvOpKinds"/> keeps the op kinds) so Sim Core's enum never names a system's kinds. <c>InvCursor…</c>,
/// <c>InvReplace</c>, <c>WorldItem…</c>, <c>BacklogUnits</c> and <c>GroupCounts</c> are appended by the stories that raise them; the
/// cursor container uses these record kinds with its owner's audience in this story.
/// </summary>
/// <example><code>if (entry.Kind == InvEventKinds.Version) { var seal = entry.PayloadAs&lt;InvVersionPayload&gt;(); }</code></example>
public static class InvEventKinds
{
    /// <summary><c>InvInserted</c>: an entry appeared in a container (+ mailId runs in Continuation entries).</summary>
    public const EventKind Inserted = (EventKind)0x0080;

    /// <summary><c>InvRemoved</c>: an entry left a container (no mailIds).</summary>
    public const EventKind Removed = (EventKind)0x0081;

    /// <summary><c>InvMerged</c>: units added to a target stack (+ mailId runs in Continuation entries).</summary>
    public const EventKind Merged = (EventKind)0x0082;

    /// <summary><c>InvMoved</c>: an entry moved within its container.</summary>
    public const EventKind Moved = (EventKind)0x0083;

    /// <summary><c>InvSplitFrom</c>: units split off an entry into a new entry.</summary>
    public const EventKind SplitFrom = (EventKind)0x0084;

    /// <summary><c>InvVersion</c>: the per-container seal, once per touched container per call.</summary>
    public const EventKind Version = (EventKind)0x0085;
}

/// <summary>
/// The descriptors of <see cref="InvEventKinds"/> (ADR-0008 section 3), bound by <see cref="InventorySystem.Bind"/>. Every kind is
/// <see cref="ReplicationClass.Targeted"/>: the raiser resolves the audience at raise time from the container — a panel or cursor
/// container (one with an owner slot) gets exactly its owner's bit (owner-only delivery), an external container its subscriber set
/// (ADR-0009 section 4 rule 1). A class is declared once per kind and these kinds serve both, so the owner-only rule is carried by the
/// one-bit audience. Resync scope 1 (Inventory; ADR-0008 section 6 scope order Players 0, Inventory 1, …). Wire codec ids are 0 until
/// the network-adapter-loopback epic assigns them. Only <c>InvRemoved</c> is coalescible (ADR-0009 section 4 rule 7).
/// </summary>
/// <example><code>foreach (EventDescriptor d in InvEventDescriptors.All) { builder.BindEventKind(d); }</code></example>
public static class InvEventDescriptors
{
    /// <summary>The resync scope bit of every inventory kind.</summary>
    public const byte ResyncScope = 1;

    /// <summary><see cref="InvEventKinds.Inserted"/>: 48 B.</summary>
    public static EventDescriptor Inserted { get; } = Of<InvInsertedPayload>(InvEventKinds.Inserted, false);

    /// <summary><see cref="InvEventKinds.Removed"/>: 12 B, coalescible.</summary>
    public static EventDescriptor Removed { get; } = Of<InvRemovedPayload>(InvEventKinds.Removed, true);

    /// <summary><see cref="InvEventKinds.Merged"/>: 48 B.</summary>
    public static EventDescriptor Merged { get; } = Of<InvMergedPayload>(InvEventKinds.Merged, false);

    /// <summary><see cref="InvEventKinds.Moved"/>: 12 B.</summary>
    public static EventDescriptor Moved { get; } = Of<InvMovedPayload>(InvEventKinds.Moved, false);

    /// <summary><see cref="InvEventKinds.SplitFrom"/>: 16 B.</summary>
    public static EventDescriptor SplitFrom { get; } = Of<InvSplitFromPayload>(InvEventKinds.SplitFrom, false);

    /// <summary><see cref="InvEventKinds.Version"/>: 20 B.</summary>
    public static EventDescriptor Version { get; } = Of<InvVersionPayload>(InvEventKinds.Version, false);

    /// <summary>Every inventory event descriptor, in ascending kind order.</summary>
    public static ImmutableArray<EventDescriptor> All { get; } = ImmutableArray.Create(Inserted, Removed, Merged, Moved, SplitFrom, Version);

    private static EventDescriptor Of<T>(EventKind kind, bool coalescible)
        where T : unmanaged
        => EventDescriptor.For<T>(kind, ReplicationClass.Targeted, coalescible, ResyncScope, wireCodecId: 0, wireSize: 0);
}

/// <summary>
/// The cause of a change (ADR-0009 section 4: 3 bits in <see cref="Ops.InvReplyExtra.CauseAndFlags"/> and the seal's <c>cause</c>).
/// <see cref="None"/> is an ordinary actor op; the others name contested or automatic outcomes (first exercised by stories 013/014).
/// Values are append-only.
/// </summary>
/// <example><code>mutator.Begin(ref plan, actor, InvCause.None);</code></example>
public enum InvCause : byte
{
    /// <summary>An ordinary change by the actor.</summary>
    None = 0,

    /// <summary>Taken by another actor.</summary>
    Taken = 1,

    /// <summary>Re-laid out by a sort.</summary>
    Sorted = 2,

    /// <summary>Consumed (delivery, crafting).</summary>
    Consumed = 3,

    /// <summary>Expired (WorldItem, bag).</summary>
    Expired = 4,

    /// <summary>Destroyed with its container.</summary>
    Destroyed = 5,

    /// <summary>A System-actor change.</summary>
    System = 6,
}
