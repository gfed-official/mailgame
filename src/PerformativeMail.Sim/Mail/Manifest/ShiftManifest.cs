using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// One shift's manifest (GDD mail-destinations R10; ADR-0017 section 10; mail-destinations story 013): every unit the shift will spawn,
/// in manifest order, with its kind, address, flags, spawn value, deadline shift and batch; per batch its tick offset from Delivery
/// start, its unit count and its first unit; and the forecast (units by kind x district). Filled only by <see cref="ManifestRoller"/>.
/// Every buffer, the roller's scratch included, is preallocated at construction, so a roll allocates 0 B. Not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var m = new ShiftManifest(maxAddresses: registry.Count);
/// int n = ManifestRoller.Roll(stream, in profile, addresses, in kinds, m);
/// for (int b = 0; b &lt; m.BatchCount; b++) { long at = deliveryStart + m.BatchOffset(b); }
/// </code>
/// </example>
public sealed class ShiftManifest
{
    /// <summary>The most units one manifest holds (a roll with more units fails with <see cref="ManifestRollStatus.TooManyUnits"/>).</summary>
    public const int MaxUnits = 4096;

    /// <summary>The most districts the forecast counts (district ids 1 to 63; index 0 unused).</summary>
    public const int ForecastDistricts = 64;

    private readonly DefIndex[] _kind = new DefIndex[MaxUnits];
    private readonly AddressId[] _address = new AddressId[MaxUnits];
    private readonly MailFlags[] _flags = new MailFlags[MaxUnits];
    private readonly ushort[] _spawnValue = new ushort[MaxUnits];
    private readonly byte[] _deadlineShift = new byte[MaxUnits];
    private readonly ushort[] _batch = new ushort[MaxUnits];
    private readonly long[] _batchOffset = new long[BatchSchedule.MaxBatches];
    private readonly int[] _batchCount = new int[BatchSchedule.MaxBatches];
    private readonly int[] _batchStart = new int[BatchSchedule.MaxBatches];
    private readonly int[] _forecast = new int[KindCounts.MaxKinds * ForecastDistricts];

    /// <summary>Creates an empty manifest sized for <paramref name="maxAddresses"/> unlocked addresses.</summary>
    /// <param name="maxAddresses">The most addresses a roll is given (the registry's row count).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAddresses"/> is negative.</exception>
    public ShiftManifest(int maxAddresses)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxAddresses);
        SortedAddresses = new AddressId[maxAddresses];
        Districts = new int[maxAddresses];
    }

    /// <summary>N, the units in the manifest (0 when empty or after a failed roll).</summary>
    public int UnitCount { get; private set; }

    /// <summary>nb, the batches in the manifest.</summary>
    public int BatchCount { get; private set; }

    /// <summary>The last roll's result: N, or a negative <see cref="ManifestRollStatus"/>, F3, F2 or F4 code (0 before any roll).</summary>
    public int Status { get; private set; }

    // Roller scratch (preallocated): the ascending address list, its districts, and ManifestDraws' outputs.
    internal AddressId[] SortedAddresses { get; }

    internal int[] Districts { get; }

    internal byte[] UnitKinds { get; } = new byte[MaxUnits];

    internal AddressId[] UnitAddresses { get; } = new AddressId[MaxUnits];

    internal bool[] UnitCursed { get; } = new bool[MaxUnits];

    internal long[] BatchTicks { get; } = new long[BatchSchedule.MaxBatches];

    internal int[] Counts { get; } = new int[BatchSchedule.MaxBatches];

    /// <summary>The mail kind of unit <paramref name="i"/> (manifest order).</summary>
    /// <param name="i">The unit, 0 to <see cref="UnitCount"/> - 1.</param>
    /// <returns>The kind.</returns>
    public DefIndex KindAt(int i) => _kind[i];

    /// <summary>The address of unit <paramref name="i"/>.</summary>
    /// <param name="i">The unit.</param>
    /// <returns>The address.</returns>
    public AddressId AddressAt(int i) => _address[i];

    /// <summary>The flags of unit <paramref name="i"/>.</summary>
    /// <param name="i">The unit.</param>
    /// <returns>The flags.</returns>
    public MailFlags FlagsAt(int i) => _flags[i];

    /// <summary>The F1 spawn value of unit <paramref name="i"/>, cents.</summary>
    /// <param name="i">The unit.</param>
    /// <returns>The spawn value.</returns>
    public ushort SpawnValueAt(int i) => _spawnValue[i];

    /// <summary>The deadline shift of unit <paramref name="i"/>.</summary>
    /// <param name="i">The unit.</param>
    /// <returns>The deadline shift.</returns>
    public byte DeadlineShiftAt(int i) => _deadlineShift[i];

    /// <summary>The batch of unit <paramref name="i"/>.</summary>
    /// <param name="i">The unit.</param>
    /// <returns>The batch index.</returns>
    public int BatchOf(int i) => _batch[i];

    /// <summary>Batch <paramref name="b"/>'s tick, as an offset from the Delivery-start tick.</summary>
    /// <param name="b">The batch, 0 to <see cref="BatchCount"/> - 1.</param>
    /// <returns>The offset in ticks.</returns>
    public long BatchOffset(int b) => _batchOffset[b];

    /// <summary>Batch <paramref name="b"/>'s unit count c_b.</summary>
    /// <param name="b">The batch.</param>
    /// <returns>The count.</returns>
    public int BatchUnitCount(int b) => _batchCount[b];

    /// <summary>Batch <paramref name="b"/>'s first unit (its units are the next <see cref="BatchUnitCount"/> in manifest order).</summary>
    /// <param name="b">The batch.</param>
    /// <returns>The unit index.</returns>
    public int BatchStart(int b) => _batchStart[b];

    /// <summary>The forecast: the manifest's units of kind <paramref name="kind"/> addressed to <paramref name="district"/>.</summary>
    /// <param name="kind">The mail kind (below <see cref="KindCounts.MaxKinds"/>).</param>
    /// <param name="district">The district id (1 to 63).</param>
    /// <returns>The unit count; 0 outside the table.</returns>
    public int Forecast(DefIndex kind, int district) =>
        kind.Value < KindCounts.MaxKinds && (uint)district < ForecastDistricts ? _forecast[(kind.Value * ForecastDistricts) + district] : 0;

    /// <summary>Empties the manifest and records a roll's result (the roller's first and last step).</summary>
    internal void Reset(int status)
    {
        Array.Clear(_forecast);
        UnitCount = 0;
        BatchCount = 0;
        Status = status;
    }

    /// <summary>Sets batch <paramref name="b"/>.</summary>
    internal void SetBatch(int b, long offset, int count, int start)
    {
        _batchOffset[b] = offset;
        _batchCount[b] = count;
        _batchStart[b] = start;
    }

    /// <summary>Sets unit <paramref name="i"/> and counts it in the forecast.</summary>
    internal void SetUnit(int i, DefIndex kind, AddressId address, MailFlags flags, ushort spawnValue, byte deadlineShift, int batch)
    {
        _kind[i] = kind;
        _address[i] = address;
        _flags[i] = flags;
        _spawnValue[i] = spawnValue;
        _deadlineShift[i] = deadlineShift;
        _batch[i] = (ushort)batch;
        int district = (int)address.District;
        if (kind.Value < KindCounts.MaxKinds && (uint)district < ForecastDistricts)
        {
            _forecast[(kind.Value * ForecastDistricts) + district]++;
        }
    }

    /// <summary>Publishes a successful roll's sizes.</summary>
    internal void Complete(int units, int batches)
    {
        UnitCount = units;
        BatchCount = batches;
        Status = units;
    }
}
