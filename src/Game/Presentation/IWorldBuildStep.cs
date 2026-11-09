using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// A boot-time world builder on the World presenter (godot-host-shell story 006): <c>TerrainView</c> (story 010) and the per-lot views
/// (story 009). Boot step 7 runs each once, in registration order, after the three presenters are initialised. A descendant of
/// <c>Main/World</c> implementing it is registered by the presenter's subtree walk; others call
/// <see cref="WorldPresenter.RegisterBuildStep"/> before step 7.
/// </summary>
/// <example><code>public void Build(IWorldQuery world) { /* build the terrain mesh from the tables */ }</code></example>
public interface IWorldBuildStep
{
    /// <summary>Builds from the client's immutable world tables.</summary>
    /// <param name="world">The world tables.</param>
    void Build(IWorldQuery world);
}
