using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>The read-only carrier facts Throwing's <c>ThrowRelease</c> validator reads (throwing-catching story 009; ADR-0015 section 1 checks 5, 6 and 9).</summary>
public sealed partial class PlayersSystem
{
    /// <inheritdoc />
    public bool IsSwimming(PlayerSlot slot) =>
        !slot.IsNone && !_records[slot.Value].IsEmpty && (HostPositions[slot].Flags & SampleFlags.Swimming) != 0;

    /// <inheritdoc />
    public bool IsMounted(PlayerSlot slot) => false; // no seat or vehicle at M-1a (BodyContext.OnFoot, as CheckInteraction)

    /// <inheritdoc />
    public ClaimCorroboration CorroborateClaim(PlayerSlot slot, in ClaimedPosition claim, int toleranceMm, out Vec3Mm newest)
    {
        if (slot.IsNone || _records[slot.Value].IsEmpty)
        {
            newest = default;
            return ClaimCorroboration.NoSample;
        }

        ref readonly HostPosition host = ref HostPositions[slot];
        newest = host.Feet;
        if (IsStale(in host))
        {
            return ClaimCorroboration.NoSample;
        }

        return _history.Corroborates(slot, in claim, toleranceMm) ? ClaimCorroboration.Corroborated : ClaimCorroboration.Uncorroborated;
    }
}
