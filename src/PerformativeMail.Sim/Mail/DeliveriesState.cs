namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The Run-owned deliveries state Mail mirrors (ADR-0017 <c>SetDeliveriesState(open, closeTick, unlockedDistrictMask)</c>):
/// whether deliveries are open, the tick they closed, and which districts are unlocked. Defaults (before story 009 writes
/// it): closed, close tick 0, District 1 unlocked. Mask convention (a Mail decision, story 009 to confirm): bit
/// <c>districtId - 1</c> is set when that district is unlocked, so District 1 is bit 0 and the 63 valid districts fit a u64.
/// All reads go through <see cref="IsDistrictUnlocked"/> so the convention lives in one place.
/// </summary>
internal sealed class DeliveriesState
{
    /// <summary>The mask with only District 1 unlocked.</summary>
    public const ulong DefaultUnlockedMask = 1UL;

    /// <summary>True when deliveries are open.</summary>
    public bool Open { get; private set; }

    /// <summary>The tick deliveries last closed (0 when never).</summary>
    public uint CloseTick { get; private set; }

    /// <summary>The unlocked-district bit mask (see the type remarks).</summary>
    public ulong UnlockedDistrictMask { get; private set; } = DefaultUnlockedMask;

    /// <summary>Replaces the state. The only writer; story 009 calls it from <c>SetDeliveriesState</c>.</summary>
    /// <param name="open">Whether deliveries are open.</param>
    /// <param name="closeTick">The tick they closed.</param>
    /// <param name="unlockedDistrictMask">The unlocked-district mask.</param>
    public void Set(bool open, uint closeTick, ulong unlockedDistrictMask)
    {
        Open = open;
        CloseTick = closeTick;
        UnlockedDistrictMask = unlockedDistrictMask;
    }

    /// <summary>True when the district is unlocked. Districts outside 1 to 63 are never unlocked.</summary>
    /// <param name="districtId">The district id.</param>
    /// <returns>True when unlocked.</returns>
    public bool IsDistrictUnlocked(uint districtId) =>
        districtId >= 1u && districtId <= 63u && ((UnlockedDistrictMask >> (int)(districtId - 1u)) & 1UL) != 0UL;
}
