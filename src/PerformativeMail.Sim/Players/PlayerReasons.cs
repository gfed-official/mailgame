using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The one place Players declares the interaction framework's reason ids (PCM-UI-1): the host's <c>CheckInteraction</c> gate is built
/// from <see cref="Framework"/>, and so is the client's focus and press gate once its composition lands (the client builds none yet),
/// so the two sides cannot disagree on an id. Evan approved (TD-194 item 9), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019". The ids sit in <see cref="StringNamespace.Reason"/>, inside
/// the framework's reserved indices 0 to 15; the string table that spells them stays with TD-119.
/// </summary>
/// <example><code>var gate = new InteractionGate(PlayerReasons.Framework);</code></example>
public static class PlayerReasons
{
    /// <summary>The id of <see cref="FrameworkReasons.DryLandKey"/> (Evan approved (TD-194 item 2), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019": index 0).</summary>
    public static readonly StringId DryLand = new(StringNamespace.Reason, 0);

    /// <summary>The id of <see cref="FrameworkReasons.ClosePanelKey"/> (Evan approved (TD-194 item 2), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019": index 1).</summary>
    public static readonly StringId ClosePanel = new(StringNamespace.Reason, 1);

    /// <summary>The pair the framework gate takes, on the host and on the client.</summary>
    public static FrameworkReasons Framework => new(DryLand, ClosePanel);
}
