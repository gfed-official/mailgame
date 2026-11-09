using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Game.Views;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// What every presenter and view receives through <see cref="IInjectable{TDeps}.Initialize"/> (godot-host-shell story 006, ADR-0019
/// section 1). Built once at boot step 7 and immutable. Views read only these and the published interfaces, never <c>SimWorld</c> or
/// <c>ClientLayer</c>. <see cref="Views"/> is filled by boot step 8 (story 008); <see cref="Entities"/> is story 009's registry.
/// </summary>
/// <example><code>ICarrierView carrier = deps.Published.Get&lt;ICarrierView&gt;(); long ms = deps.Clock.RenderSimTimeMs;</code></example>
public sealed class PresenterDeps
{
    /// <summary>The client's immutable world tables.</summary>
    public required IWorldQuery World { get; init; }

    /// <summary>Presentation time (ADR-0016 section 6), sampled once per frame before <c>Present</c>.</summary>
    public required IRenderClock Clock { get; init; }

    /// <summary>The outcome-beat feed; open a cursor in <c>Initialize</c>.</summary>
    public required PresentationEventStream Events { get; init; }

    /// <summary>The read-only ClientLayer states boot published (<c>ICarrierView</c> and the like); <c>Get&lt;T&gt;</c> throws naming a missing <c>T</c>.</summary>
    public required PublishedStates Published { get; init; }

    /// <summary>The audio cue entry (<see cref="AudioPresenter"/>); with no sink registered a cue is dropped.</summary>
    public required IAudioCueSink Audio { get; init; }

    /// <summary>The resolved view catalogs (story 008), filled by boot step 8 before the first frame; empty during step 7.</summary>
    public required ResolvedViewTables Views { get; init; }

    /// <summary>The entity-view registry (story 009); every lookup is <c>IsInstanceValid</c>-guarded.</summary>
    public required EntityViewRegistry Entities { get; init; }
    /// <summary>The HUD read model (hud-notifications story 002); optional until boot composes it, HUD views stay blank without it.</summary>
    public IHudState? HudState { get; init; }
}
