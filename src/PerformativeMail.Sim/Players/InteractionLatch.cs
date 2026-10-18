using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// What a press latches (ADR-0014 section 3): the anchor, its verb and mode, the press tick, the hold length fixed at press, the
/// feet position at press and 16 bytes for the owner. DECLARED by story 012; populated by the press and hold stories (016 to 018).
/// Explicit sequential layout, no bool, no reference.
/// </summary>
/// <example>
/// <code>
/// var latch = new InteractionLatch { Anchor = anchor, Verb = Verb.Deliver, Mode = InteractMode.Hold, PressTick = 100, HoldTicks = 12 };
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential)]
public struct InteractionLatch
{
    /// <summary>The anchor pressed.</summary>
    public AnchorRef Anchor;

    /// <summary>Its verb.</summary>
    public Verb Verb;

    /// <summary>Its mode.</summary>
    public InteractMode Mode;

    /// <summary>The tick of the press edge.</summary>
    public uint PressTick;

    /// <summary>The hold length in ticks, latched at press (from <see cref="IInteractable.GetHoldTicks"/>).</summary>
    public int HoldTicks;

    /// <summary>The carrier's feet at press.</summary>
    public ClaimedPosition Claimed;

    /// <summary>16 opaque bytes for the owner (<see cref="IInteractable.OnPress"/>).</summary>
    public OwnerPayload16 OwnerPayload;
}
