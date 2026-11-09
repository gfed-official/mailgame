// M-1a STUB — replaced by Run Structure at M1 (no GDD)
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Sim.Run;

/// <summary>
/// The M-1a shift stub's greybox configuration (mail-destinations story 009). Supplied by the composition profile
/// (<see cref="Composition.SimProfile.ShiftStub"/>), not by a balance key and not by content: Run Structure has no GDD and no balance
/// keys yet. Placement: uncredited Mail Dev note (TD-211 item 2); its constructor row is the ADR-0003 section 4 erratum 2026-11-08, approved by Evan 2026-11-08 9:51 PM PT by card click (TD-211 item 3).
/// </summary>
/// <example><code>SimProfile profile = SimProfile.M1aGreybox.WithShiftStub(new ShiftStubSettings { PrepTicks = 90, DeliverySeconds = 10 });</code></example>
public sealed record ShiftStubSettings
{
    /// <summary>Ticks of Prep before Delivery starts. PLACEHOLDER (60 s).</summary>
    public int PrepTicks { get; init; } = 60 * TickRate.Hz;

    /// <summary>Seconds of Delivery. PLACEHOLDER: 240 is F2's shift-1 example value, not a tuned one.</summary>
    public int DeliverySeconds { get; init; } = 240;

    /// <summary>The shift number, handed to Mail's <c>PrepareShift</c> (story 013).</summary>
    public byte Shift { get; init; } = 1;

    /// <summary>The unlocked-district mask (bit <c>districtId - 1</c>); District 1 only by default.</summary>
    public ulong UnlockedMask { get; init; } = 1UL;

    /// <summary>
    /// V, the shift's spawn budget in cents, handed to Mail's <c>PrepareShift</c> (mail-destinations story 013). PLACEHOLDER: 960 is the
    /// GDD F3 solo shift-1 example, not a tuned value (Run Structure owns quota x <c>spawnOverhead</c> at M1).
    /// </summary>
    public long SpawnBudgetCents { get; init; } = 960;

    /// <summary>The team <c>playerScale</c>, permille, handed to Mail's complaint buckets at construction (TD-193 item 4).</summary>
    public int PlayerScalePm { get; init; } = 1000;
}
