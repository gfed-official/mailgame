namespace PerformativeMail.Sim.Players;

/// <summary>Per-tick context bits that change how the carrier moves (ADR-0013 section 1).</summary>
[Flags]
public enum CarrierContextFlags : byte
{
    /// <summary>No context.</summary>
    None = 0,

    /// <summary>A UI panel is open: sprint is ignored and the carrier walks.</summary>
    Panel = 1,

    /// <summary>The carrier is dead (or otherwise not in control): sprint is ignored.</summary>
    Dead = 2,

    /// <summary>The carrier is mounted on a vehicle.</summary>
    Mounted = 4,

    /// <summary>Build mode is active.</summary>
    Build = 8,
}

/// <summary>
/// One tick of input to <c>OwnedCarrier.Step</c> (ADR-0013 section 1): the ADR-0012 <c>InputFrame</c> translated to carrier terms. Unmanaged,
/// integer-only. A <c>default</c> value is "no input, speed 0"; <see cref="Idle"/> is "no input at full speed" (1000 permille).
/// </summary>
/// <example><code>var i = new CarrierInput { MoveY = 32767, SpeedPermille = 1000, LookYaw = 0 };</code></example>
public readonly struct CarrierInput
{
    /// <summary>Move X, Q15 on the unit disc (32 767 = 1.0; values beyond are clamped by the solver).</summary>
    public int MoveX { get; init; }

    /// <summary>Move Y (forward), Q15 on the unit disc (32 767 = 1.0).</summary>
    public int MoveY { get; init; }

    /// <summary>Look yaw, BAM.</summary>
    public ushort LookYaw { get; init; }

    /// <summary>Sprint is held.</summary>
    public bool SprintHeld { get; init; }

    /// <summary>Jump pressed; an edge on this tick only.</summary>
    public bool JumpPressed { get; init; }

    /// <summary>Aiming: body yaw is locked to <see cref="LookYaw"/> that tick. It does not force walk.</summary>
    public bool Aiming { get; init; }

    /// <summary>Context bits (Panel forces walk).</summary>
    public CarrierContextFlags ContextFlags { get; init; }

    /// <summary>True when the host authors this body (Dead or Frozen): zero input, depenetration and ground drop only.</summary>
    public bool HostAuthored { get; init; }

    /// <summary>Speed multiplier in permille (Grid D1; 1000 when mounted).</summary>
    public int SpeedPermille { get; init; }

    /// <summary>Additive buff to speed, permille.</summary>
    public int BuffAddPm { get; init; }

    /// <summary>Extra jumps available beyond the first.</summary>
    public int ExtraJumps { get; init; }

    /// <summary>No movement input at full speed (1000 permille).</summary>
    public static CarrierInput Idle => new() { SpeedPermille = 1000 };
}
