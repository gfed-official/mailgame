using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Inventory.Ops;

// Inventory op payloads (ADR-0009 section 1; story grid-inventory/004 pad rule): Pack = 1 sequential, padding-free, every payload a
// multiple of 4 B with explicit zeroed pad fields counted in PayloadLength (the PlayerJoinPayload convention). Read and written unaligned
// only, through Op.PayloadAs / Op.SetPayload. The actor is never a payload field (it is Op.Actor, stamped by the adapter); reqId is in
// the Op header; containers are containerId u32; dependsOn never travels. A non-zero pad byte is bad_request (InvRequestCheck).
// Claimed positions are three inline i32 fields in the Player Character P13 layout (feet x, y, z mm) rather than Players'
// ClaimedPosition type: SystemGraph has PlayerCharacter depend on GridInventory, so Inventory must not name a Players type.

/// <summary>Payload of <see cref="InvOpKinds.Pick"/> (8 B): pick an entry up onto the cursor.</summary>
/// <example><code>var p = new PickPayload { From = hotbar.Value, EntryId = entry.Value };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PickPayload
{
    /// <summary>Source container id.</summary>
    public uint From;

    /// <summary>The entry (real id, or a temp id of the actor's own slot).</summary>
    public uint EntryId;
}

/// <summary>Payload of <see cref="InvOpKinds.Move"/> (24 B: the ADR's 22 B, u32s first, + 2 pad).</summary>
/// <example><code>var p = new MovePayload { From = a, EntryId = e, To = b, BaseVersion = v, X = 2, Y = 0, Count = 0 };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MovePayload
{
    /// <summary>Source container id.</summary>
    public uint From;

    /// <summary>The entry to move.</summary>
    public uint EntryId;

    /// <summary>Destination container id.</summary>
    public uint To;

    /// <summary>The destination version the client placed against (<c>stale_layout</c> below its last replace).</summary>
    public uint BaseVersion;

    /// <summary>Target column.</summary>
    public short X;

    /// <summary>Target row.</summary>
    public short Y;

    /// <summary>0 or 1; 1 on a slots-layout destination (or any value above 1) is <c>bad_request</c>.</summary>
    public byte Rotated;

    /// <summary>Units to move; 0 = the whole entry.</summary>
    public byte Count;

    /// <summary>Explicit padding, must be zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, must be zero.</summary>
    public byte Pad1;
}

/// <summary>Payload of <see cref="InvOpKinds.Merge"/> (16 B): merge an entry onto a target stack.</summary>
/// <example><code>var p = new MergePayload { From = a, EntryId = e, To = b, TargetEntryId = t };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MergePayload
{
    /// <summary>Source container id.</summary>
    public uint From;

    /// <summary>The entry to merge.</summary>
    public uint EntryId;

    /// <summary>Destination container id.</summary>
    public uint To;

    /// <summary>The stack merged into.</summary>
    public uint TargetEntryId;
}

/// <summary>Payload of <see cref="InvOpKinds.QuickMove"/> (12 B: the ADR's 9 B of fields + 3 pad; the ADR's "(13)" is a sum slip — the listed fields total 9 B).</summary>
/// <example><code>var p = new QuickMovePayload { From = a, EntryId = e, Count = 0 };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct QuickMovePayload
{
    /// <summary>Source container id.</summary>
    public uint From;

    /// <summary>The entry to quick-move.</summary>
    public uint EntryId;

    /// <summary>Units to move; 0 = the whole entry.</summary>
    public byte Count;

    /// <summary>Explicit padding, must be zero.</summary>
    public byte Pad0;

    /// <summary>Explicit padding, must be zero.</summary>
    public byte Pad1;

    /// <summary>Explicit padding, must be zero.</summary>
    public byte Pad2;
}

/// <summary>Payload of <see cref="InvOpKinds.Drop"/> (4 B): drop an entry into the world.</summary>
/// <example><code>var p = new DropPayload { EntryId = e };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DropPayload
{
    /// <summary>The entry to drop.</summary>
    public uint EntryId;
}

/// <summary>Payload of <see cref="InvOpKinds.Pickup"/> (20 B): pick a world item up, with the claimed position.</summary>
/// <example><code>var p = new PickupPayload { WorldItemId = w, EntryId = 0, ClaimedX = 1000, ClaimedY = 0, ClaimedZ = -2500 };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PickupPayload
{
    /// <summary>The world item; 0 is <c>bad_request</c>.</summary>
    public uint WorldItemId;

    /// <summary>The entry to pick up; 0 = the default entry.</summary>
    public uint EntryId;

    /// <summary>Claimed feet x, mm (P13 layout).</summary>
    public int ClaimedX;

    /// <summary>Claimed feet y, mm.</summary>
    public int ClaimedY;

    /// <summary>Claimed feet z, mm.</summary>
    public int ClaimedZ;
}

/// <summary>Payload of <see cref="InvOpKinds.Open"/> (16 B): open a container, with the claimed position.</summary>
/// <example><code>var p = new OpenPayload { Container = c, ClaimedX = 0, ClaimedY = 0, ClaimedZ = 0 };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct OpenPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>Claimed feet x, mm (P13 layout).</summary>
    public int ClaimedX;

    /// <summary>Claimed feet y, mm.</summary>
    public int ClaimedY;

    /// <summary>Claimed feet z, mm.</summary>
    public int ClaimedZ;
}

/// <summary>Payload of <see cref="InvOpKinds.Close"/> (4 B): close a container.</summary>
/// <example><code>var p = new ClosePayload { Container = c };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ClosePayload
{
    /// <summary>The container id.</summary>
    public uint Container;
}
