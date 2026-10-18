using System.Runtime.InteropServices;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The read-only inputs an <see cref="IInteractable"/> hook sees for one call (ADR-0014 section 1). DECLARED by story 012;
/// stories 013 to 018 populate it. Always passed by <c>in</c>; it holds values only (no object reference, no bool), so hooks
/// cannot reach mutable state through it.
/// </summary>
/// <example>
/// <code>
/// var c = new InteractionContext { Tick = 10, Anchor = anchor, Verb = Verb.Deliver, HoldBaseMs = 400, HoldSettingMs = 400 };
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential)]
public readonly struct InteractionContext
{
    /// <summary>The Sim tick.</summary>
    public uint Tick { get; init; }

    /// <summary>The acting player's slot.</summary>
    public PlayerSlot Actor { get; init; }

    /// <summary>The acting player's entity.</summary>
    public EntityId ActorEntity { get; init; }

    /// <summary>The actor's chest point, mm.</summary>
    public Vec3Mm ChestMm { get; init; }

    /// <summary>The anchor in question.</summary>
    public AnchorRef Anchor { get; init; }

    /// <summary>The anchor's verb.</summary>
    public Verb Verb { get; init; }

    /// <summary>The anchor's mode.</summary>
    public InteractMode Mode { get; init; }

    /// <summary>The anchor's base hold length, ms.</summary>
    public int HoldBaseMs { get; init; }

    /// <summary>The anchor's reach, mm.</summary>
    public int RangeMm { get; init; }

    /// <summary>The player's hold setting, ms.</summary>
    public int HoldSettingMs { get; init; }

    /// <summary>The shared hold constants.</summary>
    public HoldSettings HoldSettings { get; init; }

    /// <summary>The input context bit mask (1 World, 2 Build).</summary>
    public byte ContextMask { get; init; }

    /// <summary>1 when the actor is the host's own player, else 0 (a byte, not a bool).</summary>
    public byte IsHost { get; init; }
}
