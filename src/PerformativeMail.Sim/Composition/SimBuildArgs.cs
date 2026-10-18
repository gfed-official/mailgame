using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// The inputs of <see cref="SimComposition.Build"/> (ADR-0003 section 4): the profile, the accepted
/// <see cref="ContentSet"/> (passed by reference and shared, never copied; ADR-0010 section 4), the run seed every RNG stream
/// derives from and optional starting counters. <see cref="HostClock"/> is an additive, optional member: the
/// <see cref="ClientLayer"/> composed alongside the world reads it (the adapter's clock); when null the layer gets a clock
/// that is not yet valid. Immutable.
/// </summary>
/// <example>
/// <code>
/// var args = new SimBuildArgs(SimProfile.M1aGreybox, content, runSeed: 0xC0FFEE);
/// SimHost host = SimComposition.Build(in args);
/// </code>
/// </example>
public readonly struct SimBuildArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="profile">Which systems to build.</param>
    /// <param name="content">The accepted content set (shared by reference).</param>
    /// <param name="runSeed">The run seed (reaches the world's <see cref="RngRegistry"/>).</param>
    /// <param name="counters">Starting counters (last issued ids and <c>opSeq</c>); null for all zero.</param>
    /// <param name="hostClock">The client layer's host clock; null for a clock that is not yet valid.</param>
    public SimBuildArgs(SimProfile profile, ContentSet content, ulong runSeed, SimInitialCounters? counters = null, IHostClock? hostClock = null)
    {
        Profile = profile;
        Content = content;
        RunSeed = runSeed;
        Counters = counters;
        HostClock = hostClock;
    }

    /// <summary>Which systems to build (<see cref="SimProfile.M1aGreybox"/>, <see cref="SimProfile.Full"/> or a per-test profile).</summary>
    public SimProfile Profile { get; init; }

    /// <summary>The accepted content set; every built world and system holds this same instance.</summary>
    public ContentSet Content { get; init; }

    /// <summary>The run seed; every RNG stream derives from it (ADR-0005).</summary>
    public ulong RunSeed { get; init; }

    /// <summary>Starting values of the monotonic counters (last issued entity and mail ids, last <c>opSeq</c>); null for all zero.</summary>
    public SimInitialCounters? Counters { get; init; }

    /// <summary>The host clock handed to the <see cref="ClientLayer"/>; null for a clock whose <see cref="IHostClock.IsValid"/> is false.</summary>
    public IHostClock? HostClock { get; init; }

    /// <summary>
    /// The world's read interface (ADR-0026 section 1), handed to the systems that read terrain, solids and the Post Office pad
    /// (Players). Null when the caller has no world: a shipped system that needs one is then left unbuilt (its phases stay no-ops),
    /// like a system whose epic has not landed. Supplied by the production composition owner once world generation is composed.
    ///
    /// A supplied world has no replay identity (header world source 0/0): a recording made on a different supplied world replays
    /// into a dump-hash mismatch, not a <c>WorldSource</c> refusal. Mutually exclusive with <see cref="WorldSource"/>.
    /// </summary>
    public IWorldQuery? World { get; init; }

    /// <summary>
    /// The authored world to build while the world is <c>Constructed</c> (ADR-0026 section 9): the composition root runs
    /// <see cref="IWorldSource.Author"/>, builds the <see cref="WorldTables"/> with the world's id allocator and the Players
    /// walkability limits, binds its <c>World</c> dump section before any system, and hands it to the systems as their
    /// <see cref="IWorldQuery"/>. Its identity enters the <see cref="Core.Replay.RecordingHeader"/>. Null: none. Mutually exclusive
    /// with <see cref="World"/> (<see cref="SimComposition.Build"/> throws <see cref="ArgumentException"/> when both are set).
    /// </summary>
    public IWorldSource? WorldSource { get; init; }

    /// <summary>
    /// The host's <see cref="IEventSink"/>, bound before <c>Freeze()</c> (network-adapter-loopback story 007). Null: no sink.
    /// The host adapter needs the built world, so a host passes a deferred relay here and attaches the adapter afterwards.
    /// </summary>
    public IEventSink? EventSink { get; init; }

    /// <summary>
    /// The optional <see cref="IOpRecorder"/>, bound before <c>Freeze()</c> (simulation-core story 026; additive, ADR-0003 section 4
    /// drift). Null: the world uses its null-object recorder (the 0-alloc gate and perf runs build with none).
    /// </summary>
    public IOpRecorder? Recorder { get; init; }
}
