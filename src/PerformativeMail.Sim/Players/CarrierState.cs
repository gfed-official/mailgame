using System.Runtime.CompilerServices;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Per remote slot push-pair state (ADR-0013 section 2 <c>Pair[7]</c>, PC F6): overlap tick count, the window's start position
/// and whether the pair is ghosted. Declared at M-1a for a stable dump layout; the solver leaves it zero until the push stories.
/// Blittable; 12 B (fields ordered ushort, byte, int, int so there is one byte of padding).
/// </summary>
/// <example><code>state.Pair[2].OverlapTicks = 3;</code></example>
public struct PushPairState
{
    /// <summary>Ticks the pair has overlapped in the current window.</summary>
    public ushort OverlapTicks;

    /// <summary>1 when the pair passes through each other (ghosted), else 0.</summary>
    public byte Ghosted;

    /// <summary>Window start X, mm.</summary>
    public int WindowStartX;

    /// <summary>Window start Z, mm.</summary>
    public int WindowStartZ;
}

/// <summary>The seven <see cref="PushPairState"/>s of a carrier (one per other slot), as an inline array (no heap allocation).</summary>
/// <example><code>ref PushPairState p = ref state.Pair[0];</code></example>
[InlineArray(Length)]
public struct PushPairArray
{
    /// <summary>Number of pair slots: one per other player slot (8 - 1).</summary>
    public const int Length = 7;

    private PushPairState _element0;
}

/// <summary>
/// The pure solver's per-carrier state (ADR-0013 section 2, TR-movement-001: the type only; the step is a later story). Feet
/// position in mm with tick-rational remainders (1/30 mm horizontal, 1/60 mm vertical), own velocity, BAM body yaw, locomotion
/// mode, flags, jump counters, support link and push-pair state. Integer only (no floats), blittable, exactly 148 B (13 ints = 52 B, seven 12 B pairs = 84 B, four ushorts = 8 B, four bytes = 4 B, no padding; story 009 added <see cref="WadeSlowPm"/> into the former padding). Players'
/// state contributor dumps it field by field in slot order, never as a blit (ADR-0006 section 4). Aim (camera yaw and pitch) is
/// not part of it: it lives in <see cref="PlayerRecord"/> next to the carrier.
/// </summary>
/// <example><code>var s = new CarrierState { X = 1000, Z = -1200, BodyYaw = 0, Loco = LocoState.Grounded };</code></example>
public struct CarrierState
{
    // Field order is chosen to minimise padding (ints, then the pair array, then ushorts, then bytes); the dump order is
    // explicit in PlayersStateContributor and does not depend on it.

    /// <summary>Feet X, mm.</summary>
    public int X;

    /// <summary>Feet Y, mm.</summary>
    public int Y;

    /// <summary>Feet Z, mm.</summary>
    public int Z;

    /// <summary>Horizontal X remainder in 1/30 mm (-29 to 29).</summary>
    public int RemX;

    /// <summary>Horizontal Z remainder in 1/30 mm (-29 to 29).</summary>
    public int RemZ;

    /// <summary>Vertical remainder in 1/60 mm (trapezoid, F3).</summary>
    public int RemY;

    /// <summary>Own horizontal velocity X, mm/s (support carry excluded).</summary>
    public int Vx;

    /// <summary>Own horizontal velocity Z, mm/s (support carry excluded).</summary>
    public int Vz;

    /// <summary>Vertical velocity, mm/s.</summary>
    public int Vy;

    /// <summary>The support's id: belt tile or vehicle <c>EntityId</c> (0 when none).</summary>
    public uint SupportId;

    /// <summary>Position in the vehicle's local frame, X, mm (P7).</summary>
    public int SupportLocalX;

    /// <summary>Position in the vehicle's local frame, Y, mm (P7).</summary>
    public int SupportLocalY;

    /// <summary>Position in the vehicle's local frame, Z, mm (P7).</summary>
    public int SupportLocalZ;

    /// <summary>Push-pair state against each other slot (P8, F6).</summary>
    public PushPairArray Pair;

    /// <summary>Body facing, BAM (65536 = full turn; separate from the camera's aim).</summary>
    public ushort BodyYaw;

    /// <summary>The vehicle's reference yaw when the link was made, BAM (P7).</summary>
    public ushort SupportYawRef;

    /// <summary>State flags.</summary>
    public CarrierFlags Flags;

    /// <summary>
    /// The wade slow from the previous tick's phase 8, permille (0 dry or swimming, up to <c>maxWadeSlowPm</c>): phase 2 uses
    /// <c>wadePm = 1000 - WadeSlowPm</c> (story 009, F7). Stored as the slow, not the multiplier, so a default state is dry.
    /// </summary>
    public ushort WadeSlowPm;

    /// <summary>Locomotion mode.</summary>
    public LocoState Loco;

    /// <summary>Ticks of coyote time left (F3).</summary>
    public byte CoyoteLeft;

    /// <summary>Ticks of jump buffer left (F3).</summary>
    public byte JumpBufferLeft;

    /// <summary>Haul-out progress in ticks (P6).</summary>
    public byte HaulOutTicks;
}
