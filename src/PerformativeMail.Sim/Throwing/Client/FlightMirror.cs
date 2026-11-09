using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The client's live flights (ADR-0016 sections 1 and 6; ADR-0015 section 9; TR-throwing-016 client half; throwing-catching story 017),
/// rebuilt only from events: a joiner sees no flight already in the air. <see cref="Apply"/> adds a flight on <c>ThrowLaunched</c> and
/// marks it resolved on <c>FlightResolved</c>, keeping it until its visual has landed. A <c>FlightResolved</c> for an unknown flight is
/// ignored (a rejoiner draws no arc) and a repeated one changes nothing. <see cref="Sample"/>, once per frame from <c>IRenderClock</c>,
/// places every flight on <see cref="FlightTimeline"/>; a flight reported <see cref="FlightPhase.Landed"/> is dropped at the next sample.
/// Views read the result through <see cref="IFlightRead"/>; the mirror has no landing callback and plays no beat (outcome beats come
/// only from <c>PresentationEventStream</c>).
/// <para>
/// Launch verdict (TC-VA-1 Sim half): the class at release, fixed at launch and never changed. Locally it comes from
/// <see cref="ReleaseRecord.ClassAtRelease"/>: <see cref="NoteOwnRelease"/> queues each sent release, the own slot's next
/// <c>ThrowLaunched</c> takes the oldest, and a refused <c>ThrowRelease</c> reply discards the oldest (the host answers releases in send
/// order; <c>ThrowLaunched</c> carries no request id). Remote flights carry <see cref="EndClass.None"/> until the M0 payload decision.
/// </para>
/// <para>
/// <see cref="Apply"/> and <see cref="ApplyReply"/> have the ADR-0016 <c>IMirrorPart</c> shape; until <c>BindMirrorPart</c> lands, the
/// Client.Core <c>FlightMirrorConsumer</c> registers it at <c>MirrorDrain</c>. Allocation-free after construction; not thread-safe
/// (main thread).
/// </para>
/// </summary>
/// <example>
/// <code>
/// var mirror = new FlightMirror(settings, ownSlot);
/// mirror.Apply(in entry, default);                    // at MirrorDrain
/// mirror.Sample(clock.HostTick, clock.AlphaPermille); // once per frame
/// for (int i = 0; i &lt; mirror.Count; i++) { ref readonly FlightRenderState f = ref mirror[i]; }
/// </code>
/// </example>
public sealed class FlightMirror : IFlightRead
{
    /// <summary>Own releases that can wait for their <c>ThrowLaunched</c> (the release-record ring length).</summary>
    public const int PendingVerdictCapacity = ThrowReleaseSender.RecordCapacity;

    private readonly int _g;
    private readonly int _maxAge;
    private readonly PlayerSlot _ownSlot;
    private readonly Flight[] _flights;
    private readonly FlightRenderState[] _states;
    private readonly EndClass[] _pending = new EndClass[PendingVerdictCapacity];
    private int _count;
    private int _pendingHead;
    private int _pendingCount;

    /// <summary>Creates an empty mirror.</summary>
    /// <param name="settings">The throw knobs: <c>g</c> (the host's F2 gravity), <c>maxFlightTicks</c> and the flight cap.</param>
    /// <param name="ownSlot">This client's player slot (whose launches take the local verdict); <see cref="PlayerSlot.None"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public FlightMirror(ThrowSettings settings, PlayerSlot ownSlot)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _g = settings.G;
        _maxAge = settings.MaxFlightTicks;
        _ownSlot = ownSlot;
        _flights = new Flight[settings.MaxFlights];
        _states = new FlightRenderState[settings.MaxFlights];
    }

    /// <inheritdoc />
    public int Count => _count;

    /// <summary><c>ThrowLaunched</c> events dropped because the mirror was full or the flight id was already live.</summary>
    public long DroppedLaunches { get; private set; }

    /// <summary><c>FlightResolved</c> events for no mirrored flight (ignored).</summary>
    public long UnknownResolutions { get; private set; }

    /// <inheritdoc />
    public ref readonly FlightRenderState this[int index] => ref _states[index];

    /// <summary>Applies one drained event; kinds other than <c>ThrowLaunched</c> and <c>FlightResolved</c> are ignored.</summary>
    /// <param name="e">The entry.</param>
    /// <param name="blob">Unused (both payloads are inline).</param>
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
        if (e.Kind == ThrowEventKinds.ThrowLaunched)
        {
            Launched(e.PayloadAs<ThrowLaunched>());
        }
        else if (e.Kind == ThrowEventKinds.FlightResolved)
        {
            Resolved(e.PayloadAs<FlightResolved>());
        }
    }

    /// <summary>Applies one drained reply: a refused <c>ThrowRelease</c> discards the oldest queued own verdict.</summary>
    /// <param name="r">The reply.</param>
    public void ApplyReply(in Reply r)
    {
        if (r.Kind == ThrowOpKinds.ThrowRelease && !r.IsOk && _pendingCount > 0)
        {
            DequeueVerdict();
        }
    }

    /// <summary>Queues the verdict of a release this client just sent (call after a <see cref="ThrowSendResult.Sent"/> commit).</summary>
    /// <param name="record">The sender's newest record.</param>
    public void NoteOwnRelease(in ReleaseRecord record)
    {
        if (_pendingCount == PendingVerdictCapacity)
        {
            DequeueVerdict();
        }

        int tail = _pendingHead + _pendingCount;
        _pending[tail >= PendingVerdictCapacity ? tail - PendingVerdictCapacity : tail] = record.ClassAtRelease;
        _pendingCount++;
    }

    /// <summary>
    /// Places every flight for this frame at <c>hostTick − 3</c> + <paramref name="alphaPermille"/> (<see cref="FlightTimeline"/>), after
    /// dropping the flights the previous sample reported <see cref="FlightPhase.Landed"/>.
    /// </summary>
    /// <param name="hostTick"><c>IRenderClock.HostTick</c>.</param>
    /// <param name="alphaPermille"><c>IRenderClock.AlphaPermille</c>.</param>
    public void Sample(uint hostTick, int alphaPermille)
    {
        int kept = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_states[i].Frame.Phase != FlightPhase.Landed)
            {
                _flights[kept++] = _flights[i];
            }
        }

        _count = kept;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Flight f = ref _flights[i];
            FlightFrame frame = FlightTimeline.Sample(in f.Track, hostTick, alphaPermille, _maxAge);
            _states[i] = new FlightRenderState(in f.Facts, frame, FlightSpin.AngleBam(f.Facts.Id, f.Facts.Units > 1, frame.ElapsedMilliTicks));
        }
    }

    private void Launched(in ThrowLaunched l)
    {
        var id = new FlightId(l.FlightId);
        if (_count == _flights.Length || IndexOf(id) >= 0)
        {
            DroppedLaunches++;
            return;
        }

        EndClass verdict = EndClass.None;
        if (!_ownSlot.IsNone && l.Thrower == _ownSlot.Value && _pendingCount > 0)
        {
            verdict = DequeueVerdict();
        }

        var launch = new Launch(new Vec3Mm(l.OriginX, l.OriginY, l.OriginZ), new Vec3Mm(l.VelX, l.VelY, l.VelZ), _g);
        ref Flight f = ref _flights[_count];
        f.Track = new FlightTrack(launch, l.LaunchTick, FlightTrack.Unresolved, default);
        f.Facts = new FlightFacts(id, l.Thrower, l.Units, l.KindDef, l.EntryId, (l.Flags & ThrowLaunched.OriginClampedBit) != 0, verdict, Terminal.Timeout);
        _states[_count] = new FlightRenderState(in f.Facts, default, 0);
        _count++;
    }

    private void Resolved(in FlightResolved r)
    {
        int i = IndexOf(new FlightId(r.FlightId));
        if (i < 0)
        {
            UnknownResolutions++;
            return;
        }

        ref Flight f = ref _flights[i];
        if (f.Track.ResolvedAge != FlightTrack.Unresolved)
        {
            return;
        }

        long age = (long)r.Tick - f.Track.LaunchTick;
        int resolvedAge = age < 0 ? 0 : age > _maxAge ? _maxAge : (int)age;
        f.Track = new FlightTrack(f.Track.Launch, f.Track.LaunchTick, resolvedAge, new Vec3Mm(r.ContactX, r.ContactY, r.ContactZ));
        f.Facts = f.Facts.WithTerminal(r.Terminal);
    }

    private int IndexOf(FlightId id)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_flights[i].Facts.Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private EndClass DequeueVerdict()
    {
        EndClass v = _pending[_pendingHead];
        _pendingHead = _pendingHead + 1 == PendingVerdictCapacity ? 0 : _pendingHead + 1;
        _pendingCount--;
        return v;
    }

    private struct Flight
    {
        public FlightTrack Track;
        public FlightFacts Facts;
    }
}

/// <summary>
/// The read port the flight view uses (throwing-catching stories 020, 021): the mirrored flights, in launch-arrival order, as of the last
/// <see cref="FlightMirror.Sample"/>. Read-only.
/// </summary>
/// <example><code>for (int i = 0; i &lt; flights.Count; i++) { ref readonly FlightRenderState f = ref flights[i]; }</code></example>
public interface IFlightRead
{
    /// <summary>Mirrored flights.</summary>
    int Count { get; }

    /// <summary>Flight <paramref name="index"/> (0 to <see cref="Count"/> − 1).</summary>
    /// <param name="index">The index.</param>
    ref readonly FlightRenderState this[int index] { get; }
}

/// <summary>The identity and launch facts of one mirrored flight, from its <c>ThrowLaunched</c>. Values only.</summary>
/// <example><code>bool bundle = facts.Units > 1;</code></example>
public readonly struct FlightFacts
{
    /// <summary>The flight id.</summary>
    public readonly FlightId Id;

    /// <summary>The thrower's raw player slot.</summary>
    public readonly byte Thrower;

    /// <summary>Units in flight (the Later bundle tier reads it).</summary>
    public readonly byte Units;

    /// <summary>The entry's def ordinal.</summary>
    public readonly ushort KindDef;

    /// <summary>The thrown entry's raw id.</summary>
    public readonly uint EntryId;

    /// <summary>The host launched from its newest history sample, not the claimed feet.</summary>
    public readonly bool OriginClamped;

    /// <summary>The launch verdict (class at release), fixed at launch; <see cref="EndClass.None"/> when unknown (remote peers at M-1a).</summary>
    public readonly EndClass LaunchVerdict;

    /// <summary>The <c>FlightResolved</c> terminal; meaningful only once the flight is <see cref="FlightPhase.Landed"/>.</summary>
    public readonly Terminal Terminal;

    /// <summary>Creates the facts.</summary>
    /// <param name="id">The flight id.</param>
    /// <param name="thrower">The thrower slot.</param>
    /// <param name="units">Units.</param>
    /// <param name="kindDef">The def ordinal.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="originClamped">The origin-clamped flag.</param>
    /// <param name="launchVerdict">The launch verdict.</param>
    /// <param name="terminal">The terminal.</param>
    public FlightFacts(FlightId id, byte thrower, byte units, ushort kindDef, uint entryId, bool originClamped, EndClass launchVerdict, Terminal terminal)
    {
        Id = id;
        Thrower = thrower;
        Units = units;
        KindDef = kindDef;
        EntryId = entryId;
        OriginClamped = originClamped;
        LaunchVerdict = launchVerdict;
        Terminal = terminal;
    }

    /// <summary>A copy with <paramref name="terminal"/>; the launch verdict is kept.</summary>
    /// <param name="terminal">The terminal.</param>
    /// <returns>The copy.</returns>
    public FlightFacts WithTerminal(Terminal terminal) => new(Id, Thrower, Units, KindDef, EntryId, OriginClamped, LaunchVerdict, terminal);
}

/// <summary>One mirrored flight as the view draws it this frame (<see cref="FlightMirror.Sample"/>). Values only.</summary>
/// <example><code>if (f.Frame.Phase == FlightPhase.InFlight) { view.Place(f.Frame.From, f.Frame.To, f.Frame.AlphaPermille, f.SpinBam); }</code></example>
public readonly struct FlightRenderState
{
    /// <summary>Identity and launch facts.</summary>
    public readonly FlightFacts Facts;

    /// <summary>The sampled timeline position.</summary>
    public readonly FlightFrame Frame;

    /// <summary>The spin angle, BAM (<see cref="FlightSpin"/>).</summary>
    public readonly ushort SpinBam;

    /// <summary>Creates the state.</summary>
    /// <param name="facts">The facts.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="spinBam">The spin angle.</param>
    public FlightRenderState(in FlightFacts facts, FlightFrame frame, ushort spinBam)
    {
        Facts = facts;
        Frame = frame;
        SpinBam = spinBam;
    }
}
