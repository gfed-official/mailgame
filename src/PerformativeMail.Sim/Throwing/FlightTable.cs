using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The host's live flights (ADR-0015 section 9; GDD <c>design/gdd/throwing-catching.md</c> T4-T6; TR-throwing-006, -008, -009, -013):
/// one <c>FlightRecord</c> per flight as a struct of arrays, preallocated to <c>maxFlights</c> at construction and never grown.
/// Array position is launch order: <see cref="TryAppend"/> writes at <see cref="Count"/>, <see cref="Remove"/> resolves a flight and
/// only marks its slot dead, and <see cref="Compact"/> (the end of the step) closes the gaps stably, so the survivors keep their
/// relative order. Iterate <c>0 .. Count - 1</c> and skip <see cref="IsDead"/>. Flights are never ordered by <see cref="FlightId"/>.
/// <para>
/// It keeps the run's conservation record for the tick-end check: <see cref="Launched"/> appends, <see cref="Resolved"/> removals,
/// and the first <see cref="FlightId"/> resolved twice (a <see cref="Remove"/> of a flight already resolved). In a correct run
/// <c>Launched == Resolved + LiveFlights</c>. O(1) per append and remove, O(Count) per compaction; allocation-free after
/// construction. Not thread-safe: used on the Sim tick thread.
/// </para>
/// </summary>
/// <example>
/// <code>
/// if (!flights.TryAppend(in init, out int index)) { /* flight_cap */ }
/// FlightId id = flights.Id(index);
/// flights.Remove(index);   // resolved this step
/// flights.Compact();       // end of the step
/// </code>
/// </example>
internal sealed class FlightTable
{
    private readonly SimCounters _counters;
    private readonly FlightIdSerial _serial = new();
    private readonly FlightId[] _id;
    private readonly EntryId[] _entry;
    private readonly byte[] _units;
    private readonly ushort[] _kindDef;
    private readonly PlayerSlot[] _thrower;
    private readonly int[] _classPts;
    private readonly Vec3Mm[] _origin;
    private readonly Vec3Mm[] _vel;
    private readonly uint[] _launchTick;
    private readonly int[] _thrownPm;
    private readonly uint[] _releaseTick;
    private readonly bool[] _openAtRelease;
    private readonly byte[] _hop;
    private readonly bool[] _originClamped;
    private readonly bool[] _dead;
    private int _deadCount;

    /// <summary>Creates the table; every array is allocated here, once.</summary>
    /// <param name="capacity">The capacity, <see cref="ThrowSettings.MaxFlights"/> (1 to 65 535).</param>
    /// <param name="counters">The world's counters (<see cref="CounterId.ThrowingFlightCapRefusals"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1 or above 65 535.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="counters"/> is null.</exception>
    public FlightTable(int capacity, SimCounters counters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, ushort.MaxValue);
        ArgumentNullException.ThrowIfNull(counters);
        _counters = counters;
        _id = new FlightId[capacity];
        _entry = new EntryId[capacity];
        _units = new byte[capacity];
        _kindDef = new ushort[capacity];
        _thrower = new PlayerSlot[capacity];
        _classPts = new int[capacity];
        _origin = new Vec3Mm[capacity];
        _vel = new Vec3Mm[capacity];
        _launchTick = new uint[capacity];
        _thrownPm = new int[capacity];
        _releaseTick = new uint[capacity];
        _openAtRelease = new bool[capacity];
        _hop = new byte[capacity];
        _originClamped = new bool[capacity];
        _dead = new bool[capacity];
    }

    /// <summary>The capacity (<c>maxFlights</c>).</summary>
    public int Capacity => _id.Length;

    /// <summary>The slots in use, live and dead-until-<see cref="Compact"/>, in launch order.</summary>
    public int Count { get; private set; }

    /// <summary>The live flights (<see cref="Count"/> minus the dead slots).</summary>
    public int LiveFlights => Count - _deadCount;

    /// <summary>The next <see cref="FlightId"/> value the serial tries (dumped).</summary>
    public ushort NextFlightId => _serial.Next;

    /// <summary>Flights appended in the run.</summary>
    public long Launched { get; private set; }

    /// <summary>Removals in the run, a second removal of one flight included.</summary>
    public long Resolved { get; private set; }

    /// <summary>True once some <see cref="FlightId"/> has been resolved twice in the run.</summary>
    public bool HasResolvedTwice { get; private set; }

    /// <summary>The first <see cref="FlightId"/> resolved twice (meaningful when <see cref="HasResolvedTwice"/>).</summary>
    public FlightId ResolvedTwiceId { get; private set; }

    /// <summary>
    /// Stores <paramref name="init"/> as a new flight with the next <see cref="FlightId"/>, after the flights already stored. The table
    /// assigns the id, so a refused append consumes none: Mail Dev default, pending Evan (TD-197 item 2). It never compacts, so the
    /// positions a caller holds stay valid: a dead slot awaiting <see cref="Compact"/> still occupies capacity (the step compacts at its
    /// end, before the next phase-0 launch): Mail Dev default, pending Evan (TD-197 item 3).
    /// </summary>
    /// <param name="init">The launch snapshot.</param>
    /// <param name="index">The new flight's position, or -1 when refused.</param>
    /// <returns>False when all <see cref="Capacity"/> slots are in use (the caller's <c>flight_cap</c>); counted in <see cref="CounterId.ThrowingFlightCapRefusals"/>.</returns>
    public bool TryAppend(in FlightInit init, out int index)
    {
        if (Count == Capacity)
        {
            _counters.Add(CounterId.ThrowingFlightCapRefusals);
            index = -1;
            return false;
        }

        index = Count;
        _id[index] = _serial.Allocate();
        _entry[index] = init.Entry;
        _units[index] = init.Units;
        _kindDef[index] = init.KindDef;
        _thrower[index] = init.Thrower;
        _classPts[index] = init.ClassPts;
        _origin[index] = init.Origin;
        _vel[index] = init.Vel;
        _launchTick[index] = init.LaunchTick;
        _thrownPm[index] = init.ThrownPm;
        _releaseTick[index] = init.ReleaseTick;
        _openAtRelease[index] = init.OpenAtRelease;
        _hop[index] = init.Hop;
        _originClamped[index] = init.OriginClamped;
        _dead[index] = false;
        Count++;
        Launched++;
        return true;
    }

    /// <summary>
    /// Resolves the flight at <paramref name="index"/>: marks it dead until <see cref="Compact"/> and counts the resolve. A flight that
    /// was already resolved is counted again and recorded as resolved twice, for the tick-end check to report.
    /// </summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not below <see cref="Count"/>.</exception>
    public void Remove(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Resolved++;

        // A dead slot is this flight's second resolve; its id is not released again, so an id reused after the u16 wrap (a later
        // flight, live again in the serial) is never cleared or counted by it. A live slot's id is always live in the serial.
        bool twice = _dead[index] || !_serial.Release(_id[index]);
        if (twice && !HasResolvedTwice)
        {
            HasResolvedTwice = true;
            ResolvedTwiceId = _id[index];
        }

        if (!_dead[index])
        {
            _dead[index] = true;
            _deadCount++;
        }
    }

    /// <summary>Closes the dead slots stably: the live flights move down in their launch order and <see cref="Count"/> becomes <see cref="LiveFlights"/>.</summary>
    public void Compact()
    {
        if (_deadCount == 0)
        {
            return;
        }

        int write = 0;
        for (int read = 0; read < Count; read++)
        {
            if (_dead[read])
            {
                _dead[read] = false;
                continue;
            }

            if (write != read)
            {
                Move(read, write);
            }

            write++;
        }

        Count = write;
        _deadCount = 0;
    }

    /// <summary>True when the flight at <paramref name="index"/> was resolved and awaits <see cref="Compact"/>.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>True for a dead slot.</returns>
    public bool IsDead(int index) => _dead[index];

    /// <summary>The flight's id.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The id.</returns>
    public FlightId Id(int index) => _id[index];

    /// <summary>The flight's live detached entry.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The entry.</returns>
    public EntryId Entry(int index) => _entry[index];

    /// <summary>The entry's units.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The units.</returns>
    public byte Units(int index) => _units[index];

    /// <summary>The entry's kind or item def.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The def.</returns>
    public ushort KindDef(int index) => _kindDef[index];

    /// <summary>The thrower's slot.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The slot.</returns>
    public PlayerSlot Thrower(int index) => _thrower[index];

    /// <summary><c>classPts</c> of the entry's class.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The points.</returns>
    public int ClassPts(int index) => _classPts[index];

    /// <summary>The launch origin.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>A read-only reference into the table.</returns>
    public ref readonly Vec3Mm Origin(int index) => ref _origin[index];

    /// <summary>The launch velocity.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>A read-only reference into the table.</returns>
    public ref readonly Vec3Mm Vel(int index) => ref _vel[index];

    /// <summary>The launch tick.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The tick.</returns>
    public uint LaunchTick(int index) => _launchTick[index];

    /// <summary>The snapshotted <c>thrownPm</c>.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The permille.</returns>
    public int ThrownPm(int index) => _thrownPm[index];

    /// <summary>The release apply tick.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The tick.</returns>
    public uint ReleaseTick(int index) => _releaseTick[index];

    /// <summary>Whether deliveries were OPEN at the release tick.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The flag.</returns>
    public bool OpenAtRelease(int index) => _openAtRelease[index];

    /// <summary>The relay hop count.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The hop.</returns>
    public byte Hop(int index) => _hop[index];

    /// <summary>Whether the origin was clamped to the newest history sample.</summary>
    /// <param name="index">A position below <see cref="Count"/>.</param>
    /// <returns>The flag.</returns>
    public bool OriginClamped(int index) => _originClamped[index];

    private void Move(int from, int to)
    {
        _id[to] = _id[from];
        _entry[to] = _entry[from];
        _units[to] = _units[from];
        _kindDef[to] = _kindDef[from];
        _thrower[to] = _thrower[from];
        _classPts[to] = _classPts[from];
        _origin[to] = _origin[from];
        _vel[to] = _vel[from];
        _launchTick[to] = _launchTick[from];
        _thrownPm[to] = _thrownPm[from];
        _releaseTick[to] = _releaseTick[from];
        _openAtRelease[to] = _openAtRelease[from];
        _hop[to] = _hop[from];
        _originClamped[to] = _originClamped[from];
    }
}
