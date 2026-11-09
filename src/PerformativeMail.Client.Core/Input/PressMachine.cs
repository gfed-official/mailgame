using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The phase of one <see cref="PressMachine"/> (ADR-0012 section 3; GDD "Per-press state machine"). <c>Pressed</c> is not a
/// stored phase: the press edge resolves and latches in one step and goes straight to the phase its grammar names.
/// </summary>
internal enum PressPhase : byte
{
    /// <summary>The input is up; nothing is latched.</summary>
    Idle = 0,

    /// <summary>A tap (or a verb a later story owns) fired on the press; the release does nothing.</summary>
    Fired = 1,

    /// <summary>A Hold action is down; its release emits a Release edge (the Sim times the hold, ADR-0014).</summary>
    HoldActive = 2,

    /// <summary>An Aim-release action is down: its held bit is set and its release emits a Release edge.</summary>
    Aiming = 3,

    /// <summary>A HeldState action is down: its held bit is set; no edges.</summary>
    Active = 4,

    /// <summary>Nothing more fires until release, and the release is swallowed (G1 not live, G3 state change).</summary>
    Muted = 5,

    /// <summary>A Toggle-mode Interact press opened a toggled hold (story 012): its release emits nothing.</summary>
    Toggled = 6,

    /// <summary>A Sprint press on an Auto-mode device (story 012): the player walks while it is down.</summary>
    Walking = 7,
}

/// <summary>
/// One per-press state machine (ADR-0012 section 3): what was resolved and latched on the press edge of one
/// <c>(device slot, physical input)</c>, kept until release (G1). Lives in a preallocated array indexed by
/// <see cref="DeviceSlots.IndexOf"/>; a value type with no references.
/// </summary>
internal struct PressMachine
{
    /// <summary>Where the machine is.</summary>
    public PressPhase Phase;

    /// <summary>The latched action's grammar.</summary>
    public Grammar Grammar;

    /// <summary>The device the press came from (stamped on the machine's edges).</summary>
    public DeviceClass Device;

    /// <summary>The latched action's <see cref="FrameAction"/>, valid when <see cref="HasBit"/>.</summary>
    public FrameAction Bit;

    /// <summary>True when the latched action is a <see cref="Delivery.Frame"/> action with a frame bit.</summary>
    public bool HasBit;

    /// <summary>The router state the press was gated against.</summary>
    public ushort StateId;

    /// <summary>The chord match latched at press (action, modifiers, Back-ladder owner).</summary>
    public ChordMatch Match;

    /// <summary>
    /// True when the press resolved to a live action (story 026): the adapter marks the press handled, and its echoes and its
    /// release too, even after a state change has muted the machine (ADR-0012 section 7, D-3).
    /// </summary>
    public bool Consumed;

    /// <summary>True when the machine still owes one Release or Cancel edge.</summary>
    public readonly bool OwesClosingEdge => Phase is PressPhase.HoldActive or PressPhase.Aiming;
}

/// <summary>
/// The fixed device-slot table (story 007): keyboard, mouse and pads 0 to 3, each with one entry per <see cref="PhysicalInput"/>
/// low byte. The slot comes from the input's block (and the pad index for pad inputs), not from the record's
/// <see cref="DeviceClass"/>, so a synthesized release lands on the machine of the input it releases.
/// </summary>
internal static class DeviceSlots
{
    /// <summary>Pads the table has room for.</summary>
    public const int PadCount = 4;

    /// <summary>Keyboard, mouse and <see cref="PadCount"/> pads.</summary>
    public const int SlotCount = 2 + PadCount;

    /// <summary>Entries per slot (the low byte of a <see cref="PhysicalInput"/>).</summary>
    public const int InputsPerSlot = 256;

    /// <summary>Machines in the table.</summary>
    public const int MachineCount = SlotCount * InputsPerSlot;

    /// <summary>The table index of <paramref name="input"/> on pad <paramref name="padIndex"/>, or -1 when it has no slot.</summary>
    /// <param name="input">The physical input.</param>
    /// <param name="padIndex">The pad index (used for pad inputs only).</param>
    /// <returns>The index, or -1.</returns>
    public static int IndexOf(PhysicalInput input, byte padIndex)
    {
        int low = (ushort)input & 0xFF;
        switch ((ushort)input >> 8)
        {
            case 0x01:
                return low;
            case 0x02:
                return InputsPerSlot + low;
            case 0x03:
                return padIndex < PadCount ? ((2 + padIndex) * InputsPerSlot) + low : -1;
            default:
                return -1;
        }
    }

    /// <summary>The physical input of table index <paramref name="index"/>.</summary>
    /// <param name="index">A table index.</param>
    /// <returns>The input.</returns>
    public static PhysicalInput InputOf(int index)
    {
        int slot = index / InputsPerSlot;
        int block = slot < 2 ? slot + 1 : 3;
        return (PhysicalInput)((block << 8) | (index % InputsPerSlot));
    }
}
