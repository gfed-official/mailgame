namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// The typed bag of read-only ClientLayer states Presentation may read (godot-host-shell story 006): <c>ICarrierView</c>,
/// <c>IInteractionQuery</c>, <c>IThrowPreviewRead</c>, <c>IDeliverPreviewRead</c>, <c>IFlightRead</c> and the like, one entry per
/// interface type. Boot contributors fill it in steps 6-7; boot step 7 hands it to the presenters inside <c>PresenterDeps</c>.
/// It is passed explicitly, never reachable globally (no static, no autoload: ADR-0019 section 1's "no global lookup path").
/// Main thread only.
/// </summary>
/// <example>
/// <code>
/// ctx.Published.Add&lt;ICarrierView&gt;(session.Carrier.View);   // a boot contributor
/// ICarrierView carrier = deps.Published.Get&lt;ICarrierView&gt;();  // a view's Initialize(deps)
/// </code>
/// </example>
public sealed class PublishedStates
{
    private readonly Dictionary<Type, object> _states = [];
    private readonly List<Type> _order = [];

    /// <summary>The published interface types, in the order they were added.</summary>
    public IReadOnlyList<Type> Types => _order;

    /// <summary>Publishes <paramref name="state"/> under <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The read-only interface Presentation asks for.</typeparam>
    /// <param name="state">The state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A state is already published under <typeparamref name="T"/>.</exception>
    public void Add<T>(T state)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_states.TryAdd(typeof(T), state))
        {
            throw new InvalidOperationException($"A state is already published as {typeof(T).FullName}.");
        }

        _order.Add(typeof(T));
    }

    /// <summary>The state published under <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The read-only interface.</typeparam>
    /// <returns>The state, never null.</returns>
    /// <exception cref="InvalidOperationException">Nothing is published as <typeparamref name="T"/> (the message names it).</exception>
    public T Get<T>()
        where T : class =>
        _states.TryGetValue(typeof(T), out object? state)
            ? (T)state
            : throw new InvalidOperationException($"No published state {typeof(T).FullName}: no boot contributor added it to PublishedStates.");

    /// <summary>The state published under <typeparamref name="T"/>, if any (for a view that is optional on a reduced boot).</summary>
    /// <typeparam name="T">The read-only interface.</typeparam>
    /// <param name="state">The state when published.</param>
    /// <returns>True when a state is published as <typeparamref name="T"/>.</returns>
    public bool TryGet<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? state)
        where T : class
    {
        state = _states.TryGetValue(typeof(T), out object? found) ? (T)found : null;
        return state is not null;
    }
}
