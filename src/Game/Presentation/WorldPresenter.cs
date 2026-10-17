using Godot;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Inert presenter for the <c>Main/World</c> subtree. Behaviour, <c>Initialize(deps)</c> and injection are owned by
/// story 006; until then it is never initialized, has no <c>_Ready</c> and does nothing.
/// </summary>
public partial class WorldPresenter : Node3D, IPresenter
{
    /// <inheritdoc />
    public bool IsInitialized => false;

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        // Story 006 owns presentation.
    }
}
