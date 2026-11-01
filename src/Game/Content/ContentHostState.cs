namespace PerformativeMail.Game.Content;

/// <summary>
/// Where a <see cref="ContentHost"/> is in its load (ADR-0019 section 4, GDD CD21): <see cref="Unloaded"/> until
/// <see cref="ContentHost.Load"/> runs, <see cref="Loading"/> while it runs, then <see cref="Valid"/> with one
/// <c>ContentSet</c> or <see cref="Failed"/> with every diagnostic. <see cref="Reloading"/> is reserved for M0's F6 hot reload
/// and is never entered in M-1a.
/// </summary>
public enum ContentHostState
{
    /// <summary>No load has run.</summary>
    Unloaded = 0,

    /// <summary>A load is running on the calling thread.</summary>
    Loading = 1,

    /// <summary>The load produced a content set; a further load is refused.</summary>
    Valid = 2,

    /// <summary>The load produced no content set; the diagnostics say why.</summary>
    Failed = 3,

    /// <summary>Reserved for M0's F6 hot reload (TR-content-026); not entered in M-1a.</summary>
    Reloading = 4,
}
