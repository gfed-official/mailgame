namespace PerformativeMail.Sim.Core;

/// <summary>
/// An immutable set of <see cref="EventKind"/>s (ADR-0016 Key Interfaces): what a <c>PresentationEventStream</c> cursor reads
/// (godot-host-shell story 006) and what <c>ClientLayerBuilder.BindMirrorPart</c> binds a mirror part to. <see cref="All"/> contains
/// every kind; <c>default</c> contains none. Membership is a linear scan of the few kinds a consumer names; it allocates nothing.
/// </summary>
/// <example>
/// <code>
/// EventKindSet beats = EventKindSet.Of(MailEventKinds.DeliveryOutcome, ThrowEventKinds.FlightResolved);
/// bool wanted = beats.Contains(entry.Kind);
/// </code>
/// </example>
public readonly struct EventKindSet
{
    private readonly EventKind[]? _kinds;
    private readonly bool _all;

    private EventKindSet(EventKind[]? kinds, bool all)
    {
        _kinds = kinds;
        _all = all;
    }

    /// <summary>The set of every kind.</summary>
    public static EventKindSet All => new(null, all: true);

    /// <summary>A set of the given kinds (copied; duplicates are harmless).</summary>
    /// <param name="kinds">The kinds; none of them <see cref="EventKind.None"/>.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kinds"/> is null.</exception>
    /// <exception cref="ArgumentException">A kind is <see cref="EventKind.None"/>.</exception>
    public static EventKindSet Of(params EventKind[] kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (Array.IndexOf(kinds, EventKind.None) >= 0)
        {
            throw new ArgumentException("EventKind.None is never a member of a kind set.", nameof(kinds));
        }

        return new EventKindSet((EventKind[])kinds.Clone(), all: false);
    }

    /// <summary>True when <paramref name="kind"/> is in the set.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Membership.</returns>
    public bool Contains(EventKind kind) => _all || (_kinds is not null && Array.IndexOf(_kinds, kind) >= 0);
}
