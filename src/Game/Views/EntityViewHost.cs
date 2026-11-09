using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Views;

/// <summary>
/// Spawns and despawns entity views under <c>World/Entities</c> (godot-host-shell story 009, ADR-0019 section 5). Presenters call it on
/// their spawn/despawn events (player-character 022's <c>PlayerView</c>, the <c>WorldItemView</c> seam). <see cref="Spawn"/> is the only way
/// it produces a view: <see cref="PackedScene.Instantiate(PackedScene.GenEditState)"/>, parented, injected with <see cref="PresenterDeps"/>
/// when the root is <see cref="IInjectable{TDeps}"/> (inert until then), registered with the World presenter when it is presented, and
/// added to the <see cref="EntityViewRegistry"/>. Nothing is pooled yet (the registry instantiates directly until a pool exists).
/// Main thread only.
/// </summary>
/// <example><code>Node3D? view = host.SpawnForDef(itemId, deps.Views[DefTableId.Items], def);</code></example>
/// <param name="parent">The parent, <c>World/Entities</c>.</param>
/// <param name="deps">The presenters' deps (its <see cref="PresenterDeps.Entities"/> is the registry used).</param>
/// <param name="present">Registers a presented view with its presenter (<c>WorldPresenter.Register</c>); null to skip.</param>
public sealed class EntityViewHost(Node3D parent, PresenterDeps deps, Action<IPresentedView>? present = null)
{
    private readonly Node3D _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    private readonly PresenterDeps _deps = deps ?? throw new ArgumentNullException(nameof(deps));

    /// <summary>Instantiates <paramref name="scene"/>, parents, injects and registers it under <paramref name="id"/>.</summary>
    /// <param name="id">The entity.</param>
    /// <param name="scene">The view scene; its root must be a <see cref="Node3D"/>.</param>
    /// <returns>The view, or null when the registry refused the id (duplicate or full; the instance is freed).</returns>
    /// <exception cref="InvalidOperationException">The scene root is not a <see cref="Node3D"/>.</exception>
    public Node3D? Spawn(EntityId id, PackedScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Node node = scene.Instantiate();
        if (node is not Node3D view)
        {
            node.Free();
            throw new InvalidOperationException($"Entity view scene {scene.ResourcePath} has a {node.GetClass()} root; a Node3D is required.");
        }

        if (!_deps.Entities.Add(id, view))
        {
            view.Free();
            return null;
        }

        _parent.AddChild(view);
        if (view is IInjectable<PresenterDeps> { IsInitialized: false } injectable)
        {
            injectable.Initialize(_deps);
        }

        if (view is IPresentedView presented)
        {
            present?.Invoke(presented);
        }

        return view;
    }

    /// <summary><see cref="Spawn"/> with a def's resolved scene (the placeholder when its catalog has no entry).</summary>
    /// <param name="id">The entity.</param>
    /// <param name="views">The def table's resolved catalog.</param>
    /// <param name="def">The def.</param>
    /// <returns>The view, or null when refused.</returns>
    /// <exception cref="InvalidOperationException">The def has no scene (a catalog without placeholder never reaches here: boot fails).</exception>
    public Node3D? SpawnForDef(EntityId id, ResolvedViews views, DefIndex def)
    {
        ArgumentNullException.ThrowIfNull(views);
        PackedScene scene = views.Scene(def)
            ?? throw new InvalidOperationException($"No scene for {ViewMapping.TableName(views.Table)} def {def.Value}.");
        return Spawn(id, scene);
    }

    /// <summary>Unregisters and frees a view.</summary>
    /// <param name="id">The entity.</param>
    /// <returns>True when a live view was despawned.</returns>
    public bool Despawn(EntityId id)
    {
        if (!_deps.Entities.TryGet(id, out Node3D view))
        {
            return false;
        }

        _deps.Entities.Remove(id);
        view.QueueFree();
        return true;
    }
}
