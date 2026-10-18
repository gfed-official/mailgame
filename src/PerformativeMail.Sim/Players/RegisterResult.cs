namespace PerformativeMail.Sim.Players;

/// <summary>
/// The result of <c>RegisterAnchor</c>, <c>UnregisterAnchor</c> and <c>MoveAnchor</c> (ADR-0014 section 1). Append-only.
/// </summary>
/// <example><code>if (players.RegisterAnchor(in def, impl) != RegisterResult.Ok) { /* handle */ }</code></example>
public enum RegisterResult : byte
{
    /// <summary>Done.</summary>
    Ok = 0,

    /// <summary>The anchor already has a verb registered in this context; the first registration is untouched and a check was recorded.</summary>
    DuplicateVerb = 1,

    /// <summary>The registry holds <c>maxAnchors</c> anchors; counted, nothing changed.</summary>
    Full = 2,

    /// <summary>A null impl, a none ref, a verb outside the catalogue, a mode outside Tap to Channel, a priority above 3, unknown flag bits, or a negative range or hold base.</summary>
    InvalidArgument = 3,

    /// <summary>Unregister or Move named an anchor that is not registered.</summary>
    UnknownAnchor = 4,
}
