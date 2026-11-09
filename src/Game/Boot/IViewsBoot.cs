using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// What boot step 8 hands the content set to (godot-host-shell stories 008-009, ADR-0019 sections 3 and 5). The Godot side
/// (<c>Presentation.PresentersBoot</c>) loads the editor-saved view catalogs, resolves each against the content, writes the
/// <c>BOOT 8 views: …</c> and <c>VIEW …</c> lines, stores the result in <c>PresenterDeps</c> and attaches the per-lot views. Plain C# here
/// so the boot core stays Godot-free. Main thread only.
/// </summary>
/// <example><code>int errors = ctx.Views!.ResolveViews(ctx.Content!, release: false, ctx.Log);</code></example>
public interface IViewsBoot
{
    /// <summary>Resolves the view catalogs once; on success the views are usable from the first frame.</summary>
    /// <param name="content">The content set of step 2.</param>
    /// <param name="release">True in release exports: missing and unknown entries are content errors.</param>
    /// <param name="log">The boot log.</param>
    /// <returns>The number of errors (0 to continue booting).</returns>
    int ResolveViews(ContentSet content, bool release, BootLog log);
}
