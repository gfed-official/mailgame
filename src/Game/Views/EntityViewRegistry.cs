using Godot;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Views;

/// <summary>
/// Presentation's <see cref="EntityId"/> → view map (ADR-0019 section 5, ADR-0004; godot-host-shell story 009): a C#
/// <see cref="Dictionary{TKey, TValue}"/> built with its capacity, which never grows. A duplicate <see cref="Add"/> or one beyond the capacity
/// is refused and counted; every lookup is <see cref="GodotObject.IsInstanceValid"/>-guarded, and a node freed behind the registry's back is
/// dropped and counted as a stale lookup. Ids never cross into Godot (no Variant, meta or signal). Iteration order never orders gameplay.
/// Main thread only.
/// </summary>
/// <example><code>if (deps.Entities.TryGet(lot.Destination, out Node3D view)) { view.Visible = true; }</code></example>
/// <param name="capacity">The fixed capacity, usually <see cref="PresentationCaps.EntityViews"/>.</param>
public sealed class EntityViewRegistry(int capacity)
{
    private readonly Dictionary<EntityId, Node3D> _views = new(capacity);

    /// <summary>The fixed capacity.</summary>
    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");

    /// <summary>Registered views (freed ones count until a lookup drops them).</summary>
    public int Count => _views.Count;

    /// <summary><see cref="Add"/> calls refused because the id was already registered.</summary>
    public int DuplicateAdds { get; private set; }

    /// <summary><see cref="Add"/> calls refused because the registry was full.</summary>
    public int OverCapacityAdds { get; private set; }

    /// <summary>Lookups that found a freed node (the entry was removed).</summary>
    public int StaleLookups { get; private set; }

    /// <summary>Registers a view.</summary>
    /// <param name="id">The entity.</param>
    /// <param name="view">Its view node.</param>
    /// <returns>False (counted) when the id is registered or the registry is full.</returns>
    public bool Add(EntityId id, Node3D view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_views.ContainsKey(id))
        {
            DuplicateAdds++;
            return false;
        }

        if (_views.Count >= Capacity)
        {
            OverCapacityAdds++;
            return false;
        }

        _views.Add(id, view);
        return true;
    }

    /// <summary>Unregisters a view (the node is not freed).</summary>
    /// <param name="id">The entity.</param>
    /// <returns>True when it was registered.</returns>
    public bool Remove(EntityId id) => _views.Remove(id);

    /// <summary>Looks a view up; a freed node is removed and counted.</summary>
    /// <param name="id">The entity.</param>
    /// <param name="view">The live view when found.</param>
    /// <returns>True for a live view.</returns>
    public bool TryGet(EntityId id, out Node3D view)
    {
        if (_views.TryGetValue(id, out Node3D? found))
        {
            if (GodotObject.IsInstanceValid(found))
            {
                view = found;
                return true;
            }

            _views.Remove(id);
            StaleLookups++;
        }

        view = null!;
        return false;
    }
}
