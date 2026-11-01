using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The immutable view of the <see cref="ThrowBalance"/> knobs that F1 and F2 read (ADR-0015 section 3, ADR-0010;
/// GDD <c>design/gdd/throwing-catching.md</c> Tuning Knobs): <c>baseSpeed[class]</c>, <c>g</c>, <c>maxFlightTicks</c>,
/// <c>handHeightMm</c>, <c>fwdOffMm</c>, <c>sideOffMm</c>, plus the flight capacity <see cref="MaxFlights"/> (not a knob). Built once from the content set's <see cref="ThrowBalance"/>
/// record; every value is a plain copy (no code literal), so designers tune through <c>balance/throw.jsonc</c>.
/// The receiver rank table is story 002's and is not copied here. Immutable; safe from any thread.
/// </summary>
/// <example>
/// <code>
/// var s = new ThrowSettings(contentSet.Throw);
/// int light = s.BaseSpeed(WeightClass.Light);   // 14 000 mm/s with the M-1a defaults
/// </code>
/// </example>
public sealed class ThrowSettings
{
    /// <summary>
    /// Creates the view from the content set's throw record. The record must already have passed <c>ContentValidator</c>
    /// (CV-THR rules, e.g. speeds at most 30 000 mm/s): the F1/F2 <see cref="int"/> casts rely on those bounds.
    /// </summary>
    /// <param name="balance">The <c>throw.*</c> values (for example <c>ContentSet.Throw</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="balance"/> is null.</exception>
    public ThrowSettings(ThrowBalance balance)
        : this(balance, DefaultMaxFlights)
    {
    }

    /// <summary>Creates the view with a chosen flight capacity (tests; 16 is the perf fixture).</summary>
    /// <param name="balance">The <c>throw.*</c> values.</param>
    /// <param name="maxFlights">The flight table capacity, 1 to 65 535.</param>
    /// <exception cref="ArgumentNullException"><paramref name="balance"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFlights"/> is below 1 or above 65 535.</exception>
    internal ThrowSettings(ThrowBalance balance, int maxFlights)
    {
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFlights, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFlights, ushort.MaxValue);
        MaxFlights = maxFlights;
        SpeedLight = balance.ThrowSpeedLight;
        SpeedMedium = balance.ThrowSpeedMedium;
        SpeedHeavy = balance.ThrowSpeedHeavy;
        G = balance.G;
        MaxFlightTicks = balance.MaxFlightTicks;
        HandHeightMm = balance.HandHeightMm;
        FwdOffMm = balance.FwdOffMm;
        SideOffMm = balance.SideOffMm;
    }

    /// <summary>
    /// The shipped flight capacity, <c>maxFlights</c> (ADR-0015 section 9: 8 players x 6/s x 3 s = 144, plus slack).
    /// Evan approved (TD-195 item 1), 2026-10-29 1:52 PM PT: "Approve all three TD-195 defaults for throwing-catching 008 and all three TD-196 defaults for throwing-catching 006": set by the constructor, not a balance key, so content and its
    /// hash are unchanged (the <c>InventorySettings.EntryInitialLastId</c> precedent).
    /// </summary>
    public const int DefaultMaxFlights = 160;

    /// <summary>
    /// The flight table capacity (<see cref="DefaultMaxFlights"/> unless a test chose another). At most 65 535, so a live
    /// <see cref="Primitives.FlightId"/> is never reused while its flight is in the air.
    /// </summary>
    public int MaxFlights { get; }

    /// <summary>Light throw speed, mm/s (<c>throw.throwSpeed.light</c>, default 14 000).</summary>
    public int SpeedLight { get; }

    /// <summary>Medium throw speed, mm/s (<c>throw.throwSpeed.medium</c>, default 10 000).</summary>
    public int SpeedMedium { get; }

    /// <summary>Heavy throw speed, mm/s (<c>throw.throwSpeed.heavy</c>, default 6000).</summary>
    public int SpeedHeavy { get; }

    /// <summary>Thrown-item gravity, mm/s^2 (<c>throw.g</c>, default 9800).</summary>
    public int G { get; }

    /// <summary>Flight time cap, ticks (<c>throw.maxFlightTicks</c>, default 90; validator range 30 to 150).</summary>
    public int MaxFlightTicks { get; }

    /// <summary>Launch height above the feet, mm (<c>throw.handHeightMm</c>, default 1200).</summary>
    public int HandHeightMm { get; }

    /// <summary>Launch forward offset, mm (<c>throw.fwdOffMm</c>, default 300).</summary>
    public int FwdOffMm { get; }

    /// <summary>Launch side offset toward the throwing hand, mm (<c>throw.sideOffMm</c>, default 250).</summary>
    public int SideOffMm { get; }

    /// <summary>
    /// <c>baseSpeed[class]</c> in mm/s: Light, Medium and Heavy map to their <c>throw.throwSpeed.*</c> knob. Bulk and any
    /// value outside the enum return 0 ("not throwable"): GDD "Bulk" says cargo can never be on the hotbar, so it can never
    /// be selected for a throw, and non-mail without a class already defaults to <c>light</c> at content load (Grid
    /// Inventory R16). <see cref="LaunchMath.Compute"/> rejects a 0 base speed.
    /// </summary>
    /// <param name="weightClass">The thrown stack's weight class.</param>
    /// <returns>The base speed in mm/s, or 0 when the class is not throwable.</returns>
    /// <example><code>int v0 = settings.BaseSpeed(WeightClass.Heavy);   // 6000</code></example>
    public int BaseSpeed(WeightClass weightClass) => weightClass switch
    {
        WeightClass.Light => SpeedLight,
        WeightClass.Medium => SpeedMedium,
        WeightClass.Heavy => SpeedHeavy,
        _ => 0,
    };
}
