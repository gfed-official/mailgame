namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The solo-pause rule, owned by Networking and Session (ADR-0002 section 4, GDD simulation-core R16, story godot-host-shell
/// 003 decision 2): a pause is a tick-driver state and is allowed only while no remote peer is present; multiplayer never
/// pauses. The host shell's <c>GameLoop.TryPause</c> asks this policy and obeys the answer; it does not decide the rule
/// itself. Stateless and allocation-free (the reasons are constant strings); thread-safe as long as the presence is.
/// </summary>
/// <example>
/// <code>
/// if (SoloPausePolicy.CanPause(presence, out string reason))
/// {
///     driver.Pause();
/// }
/// else
/// {
///     log(reason);   // "multiplayer never pauses: ..."
/// }
/// </code>
/// </example>
public static class SoloPausePolicy
{
    /// <summary>The reason given when a remote peer is present.</summary>
    public const string RemotePeerPresentReason = "multiplayer never pauses: a remote peer is present (ADR-0002 section 4)";

    /// <summary>Decides whether the session may pause now.</summary>
    /// <param name="presence">Networking and Session's peer presence.</param>
    /// <param name="reason">Empty when allowed; otherwise a one-line, human-readable reason for the refusal.</param>
    /// <returns>True when a solo pause is allowed; false when it is refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="presence"/> is null.</exception>
    /// <example><code>bool ok = SoloPausePolicy.CanPause(LoopbackPeerPresence.Instance, out _);   // true</code></example>
    public static bool CanPause(IPeerPresence presence, out string reason)
    {
        ArgumentNullException.ThrowIfNull(presence);
        if (presence.AnyRemotePeer)
        {
            reason = RemotePeerPresentReason;
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
