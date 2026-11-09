namespace PerformativeMail.Game.Presentation;

/// <summary>
/// The one injection contract of Presentation (ADR-0019 section 1, godot-host-shell story 006). A view is inert until its presenter
/// calls <see cref="Initialize"/> at boot step 7 (or when it is registered later); it never looks a dependency up in <c>_Ready</c>.
/// </summary>
/// <typeparam name="TDeps">The dependencies, <see cref="PresenterDeps"/> for every scene view.</typeparam>
/// <example>
/// <code>
/// public partial class CameraRig : Node3D, IInjectable&lt;PresenterDeps&gt;
/// {
///     public bool IsInitialized { get; private set; }
///     public void Initialize(PresenterDeps deps) { _carrier = deps.Published.Get&lt;ICarrierView&gt;(); IsInitialized = true; }
/// }
/// </code>
/// </example>
public interface IInjectable<in TDeps>
{
    /// <summary>True once <see cref="Initialize"/> has run; a presenter does not initialise an initialised view again.</summary>
    bool IsInitialized { get; }

    /// <summary>Receives the dependencies.</summary>
    /// <param name="deps">The dependencies.</param>
    void Initialize(TDeps deps);
}
