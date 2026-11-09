using Godot;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Smoke;

/// <summary>The <c>entity-registry</c> smoke's injectable view root (godot-host-shell story 009): records the deps it received. Debug smoke code only.</summary>
public partial class RegistryProbeView : Node3D, IInjectable<PresenterDeps>
{
    /// <summary>The deps of <see cref="Initialize"/>; null until then.</summary>
    public PresenterDeps? InjectedDeps { get; private set; }

    /// <inheritdoc />
    public bool IsInitialized => InjectedDeps is not null;

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps) => InjectedDeps = deps;
}
