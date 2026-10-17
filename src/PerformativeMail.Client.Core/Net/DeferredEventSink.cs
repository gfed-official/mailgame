using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// An <see cref="IEventSink"/> that is bound to the world before <c>Freeze()</c> and given its real targets afterwards
/// (TD-065 item 1). The world needs its sink at build time and <see cref="HostAdapter"/> needs the built world; this relay
/// closes the loop. It also fans one phase-13 delivery out to several targets in attach order (the host adapter first, then
/// host Presentation), so the adapter always sees a tick before presentation does. Like every sink it copies or reads only:
/// the entries span is valid for the call. Until a target is attached a delivery goes nowhere. Main thread only; setup-time
/// <see cref="Attach"/> allocates, <see cref="Deliver"/> does not.
/// </summary>
/// <example>
/// <code>
/// var relay = new DeferredEventSink();
/// SimHost host = SimComposition.Build(args with { EventSink = relay });
/// relay.Attach(new HostAdapter(host.World, ...));
/// relay.Attach(presentationSink);
/// </code>
/// </example>
public sealed class DeferredEventSink : IEventSink
{
    private IEventSink[] _targets = Array.Empty<IEventSink>();

    /// <summary>Number of attached targets.</summary>
    public int TargetCount => _targets.Length;

    /// <summary>Appends a target; targets receive every delivery in attach order. Setup-time call (allocates).</summary>
    /// <param name="target">The sink to forward to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="target"/> is this relay.</exception>
    public void Attach(IEventSink target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(target, this))
        {
            throw new ArgumentException("A relay cannot forward to itself.", nameof(target));
        }

        var next = new IEventSink[_targets.Length + 1];
        _targets.CopyTo(next, 0);
        next[^1] = target;
        _targets = next;
    }

    /// <inheritdoc />
    public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
    {
        IEventSink[] targets = _targets;
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].Deliver(tick, entries, in overflow);
        }
    }
}
