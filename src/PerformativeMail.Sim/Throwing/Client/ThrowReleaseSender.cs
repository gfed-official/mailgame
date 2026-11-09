using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Sends the <c>ThrowRelease</c> on the router's Throw commit (GDD T1 cooldown, T4 "Release"; ADR-0012 section 6, ADR-0016 section 1;
/// throwing-catching story 007). <see cref="Commit"/> is what the input router's Immediate sink forwards to: it builds the payload from the
/// last published preview (<see cref="ThrowReleaseBuilder"/>) and sends it through <see cref="IRequestSink"/>, whose adapter assigns the
/// <c>reqId</c>. A commit within <c>throwCooldownTicks</c> of this client's last sent release, in the host-tick domain
/// (<see cref="IHostClock.EstimatedHostTick"/>; no check while the clock is not valid), is dropped and counted: the M-1a greybox drops it
/// instead of holding it (Mail Lead simplify table, regroup 2026-11-08). A busy sink sends nothing and does not restart the cooldown. Each
/// sent release appends a <see cref="ReleaseRecord"/> to a preallocated ring. Never reads the container mirror (the router cancels on
/// <c>EntryStale</c>) and never references <c>SimWorld</c>. Allocation-free per call; not thread-safe.
/// </summary>
/// <example><code>ThrowSendResult r = sender.Commit(layer.ClientTick);</code></example>
public sealed class ThrowReleaseSender
{
    /// <summary>The release-record ring length.</summary>
    public const int RecordCapacity = 16;

    private const int TicksPerSecond = 30;

    private readonly IThrowPreviewRead _preview;
    private readonly IRequestSink _requests;
    private readonly IHostClock _clock;
    private readonly int _cooldownTicks;
    private readonly ReleaseRecord[] _records = new ReleaseRecord[RecordCapacity];
    private bool _hasSent;
    private uint _lastSentHostTick;
    private uint _released;
    private int _newest = -1;

    /// <summary>Creates the sender.</summary>
    /// <param name="preview">The aim preview's published state.</param>
    /// <param name="requests">The request port.</param>
    /// <param name="clock">The host tick estimate.</param>
    /// <param name="cooldownTicks"><c>throw.throwCooldownTicks</c> (default 5).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ThrowReleaseSender(IThrowPreviewRead preview, IRequestSink requests, IHostClock clock, int cooldownTicks)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(clock);
        _preview = preview;
        _requests = requests;
        _clock = clock;
        _cooldownTicks = cooldownTicks;
    }

    /// <summary>Releases sent so far (also the newest record's <see cref="ReleaseRecord.Seq"/>).</summary>
    public uint Released => _released;

    /// <summary>Commits dropped inside the cooldown.</summary>
    public int CooldownDrops { get; private set; }

    /// <summary>The newest sent release; <c>default</c> before the first.</summary>
    public ref readonly ReleaseRecord Newest => ref _records[_newest < 0 ? 0 : _newest];

    /// <summary>Handles one Throw commit (see the type remarks).</summary>
    /// <param name="clientTick">The client tick of the release frame (the published state's tick or later).</param>
    /// <returns>What happened.</returns>
    public ThrowSendResult Commit(uint clientTick)
    {
        ref readonly ThrowPreviewState s = ref _preview.Current;
        if (!ThrowReleaseBuilder.TryBuild(in s, out ThrowReleasePayload payload))
        {
            return ThrowSendResult.NothingToThrow;
        }

        bool clocked = _clock.IsValid;
        uint hostTick = _clock.EstimatedHostTick;
        if (clocked && _hasSent && unchecked(hostTick - _lastSentHostTick) < (uint)_cooldownTicks)
        {
            CooldownDrops++;
            return ThrowSendResult.Cooldown;
        }

        var op = new Op { Kind = ThrowOpKinds.ThrowRelease };
        op.SetPayload(payload);
        if (!_requests.TrySend(in op))
        {
            return ThrowSendResult.Busy;
        }

        _hasSent = clocked;
        _lastSentHostTick = hostTick;
        _released++;
        var sink = default(SimCounterSink); // divisor 30: nothing to count
        int redAgeMs = s.EndClass == EndClass.Misdeliver ? (int)Fix.FloorDiv((long)unchecked(clientTick - s.RedSinceTick) * 1000, TicksPerSecond, ref sink) : 0;
        _newest = _newest + 1 == RecordCapacity ? 0 : _newest + 1;
        _records[_newest] =
            new ReleaseRecord(_released, clientTick, s.EndClass, redAgeMs, s.Feet, s.DirX, s.DirY, s.DirZ);
        return ThrowSendResult.Sent;
    }
}
