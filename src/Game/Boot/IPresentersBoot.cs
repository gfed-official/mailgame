using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The presenters boot step 7 hands the world to (godot-host-shell story 006, ADR-0019 sections 1-2). The Godot side
/// (<c>Presentation.PresentersBoot</c> over <c>Main/World</c>, <c>Main/Hud</c>, <c>Main/Audio</c>) builds <c>PresenterDeps</c> from
/// <see cref="PresentationInputs"/>, calls <c>Initialize(deps)</c> on the World, Hud and Audio presenters in that order, then runs the
/// world-build steps registered on the World presenter. Plain C# here so the boot core stays Godot-free. Main thread only.
/// </summary>
/// <example><code>ctx.Presenters?.Initialize(new PresentationInputs(world, clock, events, ctx.Published), ctx.Log);</code></example>
public interface IPresentersBoot
{
    /// <summary>Initialises the presenters once, in order, and runs the world-build steps; writes its <c>BOOT 7 world: …</c> lines.</summary>
    /// <param name="inputs">What step 7 hands over.</param>
    /// <param name="log">The boot log.</param>
    void Initialize(PresentationInputs inputs, BootLog log);
}

/// <summary>What boot step 7 hands the presenters: the client's immutable world tables, the render clock, the event stream and the published states.</summary>
/// <param name="World">The client's immutable world tables (an immutable table interface, not <c>SimWorld</c>).</param>
/// <param name="Clock">The render clock step 6 built.</param>
/// <param name="Events">The outcome-beat feed, already set on the client adapter.</param>
/// <param name="Published">The states the boot contributors published.</param>
public sealed record PresentationInputs(IWorldQuery World, IRenderClock Clock, PresentationEventStream Events, PublishedStates Published);
