using Godot;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Presenter of the <c>Main/Hud</c> subtree (godot-host-shell story 006, ADR-0019 sections 1-2). Inert until <see cref="Initialize"/>,
/// which walks the subtree once like <see cref="WorldPresenter"/>. HUD and aim layers come in through <see cref="AddLayer"/>, which keeps
/// them as children in ascending <c>order</c> (stable for equal orders); the stack's final order is ADR-0018's and is the caller's
/// parameter, not written here. Owns registration only, no widget. Main thread only.
/// </summary>
/// <example><code>hudPresenter.AddLayer(aimLayer, order: AimLayerOrder);   // injected now if the presenter is initialised</code></example>
public partial class HudPresenter : Node, IPresenter, IInjectable<PresenterDeps>
{
    private readonly PresenterViews _views = new();
    private readonly List<(int Order, CanvasLayer Layer)> _layers = [];

    /// <inheritdoc />
    public bool IsInitialized => _views.Deps is not null;

    /// <summary>Registered views.</summary>
    public int ViewCount => _views.Count;

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps) => _views.Initialize(deps, this, null);

    /// <summary>Registers a view; once the presenter is initialised the view is initialised at once and presented from the next frame.</summary>
    /// <param name="view">The view.</param>
    public void Register(IPresentedView view) => _views.Register(view);

    /// <summary>
    /// Adds <paramref name="layer"/> as a child after every layer of a lower or equal order and before the first of a higher one. Once
    /// the presenter is initialised the layer's subtree is injected and its views registered at once.
    /// </summary>
    /// <param name="layer">The layer (not yet in the tree).</param>
    /// <param name="order">Its place in the stack, ascending.</param>
    public void AddLayer(CanvasLayer layer, int order)
    {
        ArgumentNullException.ThrowIfNull(layer);
        int at = _layers.Count;
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].Order > order)
            {
                at = i;
                break;
            }
        }

        _layers.Insert(at, (order, layer));
        AddChild(layer);
        if (at + 1 < _layers.Count)
        {
            MoveChild(layer, _layers[at + 1].Layer.GetIndex());
        }

        if (IsInitialized)
        {
            _views.Adopt(layer);
        }
    }

    /// <inheritdoc />
    public void Present(int alphaPermille) => _views.Present(alphaPermille);
}
