using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The interaction framework's two reason keys (PC GDD UI Requirements, PCM-UI-1): <c>reason.dry_land{verb}</c> and
/// <c>reason.close_panel</c> (no argument; text "Close panel to deliver", rendered by HUD). An injected value: the framework
/// never mints a <see cref="StringId"/>. Where the ids come from is open (OQ-013-2, TD-119); composition supplies them once that is
/// decided. <see cref="InteractionGate"/> rejects <c>None</c> or equal ids.
/// </summary>
/// <example><code>var reasons = new FrameworkReasons(dryLandId, closePanelId);   // ids from the (future) string table</code></example>
public readonly struct FrameworkReasons
{
    /// <summary>The key name of <see cref="DryLand"/>, as spelled by PCM-UI-1.</summary>
    public const string DryLandKey = "reason.dry_land";

    /// <summary>The key name of <see cref="ClosePanel"/>, as spelled by PCM-UI-1.</summary>
    public const string ClosePanelKey = "reason.close_panel";

    /// <summary>Creates the pair.</summary>
    /// <param name="dryLand">The id of <see cref="DryLandKey"/>.</param>
    /// <param name="closePanel">The id of <see cref="ClosePanelKey"/>.</param>
    public FrameworkReasons(StringId dryLand, StringId closePanel)
    {
        DryLand = dryLand;
        ClosePanel = closePanel;
    }

    /// <summary>The id of <c>reason.dry_land</c> (an anchor refused while swimming).</summary>
    public StringId DryLand { get; }

    /// <summary>The id of <c>reason.close_panel</c> (every focused anchor while the panel is open).</summary>
    public StringId ClosePanel { get; }
}
