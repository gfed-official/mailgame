namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The mean distance multiplier of F3 as an exact rational (GDD F3: <c>meanDistPm = 1000 + 250 x SUM(district - 1) / H</c>,
/// never a float). Stored as <see cref="HouseCount"/> H, the integer <see cref="DistrictOffsetSum"/> S = SUM(district - 1)
/// and the distance step, so <c>meanDistPm = (1000 H + step S) / H</c>. Together with the GDD's F3 common denominator
/// <c>H x 10^6</c> (<see cref="Denominator"/>) every expected unit value E_k (<see cref="ExpectedNumerator"/>), every
/// V_k (<see cref="ShareScale"/> = H x 10^3 times <c>V x share</c>) and the carry r is an integer over that denominator.
/// Immutable, 0 B, thread-safe. Construction never throws: out-of-range inputs are clamped (districts to
/// [1, <see cref="MaxDistrict"/>], the step to [0, <see cref="MaxStepPm"/>]) and an oversize house list is reported by
/// <see cref="IsWithinLimit"/> (F3 then returns <see cref="KindCountsStatus.InvalidInput"/>).
/// </summary>
/// <example>
/// <code>
/// MeanDist md = MeanDist.Create(districts, 250);          // districts in ascending AddressId order
/// long denom = md.Denominator;                            // H * 10^6
/// </code>
/// </example>
public readonly struct MeanDist
{
    /// <summary>The largest house count F3 accepts (bounds every F3 numerator well inside <see cref="Int128"/>).</summary>
    public const int MaxHouses = 10_000;

    /// <summary>The largest district id that contributes (larger ids are clamped; the GDD range is 1 to 5).</summary>
    public const int MaxDistrict = 1000;

    /// <summary>The largest <c>distanceStepPm</c> that contributes (larger values are clamped).</summary>
    public const int MaxStepPm = 1 << 20;

    private MeanDist(int houseCount, long districtOffsetSum, int distanceStepPm)
    {
        HouseCount = houseCount;
        DistrictOffsetSum = districtOffsetSum;
        DistanceStepPm = distanceStepPm;
    }

    /// <summary>H, the number of unlocked houses.</summary>
    public int HouseCount { get; }

    /// <summary>S = SUM(district - 1) over the houses (each district clamped to [1, <see cref="MaxDistrict"/>]).</summary>
    public long DistrictOffsetSum { get; }

    /// <summary>The <c>distanceStepPm</c> knob (clamped to [0, <see cref="MaxStepPm"/>]).</summary>
    public int DistanceStepPm { get; }

    /// <summary>True when H is at most <see cref="MaxHouses"/>.</summary>
    public bool IsWithinLimit => HouseCount <= MaxHouses;

    /// <summary>The common denominator <c>H x 10^6</c> of every E_k, V_k and the carry r (0 when there are no houses).</summary>
    public long Denominator => (long)HouseCount * 1_000_000L;

    /// <summary>The scale that turns <c>V x sharePm</c> (cents x permille) into a numerator over <see cref="Denominator"/>: <c>H x 10^3</c>.</summary>
    public long ShareScale => (long)HouseCount * 1_000L;

    /// <summary>Builds the mean from the unlocked houses' districts (the caller passes them in ascending <c>AddressId</c> order; the sum is order-free).</summary>
    /// <param name="districts">District id of each unlocked house.</param>
    /// <param name="distanceStepPm">The <c>distanceStepPm</c> knob.</param>
    /// <returns>The mean; <see cref="HouseCount"/> is 0 for an empty span (F3 reports a content error then).</returns>
    public static MeanDist Create(ReadOnlySpan<int> districts, int distanceStepPm)
    {
        long sum = 0;
        for (int i = 0; i < districts.Length; i++)
        {
            sum += Math.Clamp(districts[i], 1, MaxDistrict) - 1L;
        }

        return new MeanDist(districts.Length, sum, Math.Clamp(distanceStepPm, 0, MaxStepPm));
    }

    /// <summary>
    /// The numerator of <c>E = baseValue x shiftPm x meanDistPm / 10^6</c> over <see cref="Denominator"/>:
    /// <c>baseValue x shiftPm x (1000 H + step x S)</c>, formed in <see cref="Int128"/> (no overflow for any int H).
    /// </summary>
    /// <param name="baseValueCents">The kind's base value in cents.</param>
    /// <param name="shiftPm">The shift multiplier in permille.</param>
    /// <returns>The exact numerator.</returns>
    public Int128 ExpectedNumerator(long baseValueCents, long shiftPm)
        => (Int128)baseValueCents * shiftPm * (((Int128)1000 * HouseCount) + ((Int128)DistanceStepPm * DistrictOffsetSum));
}
