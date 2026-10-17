using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.Players;

/// <summary>Inline fixed-capacity buffer of static-solid indices (no heap allocation).</summary>
[InlineArray(CarrierScratch.SolidCapacity)]
public struct CarrierSolidBuffer
{
    private ushort _element0;
}

/// <summary>Inline fixed-capacity buffer of construct boxes (no heap allocation).</summary>
[InlineArray(CarrierScratch.BoxCapacity)]
public struct CarrierBoxBuffer
{
    private AabbMm _element0;
}

/// <summary>Inline fixed-capacity buffer of indices into <see cref="ISupportTransformSource.Vehicles"/> (no heap allocation).</summary>
[InlineArray(CarrierScratch.VehicleCapacity)]
public struct CarrierVehicleBuffer
{
    private ushort _element0;
}

/// <summary>Inline fixed-capacity buffer of blocked tiles packed as <c>(tz &lt;&lt; 16) | tx</c> (no heap allocation; story 008).</summary>
[InlineArray(CarrierScratch.TileCapacity)]
public struct CarrierTileBuffer
{
    private int _element0;
}

/// <summary>
/// The solver's per-context working memory (ADR-0013 section 4): fixed-capacity candidate buffers for static solids, construct boxes and
/// vehicle refs, plus the probe counters (five solver counters and the two numerics counters the step's <c>Fix</c> helpers bump). Allocated once per stepping context, then <see cref="Clear"/>ed each step; never grown. A
/// full buffer drops the candidate and counts <see cref="CandidateOverflow"/> (never throws). The effective limit of each buffer is
/// <c>min(settings.MaxCarrierCandidates, hard capacity)</c>: solids 64, construct boxes 32, vehicle refs 8; the hard capacities are layout constants,
/// not tunables, and the knob's hard max equals <see cref="SolidCapacity"/>. About 1 KiB, blittable.
/// <para>
/// <b>Warning:</b> <c>default(CarrierScratch)</c> and <c>new CarrierScratch()</c> have limit 0, so every add overflows. Always build one with
/// <c>new CarrierScratch(settings.MaxCarrierCandidates)</c>.
/// </para>
/// The solver cannot write <c>SimCounters</c>, so its caller runs <see cref="CarrierCounterFlush"/> after the step.
/// </summary>
/// <example>
/// <code>
/// var scratch = new CarrierScratch(settings.MaxCarrierCandidates);
/// scratch.Clear();
/// bool kept = scratch.TryAddSolid(17);
/// </code>
/// </example>
public struct CarrierScratch
{
    /// <summary>Hard capacity of the static-solid buffer (the default <c>player.maxCarrierCandidates</c>).</summary>
    public const int SolidCapacity = 64;

    /// <summary>Hard capacity of the construct-box buffer.</summary>
    public const int BoxCapacity = 32;

    /// <summary>Hard capacity of the vehicle-ref buffer.</summary>
    public const int VehicleCapacity = 8;

    /// <summary>Hard capacity of the blocked-tile buffer (the swept region of 2 x (1200 + 300) mm spans at most 3 x 3 tiles; story 008).</summary>
    public const int TileCapacity = 16;

    private CarrierSolidBuffer _solids;
    private CarrierBoxBuffer _boxes;
    private CarrierVehicleBuffer _vehicles;
    private int _solidCount;
    private int _boxCount;
    private int _vehicleCount;
    private CarrierTileBuffer _tiles;
    private int _tileCount;
    private int _limit;

    /// <summary>Max over the tick of sub-steps taken (PCM-PERF-1).</summary>
    public int SubStepsMaxThisTick;

    /// <summary>Max over the tick of slide iterations in one sub-step (PCM-PERF-1).</summary>
    public int SlideIterationsMax;

    /// <summary>Count of sub-step or per-tick caps that clamped a move.</summary>
    public int StepClamped;

    /// <summary>Count of depenetration corrections.</summary>
    public int Depenetrations;

    /// <summary>Count of candidates dropped because a buffer was full.</summary>
    public int CandidateOverflow;

    /// <summary>Count of <c>Fix</c> helper calls inside the step that received a zero divisor (flushed to <c>CounterId.FixDivZero</c>; TD-071 item 2).</summary>
    public int FixDivZero;

    /// <summary>Count of <c>Fix</c> helper results inside the step that were clamped to the 64-bit domain (flushed to <c>CounterId.FixClamped</c>; TD-071 item 2).</summary>
    public int FixClamped;

    /// <summary>Creates an empty scratch with the candidate limit from settings. <c>default</c> / <c>new CarrierScratch()</c> instead have limit 0.</summary>
    /// <param name="maxCarrierCandidates"><c>player.maxCarrierCandidates</c>; clamped into 0 to the hard capacity of each buffer.</param>
    /// <example><code>var s = new CarrierScratch(64);</code></example>
    public CarrierScratch(int maxCarrierCandidates)
    {
        _limit = Math.Max(0, maxCarrierCandidates);
    }

    /// <summary>The configured candidate limit (before clamping per buffer).</summary>
    public readonly int Limit => _limit;

    /// <summary>Blocked-tile candidates held.</summary>
    public readonly int TileCount => _tileCount;

    /// <summary>Static-solid candidates held.</summary>
    public readonly int SolidCount => _solidCount;

    /// <summary>Construct boxes held.</summary>
    public readonly int BoxCount => _boxCount;

    /// <summary>Vehicle refs held.</summary>
    public readonly int VehicleCount => _vehicleCount;

    /// <summary>The static-solid indices held, in insertion order.</summary>
    [UnscopedRef]
    public readonly ReadOnlySpan<ushort> Solids => ((ReadOnlySpan<ushort>)_solids)[.._solidCount];

    /// <summary>The construct boxes held, in insertion order.</summary>
    [UnscopedRef]
    public readonly ReadOnlySpan<AabbMm> Boxes => ((ReadOnlySpan<AabbMm>)_boxes)[.._boxCount];

    /// <summary>The vehicle indices held (into <see cref="ISupportTransformSource.Vehicles"/>), in insertion order.</summary>
    [UnscopedRef]
    public readonly ReadOnlySpan<ushort> Vehicles => ((ReadOnlySpan<ushort>)_vehicles)[.._vehicleCount];

    /// <summary>Adds a static-solid index; at the limit it is dropped and <see cref="CandidateOverflow"/> increments.</summary>
    /// <param name="index">The <c>StaticSolid.Index</c>.</param>
    /// <returns>True when kept.</returns>
    public bool TryAddSolid(ushort index)
    {
        if (_solidCount >= Math.Min(_limit, SolidCapacity))
        {
            CandidateOverflow++;
            return false;
        }

        _solids[_solidCount++] = index;
        return true;
    }

    /// <summary>Blocked tile <paramref name="i"/> (0 to <see cref="TileCount"/> - 1).</summary>
    /// <param name="i">The slot.</param>
    /// <param name="tx">Tile X.</param>
    /// <param name="tz">Tile Z.</param>
    public readonly void TileAt(int i, out int tx, out int tz)
    {
        int packed = _tiles[i];
        tx = packed & 0xFFFF;
        tz = packed >> 16;
    }

    /// <summary>Adds a blocked tile (0 to 65535 per axis); at the limit it is dropped and <see cref="CandidateOverflow"/> increments.</summary>
    /// <param name="tx">Tile X.</param>
    /// <param name="tz">Tile Z.</param>
    /// <returns>True when kept.</returns>
    public bool TryAddTile(int tx, int tz)
    {
        if (_tileCount >= Math.Min(_limit, TileCapacity))
        {
            CandidateOverflow++;
            return false;
        }

        _tiles[_tileCount++] = (tz << 16) | (tx & 0xFFFF);
        return true;
    }

    /// <summary>Adds a construct box; at the limit it is dropped and <see cref="CandidateOverflow"/> increments.</summary>
    /// <param name="box">The box.</param>
    /// <returns>True when kept.</returns>
    public bool TryAddBox(in AabbMm box)
    {
        if (_boxCount >= Math.Min(_limit, BoxCapacity))
        {
            CandidateOverflow++;
            return false;
        }

        _boxes[_boxCount++] = box;
        return true;
    }

    /// <summary>Adds a vehicle ref; at the limit it is dropped and <see cref="CandidateOverflow"/> increments.</summary>
    /// <param name="vehicleIndex">Index into the support source's vehicle span.</param>
    /// <returns>True when kept.</returns>
    public bool TryAddVehicle(ushort vehicleIndex)
    {
        if (_vehicleCount >= Math.Min(_limit, VehicleCapacity))
        {
            CandidateOverflow++;
            return false;
        }

        _vehicles[_vehicleCount++] = vehicleIndex;
        return true;
    }

    /// <summary>Empties the candidate buffers (keeps the limit and the counters). Call once per gather (ADR-0013 section 4 gathers once per tick, not per sub-step).</summary>
    public void ClearCandidates()
    {
        _solidCount = 0;
        _boxCount = 0;
        _vehicleCount = 0;
        _tileCount = 0;
    }

    /// <summary>Resets the probe counters (the five solver counters and the two numerics counters) to zero (done by <see cref="CarrierCounterFlush.Flush"/> after it reads them).</summary>
    public void ClearCounters()
    {
        SubStepsMaxThisTick = 0;
        SlideIterationsMax = 0;
        StepClamped = 0;
        Depenetrations = 0;
        CandidateOverflow = 0;
        FixDivZero = 0;
        FixClamped = 0;
    }

    /// <summary>Empties the candidates and zeroes the counters (keeps the limit). Call before each step.</summary>
    public void Clear()
    {
        ClearCandidates();
        ClearCounters();
    }
}
