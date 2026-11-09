using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's two held views (story 007). <b>Raw</b> held state is one bit per <see cref="DeviceSlots"/> entry, written from
/// every button record, muted and swallowed ones included; chord modifiers read it (never the mute-filtered view).
/// <b>Logical</b> held state is the <c>InputFrame.HeldBits</c> word: a per-<see cref="FrameAction"/> count of the machines
/// holding that action, so the bit is the OR across devices. Preallocated; 0 B after construction.
/// </summary>
internal sealed class HeldTable
{
    private const int WordsPerSlot = DeviceSlots.InputsPerSlot / 64;

    private static readonly int _shiftIndex = DeviceSlots.IndexOf(PhysicalInput.KeyShift, 0);

    private readonly ulong[] _raw = new ulong[DeviceSlots.SlotCount * WordsPerSlot];
    private readonly byte[] _actionCounts = new byte[FrameActions.MaxCount];

    /// <summary>The logical held bits, one per <see cref="FrameAction"/>.</summary>
    public ulong ActionBits { get; private set; }

    /// <summary>The chord modifiers held right now, from raw state.</summary>
    public ModifierMask Modifiers => IsRawHeld(_shiftIndex) ? ModifierMask.Shift : ModifierMask.None;

    /// <summary>Whether table entry <paramref name="index"/> is physically down.</summary>
    /// <param name="index">A <see cref="DeviceSlots"/> index.</param>
    /// <returns>True when down.</returns>
    public bool IsRawHeld(int index) => (_raw[index >> 6] & (1UL << (index & 63))) != 0;

    /// <summary>Records the physical state of table entry <paramref name="index"/>.</summary>
    /// <param name="index">A <see cref="DeviceSlots"/> index.</param>
    /// <param name="down">True for down.</param>
    public void SetRaw(int index, bool down)
    {
        ulong bit = 1UL << (index & 63);
        if (down)
        {
            _raw[index >> 6] |= bit;
        }
        else
        {
            _raw[index >> 6] &= ~bit;
        }
    }

    /// <summary>One more machine holds <paramref name="action"/>.</summary>
    /// <param name="action">The frame action.</param>
    public void AddAction(FrameAction action)
    {
        int i = (byte)action;
        if (i < FrameActions.MaxCount && _actionCounts[i]++ == 0)
        {
            ActionBits |= FrameActions.Bit(action);
        }
    }

    /// <summary>One machine fewer holds <paramref name="action"/>; the bit clears when none does.</summary>
    /// <param name="action">The frame action.</param>
    public void RemoveAction(FrameAction action)
    {
        int i = (byte)action;
        if (i < FrameActions.MaxCount && _actionCounts[i] > 0 && --_actionCounts[i] == 0)
        {
            ActionBits &= ~FrameActions.Bit(action);
        }
    }
}
