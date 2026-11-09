using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Views.Player;

/// <summary>
/// The local carrier's view lifecycle (player-character story 022, ADR-0019 sections 2 and 5), owned by the World presenter as one of
/// its presented views. Each frame it reads the published <see cref="ILocalPlayerView"/>: once it is known (the M-1a solo join) it spawns
/// <c>PlayerView.tscn</c> under <c>World/Entities</c> through <see cref="EntityViewHost"/>, registered in the
/// <see cref="EntityViewRegistry"/> under the player's <see cref="EntityId"/>; if it stops being known it despawns it. It then presents the
/// view through an <c>IsInstanceValid</c>-guarded registry lookup. Main thread only.
/// </summary>
/// <example><code>worldPresenter.Register(new PlayerPresenter(entitiesNode, deps, deps.Published.Get&lt;ILocalPlayerView&gt;()));</code></example>
public sealed class PlayerPresenter : IPresentedView
{
    /// <summary>The greybox carrier scene.</summary>
    public const string ScenePath = "res://src/Game/Views/Player/PlayerView.tscn";

    private readonly EntityViewHost _host;
    private readonly PresenterDeps _deps;
    private readonly ILocalPlayerView _local;
    private readonly PackedScene _scene;
    private EntityId _spawned = EntityId.None;

    /// <summary>Creates the presenter and loads the view scene.</summary>
    /// <param name="entities">The parent, <c>World/Entities</c>.</param>
    /// <param name="deps">The presenters' deps.</param>
    /// <param name="local">The local player.</param>
    /// <exception cref="InvalidOperationException">The view scene does not load.</exception>
    public PlayerPresenter(Node3D entities, PresenterDeps deps, ILocalPlayerView local)
    {
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _host = new EntityViewHost(entities, deps);
        _scene = GD.Load<PackedScene>(ScenePath) ?? throw new InvalidOperationException($"No player view scene at {ScenePath}.");
    }

    /// <summary>The entity whose view is spawned; <see cref="EntityId.None"/> while none is.</summary>
    public EntityId Spawned => _spawned;

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        if (_spawned != EntityId.None && (!_local.IsKnown || _local.Id != _spawned))
        {
            _host.Despawn(_spawned);
            _spawned = EntityId.None;
        }

        if (_spawned == EntityId.None && _local.IsKnown && _local.Id != EntityId.None)
        {
            if (_host.Spawn(_local.Id, _scene) is null)
            {
                return;
            }

            _spawned = _local.Id;
        }

        if (_spawned != EntityId.None && _deps.Entities.TryGet(_spawned, out Node3D node) && node is PlayerView view)
        {
            view.Present(alphaPermille);
        }
    }
}
