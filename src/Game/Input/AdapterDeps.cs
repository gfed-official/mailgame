using Godot;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;

namespace PerformativeMail.Game.Input;

/// <summary>
/// What <see cref="InputAdapter.Initialize"/> receives (ADR-0019 Implementation Guidelines: a node is inert until injected and
/// never looks a dependency up in <c>_Ready</c>). Built once by the input boot (story 028) and immutable.
/// </summary>
/// <example><code>adapter.Initialize(new AdapterDeps { Router = router, Clock = clock, Settings = InputSettings.Defaults });</code></example>
public sealed class AdapterDeps
{
    /// <summary>The router every converted record is fed to.</summary>
    public required InputRouter Router { get; init; }

    /// <summary>The router's own ms clock (the same instance), read once per <c>_Input</c> call for the arrival stamp.</summary>
    public required IInputClock Clock { get; init; }

    /// <summary>The input settings the router was built with (for the adapter-side settings of story 027).</summary>
    public required InputSettings Settings { get; init; }

    /// <summary>
    /// The <c>Main</c> node, for the adapter's one debug-build <see cref="FocusPolicyCheck"/> on its first frame (story 027);
    /// null skips that check.
    /// </summary>
    public Node? Main { get; init; }
}
