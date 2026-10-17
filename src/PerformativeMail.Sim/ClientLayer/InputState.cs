namespace PerformativeMail.Sim.ClientLayer;

/// <summary>Per-frame flags of an <see cref="InputFrame"/> (ADR-0012 section 4). Values are explicit and append-only.</summary>
[Flags]
public enum InputFrameFlags : byte
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>Set by the latch: this frame is the first tick of its batch (the only one that carries edges and steps).</summary>
    FirstInBatch = 1,

    /// <summary>Set by the router: the window has lost focus. Transported only.</summary>
    FocusLost = 2,

    /// <summary>Set by the router: the game is paused. Transported only.</summary>
    Paused = 4,

    /// <summary>Set by the router: the look was re-seeded this frame (teleport, mount, dismount). Transported only.</summary>
    LookReseeded = 8,
}

/// <summary>
/// The router's current continuous input state, pushed through <see cref="IInputLatchWriter.SetState"/> once per
/// flush (ADR-0012 section 4). The latest value before a tick wins. Integer fields only.
/// </summary>
/// <example>
/// <code>
/// writer.SetState(new InputState { MoveY = 32767, LookYaw = 0x4000, HeldBits = FrameActions.Bit(FrameAction.Sprint) });
/// </code>
/// </example>
public readonly struct InputState
{
    /// <summary>Move intent X, Q15 on the unit disc after the deadzone (x right).</summary>
    public short MoveX { get; init; }

    /// <summary>Move intent Y, Q15 on the unit disc after the deadzone (y forward).</summary>
    public short MoveY { get; init; }

    /// <summary>Absolute look yaw in BAM (65 536 = 360 degrees); never a delta.</summary>
    public ushort LookYaw { get; init; }

    /// <summary>Absolute look pitch in signed BAM.</summary>
    public short LookPitch { get; init; }

    /// <summary>Logical held state; bit <c>n</c> is <c>(FrameAction)n</c>.</summary>
    public ulong HeldBits { get; init; }

    /// <summary>Focus cone threshold in permille of cosine (from the camera FOV and aspect).</summary>
    public short FocusCosMinPm { get; init; }

    /// <summary>The router state id (<see cref="RouterStateInfo"/>); informational.</summary>
    public ushort RouterStateId { get; init; }

    /// <summary>Router-owned flags (<c>FocusLost</c>, <c>Paused</c>, <c>LookReseeded</c>). The latch ignores a <c>FirstInBatch</c> bit set here.</summary>
    public InputFrameFlags Flags { get; init; }
}
