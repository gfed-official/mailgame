namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// The stored unit of a balance value (GDD CD4): every balance value is a <c>long</c> in this unit. A local
/// placeholder until content-data story 016 introduces the shared <c>FieldSpec</c> unit enum; this enum is then
/// replaced by it. Values are append-only.
/// </summary>
/// <example>
/// <code>
/// var row = new BalanceRow("sim", "warmupTicks", BalanceUnit.Ticks, 0, 100_000);
/// </code>
/// </example>
public enum BalanceUnit : byte
{
    /// <summary>A plain count or capacity.</summary>
    Count = 1,

    /// <summary>Simulation ticks (30 Hz).</summary>
    Ticks = 2,

    /// <summary>Milliseconds.</summary>
    Ms = 3,

    /// <summary>Millimetres.</summary>
    Mm = 4,

    /// <summary>Permille (1000 = 1.0).</summary>
    Permille = 5,

    /// <summary>Milli-points (1000 = 1 point).</summary>
    MilliPoints = 6,

    /// <summary>Cents.</summary>
    Cents = 7,

    /// <summary>Millimetres per second (speeds).</summary>
    MmPerSec = 8,

    /// <summary>Millimetres per second squared (gravity).</summary>
    MmPerSecSq = 9,

    /// <summary>Thousandths of a degree (angles).</summary>
    MilliDeg = 10,

    /// <summary>Whole degrees per second (turn rates).</summary>
    DegPerSec = 11,
}
