using Godot;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Presenter of the <c>Main/World</c> subtree (godot-host-shell story 006, ADR-0019 sections 1-2). Inert until
/// <see cref="Initialize"/>: boot step 7 walks the subtree once, initialises every <see cref="IInjectable{TDeps}"/> descendant and
/// registers, in tree order, the <see cref="IPresentedView"/>s (presented each frame) and the <see cref="IWorldBuildStep"/>s (run once
/// by step 7 after the three presenters are initialised). Views created later (pools, per-lot views) come in through
/// <see cref="Register"/>. Never reads the Sim. Main thread only.
/// </summary>
/// <example><code>worldPresenter.Register(playerView);   // initialised now, presented from the next frame</code></example>
public partial class WorldPresenter : Node3D, IPresenter, IInjectable<PresenterDeps>
{
    private readonly PresenterViews _views = new();
    private readonly List<IWorldBuildStep> _buildSteps = [];

    /// <inheritdoc />
    public bool IsInitialized => _views.Deps is not null;

    /// <summary>Registered views.</summary>
    public int ViewCount => _views.Count;

    /// <summary>Registered world-build steps.</summary>
    public int BuildStepCount => _buildSteps.Count;

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps) => _views.Initialize(deps, this, _buildSteps);

    /// <summary>Registers a view; once the presenter is initialised the view is initialised at once and presented from the next frame.</summary>
    /// <param name="view">The view.</param>
    public void Register(IPresentedView view) => _views.Register(view);

    /// <summary>Registers a world-build step that is not a descendant of <c>Main/World</c>; register before boot step 7.</summary>
    /// <param name="step">The step.</param>
    public void RegisterBuildStep(IWorldBuildStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _buildSteps.Add(step);
    }

    /// <summary>Boot step 7: runs every world-build step once, in registration order.</summary>
    /// <param name="world">The client's world tables.</param>
    /// <returns>How many steps ran.</returns>
    public int BuildWorld(IWorldQuery world)
    {
        for (int i = 0; i < _buildSteps.Count; i++)
        {
            _buildSteps[i].Build(world);
        }

        return _buildSteps.Count;
    }

    /// <inheritdoc />
    public void Present(int alphaPermille) => _views.Present(alphaPermille);
}
