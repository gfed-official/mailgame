using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The launch-time snapshot <see cref="FlightTable.TryAppend"/> stores as one <c>FlightRecord</c> (ADR-0015 section 9; GDD
/// <c>design/gdd/throwing-catching.md</c> T4 "The host creates the <c>FlightRecord</c>"; TR-throwing-009): every field but the
/// <see cref="FlightId"/>, which the table assigns. The entry reference is the <see cref="EntryId"/> and <see cref="Units"/> of
/// <c>IInventoryApi.DetachForThrow</c>'s <c>DetachResult</c>: the mailIds stay in Inventory's arena under that live detached entry,
/// so the record holds them by reference and no Throwing-side unit list exists (Mail Lead planning default, pending Evan (TD-195 item 2)).
/// Immutable.
/// </summary>
/// <example>
/// <code>
/// var init = new FlightInit { Entry = detached.Entry, Units = detached.Units, Thrower = slot, Origin = l.Origin, Vel = l.Vel, LaunchTick = tick };
/// bool stored = flights.TryAppend(in init, out int index);
/// </code>
/// </example>
internal readonly struct FlightInit
{
    /// <summary>The live detached entry the flight carries (<c>DetachResult.Entry</c>).</summary>
    public EntryId Entry { get; init; }

    /// <summary>The entry's units (<c>DetachResult.Units</c>).</summary>
    public byte Units { get; init; }

    /// <summary>The entry's kind or item def, the <c>ThrowLaunched</c> <c>kindDef</c> field.</summary>
    public ushort KindDef { get; init; }

    /// <summary>The thrower's slot.</summary>
    public PlayerSlot Thrower { get; init; }

    /// <summary>
    /// <c>classPts</c> of the entry's class (F6 damage reads it). <see cref="ClassPts"/> and <see cref="ThrownPm"/> are i32, the width of
    /// <c>Weight.ClassPoints</c> and of a stat value, in the record and the dump: Mail Dev default, pending Evan (TD-197 item 1).
    /// </summary>
    public int ClassPts { get; init; }

    /// <summary>The F1 launch origin, mm.</summary>
    public Vec3Mm Origin { get; init; }

    /// <summary>The F1 launch velocity, mm/s.</summary>
    public Vec3Mm Vel { get; init; }

    /// <summary>The launch tick (flight age 0).</summary>
    public uint LaunchTick { get; init; }

    /// <summary>The thrower's <c>ThrownCorrectPayMultPm</c>, snapshotted at launch (Mail R13).</summary>
    public int ThrownPm { get; init; }

    /// <summary>The <c>ThrowRelease</c> apply tick (Mail R4 closing grace and F7 timeliness).</summary>
    public uint ReleaseTick { get; init; }

    /// <summary>Whether deliveries were OPEN at <see cref="ReleaseTick"/>.</summary>
    public bool OpenAtRelease { get; init; }

    /// <summary>The T13 relay hop count (saturates at 255).</summary>
    public byte Hop { get; init; }

    /// <summary>Whether the claimed origin was replaced by the newest history sample (validation row 9).</summary>
    public bool OriginClamped { get; init; }
}
