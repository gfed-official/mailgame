using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Everything Players holds for one player slot (PC P1, ADR-0013 section 2, ADR-0014 section 6): the <c>player</c>
/// <see cref="EntityId"/>, the host-owned life state with its absolute-tick deadlines, the solver's <see cref="CarrierState"/>
/// and the camera aim. Body yaw (<see cref="CarrierState.BodyYaw"/>) and aim (<see cref="LookYaw"/>, <see cref="LookPitch"/>) are
/// separate fields: writing one never changes the other. The capsule size is copied from <see cref="PlayerSettings"/> at join and
/// is derivable, so it is not dumped. An empty slot is <c>default</c> (no <see cref="PlayerId"/>). Nothing decrements a deadline
/// per tick; later phase-3 timers compare them with the current tick.
/// </summary>
/// <example><code>ref PlayerRecord r = ref players.RecordRef(PlayerSlot.FromValue(0)); int radius = r.CapsuleRadiusMm;</code></example>
public struct PlayerRecord
{
    /// <summary>The player's id (class <c>player</c>), as assigned in the slot table; none for an empty slot.</summary>
    public EntityId PlayerId;

    /// <summary>The life state.</summary>
    public LifeState Life;

    /// <summary>Absolute tick at which a knocked-down player clocks back in; at M-1a the tick the player joined.</summary>
    public uint ClockInTick;

    /// <summary>Absolute tick at which an on-break body despawns (0 when not frozen).</summary>
    public uint FrozenUntilTick;

    /// <summary>Absolute tick until which the player is protected (0 when not protected).</summary>
    public uint ProtectedUntilTick;

    /// <summary>Teleport sequence number (wrap-safe u8; bumped by teleports from M0).</summary>
    public byte TeleportSeq;

    /// <summary>The solver state.</summary>
    public CarrierState Carrier;

    /// <summary>Camera yaw, BAM.</summary>
    public ushort LookYaw;

    /// <summary>Camera pitch, signed BAM (positive looks up).</summary>
    public short LookPitch;

    /// <summary>Capsule radius, mm, from <see cref="PlayerSettings.CapsuleRadiusMm"/> at join.</summary>
    public int CapsuleRadiusMm;

    /// <summary>Capsule height, mm, from <see cref="PlayerSettings.CapsuleHeightMm"/> at join.</summary>
    public int CapsuleHeightMm;

    /// <summary>True when no player occupies the slot.</summary>
    public readonly bool IsEmpty => PlayerId.IsNone;
}
