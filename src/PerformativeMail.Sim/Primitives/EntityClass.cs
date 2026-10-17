namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// The class byte (bits 24-31) of an <see cref="EntityId"/>. Append-only: adding a class
/// amends ADR-0004 and GDD R19. Mail is deliberately not a class (see <see cref="MailId"/>).
/// 0x09-0xEF are reserved and never allocated; 0xF0-0xFF are test-only.
/// </summary>
public enum EntityClass : byte
{
    /// <summary>No class; only ever appears in <see cref="EntityId.None"/> and <see cref="ActorId.System"/>.</summary>
    None = 0x00,

    /// <summary>A player avatar.</summary>
    Player = 0x01,

    /// <summary>An item lying in the world.</summary>
    WorldItem = 0x02,

    /// <summary>A placed construct.</summary>
    Construct = 0x03,

    /// <summary>A vehicle.</summary>
    Vehicle = 0x04,

    /// <summary>An enemy.</summary>
    Enemy = 0x05,

    /// <summary>A non-player character.</summary>
    Npc = 0x06,

    /// <summary>A delivery destination.</summary>
    Destination = 0x07,

    /// <summary>A static prop.</summary>
    Prop = 0x08,

    /// <summary>First class reserved for tests.</summary>
    TestFirst = 0xF0,

    /// <summary>Last class reserved for tests.</summary>
    TestLast = 0xFF,
}
