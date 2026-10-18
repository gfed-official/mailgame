using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;

namespace PerformativeMail.Sim.Core.Replay;

/// <summary>
/// The header of a recording (ADR-0006 section 7): the format version, the 32-byte <see cref="ContentHash"/> of the content set,
/// the 32-byte SHA-256 of the <see cref="SimSettings"/> canonical encoding, the run seed and the initial counters. Everything a
/// replay needs to prove it is rebuilding the same world. Immutable; created once outside the tick (hashing a content set is
/// not free).
/// </summary>
/// <example>
/// <code>
/// RecordingHeader header = RecordingHeader.Create(content, runSeed: 7);
/// var recorder = new InMemoryRecorder(header);
/// </code>
/// </example>
public sealed class RecordingHeader
{
    /// <summary>The format version this build writes and accepts.</summary>
    public const ushort CurrentFormatVersion = 1;

    /// <summary>Creates a header from explicit values (a loader, or a test that needs a mismatching field).</summary>
    /// <param name="formatVersion">The recording format version.</param>
    /// <param name="contentHash">The content set hash.</param>
    /// <param name="settingsHash">The SHA-256 of the settings canonical encoding.</param>
    /// <param name="runSeed">The run seed.</param>
    /// <param name="counters">The initial counters; null for all zero.</param>
    /// <param name="worldSourceKind">The world source kind (<see cref="World.IWorldSource.Kind"/>); 0 = no world.</param>
    /// <param name="worldSourceVersion">The world source version (<see cref="World.IWorldSource.Version"/>); 0 = no world.</param>
    public RecordingHeader(ushort formatVersion, Hash256 contentHash, Hash256 settingsHash, ulong runSeed, SimInitialCounters? counters, ushort worldSourceKind = 0, uint worldSourceVersion = 0)
    {
        WorldSourceKind = worldSourceKind;
        WorldSourceVersion = worldSourceVersion;
        FormatVersion = formatVersion;
        ContentHash = contentHash;
        SettingsHash = settingsHash;
        RunSeed = runSeed;
        LastOpSeq = counters?.LastOpSeq ?? 0;
        LastMailId = counters?.LastMailId ?? 0;
        LastEntityCounters = counters is null || counters.LastEntityCounters.IsDefault
            ? ImmutableArray<uint>.Empty
            : counters.LastEntityCounters;
    }

    /// <summary>The recording format version.</summary>
    public ushort FormatVersion { get; }

    /// <summary>The <see cref="Content.Hashing.ContentHash.Compute"/> of the content set the session ran on.</summary>
    public Hash256 ContentHash { get; }

    /// <summary>The <see cref="Content.Hashing.ContentHash.ComputeSettings"/> of the session's settings.</summary>
    public Hash256 SettingsHash { get; }

    /// <summary>The run seed every RNG stream derived from.</summary>
    public ulong RunSeed { get; }

    /// <summary>The world source kind the session ran on (<see cref="World.IWorldSource.Kind"/>; 0 = no world source). Story world-stub/007.</summary>
    public ushort WorldSourceKind { get; }

    /// <summary>The world source version the session ran on (<see cref="World.IWorldSource.Version"/>; 0 = no world source).</summary>
    public uint WorldSourceVersion { get; }

    /// <summary>The last <c>opSeq</c> already issued at build (0 = none).</summary>
    public uint LastOpSeq { get; }

    /// <summary>The last mail id already issued at build (0 = none).</summary>
    public uint LastMailId { get; }

    /// <summary>The last entity counter already issued per class at build (empty = all 0).</summary>
    public ImmutableArray<uint> LastEntityCounters { get; }

    /// <summary>Computes the header for a session about to start (hashes the content set and its settings).</summary>
    /// <param name="content">The accepted content set.</param>
    /// <param name="runSeed">The run seed.</param>
    /// <param name="counters">The initial counters passed to the build; null for all zero.</param>
    /// <param name="world">The world source the build will use (<c>SimBuildArgs.WorldSource</c>); null for none.</param>
    /// <returns>A header with <see cref="CurrentFormatVersion"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <example><code>var header = RecordingHeader.Create(BuiltInContent.CreateM1a(), 42);</code></example>
    public static RecordingHeader Create(ContentSet content, ulong runSeed, SimInitialCounters? counters = null, World.IWorldSource? world = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new RecordingHeader(
            CurrentFormatVersion,
            Content.Hashing.ContentHash.Compute(content),
            Content.Hashing.ContentHash.ComputeSettings(content.Settings),
            runSeed,
            counters,
            world?.Kind ?? 0,
            world?.Version ?? 0u);
    }

    /// <summary>The initial counters as the value <c>SimBuildArgs</c> takes.</summary>
    /// <returns>A new record.</returns>
    /// <example><code>var args = new SimBuildArgs(profile, content, header.RunSeed, header.ToInitialCounters());</code></example>
    public SimInitialCounters ToInitialCounters() => new()
    {
        LastOpSeq = LastOpSeq,
        LastMailId = LastMailId,
        LastEntityCounters = LastEntityCounters,
    };

    /// <summary>
    /// Compares this (recorded) header with the header of the replay inputs, field by field, and returns the first field that
    /// differs. Order: format version, settings hash, content hash, run seed, world source (kind, then version), initial counters. The settings hash comes before
    /// the content hash because the content hash covers the settings: a changed <c>sim.*</c> key changes both, and the more
    /// specific field is the one worth naming.
    /// </summary>
    /// <param name="replayInputs">The header computed from the replay inputs.</param>
    /// <returns><see cref="ReplayRefusal.None"/> when every field matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="replayInputs"/> is null.</exception>
    /// <example><code>ReplayRefusal r = recording.Header.FirstMismatch(RecordingHeader.Create(content, seed));</code></example>
    public ReplayRefusal FirstMismatch(RecordingHeader replayInputs)
    {
        ArgumentNullException.ThrowIfNull(replayInputs);
        if (FormatVersion != replayInputs.FormatVersion || FormatVersion != CurrentFormatVersion)
        {
            return ReplayRefusal.FormatVersion;
        }

        if (SettingsHash != replayInputs.SettingsHash)
        {
            return ReplayRefusal.SettingsHash;
        }

        if (ContentHash != replayInputs.ContentHash)
        {
            return ReplayRefusal.ContentHash;
        }

        if (RunSeed != replayInputs.RunSeed)
        {
            return ReplayRefusal.RunSeed;
        }

        if (WorldSourceKind != replayInputs.WorldSourceKind || WorldSourceVersion != replayInputs.WorldSourceVersion)
        {
            return ReplayRefusal.WorldSource;
        }

        return CountersEqual(replayInputs) ? ReplayRefusal.None : ReplayRefusal.InitialCounters;
    }

    private bool CountersEqual(RecordingHeader other)
    {
        if (LastOpSeq != other.LastOpSeq || LastMailId != other.LastMailId)
        {
            return false;
        }

        // A shorter entity-counter array means the remaining classes are 0.
        int n = Math.Max(LastEntityCounters.Length, other.LastEntityCounters.Length);
        for (int i = 0; i < n; i++)
        {
            uint a = i < LastEntityCounters.Length ? LastEntityCounters[i] : 0u;
            uint b = i < other.LastEntityCounters.Length ? other.LastEntityCounters[i] : 0u;
            if (a != b)
            {
                return false;
            }
        }

        return true;
    }
}
