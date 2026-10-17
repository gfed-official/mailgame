namespace PerformativeMail.Sim.Players;

/// <summary>The carrier's locomotion mode (ADR-0013 section 2 <c>Loco</c>). Explicit, append-only values (dumped as a byte).</summary>
/// <example><code>CarrierState s = default;   // s.Loco == LocoState.Grounded</code></example>
public enum LocoState : byte
{
    /// <summary>Standing or walking on terrain, a box top or a support.</summary>
    Grounded = 0,

    /// <summary>In the air (jump or fall).</summary>
    Airborne = 1,

    /// <summary>Swimming (water depth at or beyond the enter depth, PC P6).</summary>
    Swimming = 2,

    /// <summary>Seated in a vehicle or turret (M1).</summary>
    Mounted = 3,
}

/// <summary>Bit flags of <see cref="CarrierState.Flags"/> (ADR-0013 section 2). Explicit, append-only bits (dumped as a ushort).</summary>
/// <example><code>bool sprinting = (s.Flags &amp; CarrierFlags.Sprinting) != 0;</code></example>
[Flags]
public enum CarrierFlags : ushort
{
    /// <summary>No flag set.</summary>
    None = 0,

    /// <summary>Sprint gait active.</summary>
    Sprinting = 1 << 0,

    /// <summary>Standing on a support (belt tile or vehicle).</summary>
    OnSupport = 1 << 1,

    /// <summary>The support is a vehicle (so the support fields are vehicle-local).</summary>
    SupportIsVehicle = 1 << 2,

    /// <summary>The mid-air jump was used in this airborne period.</summary>
    ExtraJumpUsed = 1 << 3,

    /// <summary>Wading through shallow water.</summary>
    Wading = 1 << 4,
}
