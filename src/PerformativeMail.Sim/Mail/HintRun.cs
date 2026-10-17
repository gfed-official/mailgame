namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// One run of a value-hint (Mail R17, F10): <see cref="Count"/> consecutive units of a stack that share a deadline shift and a
/// spawn value. An entry carries at most 5 runs (about 4 B each on the wire). A small immutable value; the host and the client
/// both feed <see cref="MailPay.HintPayout"/> from it.
/// </summary>
/// <example>
/// <code>
/// HintRun[] runs = [new HintRun(1, 3, 8), new HintRun(2, 2, 9)];   // 3 letters worth 8 due shift 1, then 2 worth 9 due shift 2
/// </code>
/// </example>
public readonly struct HintRun
{
    /// <summary>The deadline shift shared by the run's units.</summary>
    public readonly byte DeadlineShift;

    /// <summary>How many units the run holds.</summary>
    public readonly byte Count;

    /// <summary>The spawn value, in cents, of each unit of the run.</summary>
    public readonly ushort UnitSpawnValue;

    /// <summary>Creates a run.</summary>
    /// <param name="deadlineShift">The deadline shift of the run's units.</param>
    /// <param name="count">The number of units in the run.</param>
    /// <param name="unitSpawnValue">The spawn value of each unit, in cents.</param>
    /// <example>
    /// <code>var run = new HintRun(deadlineShift: 2, count: 2, unitSpawnValue: 9);</code>
    /// </example>
    public HintRun(byte deadlineShift, byte count, ushort unitSpawnValue)
    {
        DeadlineShift = deadlineShift;
        Count = count;
        UnitSpawnValue = unitSpawnValue;
    }
}
