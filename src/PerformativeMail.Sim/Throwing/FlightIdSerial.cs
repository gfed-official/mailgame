using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The wrapping u16 <see cref="FlightId"/> allocator and the live-id set (ADR-0004, ADR-0015 section 9; TR-throwing-009, GDD TC-E-6 c).
/// Ids are handed out in creation order and wrap after 65 535 by <c>unchecked</c> u16 arithmetic; they are only ever compared for
/// equality. A <see cref="FlightId"/> is never live twice: the allocator skips an id whose flight is still live
/// (<see cref="SimAssertCode.ThrowingFlightIdStillLive"/> under <c>SIM_ASSERT</c>), which needs 65 536 launches while one flight stays
/// in the air. The table capacity is at most 65 535, so a free id always exists. The live set is a fixed 8 KiB bitset allocated once;
/// <see cref="Release"/> reports an id that was not live, which is how a second resolve of one flight is found. Allocation-free after
/// construction. Not thread-safe.
/// </summary>
/// <example>
/// <code>
/// FlightId id = serial.Allocate();
/// bool first = serial.Release(id);    // true
/// bool again = serial.Release(id);    // false: resolved twice
/// </code>
/// </example>
internal sealed class FlightIdSerial
{
    private const int WordShift = 6;
    private const int BitMask = 63;

    private readonly ulong[] _live = new ulong[(ushort.MaxValue + 1) >> WordShift];

    /// <summary>The next value <see cref="Allocate"/> tries (the dumped serial).</summary>
    public ushort Next { get; private set; }

    /// <summary>Hands out the next id that is not live and marks it live.</summary>
    /// <returns>The new id.</returns>
    public FlightId Allocate()
    {
        ushort id = Next;
        while (IsLive(id))
        {
            SimAssert.That(false, SimAssertCode.ThrowingFlightIdStillLive);
            id = unchecked((ushort)(id + 1));
        }

        Next = unchecked((ushort)(id + 1));
        _live[id >> WordShift] |= 1UL << (id & BitMask);
        return new FlightId(id);
    }

    /// <summary>Marks <paramref name="id"/> no longer live.</summary>
    /// <param name="id">The resolved flight.</param>
    /// <returns>False when <paramref name="id"/> was not live (resolved twice, or never allocated).</returns>
    public bool Release(FlightId id)
    {
        bool wasLive = IsLive(id.Value);
        _live[id.Value >> WordShift] &= ~(1UL << (id.Value & BitMask));
        return wasLive;
    }

    private bool IsLive(ushort id) => (_live[id >> WordShift] & (1UL << (id & BitMask))) != 0;
}
