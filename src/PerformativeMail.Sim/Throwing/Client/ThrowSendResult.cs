namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>What one <see cref="ThrowReleaseSender.Commit"/> did (throwing-catching story 007).</summary>
/// <example><code>bool armGate = sender.Commit(tick) == ThrowSendResult.Sent;</code></example>
public enum ThrowSendResult : byte
{
    /// <summary>One <c>ThrowRelease</c> was sent.</summary>
    Sent = 0,

    /// <summary>The preview had nothing to throw: nothing was sent.</summary>
    NothingToThrow = 1,

    /// <summary>Within <c>throwCooldownTicks</c> of this client's last sent release: dropped (not held) and counted.</summary>
    Cooldown = 2,

    /// <summary>The request sink was busy: nothing was sent and the cooldown did not restart.</summary>
    Busy = 3,
}
