using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>The host's selected hotbar slot (the <c>HotbarSelect</c> op; Throwing's <c>ThrowRelease</c> check 2, ADR-0015 section 1).</summary>
public sealed partial class PlayersSystem
{
    /// <inheritdoc />
    public byte SelectedHotbarIndex(PlayerSlot slot) => slot.IsNone ? (byte)0 : _records[slot.Value].HotbarIndex;
}
