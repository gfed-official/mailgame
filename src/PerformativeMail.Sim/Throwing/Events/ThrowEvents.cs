using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Payload of <see cref="ThrowEventKinds.ThrowLaunched"/> (ADR-0015 section 9): 40 B, no padding (flightId 0, thrower 2, flags 3,
/// entryId 4, units 8, hop 9, kindDef 10, origin 12-23, vel 24-35, launchTick 36-39). Enough for any peer to evaluate F2 at any age.
/// <see cref="FlightId"/> is the raw u16 (wrap-safe), <see cref="Thrower"/> the raw <c>PlayerSlot</c>, <see cref="KindDef"/> the
/// entry's def ordinal. <see cref="Flags"/> bit 0 is <see cref="OriginClampedBit"/>; bits 1 to 7 must be zero (reserved for an amendment).
/// </summary>
/// <example><code>events.TryRaise(ThrowEventKinds.ThrowLaunched, connectedMask, in launched);</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ThrowLaunched
{
    /// <summary><see cref="Flags"/> bit 0: the host launched from the newest history sample, not the claimed feet.</summary>
    public const byte OriginClampedBit = 0x01;

    /// <summary>The flight id (raw u16).</summary>
    public ushort FlightId;

    /// <summary>The thrower's raw player slot.</summary>
    public byte Thrower;

    /// <summary>Bit 0 <see cref="OriginClampedBit"/>; bits 1 to 7 zero.</summary>
    public byte Flags;

    /// <summary>The thrown entry.</summary>
    public uint EntryId;

    /// <summary>Units in flight.</summary>
    public byte Units;

    /// <summary>The T13 hop count.</summary>
    public byte Hop;

    /// <summary>The entry's def ordinal.</summary>
    public ushort KindDef;

    /// <summary>Launch origin x, mm.</summary>
    public int OriginX;

    /// <summary>Launch origin y, mm.</summary>
    public int OriginY;

    /// <summary>Launch origin z, mm.</summary>
    public int OriginZ;

    /// <summary>Launch velocity x (F1 units).</summary>
    public int VelX;

    /// <summary>Launch velocity y.</summary>
    public int VelY;

    /// <summary>Launch velocity z.</summary>
    public int VelZ;

    /// <summary>The apply tick the flight launched on.</summary>
    public uint LaunchTick;
}

/// <summary>
/// Payload of <see cref="ThrowEventKinds.FlightResolved"/> (ADR-0015 section 9): 32 B, the ADR's 30 B of fields in order (flightId 0,
/// terminal 2, rank 3, receiver 4, contact 8-19, tick 20, unitsIn 24, unitsFumbled 25, worldItem 26-29) then <see cref="Pad"/> at 30-31.
/// <see cref="Receiver"/> and <see cref="WorldItem"/> are raw <c>EntityId</c> values.
/// </summary>
/// <example><code>events.TryRaise(ThrowEventKinds.FlightResolved, connectedMask, in resolved);</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FlightResolved
{
    /// <summary>The flight id (raw u16).</summary>
    public ushort FlightId;

    /// <summary>How the flight ended.</summary>
    public Terminal Terminal;

    /// <summary>The receiver's rank.</summary>
    public byte Rank;

    /// <summary>The receiver's raw entity id (0 for terrain, water and the map sides).</summary>
    public uint Receiver;

    /// <summary>Contact point x, mm.</summary>
    public int ContactX;

    /// <summary>Contact point y, mm.</summary>
    public int ContactY;

    /// <summary>Contact point z, mm.</summary>
    public int ContactZ;

    /// <summary>The tick the flight resolved on.</summary>
    public uint Tick;

    /// <summary>Units the receiver took in.</summary>
    public byte UnitsIn;

    /// <summary>Units fumbled.</summary>
    public byte UnitsFumbled;

    /// <summary>The raw entity id of the WorldItem the flight left (0 for none).</summary>
    public uint WorldItem;

    /// <summary>
    /// Explicit trailing padding, must be zero.
    /// Evan approved (TD-196 item 5), by 1:52 PM PT 2026-10-29: "Approve TD-196 items 4 and 5 for throwing-catching 006: ThrowRelease flags must be 0, and FlightResolved's padding goes at bytes 30-31".
    /// </summary>
    public ushort Pad;
}

/// <summary>How a flight ended (ADR-0015 section 5): exactly one per flight. Values are append-only; <see cref="Timeout"/> is the largest.</summary>
/// <example><code>if (resolved.Terminal == Terminal.Caught) { }</code></example>
public enum Terminal : byte
{
    /// <summary>A player caught it.</summary>
    Caught = 0,

    /// <summary>A player fumbled it.</summary>
    Fumble = 1,

    /// <summary>A mailbox accepted it.</summary>
    MailboxAccept = 2,

    /// <summary>The wrong mailbox took it.</summary>
    MailboxMisdeliver = 3,

    /// <summary>A mailbox refused it.</summary>
    MailboxReject = 4,

    /// <summary>It hit an enemy.</summary>
    HitEnemy = 5,

    /// <summary>It bonked a player.</summary>
    Bonk = 6,

    /// <summary>It landed on a belt.</summary>
    Belt = 7,

    /// <summary>It landed on the ground.</summary>
    Ground = 8,

    /// <summary>It landed in water.</summary>
    Water = 9,

    /// <summary>It ran out of time.</summary>
    Timeout = 10,
}

/// <summary>
/// The descriptors of <see cref="ThrowEventKinds"/> (ADR-0008 sections 1 and 3; ADR-0015 section 9): <see cref="ReplicationClass.Replicated"/>
/// (the raiser supplies the connected-slot mask), not coalescible. Static declarations only: <c>ThrowingSystem.Bind</c> binds them.
/// </summary>
/// <example><code>builder.BindEventKind(ThrowEventDescriptors.ThrowLaunched);</code></example>
public static class ThrowEventDescriptors
{
    /// <summary>
    /// The resync scope bit of every Throwing kind (ADR-0008 section 6 order: Players 0, Inventory 1, Mail 2, Throwing 3).
    /// Evan approved (TD-196 item 3), 2026-10-29 1:52 PM PT: "Approve all three TD-195 defaults for throwing-catching 008 and all three TD-196 defaults for throwing-catching 006".
    /// </summary>
    public const byte ResyncScope = 3;

    /// <summary>Codec id of the <c>ThrowLaunched</c> wire codec. Evan approved (TD-196 item 2), 2026-10-29 1:52 PM PT: "Approve all three TD-195 defaults for throwing-catching 008 and all three TD-196 defaults for throwing-catching 006".</summary>
    public const ushort ThrowLaunchedCodecId = 4;

    /// <summary>Codec id of the <c>FlightResolved</c> wire codec. Evan approved (TD-196 item 2), 2026-10-29 1:52 PM PT: "Approve all three TD-195 defaults for throwing-catching 008 and all three TD-196 defaults for throwing-catching 006".</summary>
    public const ushort FlightResolvedCodecId = 5;

    /// <summary><see cref="ThrowEventKinds.ThrowLaunched"/>: 40 B payload and wire size.</summary>
    public static EventDescriptor ThrowLaunched { get; } = EventDescriptor.For<ThrowLaunched>(
        ThrowEventKinds.ThrowLaunched, ReplicationClass.Replicated, coalescible: false, ResyncScope, ThrowLaunchedCodecId, wireSize: 40);

    /// <summary><see cref="ThrowEventKinds.FlightResolved"/>: 32 B payload and wire size.</summary>
    public static EventDescriptor FlightResolved { get; } = EventDescriptor.For<FlightResolved>(
        ThrowEventKinds.FlightResolved, ReplicationClass.Replicated, coalescible: false, ResyncScope, FlightResolvedCodecId, wireSize: 32);
}
