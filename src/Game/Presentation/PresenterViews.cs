using Godot;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// One presenter's injected views (godot-host-shell story 006): the subtree walk at <c>Initialize</c> (once, tree order), late
/// registration and the per-frame <see cref="Present"/>. A view registered while <see cref="Present"/> runs is first presented in the
/// next frame. A freed node view is skipped (<see cref="GodotObject.IsInstanceValid"/>). Main thread only.
/// </summary>
internal sealed class PresenterViews
{
    private readonly List<IPresentedView> _views = [];

    /// <summary>The dependencies; null until <see cref="Initialize"/>.</summary>
    public PresenterDeps? Deps { get; private set; }

    /// <summary>Registered views.</summary>
    public int Count => _views.Count;

    /// <summary>Initialises the views registered so far, then every descendant of <paramref name="root"/>, and registers the presented ones.</summary>
    /// <param name="deps">The dependencies.</param>
    /// <param name="root">The presenter node (itself not visited).</param>
    /// <param name="buildSteps">Receives the descendants implementing <see cref="IWorldBuildStep"/>; null to ignore them.</param>
    public void Initialize(PresenterDeps deps, Node root, List<IWorldBuildStep>? buildSteps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        Deps = deps;
        for (int i = 0; i < _views.Count; i++)
        {
            Inject(_views[i]);
        }

        Walk(root, buildSteps);
    }

    /// <summary>Adopts <paramref name="node"/> and its descendants (a subtree added after <see cref="Initialize"/>).</summary>
    /// <param name="node">The subtree root.</param>
    public void Adopt(Node node)
    {
        Visit(node, null);
        Walk(node, null);
    }

    /// <summary>Registers a view (initialising it when the deps exist); a view already registered is not added twice.</summary>
    /// <param name="view">The view.</param>
    public void Register(IPresentedView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!_views.Contains(view))
        {
            _views.Add(view);
        }

        Inject(view);
    }

    /// <summary>Presents the views registered before this call, in registration order.</summary>
    /// <param name="alphaPermille">Passed through unchanged.</param>
    public void Present(int alphaPermille)
    {
        int count = _views.Count;
        for (int i = 0; i < count; i++)
        {
            IPresentedView view = _views[i];
            if (view is GodotObject node && !GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            view.Present(alphaPermille);
        }
    }

    private void Walk(Node parent, List<IWorldBuildStep>? buildSteps)
    {
        foreach (Node child in parent.GetChildren())
        {
            Visit(child, buildSteps);
            Walk(child, buildSteps);
        }
    }

    private void Visit(Node node, List<IWorldBuildStep>? buildSteps)
    {
        Inject(node);
        if (node is IPresentedView view && !_views.Contains(view))
        {
            _views.Add(view);
        }

        if (buildSteps is not null && node is IWorldBuildStep step && !buildSteps.Contains(step))
        {
            buildSteps.Add(step);
        }
    }

    private void Inject(object candidate)
    {
        if (Deps is { } deps && candidate is IInjectable<PresenterDeps> { IsInitialized: false } injectable)
        {
            injectable.Initialize(deps);
        }
    }
}
