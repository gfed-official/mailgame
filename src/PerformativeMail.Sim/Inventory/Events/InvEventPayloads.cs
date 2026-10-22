using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Inventory.Events;

// Inventory event payloads (ADR-0009 section 4; ADR-0008 section 1; story grid-inventory/005): Pack = 1 sequential, padding-free
// (explicit zeroed pad/reserved fields), string-free, no Int128, at most 48 B, read and written unaligned only through
// EventLog.TryRaise / EventEntry.PayloadAs. Every container payload starts with the container id (u32 at offset 0). Records and seals
// carry no reqId (the adapter tags the actor's own entries, ADR-0008 section 7 (d)). InvInserted and InvMerged declare 48 B so that
// their Continuation chunks (8 mailId runs = 48 B) stay within the descriptor length, as ADR-0008 section 1 requires of a chunk.

/// <summary>
/// <see cref="InvEventKinds.Inserted"/> (48 B): an entry appeared in a container (ADR-0009 section 4 <c>InvInserted{c, entryId, x, y,
/// rotated, count, key}</c>). The key is the mail key (<see cref="MailKind"/>, <see cref="AddressId"/>, <see cref="Flags"/>) or the item
/// key (<see cref="ItemId"/>), selected by <see cref="IsMail"/>. When <see cref="RunCount"/> is above 0 the entry is followed by
/// <c>ceil(RunCount / 8)</c> <c>Continuation</c> entries of the same kind and <c>CallSeq</c> holding the mailIds as
/// <see cref="InvMailIdRuns"/>.
/// </summary>
/// <example><code>var p = e.PayloadAs&lt;InvInsertedPayload&gt;(); int chunks = (p.RunCount + 7) / 8;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InvInsertedPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>The entry id.</summary>
    public uint EntryId;

    /// <summary>Mail key: the packed address; 0 for an item.</summary>
    public uint AddressId;

    /// <summary>Intake arrival sequence (0 outside the Intake).</summary>
    public uint ArrivalSeq;

    /// <summary>Mail key: the mail kind; 0 for an item.</summary>
    public ushort MailKind;

    /// <summary>Item key: the item def ordinal; 0 for mail.</summary>
    public ushort ItemId;

    /// <summary>The number of mailId runs that follow in <c>Continuation</c> entries (0 for an item).</summary>
    public ushort RunCount;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad0;

    /// <summary>Column of the top-left cell.</summary>
    public byte X;

    /// <summary>Row of the top-left cell.</summary>
    public byte Y;

    /// <summary>Unrotated footprint width.</summary>
    public byte W;

    /// <summary>Unrotated footprint height.</summary>
    public byte H;

    /// <summary>1 when placed rotated.</summary>
    public byte Rotated;

    /// <summary>The stack count.</summary>
    public byte Count;

    /// <summary>Mail key: the mail flags; 0 for an item.</summary>
    public byte Flags;

    /// <summary>1 when the mail key is live, 0 for an item.</summary>
    public byte IsMail;

    /// <summary>Reserved (zero): keeps the declared length at 48 B so Continuation chunks fit (see the file comment).</summary>
    public InvReserved16 Reserved;
}

/// <summary><see cref="InvEventKinds.Removed"/> (12 B): an entry left a container. No mailIds: the viewer already holds them (ADR-0009 section 4).</summary>
/// <example><code>var p = e.PayloadAs&lt;InvRemovedPayload&gt;();</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InvRemovedPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>The entry id.</summary>
    public uint EntryId;

    /// <summary>Units removed.</summary>
    public byte Count;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad1;
}

/// <summary>
/// <see cref="InvEventKinds.Merged"/> (48 B): units were added to a target stack (ADR-0009 section 4 <c>InvMerged{c, targetEntryId,
/// addedCount}</c>), followed by the added mailIds as runs in <c>Continuation</c> entries. Declared here; raised by the merge stories.
/// </summary>
/// <example><code>var p = e.PayloadAs&lt;InvMergedPayload&gt;();</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InvMergedPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>The target stack's entry id.</summary>
    public uint TargetEntryId;

    /// <summary>Units added.</summary>
    public byte AddedCount;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;

    /// <summary>The number of mailId runs that follow in <c>Continuation</c> entries.</summary>
    public ushort RunCount;

    /// <summary>Reserved (zero): keeps the declared length at 48 B so Continuation chunks fit.</summary>
    public InvReserved36 Reserved;
}

/// <summary><see cref="InvEventKinds.Moved"/> (12 B): an entry moved within its container.</summary>
/// <example><code>var p = e.PayloadAs&lt;InvMovedPayload&gt;();</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InvMovedPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>The entry id.</summary>
    public uint EntryId;

    /// <summary>New column of the top-left cell.</summary>
    public byte X;

    /// <summary>New row of the top-left cell.</summary>
    public byte Y;

    /// <summary>1 when now placed rotated.</summary>
    public byte Rotated;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;
}

/// <summary><see cref="InvEventKinds.SplitFrom"/> (16 B): <see cref="Count"/> units were split off <see cref="EntryId"/> into <see cref="NewEntryId"/>. Declared here; raised by the split story.</summary>
/// <example><code>var p = e.PayloadAs&lt;InvSplitFromPayload&gt;();</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InvSplitFromPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>The source entry id.</summary>
    public uint EntryId;

    /// <summary>The new entry's id.</summary>
    public uint NewEntryId;

    /// <summary>Units split off.</summary>
    public byte Count;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad1;
}

/// <summary>
/// <see cref="InvEventKinds.Version"/> (20 B): the seal (ADR-0009 section 4 <c>InvVersion{c, version_from, version_to, actorId,
/// cause}</c>), raised once per touched container per call after that container's records; the client applies the staged records
/// when it arrives.
/// </summary>
/// <example><code>var s = e.PayloadAs&lt;InvVersionPayload&gt;(); bool gapFree = s.VersionFrom == mirrorVersion;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InvVersionPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>The version before the call.</summary>
    public uint VersionFrom;

    /// <summary>The version after the call (<see cref="VersionFrom"/> + 1).</summary>
    public uint VersionTo;

    /// <summary>The actor credited with the change (raw <c>ActorId</c>).</summary>
    public uint ActorId;

    /// <summary>The cause (<see cref="InvCause"/>).</summary>
    public byte Cause;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad1;
}

/// <summary>One ascending mailId run (6 B, Pack = 1): <see cref="Length"/> consecutive ids starting at <see cref="Start"/>.</summary>
/// <example><code>var run = new InvMailIdRun { Start = 100, Length = 20 };   // ids 100..119</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InvMailIdRun
{
    /// <summary>The first id of the run.</summary>
    public uint Start;

    /// <summary>The number of consecutive ids (at least 1).</summary>
    public ushort Length;
}

/// <summary>
/// The payload of an <c>InvInserted</c> / <c>InvMerged</c> <c>Continuation</c> chunk: up to <see cref="Capacity"/> runs (48 B); the
/// entry's <c>PayloadLength</c> is <c>6 * runs</c>, unused runs are zero.
/// </summary>
/// <example><code>var runs = e.PayloadAs&lt;InvMailIdRuns&gt;(); InvMailIdRun first = runs[0];</code></example>
[InlineArray(Capacity)]
public struct InvMailIdRuns
{
    /// <summary>Runs per 48 B chunk.</summary>
    public const int Capacity = 8;

    /// <summary>Bytes per run.</summary>
    public const int RunBytes = 6;

#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private InvMailIdRun _e0;
#pragma warning restore CS0169
}

/// <summary>16 reserved (zero) bytes.</summary>
[InlineArray(4)]
public struct InvReserved16
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private uint _e0;
#pragma warning restore CS0169
}

/// <summary>36 reserved (zero) bytes.</summary>
[InlineArray(9)]
public struct InvReserved36
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private uint _e0;
#pragma warning restore CS0169
}

// World payloads (story grid-inventory/013; GDD Network table "WorldItems and bags"): Pack = 1, padding-free, at most 48 B. They start
// with the WorldItem id (not a container id) and every WorldItem record carries the global worldSeq (u32) it was raised with, so a
// client detects a gap and resyncs. ItemsLost carries no worldSeq (it is not a WorldItem state change).

/// <summary>
/// <see cref="InvEventKinds.WorldItemSpawned"/> (44 B): a WorldItem appeared with its one entry (GDD R12). Position in mm (the GDD's
/// cm is flagged to its re-review). The key is the mail key (<see cref="MailKind"/>, <see cref="AddressId"/>, <see cref="MailFlags"/>)
/// or the item key (<see cref="ItemId"/>), selected by bit 0 of <see cref="Bits"/>; bit 1 is the water flag.
/// </summary>
/// <example><code>var p = e.PayloadAs&lt;WorldItemSpawnedPayload&gt;(); bool floats = (p.Bits &amp; WorldItemSpawnedPayload.OnWaterBit) != 0;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WorldItemSpawnedPayload
{
    /// <summary><see cref="Bits"/>: the entry is mail.</summary>
    public const byte IsMailBit = 1;

    /// <summary><see cref="Bits"/>: the item landed on water (it floats, no pickup prompt).</summary>
    public const byte OnWaterBit = 2;

    /// <summary>The WorldItem id (raw <c>EntityId</c>, class <c>WorldItem</c>).</summary>
    public uint WorldItemId;

    /// <summary>The global <c>worldSeq</c> of this record.</summary>
    public uint WorldSeq;

    /// <summary>The entry's id.</summary>
    public uint EntryId;

    /// <summary>Mail key: the packed address; 0 for an item.</summary>
    public uint AddressId;

    /// <summary>X, mm.</summary>
    public int X;

    /// <summary>Y (up), mm.</summary>
    public int Y;

    /// <summary>Z, mm.</summary>
    public int Z;

    /// <summary>The creation (landing) tick.</summary>
    public uint CreatedTick;

    /// <summary>The expiry tick (0 for Loot: never).</summary>
    public uint ExpiresTick;

    /// <summary>Mail key: the mail kind; 0 for an item.</summary>
    public ushort MailKind;

    /// <summary>Item key: the item def ordinal; 0 for mail.</summary>
    public ushort ItemId;

    /// <summary>The entry's count.</summary>
    public byte Count;

    /// <summary>The origin (<c>WorldItemOrigin</c>).</summary>
    public byte Origin;

    /// <summary>Mail key: the mail flags; 0 for an item.</summary>
    public byte MailFlags;

    /// <summary><see cref="IsMailBit"/> | <see cref="OnWaterBit"/>.</summary>
    public byte Bits;
}

/// <summary>
/// <see cref="InvEventKinds.WorldItemDespawned"/> (16 B): a WorldItem left the world. <see cref="ByActorId"/> and <see cref="Cause"/>
/// (<see cref="InvCause.Taken"/> picked up, <see cref="InvCause.Expired"/> mail returned to the Intake, <see cref="InvCause.Destroyed"/>
/// non-mail destroyed) are what its tombstone answers later ops with.
/// </summary>
/// <example><code>var p = e.PayloadAs&lt;WorldItemDespawnedPayload&gt;();</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WorldItemDespawnedPayload
{
    /// <summary>The WorldItem id.</summary>
    public uint WorldItemId;

    /// <summary>The global <c>worldSeq</c> of this record.</summary>
    public uint WorldSeq;

    /// <summary>The actor credited (raw <c>ActorId</c>; <c>System</c> for expiry and eviction).</summary>
    public uint ByActorId;

    /// <summary>The cause (<see cref="InvCause"/>).</summary>
    public byte Cause;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad1;
}

/// <summary><see cref="InvEventKinds.WorldItemEntriesChanged"/> (16 B): the WorldItem's entry now holds <see cref="Count"/> units (raised by story 014's partial pickup).</summary>
/// <example><code>var p = e.PayloadAs&lt;WorldItemEntriesChangedPayload&gt;();</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WorldItemEntriesChangedPayload
{
    /// <summary>The WorldItem id.</summary>
    public uint WorldItemId;

    /// <summary>The global <c>worldSeq</c> of this record.</summary>
    public uint WorldSeq;

    /// <summary>The entry's id.</summary>
    public uint EntryId;

    /// <summary>The entry's count now.</summary>
    public byte Count;

    /// <summary>Explicit padding, zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad1;
}

/// <summary><see cref="InvEventKinds.ItemsLost"/> (8 B): <see cref="Count"/> units of item <see cref="ItemId"/> were destroyed (GDD R12 <c>ItemsLost {itemDef, count}</c>), from WorldItem <see cref="WorldItemId"/>.</summary>
/// <example><code>var p = e.PayloadAs&lt;ItemsLostPayload&gt;();   // {plank, 5}</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ItemsLostPayload
{
    /// <summary>The WorldItem the units were in.</summary>
    public uint WorldItemId;

    /// <summary>The item def ordinal.</summary>
    public ushort ItemId;

    /// <summary>Units destroyed.</summary>
    public byte Count;

    /// <summary>The cause (<see cref="InvCause.Destroyed"/>).</summary>
    public byte Cause;
}
